// UIPanelConfigSO.cs
// UI 面板配置。不继承 ModuleConfigSO，避免被 ModuleService.LoadAll<ModuleConfigSO> 误加载。
// 由 UIService 独立从 "Configs/UI" 目录加载。
//
// 命名约定：资产文件名与 panelName 一致（如 MainMenu.asset 的 panelName = "MainMenu"），
// 便于人工排查，但 UIService 以 panelName 为 id，不依赖文件名。

using UnityEngine;

/// <summary>UI 层级。决定面板在 Canvas 下的渲染顺序与交互优先级。</summary>
public enum UILayer
{
    /// <summary>背景层：主菜单背景、场景过场底图。不响应输入。</summary>
    Background = 0,
    /// <summary>常规层：HUD、主界面。可被 Popup 遮挡。</summary>
    Normal = 100,
    /// <summary>弹窗层：设置、暂停、确认框。模态时遮挡下层的输入。</summary>
    Popup = 200,
    /// <summary>顶层：提示、Toast、引导箭头。永远在最上。</summary>
    Top = 300,
    /// <summary>加载层：Loading 界面。全屏遮挡，最高。</summary>
    Loading = 400,
}

[CreateAssetMenu(fileName = "UIPanelConfig", menuName = "Game/Configs/UIPanel")]
public class UIPanelConfigSO : ScriptableObject
{
    [Header("标识")]
    [Tooltip("面板唯一 id。UIService 以此为 key 查找。不允许为空或重复。")]
    [SerializeField] private string panelName;

    [Tooltip("面板 Prefab。根节点上必须挂 UIPanel 子类。")]
    [SerializeField] private GameObject prefab;

    [Header("层级与行为")]
    [Tooltip("所属层级。决定渲染顺序与交互优先级。")]
    [SerializeField] private UILayer layer = UILayer.Normal;

    [Tooltip("关闭后是否缓存。true=隐藏保留，下次打开快；false=销毁，省内存。")]
    [SerializeField] private bool cacheOnClose = true;

    [Tooltip("是否模态。模态面板打开时，下层输入被拦截，点击遮罩可关闭（若配置）。")]
    [SerializeField] private bool modal = false;

    [Tooltip("点击遮罩是否关闭。仅 modal=true 时有效。")]
    [SerializeField] private bool closeOnMaskClick = true;

    #region 运行时访问

    public string PanelName => panelName;
    public GameObject Prefab => prefab;
    public UILayer Layer => layer;
    public bool CacheOnClose => cacheOnClose;
    public bool Modal => modal;
    public bool CloseOnMaskClick => closeOnMaskClick;

    #endregion

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(panelName))
            Debug.LogWarning($"[{name}] UIPanelConfigSO: panelName 为空。", this);
        if (prefab == null)
            Debug.LogWarning($"[{name}] UIPanelConfigSO: prefab 未指定。", this);
    }
#endif
}