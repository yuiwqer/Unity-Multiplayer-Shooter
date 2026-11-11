// SceneService.cs
// 场景加载服务：异步加载、进度回调、切场景前的资源/模块清理、切场景后的模块重建。
//
// 设计要点：
//   1. 切场景的完整流程：
//        LockLoading()
//        → ModuleService.DisposeAllHandlers()   回收旧场景 Handler
//        → PoolService.ClearAll()               清池
//        → 打开 Loading UI（若配置）
//        → SceneManager.LoadSceneAsync(..., Single)
//        → 等 progress >= 0.9 后 allowSceneActivation = true
//        → 等 isDone
//        → ModuleService.RebuildHandlers()      为新场景重建 Handler
//        → 派发 SceneLoadedEvent
//        → 关闭 Loading UI
//        → UnlockLoading()
//   2. Loading UI 通过 UIService 打开，约定面板名 "Loading"，并实现 ILoadingPanel 接口。
//   3. 并发保护：加载中再调用 LoadScene 会被拒绝并打日志。
//   4. 单例跨场景常驻，加载协程放在本 Service 上，切场景不会中断。
//
// 调用方：
//   SceneService.Instance.LoadScene("GameScene");
//   // 或
//   StartCoroutine(SceneService.Instance.LoadSceneAsync("GameScene", p => Debug.Log(p)));
//
// 依赖：ModuleService、PoolService、UIService、EventService。
// 需在 ModuleService 里补 RebuildHandlers 方法（见文末补充）。

using System;
using System.Collections;
using Game.Events;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Loading 面板必须实现的接口，SceneService 通过它更新进度。</summary>
public interface ILoadingPanel
{
    void SetProgress(float progress);   // 0~1
    void SetStatus(string status);
}

public class SceneService : MonoBehaviour
{
    #region 单例

    private static SceneService _instance;
    private static readonly object _lock = new object();
    private static bool _quitting;

    public static SceneService Instance
    {
        get
        {
            if (_quitting) return null;

            lock (_lock)
            {
                if (_instance != null) return _instance;

                _instance = FindExisting();
                if (_instance != null) return _instance;

                var go = new GameObject($"[{nameof(SceneService)}]");
                _instance = go.AddComponent<SceneService>();
                DontDestroyOnLoad(go);
                return _instance;
            }
        }
    }

    private static SceneService FindExisting()
    {
#if UNITY_2023_1_OR_NEWER
        return FindFirstObjectByType<SceneService>(FindObjectsInactive.Include);
#else
        return FindObjectOfType<SceneService>();
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
    }

    void OnApplicationQuit() => _quitting = true;

    void OnDestroy()
    {
        if (_instance != this) return;
        _instance = null;
    }

    #endregion

    #region 状态

    [Header("Loading UI")]
    [Tooltip("Loading 面板在 UIPanelConfigSO 中的 panelName。为空则不显示 Loading UI。")]
    [SerializeField] private string loadingPanelName = "loading";

    /// <summary>当前场景名。</summary>
    public string CurrentSceneName { get; private set; }

    /// <summary>是否正在加载场景。加载中再次调用 LoadScene 会被拒绝。</summary>
    public bool IsLoading { get; private set; }

    /// <summary>当前加载进度（0~1）。非加载中为 0。</summary>
    public float CurrentProgress { get; private set; }

    /// <summary>加载完成回调（可选）。</summary>
    public event Action<string> OnLoadCompleted;

    #endregion

    #region 加载入口

    /// <summary>
    /// 启动场景加载（非阻塞，内部起协程）。
    /// 返回 false 表示已在加载中或参数非法。
    /// </summary>
    public bool LoadScene(string sceneName, Action<float> onProgress = null)
    {
        if (IsLoading)
        {
            Debug.LogWarning($"[SceneService] 正在加载 {CurrentSceneName}，忽略对 {sceneName} 的请求。");
            return false;
        }
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[SceneService] sceneName 为空。");
            return false;
        }

        StartCoroutine(LoadSceneAsync(sceneName, onProgress));
        return true;
    }

    /// <summary>
    /// 场景加载协程。供调用方 StartCoroutine 或自行 yield。
    /// 注意：本方法内部会切换 IsLoading，无需外层重复检查。
    /// </summary>
    public IEnumerator LoadSceneAsync(string sceneName, Action<float> onProgress = null)
    {
        if (IsLoading)
        {
            Debug.LogWarning($"[SceneService] 正在加载中，忽略对 {sceneName} 的请求。");
            yield break;
        }

        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[SceneService] sceneName 为空。");
            yield break;
        }

        IsLoading = true;
        CurrentProgress = 0f;

        // ── 阶段 1：切场景前清理 ──
        try { ModuleService.Instance?.DisposeAllHandlers(); }
        catch (Exception e) { Debug.LogException(e); }

        // ── 阶段 2：打开 Loading UI ──
        ILoadingPanel loadingPanel = null;
        if (!string.IsNullOrEmpty(loadingPanelName) && UIService.Instance != null)
        {
            try
            {
                var panel = UIService.Instance.Open(loadingPanelName);
                if (panel is ILoadingPanel lp)
                {
                    loadingPanel = lp;
                    lp.SetProgress(0f);
                    lp.SetStatus($"Loading {sceneName}...");
                }
                else if (panel != null)
                {
                    Debug.LogWarning($"[SceneService] Loading 面板 {loadingPanelName} 未实现 ILoadingPanel。");
                }
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ── 阶段 3：异步加载场景 ──
        AsyncOperation op = null;
        try
        {
            op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (op == null)
            {
                Debug.LogError($"[SceneService] LoadSceneAsync 返回 null，场景 {sceneName} 未加入 Build Settings？");
                CleanupOnFailure(loadingPanel);
                yield break;
            }
            op.allowSceneActivation = false;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            CleanupOnFailure(loadingPanel);
            yield break;
        }

        // 等 progress 到 0.9（Unity 在 allowSceneActivation=false 时最多到 0.9）
        while (op.progress < 0.9f)
        {
            // 0.9 映射到 0~1 进度
            CurrentProgress = Mathf.Clamp01(op.progress / 0.9f);
            loadingPanel?.SetProgress(CurrentProgress);
            onProgress?.Invoke(CurrentProgress);
            yield return null;
        }

        // 到 0.9，通知 UI 满进度并激活场景
        CurrentProgress = 0.95f;
        loadingPanel?.SetProgress(CurrentProgress);
        onProgress?.Invoke(CurrentProgress);

        op.allowSceneActivation = true;

        // 等 isDone（激活后有 1 帧左右）
        while (!op.isDone)
            yield return null;

        // ── 阶段 4：新场景初始化 ──
        CurrentSceneName = sceneName;
        CurrentProgress = 1f;
        loadingPanel?.SetProgress(1f);
        loadingPanel?.SetStatus("Ready");
        

        // 派发场景加载完成事件
        try { EventService.Instance?.Publish(new SceneLoadedEvent(sceneName)); }
        catch (Exception e) { Debug.LogException(e); }

        OnLoadCompleted?.Invoke(sceneName);

        // 关闭 Loading UI
        if (!string.IsNullOrEmpty(loadingPanelName) && UIService.Instance != null)
        {
            try { UIService.Instance.Close(loadingPanelName); }
            catch (Exception e) { Debug.LogException(e); }
        }

        IsLoading = false;
    }

    private void CleanupOnFailure(ILoadingPanel loadingPanel)
    {
        IsLoading = false;
        CurrentProgress = 0f;
        if (!string.IsNullOrEmpty(loadingPanelName) && UIService.Instance != null)
        {
            try { UIService.Instance.Close(loadingPanelName); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    #endregion

    #region 调试

    public void DumpState()
    {
        Debug.Log($"[SceneService] current={CurrentSceneName} loading={IsLoading} progress={CurrentProgress:F2}");
    }

    #endregion
}