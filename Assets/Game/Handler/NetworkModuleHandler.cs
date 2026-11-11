using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public abstract class NetworkModuleHandler : NetworkBehaviour, IModuleHandler
{
    // ★ 利用 NetworkVariable 自动同步模块名
    public NetworkVariable<FixedString64Bytes> SyncModuleName = new NetworkVariable<FixedString64Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    protected ModuleConfigSO Config { get; private set; }
    protected string ModuleName => Config != null ? Config.ModuleName : name;
    public bool IsInitialized { get; private set; }
    public bool IsActiveModule => IsInitialized;

    public void Initialize(ModuleConfigSO config)
    {
        if (IsInitialized) return;
        if (config == null) return;

        Config = config;
        IsInitialized = true;
        try { OnModuleInitialize(); } catch (Exception e) { Debug.LogException(e, this); }
    }

    public void Dispose()
    {
        if (!IsInitialized) return;
        try { OnModuleDispose(); } catch (Exception e) { Debug.LogException(e, this); }
        finally { IsInitialized = false; Config = null; }
    }

    protected virtual void OnModuleInitialize() { }
    public virtual void OnModuleUpdate(float deltaTime) { }
    protected virtual void OnModuleDispose() { }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (!IsServer)
        {
            // ★ 客户端：监听服务器同步过来的 ModuleName
            SyncModuleName.OnValueChanged += OnSyncNameChanged;
            if (!SyncModuleName.Value.IsEmpty)
            {
                LoadConfigOnClient(SyncModuleName.Value.ToString());
            }
        }
    }

    private void OnSyncNameChanged(FixedString64Bytes oldVal, FixedString64Bytes newVal)
    {
        if (!IsServer && !newVal.IsEmpty)
        {
            LoadConfigOnClient(newVal.ToString());
        }
    }

    private void LoadConfigOnClient(string moduleName)
    {
        if (IsInitialized) return;
        
        var cfg = ModuleService.Instance.GetConfig(moduleName);
        if (cfg != null)
        {
            Initialize(cfg);
            // ★ 客户端拿到配置后，必须注册进 ModuleService 才能被 Update 驱动
            ModuleService.Instance.RegisterHandler(this); 
        }
    }
}