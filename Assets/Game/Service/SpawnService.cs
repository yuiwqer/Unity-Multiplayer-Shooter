using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class SpawnService : MonoBehaviour
{
    #region 单例
    private static SpawnService _instance;
    private static readonly object _lock = new object();
    private static bool _quitting;

    public static SpawnService Instance
    {
        get
        {
            if (_quitting) return null;
            lock (_lock)
            {
                if (_instance != null) return _instance;
                _instance = FindObjectOfType<SpawnService>();
                if (_instance != null) return _instance;

                var go = new GameObject($"[{nameof(SpawnService)}]");
                _instance = go.AddComponent<SpawnService>();
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
    }

    void OnApplicationQuit() => _quitting = true;
    void OnDestroy() { if (_instance == this) _instance = null; }
    #endregion

    [Header("默认玩家 SO 名称")]
    public string playerModuleName = "pistol";

    // 记录已经生成的玩家
    private readonly Dictionary<ulong, IModuleHandler> _spawnedPlayers = new Dictionary<ulong, IModuleHandler>();

    /// <summary>
    /// 为指定客户端生成玩家
    /// </summary>
    public void SpawnPlayerForClient(ulong clientId)
    {
        if (_spawnedPlayers.ContainsKey(clientId))
        {
            Debug.LogWarning($"[SpawnService] 客户端 {clientId} 的玩家已存在。");
            return;
        }

        // 错开出生点（这里可以根据你当前场景的出生点逻辑来写）
        float offsetX = (float)clientId * 2.5f - 2.5f; 
        Vector3 spawnPos = new Vector3(offsetX, 0f, 0f);

        var handler = ModuleService.Instance.SpawnHandler(playerModuleName, spawnPos, Quaternion.identity, clientId);
        
        if (handler != null)
        {
            _spawnedPlayers.Add(clientId, handler);
            Debug.Log($"[SpawnService] 成功为客户端 {clientId} 生成玩家。");
        }
    }

    /// <summary>
    /// 为当前所有已连接的客户端生成玩家（常用于场景刚加载完时）
    /// </summary>
    public void SpawnAllConnectedPlayers()
    {
        if (!NetworkManager.Singleton.IsServer) return;

        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            SpawnPlayerForClient(clientId);
        }
    }

    /// <summary>
    /// 销毁指定玩家的对象
    /// </summary>
    public void DespawnPlayer(ulong clientId)
    {
        if (_spawnedPlayers.TryGetValue(clientId, out var player))
        {
            if (player is MonoBehaviour mono && mono != null && mono.TryGetComponent<NetworkObject>(out var netObj))
            {
                if (netObj.IsSpawned) netObj.Despawn(true);
            }
            _spawnedPlayers.Remove(clientId);
        }
    }

    /// <summary>
    /// 销毁所有玩家（常用于切回主菜单）
    /// </summary>
    public void DespawnAllPlayers()
    {
        var keys = new List<ulong>(_spawnedPlayers.Keys);
        foreach (var id in keys)
        {
            DespawnPlayer(id);
        }
    }
}