// UIService.cs
// UI 面板管理服务：加载配置、打开/关闭面板、层级管理、缓存、弹窗栈、每帧驱动。
//
// 设计要点：
//   1. 配置从 "Configs/UI" 独立加载，不走 ModuleService。
//   2. Canvas、EventSystem、层级容器由 UIService 在初始化时自动创建并常驻（DontDestroyOnLoad）。
//   3. 面板缓存策略由 UIPanelConfigSO.CacheOnClose 决定，缓存的 UI 统一放在隐藏的 [UICache] 节点下。
//   4. Popup 层支持栈：Open 时入栈，Close 时出栈，CloseTop() 关闭栈顶。
//   5. 每帧驱动：UIService.Update 遍历所有打开中的 Panel，调 OnPanelUpdate(dt)。

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIService : MonoBehaviour
{
    #region 单例

    private static UIService _instance;
    private static readonly object _lock = new object();
    private static bool _quitting;

    public static UIService Instance
    {
        get
        {
            if (_quitting) return null;

            lock (_lock)
            {
                if (_instance != null) return _instance;

                _instance = FindExisting();
                if (_instance != null) return _instance;

                var go = new GameObject($"[{nameof(UIService)}]");
                _instance = go.AddComponent<UIService>();
                DontDestroyOnLoad(go);
                return _instance;
            }
        }
    }

    private static UIService FindExisting()
    {
#if UNITY_2023_1_OR_NEWER
        return FindFirstObjectByType<UIService>(FindObjectsInactive.Include);
#else
        return FindObjectOfType<UIService>();
#endif
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

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

    private Canvas _canvas;
    private Transform _cacheRoot;
    private readonly Dictionary<UILayer, RectTransform> _layerRoots = new Dictionary<UILayer, RectTransform>();
    private readonly Dictionary<string, UIPanelConfigSO> _configMap = new Dictionary<string, UIPanelConfigSO>();
    private readonly Dictionary<string, UIPanel> _openPanels = new Dictionary<string, UIPanel>();
    private readonly Dictionary<string, UIPanel> _cachedPanels = new Dictionary<string, UIPanel>();

    private readonly Stack<string> _popupStack = new Stack<string>();
    private readonly List<UIPanel> _activePanels = new List<UIPanel>();
    private readonly Dictionary<string, GameObject> _modalMasks = new Dictionary<string, GameObject>();

    #endregion

    #region 初始化

    private void TryInitialize()
    {
        if (_initialized) return;
        if (ResourceService.Instance == null)
        {
            Debug.LogWarning("[UIService] ResourceService 未就绪，初始化延后。");
            return;
        }

        EnsureCanvasAndEventSystem();
        LoadConfigs();
        _initialized = true;
    }

    private void EnsureCanvasAndEventSystem()
    {
        // 1. 创建常驻 Canvas
        var go = new GameObject("[UICanvas]");
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 0;
    
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    
        go.AddComponent<GraphicRaycaster>();
        DontDestroyOnLoad(go);

        // 2. 解决 EventSystem 丢失问题（旧版输入系统）
        // 首先，强行清理场景里可能残留的废品 EventSystem（防止出现两个打架）
        var existingSystems = FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
        foreach (var existing in existingSystems)
        {
            if (existing != null && !existing.gameObject.name.Contains("DontDestroy")) 
            {
                Destroy(existing.gameObject);
            }
        }

        // 然后，绝对强制创建一个带 DontDestroyOnLoad 的 EventSystem
        var es = new GameObject("[EventSystem]");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>(); // 确保旧版输入模块在
        DontDestroyOnLoad(es);

        // 3. 创建层级容器
        _layerRoots.Clear();
        foreach (UILayer layer in Enum.GetValues(typeof(UILayer)))
        {
            var layerGo = new GameObject($"[{layer}]");
            var rt = layerGo.AddComponent<RectTransform>();
            rt.SetParent(_canvas.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _layerRoots[layer] = rt;
        }

        // 4. 创建用于缓存隐藏 UI 的根节点
        var cacheGo = new GameObject("[UICache]");
        _cacheRoot = cacheGo.transform;
        _cacheRoot.SetParent(_canvas.transform, false);
        cacheGo.SetActive(false); 
    }

    private void LoadConfigs()
    {
        var configs = ResourceService.Instance.LoadAll<UIPanelConfigSO>("Configs/UI");
        if (configs == null || configs.Length == 0)
        {
            Debug.LogWarning("[UIService] Resources/Configs/UI 下未找到 UIPanelConfigSO。");
            return;
        }

        foreach (var cfg in configs)
        {
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.PanelName)) continue;
            
            var key = cfg.PanelName.Trim().ToLowerInvariant(); // 强制统一为小写
            if (_configMap.ContainsKey(key))
            {
                Debug.LogError($"[UIService] 面板名重复：{key}，后加载的被跳过。", cfg);
                continue;
            }
            _configMap[key] = cfg;
        }
    }

    #endregion

    #region 打开 / 关闭

    public T Open<T>(string panelName, object data = null) where T : UIPanel
    {
        return OpenInternal(panelName, data) as T;
    }

    public UIPanel Open(string panelName, object data = null)
    {
        return OpenInternal(panelName, data);
    }

    private UIPanel OpenInternal(string panelName, object data)
    {
        if (!_initialized)
        {
            Debug.LogError("[UIService] 未初始化就调用 Open。");
            return null;
        }

        if (string.IsNullOrEmpty(panelName)) return null;
        panelName = panelName.Trim().ToLowerInvariant(); // 强制小写匹配

        if (!_configMap.TryGetValue(panelName, out var cfg))
        {
            Debug.LogError($"[UIService] 未找到面板配置：{panelName}");
            return null;
        }

        // 已打开：再次 Open 视为刷新数据
        if (_openPanels.TryGetValue(panelName, out var openPanel))
        {
            openPanel.InternalOpen(cfg, data);
            return openPanel;
        }

        // 缓存命中：取出复用
        UIPanel panel;
        if (_cachedPanels.TryGetValue(panelName, out var cached) && cached != null)
        {
            _cachedPanels.Remove(panelName);
            panel = cached;
        }
        else
        {
            panel = CreatePanel(cfg);
            if (panel == null) return null;
        }

        // 挂到对应层级容器
        panel.transform.SetParent(_layerRoots[cfg.Layer], false);
        panel.gameObject.SetActive(true);

        // 模态遮罩
        if (cfg.Modal) CreateModalMask(cfg);

        panel.InternalOpen(cfg, data);
        _openPanels[panelName] = panel;
        _activePanels.Add(panel);

        // Popup 层入栈
        if (cfg.Layer == UILayer.Popup) _popupStack.Push(panelName);

        return panel;
    }

    private UIPanel CreatePanel(UIPanelConfigSO cfg)
    {
        if (cfg.Prefab == null)
        {
            Debug.LogError($"[UIService] {cfg.PanelName} 的 Prefab 为空。", cfg);
            return null;
        }

        var go = Instantiate(cfg.Prefab);
        var panel = go.GetComponent<UIPanel>();
        if (panel == null)
        {
            var childPanels = go.GetComponentsInChildren<UIPanel>(true);
            if (childPanels.Length > 0)
            {
                Debug.LogError($"[UIService] {cfg.PanelName} 的 Prefab 根节点没挂 UIPanel 子类，但在子节点 '{childPanels[0].name}' 上找到了 {childPanels[0].GetType().Name}。请把 UI 脚本移到根节点上。", go);
            }
            else
            {
                Debug.LogError($"[UIService] {cfg.PanelName} 的 Prefab 根节点没挂任何 UIPanel 子类。请检查是否继承了 UIPanel。", go);
            }
            Destroy(go);
            return null;
        }
        return panel;
    }

    public void Close(string panelName)
    {
        if (!_initialized || string.IsNullOrEmpty(panelName)) return;
        panelName = panelName.Trim().ToLowerInvariant();

        if (!_openPanels.TryGetValue(panelName, out var panel) || panel == null)
            return;

        _openPanels.Remove(panelName);
        _activePanels.Remove(panel);

        panel.InternalClose();

        // 移除模态遮罩
        if (_modalMasks.TryGetValue(panelName, out var mask))
        {
            if (mask != null) Destroy(mask);
            _modalMasks.Remove(panelName);
        }

        // Popup 栈清理
        if (panel.Config != null && panel.Config.Layer == UILayer.Popup)
            RemoveFromPopupStack(panelName);

        if (panel.Config != null && panel.Config.CacheOnClose)
        {
            panel.gameObject.SetActive(false);
            // 挂到专门的缓存节点，避免挂在 UIService 自身导致销毁时报错
            panel.transform.SetParent(_cacheRoot, false); 
            _cachedPanels[panelName] = panel;
        }
        else
        {
            panel.InternalDispose();
            Destroy(panel.gameObject);
        }
    }

    public void CloseTop()
    {
        while (_popupStack.Count > 0)
        {
            var top = _popupStack.Peek();
            if (_openPanels.ContainsKey(top))
            {
                Close(top);
                return;
            }
            _popupStack.Pop();
        }
    }

    private void RemoveFromPopupStack(string panelName)
    {
        var temp = new List<string>(_popupStack.ToArray());
        temp.Reverse();
        temp.Remove(panelName);
        _popupStack.Clear();
        foreach (var n in temp) _popupStack.Push(n);
    }

    #endregion

    #region 模态遮罩

    private void CreateModalMask(UIPanelConfigSO cfg)
    {
        var maskGo = new GameObject($"[Mask:{cfg.PanelName}]");
        var rt = maskGo.AddComponent<RectTransform>();
        rt.SetParent(_layerRoots[cfg.Layer], false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var image = maskGo.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.5f);
        image.raycastTarget = true; // 拦截下层输入

        // 确保遮罩在面板下面
        maskGo.transform.SetAsFirstSibling();

        if (cfg.CloseOnMaskClick)
        {
            var btn = maskGo.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => Close(cfg.PanelName));
        }

        _modalMasks[cfg.PanelName] = maskGo;
    }

    #endregion

    #region 查询与驱动

    public bool IsOpen(string panelName)
    {
        if (string.IsNullOrEmpty(panelName)) return false;
        return _openPanels.ContainsKey(panelName.Trim().ToLowerInvariant());
    }

    void Update()
    {
        if (!_initialized || _activePanels.Count == 0) return;

        var dt = Time.deltaTime;
        for (int i = _activePanels.Count - 1; i >= 0; i--)
        {
            var p = _activePanels[i];
            if (p == null) { _activePanels.RemoveAt(i); continue; }
            if (!p.IsOpen) continue;
            p.InternalUpdate(dt);
        }
    }

    #endregion

    #region 清理

    private void Teardown()
    {
        // 退出时直接销毁，不触发业务 Close，防止在销毁期挂载节点报错
        foreach (var p in _openPanels.Values)
            if (p != null) Destroy(p.gameObject);
        _openPanels.Clear();

        foreach (var p in _cachedPanels.Values)
            if (p != null) Destroy(p.gameObject);
        _cachedPanels.Clear();

        foreach (var mask in _modalMasks.Values)
            if (mask != null) Destroy(mask);
        _modalMasks.Clear();

        _activePanels.Clear();
        _popupStack.Clear();
        _configMap.Clear();
        _layerRoots.Clear();
        _initialized = false;
    }

    #endregion

    #region 调试

    public void DumpPanels()
    {
        Debug.Log($"[UIService] initialized={_initialized} configs={_configMap.Count} open={_openPanels.Count} cached={_cachedPanels.Count}");
        foreach (var kv in _openPanels)
            Debug.Log($"  · [OPEN] {kv.Key} ({kv.Value.GetType().Name}, layer={kv.Value.Config?.Layer})");
        foreach (var kv in _cachedPanels)
            Debug.Log($"  · [CACHED] {kv.Key} ({kv.Value.GetType().Name})");
    }

    #endregion
}