// NetworkService.cs
// 网络服务：封装 Unity Netcode for GameObjects (NGO)，维护连接状态并桥接本地事件总线。

using System;
using UnityEngine;
using Unity.Netcode; // 引入 NGO 命名空间

public class NetworkService : MonoBehaviour
{
    #region 单例
    private static NetworkService _instance;
    private static readonly object _lock = new object();
    private static bool _quitting;

    public static NetworkService Instance
    {
        get
        {
            if (_quitting) return null;
            lock (_lock)
            {
                if (_instance != null) return _instance;
                _instance = FindObjectOfType<NetworkService>();
                if (_instance != null) return _instance;

                var go = new GameObject($"[{nameof(NetworkService)}]");
                _instance = go.AddComponent<NetworkService>();
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

    void OnDestroy()
    {
        if (_instance != this) return;
        
        // 清理 NGO 事件绑定
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
        }
        
        _instance = null;
    }
    #endregion

    #region 状态定义
    public enum NetworkState
    {
        Disconnected,
        Connecting,
        Connected
    }

    public NetworkState State { get; private set; } = NetworkState.Disconnected;
    
    // 便捷访问器
    public bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
    public bool IsClient => NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient;
    public bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
    public ulong LocalClientId => NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
    #endregion

    void Start()
    {
        // 检查场景里是否有 NetworkManager
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[NetworkService] 场景中缺少 NetworkManager！请添加 NetworkManager 组件。");
            return;
        }

        // 注册 NGO 的内置回调
        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    #region 启动与关闭 API

    /// <summary>
    /// 启动 Host（服务器 + 本地玩家）
    /// </summary>
    public bool StartHost()
    {
        if (NetworkManager.Singleton == null) return false;
        
        Debug.Log("[NetworkService] 尝试启动 Host...");
        State = NetworkState.Connecting;
        bool success = NetworkManager.Singleton.StartHost();
        if (!success) State = NetworkState.Disconnected;
        return success;
    }

    /// <summary>
    /// 启动 Dedicated Server（纯服务器，无画面）
    /// </summary>
    public bool StartServer()
    {
        if (NetworkManager.Singleton == null) return false;
        
        Debug.Log("[NetworkService] 尝试启动 Server...");
        State = NetworkState.Connecting;
        bool success = NetworkManager.Singleton.StartServer();
        if (!success) State = NetworkState.Disconnected;
        return success;
    }

    /// <summary>
    /// 启动 Client（纯客户端，连接到远端服务器）
    /// </summary>
    public bool StartClient()
    {
        if (NetworkManager.Singleton == null) return false;

        Debug.Log("[NetworkService] 尝试启动 Client...");
        State = NetworkState.Connecting;
        bool success = NetworkManager.Singleton.StartClient();
        if (!success) State = NetworkState.Disconnected;
        return success;
    }

    /// <summary>
    /// 断开连接（关闭网络）
    /// </summary>
    public void Disconnect()
    {
        if (NetworkManager.Singleton == null || State == NetworkState.Disconnected) return;

        Debug.Log("[NetworkService] 正在断开网络连接...");
        NetworkManager.Singleton.Shutdown();
        State = NetworkState.Disconnected;
        
        // 触发本地事件
        EventService.Instance?.Publish(new NetworkDisconnectedEvent());
    }

    #endregion

    #region NGO 回调处理（桥接给 EventService）

    private void OnServerStarted()
    {
        Debug.Log("[NetworkService] 服务器已启动。");
        
        // 如果是 Host 模式，服务器启动后，本地客户端也会紧接着触发 OnClientConnected
        if (IsHost)
        {
            State = NetworkState.Connected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        // 如果是本地客户端连接成功
        if (clientId == LocalClientId)
        {
            Debug.Log($"[NetworkService] 本地客户端 {clientId} 连接成功。");
            State = NetworkState.Connected;
            EventService.Instance?.Publish(new NetworkConnectedEvent());
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        // 如果是本地客户端断开连接
        if (clientId == LocalClientId)
        {
            Debug.Log($"[NetworkService] 本地客户端 {clientId} 已断开。");
            State = NetworkState.Disconnected;
            EventService.Instance?.Publish(new NetworkDisconnectedEvent());
        }
    }

    #endregion
    
    // 在 NetworkService.cs 中加入这个方法：

    /// <summary>
    /// 注册网络预制体到 NGO，并桥接到对象池
    /// </summary>
    public void RegisterNetworkPrefab(GameObject prefab)
    {
        if (NetworkManager.Singleton == null) return;

        var netObj = prefab.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError($"[NetworkService] {prefab.name} 上没有 NetworkObject 组件！");
            return;
        }

        // ★ 纯净版：只把 Prefab 加入 NGO 列表，不再搞对象池拦截
        NetworkManager.Singleton.AddNetworkPrefab(prefab);
    
        Debug.Log($"[NetworkService] 已注册网络 Prefab: {prefab.name}");
    }
}

#region 网络事件定义（可以直接放到你的 Events.cs 里）
public readonly struct NetworkConnectedEvent { }
public readonly struct NetworkDisconnectedEvent { }
#endregion