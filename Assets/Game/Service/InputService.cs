// InputService.cs
// 输入服务：封装旧版 Input Manager，提供动作映射和按键查询。
using System;
using System.Collections.Generic;
using UnityEngine;

public class InputService : MonoBehaviour
{
    #region 单例
    private static InputService _instance;
    private static readonly object _lock = new object();
    private static bool _quitting;

    public static InputService Instance
    {
        get
        {
            if (_quitting) return null;
            lock (_lock)
            {
                if (_instance != null) return _instance;
                _instance = FindObjectOfType<InputService>();
                if (_instance != null) return _instance;

                var go = new GameObject($"[{nameof(InputService)}]");
                _instance = go.AddComponent<InputService>();
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
        LoadBindings();
    }
    void OnApplicationQuit() => _quitting = true;
    void OnDestroy() { if (_instance == this) _instance = null; }
    #endregion

    // 定义常用动作
    public enum Action
    {
        Fire,       // 开火
        Reload,     // 换弹
        Interact,   // 交互
        Pause,      // 暂停
        Skill1,     // 技能1
        Skill2      // 技能2
    }

    // 默认键位映射
    private readonly Dictionary<Action, KeyCode> _bindings = new Dictionary<Action, KeyCode>
    {
        { Action.Fire, KeyCode.Mouse0 },
        { Action.Reload, KeyCode.R },
        { Action.Interact, KeyCode.F },
        { Action.Pause, KeyCode.Escape },
        { Action.Skill1, KeyCode.Q },
        { Action.Skill2, KeyCode.E }
    };

    // 协程用：等待按键按下的缓存
    private Action _waitingRebindAction = default;

    #region 输入查询 API

    /// <summary>按住（持续触发，比如自动步枪）</summary>
    public bool GetButton(Action action) => Input.GetKey(_bindings[action]);

    /// <summary>按下（单次触发，比如开一枪、跳跃）</summary>
    public bool GetButtonDown(Action action) => Input.GetKeyDown(_bindings[action]);

    /// <summary>抬起（比如松开蓄力）</summary>
    public bool GetButtonUp(Action action) => Input.GetKeyUp(_bindings[action]);
    
    /// <summary>获取水平轴 (-1 到 1，对应 A/D)</summary>
    public float GetHorizontalAxis() => Input.GetAxisRaw("Horizontal");

    /// <summary>获取垂直轴 (-1 到 1，对应 W/S)</summary>
    public float GetVerticalAxis() => Input.GetAxisRaw("Vertical");

    /// <summary>获取鼠标横向移动速度 (用于旋转视角)</summary>
    public float GetMouseX() => Input.GetAxis("Mouse X");

    /// <summary>获取鼠标纵向移动速度 (用于上下看)</summary>
    public float GetMouseY() => Input.GetAxis("Mouse Y");

    #endregion

    #region 改键 API

    /// <summary>
    /// 开始监听改键。在 Update 里检测玩家按下了哪个键，然后保存。
    /// 用法：InputService.Instance.StartRebind(InputService.Action.Fire);
    /// </summary>
    public void StartRebind(Action action)
    {
        _waitingRebindAction = action;
        EventService.Instance.Publish(new StartRebindEvent());
    }

    void Update()
    {
        if (_waitingRebindAction == default) return;

        // 遍历所有可能的按键（这里为了简单，只演示了鼠标和部分键盘）
        foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
        {
            if (Input.GetKeyDown(key))
            {
                // 如果按下了 Escape，取消改键
                if (key == KeyCode.Escape) 
                {
                    _waitingRebindAction = default;
                    return;
                }

                _bindings[_waitingRebindAction] = key;
                SaveBindings();
                
                Debug.Log($"[InputService] 动作 {_waitingRebindAction} 已绑定至 {key}");
                _waitingRebindAction = default;
                break;
            }
        }
    }

    private void SaveBindings()
    {
        foreach (var kv in _bindings)
            PlayerPrefs.SetString($"input.{kv.Key}", kv.Value.ToString());
        PlayerPrefs.Save();
    }

    private void LoadBindings()
    {
        foreach (var action in Enum.GetValues(typeof(Action)))
        {
            var saved = PlayerPrefs.GetString($"input.{action}", "");
            if (!string.IsNullOrEmpty(saved) && Enum.TryParse(saved, out KeyCode key))
                _bindings[(Action)action] = key;
        }
    }

    #endregion
}

// 事件定义（放到你的事件文件里）
public readonly struct StartRebindEvent { }