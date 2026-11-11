// UIPanel.cs
// 所有 UI 面板的基类。挂在面板 Prefab 的根节点上。
//
// 与 ModuleHandler 的关系：两者都是"挂在 Prefab 上、由 Service 驱动 Update"的对象，
// 但 UIPanel 不走 ModuleService，由 UIService 独立管理——UI 的层级、缓存、栈语义
// 和业务模块完全不同，强行统一反而把两套逻辑搅在一起。
//
// 生命周期：
//    OnPanelInitialize()  首次创建时调用一次（缓存命中不重调）
//    OnPanelOpen(data)    每次打开调用（含缓存复用）
//    OnPanelClose()       每次关闭调用
//    OnPanelUpdate(dt)    每帧（由 UIService 驱动，本类不实现 Unity Update）
//    OnPanelDispose()     真正销毁前调用一次
//
// 设计约定：
//   1. 不实现 Unity 的 Update()。每帧逻辑走 OnPanelUpdate(dt)，由 UIService 统一驱动。
//   2. 打开/关闭与缓存无关。CacheOnClose 只决定"关闭后销毁还是隐藏"，不影响业务感知——
//      业务层在 OnPanelClose 里做的清理必须每次关闭都执行。
//   3. 不要假设 OnPanelInitialize 和 OnPanelOpen 的先后一定紧邻。
//      缓存面板重新打开时，只有 OnPanelOpen，没有 OnPanelInitialize。
//   4. UIService 负责 SetActive 和层级，Panel 不要自己 SetActive 或改父节点。

using System;
using UnityEngine;

public abstract class UIPanel : MonoBehaviour
{
    #region 状态

    /// <summary>本面板的配置。Open 后必定非空，Dispose 后置 null。</summary>
    public UIPanelConfigSO Config { get; private set; }

    /// <summary>是否处于打开状态。关闭后仍可能因缓存而保留 GameObject。</summary>
    public bool IsOpen { get; private set; }

    /// <summary>面板名，等价于 Config.PanelName。日志与查找用。</summary>
    public string PanelName => Config != null ? Config.PanelName : name;

    private bool _initialized;

    #endregion

    #region 内部驱动（仅供 UIService 调用）

    internal void InternalOpen(UIPanelConfigSO config, object data)
    {
        if (config == null)
        {
            Debug.LogError($"[{GetType().Name}] InternalOpen 传入 config 为 null。", this);
            return;
        }

        Config = config;
        IsOpen = true;

        if (!_initialized)
        {
            _initialized = true;
            try { OnPanelInitialize(); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        try { OnPanelOpen(data); }
        catch (Exception e) { Debug.LogException(e, this); }
    }

    internal void InternalClose()
    {
        if (!IsOpen) return;
        IsOpen = false;

        try { OnPanelClose(); }
        catch (Exception e) { Debug.LogException(e, this); }
    }

    internal void InternalUpdate(float dt)
    {
        if (!IsOpen) return;
        try { OnPanelUpdate(dt); }
        catch (Exception e) { Debug.LogException(e, this); }
    }

    internal void InternalDispose()
    {
        if (IsOpen) InternalClose();

        try { OnPanelDispose(); }
        catch (Exception e) { Debug.LogException(e, this); }

        _initialized = false;
        Config = null;
    }

    #endregion

    #region 业务生命周期

    /// <summary>首次创建时调用一次。在此做一次性初始化：绑定按钮、查找子节点。</summary>
    protected virtual void OnPanelInitialize() { }

    /// <summary>每次打开调用。data 为调用方 Open 时传入的参数，可能为 null。</summary>
    protected virtual void OnPanelOpen(object data) { }

    /// <summary>每次关闭调用。无论是否缓存，都必须在此清理本次打开产生的状态。</summary>
    protected virtual void OnPanelClose() { }

    /// <summary>每帧调用。默认空实现，按需 override。</summary>
    protected virtual void OnPanelUpdate(float deltaTime) { }

    /// <summary>真正销毁前调用一次。在此释放非池管理的资源。</summary>
    protected virtual void OnPanelDispose() { }

    #endregion
    #region 服务访问辅助

    /// <summary>便捷方法：向 EventService 订阅。建议只在 OnPanelInitialize 中调用。</summary>
    protected void Subscribe<T>(Action<T> handler)
    {
        EventService.Instance?.Subscribe(handler);
    }

    /// <summary>便捷方法：从 EventService 退订。建议只在 OnPanelDispose 中调用。</summary>
    protected void Unsubscribe<T>(Action<T> handler)
    {
        EventService.Instance?.Unsubscribe(handler);
    }

    #endregion
}