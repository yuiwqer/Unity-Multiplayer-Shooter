using UnityEngine;

public class CameraService : MonoBehaviour
{
    #region 单例
    private static CameraService _instance;
    private static readonly object _lock = new object();
    private static bool _quitting;

    public static CameraService Instance
    {
        get
        {
            if (_quitting) return null;
            lock (_lock)
            {
                if (_instance != null) return _instance;
                _instance = FindObjectOfType<CameraService>();
                if (_instance != null) return _instance;

                var go = new GameObject($"[{nameof(CameraService)}]");
                _instance = go.AddComponent<CameraService>();
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

    public Camera MainCamera { get; private set; }

    // 震动状态
    private float _shakeTime = 0f;
    private float _shakeMagnitude = 0f;
    private Vector3 _originalLocalPos;
    private bool _isShaking = false;

    /// <summary>
    /// 绑定主相机（在场景初始化或玩家生成时调用）
    /// </summary>
    public void RegisterMainCamera(Camera cam)
    {
        MainCamera = cam;
    }

    /// <summary>
    /// 获取屏幕中心发射的射线（准星射线），用于射击判定
    /// </summary>
    public Ray GetCenterScreenRay()
    {
        if (MainCamera != null)
            return MainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)); // 屏幕正中心
        else
            return new Ray(Vector3.zero, Vector3.forward);
    }

    /// <summary>
    /// 触发相机震动
    /// </summary>
    /// <param name="duration">持续时间</param>
    /// <param name="magnitude">震动幅度</param>
    public void Shake(float duration, float magnitude)
    {
        if (MainCamera == null) return;
        
        // 如果当前没有在震动，记录初始位置
        if (!_isShaking)
        {
            _originalLocalPos = MainCamera.transform.localPosition;
            _isShaking = true;
        }

        _shakeTime = Mathf.Max(_shakeTime, duration);
        _shakeMagnitude = Mathf.Max(_shakeMagnitude, magnitude);
    }

    void LateUpdate()
    {
        if (MainCamera == null || !_isShaking) return;

        if (_shakeTime > 0)
        {
            _shakeTime -= Time.deltaTime;

            // 使用 Perlin Noise 获得平滑的随机震动
            float shakeX = (Mathf.PerlinNoise(Time.time * 25f, 0f) - 0.5f) * 2f * _shakeMagnitude;
            float shakeY = (Mathf.PerlinNoise(0f, Time.time * 25f) - 0.5f) * 2f * _shakeMagnitude;

            // 在当前基础位置叠加震动偏移
            MainCamera.transform.localPosition = _originalLocalPos + new Vector3(shakeX, shakeY, 0f);
        }
        else
        {
            // 震动结束，恢复原始位置
            MainCamera.transform.localPosition = _originalLocalPos;
            _shakeMagnitude = 0f;
            _isShaking = false;
        }
    }
}