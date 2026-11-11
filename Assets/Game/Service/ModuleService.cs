// ModuleService.cs
using System;
using System.Collections.Generic;
using UnityEngine;

public class ModuleService : MonoBehaviour
{
    #region 单例
    private static ModuleService _instance;
    private static readonly object _lock = new object();
    private static bool _quitting;

    public static ModuleService Instance
    {
        get
        {
            if (_quitting) return null;
            lock (_lock)
            {
                if (_instance != null) return _instance;
                _instance = FindObjectOfType<ModuleService>();
                if (_instance != null) return _instance;

                var go = new GameObject($"[{nameof(ModuleService)}]");
                _instance = go.AddComponent<ModuleService>();
                DontDestroyOnLoad(go);
                return _instance;
            }
        }
    }

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        TryInitialize();
    }

    void OnApplicationQuit() => _quitting = true;
    void OnDestroy()
    {
        if (_instance != this) return;
        Teardown();
        _instance = null;
    }
    #endregion

    #region 状态
    private bool _initialized;
    private readonly List<ModuleConfigSO> _loadedConfigs = new List<ModuleConfigSO>(32);
    private readonly Dictionary<string, ModuleConfigSO> _configMap = new Dictionary<string, ModuleConfigSO>(32);
    
    // ★ 核心：全部改为 IModuleHandler
    private readonly List<IModuleHandler> _activeHandlers = new List<IModuleHandler>(32);
    private readonly Dictionary<string, IModuleHandler> _handlerMap = new Dictionary<string, IModuleHandler>(32);

    public bool IsInitialized => _initialized;
    #endregion

    #region 初始化与清理
    public void EnsureInitialized() { if (!_initialized) TryInitialize(); }

    private bool TryInitialize()
    {
        if (_initialized) return true;
        if (ResourceService.Instance == null || EventService.Instance == null) return false;

        LoadConfigs();
        _initialized = true;
        return true;
    }

    private void LoadConfigs()
    {
        var configs = ResourceService.Instance.LoadAll<ModuleConfigSO>("Configs/Modules");
        if (configs == null || configs.Length == 0) return;

        foreach (var cfg in configs)
        {
            if (cfg == null) continue;
            var name = cfg.ModuleName;
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (_configMap.ContainsKey(name)) continue;

            _configMap.Add(name, cfg);
            _loadedConfigs.Add(cfg);
            try { cfg.OnConfigLoaded(); } catch (Exception e) { Debug.LogException(e, cfg); }
        }
    }
    #endregion

    #region 动态生成与回收
    // 在 ModuleService.cs 中修改 SpawnHandler 方法：
    public IModuleHandler SpawnHandler(string moduleName, Vector3 position = default, Quaternion rotation = default,
        ulong? ownerClientId = null)
    {
        if (!_initialized) return null;

        var cfg = GetConfig(moduleName);
        if (cfg == null || cfg.Prefab == null)
        {
            Debug.LogError($"[ModuleService] 无法生成 {moduleName}，配置或 Prefab 为空。");
            return null;
        }

        // ★ 放弃对象池，直接 Instantiate
        var go = Instantiate(cfg.Prefab);
        go.transform.SetPositionAndRotation(position, rotation);

        IModuleHandler handler = go.GetComponent(cfg.HandlerType) as IModuleHandler;
        if (handler == null)
        {
            handler = go.GetComponentInChildren<IModuleHandler>(true);
        }

        if (handler == null)
        {
            Debug.LogError($"[ModuleService] {moduleName} 的 Prefab 上找不到 IModuleHandler 组件。");
            Destroy(go);
            return null;
        }

        handler.Initialize(cfg);
        RegisterHandler(handler);

        if (NetworkService.Instance != null && NetworkService.Instance.IsServer)
        {
            var netObj = go.GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj != null && !netObj.IsSpawned)
            {
                if (ownerClientId.HasValue)
                    netObj.SpawnWithOwnership(ownerClientId.Value, true);
                else
                    netObj.Spawn(true);

                // ★ 网络生成完毕后，服务器把模块名同步给所有客户端
                if (handler is NetworkModuleHandler netHandler)
                {
                    netHandler.SyncModuleName.Value = moduleName;
                }
            }
        }

        return handler;
    }

    public void DespawnHandler(string moduleName)
{
    if (_handlerMap.TryGetValue(moduleName, out var handler) && handler != null)
    {
        _handlerMap.Remove(moduleName);
        UnregisterHandler(handler);
        handler.Dispose();
        
        if (handler is MonoBehaviour mono)
        {
            // ★ 直接用 Destroy，或者如果是网络对象就用 Despawn
            if (mono.TryGetComponent<Unity.Netcode.NetworkObject>(out var netObj) && netObj.IsSpawned)
            {
                if (NetworkService.Instance != null && NetworkService.Instance.IsServer)
                    netObj.Despawn(true);
            }
            else
            {
                Destroy(mono.gameObject);
            }
        }
    }
}
    #endregion

    #region 注册与驱动
    public void RegisterHandler(IModuleHandler handler)
    {
        if (handler == null || _activeHandlers.Contains(handler)) return;
        _activeHandlers.Add(handler);
    }

    public void UnregisterHandler(IModuleHandler handler)
    {
        if (handler == null) return;
        _activeHandlers.Remove(handler);
    }

    void Update()
    {
        if (!_initialized || _activeHandlers.Count == 0) return;

        var dt = Time.deltaTime;
        for (int i = _activeHandlers.Count - 1; i >= 0; i--)
        {
            var h = _activeHandlers[i];
            if (h == null || !h.IsActiveModule) continue;

            try { h.OnModuleUpdate(dt); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
    #endregion

    #region 查询与关闭
    public ModuleConfigSO GetConfig(string name) => _configMap.TryGetValue(name, out var cfg) ? cfg : null;
    public T GetConfig<T>(string name) where T : ModuleConfigSO => GetConfig(name) as T;

    public void DisposeAllHandlers()
    {
        for (int i = _activeHandlers.Count - 1; i >= 0; i--)
        {
            var h = _activeHandlers[i];
            if (h == null) continue;
            
            h.Dispose();

            if (h is MonoBehaviour mono)
            {
                // ★ 联机模式：如果是网络对象，必须走 NGO 的 Despawn
                if (mono.TryGetComponent<Unity.Netcode.NetworkObject>(out var netObj) && netObj.IsSpawned)
                {
                    if (NetworkService.Instance != null && NetworkService.Instance.IsServer)
                    {
                        netObj.Despawn(true);
                    }
                }
                else
                {
                    // ★ 单机模式：直接 Destroy
                    Destroy(mono.gameObject);
                }
            }
        }
        _activeHandlers.Clear();
        _handlerMap.Clear();
    }

    private void Teardown()
    {
        DisposeAllHandlers();
        foreach (var cfg in _loadedConfigs) { try { cfg.OnConfigUnloaded(); } catch { } }
        _loadedConfigs.Clear();
        _configMap.Clear();
        _initialized = false;
    }
    #endregion
}