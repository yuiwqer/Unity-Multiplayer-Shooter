// EventService.cs
// 全局事件总线：模块间通信的唯一标准通道。
// 用法：
//   EventService.Instance.Subscribe<MyEvent>(OnMyEvent);
//   EventService.Instance.Publish(new MyEvent { ... });
//   EventService.Instance.Unsubscribe<MyEvent>(OnMyEvent);
//
// 设计约定：
//   1. 事件类型即 key，不要用字符串事件名。
//   2. 订阅方必须在销毁/停用时 Unsubscribe，否则会持有已销毁对象的引用。
//   3. Publish 时对回调做快照并逐个 try-catch，一个回调抛异常不影响其余回调。
//   4. 单例生命周期跨场景（DontDestroyOnLoad），切场景不清理订阅；
//      业务模块应在 OnModuleDispose / OnDisable 中自行退订。

using System;
using System.Collections.Generic;
using UnityEngine;

public class EventService : MonoBehaviour
{
    #region 单例

    private static EventService _instance;
    private static readonly object _lock = new object();
    private static bool _quitting;

    public static EventService Instance
    {
        get
        {
            if (_quitting) return null;

            lock (_lock)
            {
                if (_instance != null) return _instance;

                // 场景里已有就直接用
                _instance = FindExisting();
                if (_instance != null) return _instance;

                // 没有就创建，并跨场景常驻
                var go = new GameObject($"[{nameof(EventService)}]");
                _instance = go.AddComponent<EventService>();
                DontDestroyOnLoad(go);
                return _instance;
            }
        }
    }

    private static EventService FindExisting()
    {
#if UNITY_2023_1_OR_NEWER
        return FindFirstObjectByType<EventService>(FindObjectsInactive.Include);
#else
        return FindObjectOfType<EventService>();
#endif
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            // 场景里已经有一个（比如从其他场景切换过来），销毁重复的
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnApplicationQuit()
    {
        // 退出时禁止再懒创建，避免 OnDestroy 阶段 new 出 GameObject 报错
        _quitting = true;
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
        _handlers.Clear();
    }

    #endregion

    #region 事件表

    // Type(事件类型) -> 该事件的委托链（Delegate.Combine 合并的 Action<T>）
    private readonly Dictionary<Type, Delegate> _handlers = new Dictionary<Type, Delegate>(64);

    #endregion

    #region 订阅 / 退订

    /// <summary>订阅事件。重复订阅同一回调会被调用多次，请自行保证成对调用。</summary>
    public void Subscribe<T>(Action<T> handler)
    {
        if (handler == null) return;

        var type = typeof(T);
        if (_handlers.TryGetValue(type, out var existing))
            _handlers[type] = Delegate.Combine(existing, handler);
        else
            _handlers[type] = handler;
    }

    /// <summary>退订事件。未订阅时调用无副作用。</summary>
    public void Unsubscribe<T>(Action<T> handler)
    {
        if (handler == null) return;

        var type = typeof(T);
        if (!_handlers.TryGetValue(type, out var existing)) return;

        var result = Delegate.Remove(existing, handler);
        if (result == null)
            _handlers.Remove(type);
        else
            _handlers[type] = result;
    }

    /// <summary>清空某类事件的所有订阅。</summary>
    public void Clear<T>()
    {
        _handlers.Remove(typeof(T));
    }

    /// <summary>清空全部订阅（一般只在退出或重载场景时使用）。</summary>
    public void ClearAll()
    {
        _handlers.Clear();
    }

    #endregion

    #region 派发

    /// <summary>
    /// 同步派发事件。回调异常会被捕获并打印，不会中断其他回调。
    /// 注意：不要在回调中假设执行顺序；同类型多回调按订阅顺序执行。
    /// </summary>
    public void Publish<T>(T evt)
    {
        if (!_handlers.TryGetValue(typeof(T), out var del)) return;
        if (del is not Action<T> callbacks) return;

        // 快照：防止回调中 Subscribe/Unsubscribe 导致遍历异常
        var invocationList = callbacks.GetInvocationList();
        for (int i = 0; i < invocationList.Length; i++)
        {
            try
            {
                ((Action<T>)invocationList[i]).Invoke(evt);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }

    #endregion

    #region 调试

    /// <summary>打印当前订阅情况，排查“事件没人收到”类问题。</summary>
    public void DumpSubscribers()
    {
        if (_handlers.Count == 0)
        {
            Debug.Log("[EventService] 无任何订阅。");
            return;
        }

        foreach (var kv in _handlers)
        {
            var list = kv.Value?.GetInvocationList();
            Debug.Log($"[EventService] {kv.Key.Name} 订阅数：{list?.Length ?? 0}");
        }
    }

    #endregion
}