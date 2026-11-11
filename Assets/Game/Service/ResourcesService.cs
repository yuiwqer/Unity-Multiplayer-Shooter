// ResourceService.cs
// 资源加载抽象层。所有 SO、Prefab、音频、贴图的加载都经过此 Service。
//
// 设计约定：
//   1. 对外暴露接口 IResourceService，业务层只依赖接口，不依赖具体实现。
//   2. 默认实现基于 Resources，后期切 Addressables 只需新增一个 IResourceService 实现，
//      并把 ResourceService.Instance 的注册点替换掉即可，调用方零改动。
//   3. 内部对已加载对象做缓存，同路径重复 Load 不会重复 IO。
//   4. Prefab 通过 Instantiate 走缓存并返回实例；实例由调用方 Destroy/入池，
//      不要把实例交给 Release（Release 只针对资产本体）。
//   5. 单例跨场景常驻，切场景不自动清缓存；如果内存吃紧可显式调用 ClearCache。

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

#region 接口

public interface IResourceService
{
    /// <summary>同步加载单个资源，失败返回 null 并打错误日志。</summary>
    T Load<T>(string path) where T : Object;

    /// <summary>同步加载文件夹下全部指定类型资源，失败返回空数组。</summary>
    T[] LoadAll<T>(string folderPath) where T : Object;

    /// <summary>异步加载单个资源，加载完成回调 onLoaded（失败时传 null）。</summary>
    IEnumerator LoadAsync<T>(string path, Action<T> onLoaded) where T : Object;

    /// <summary>按路径加载 Prefab 并实例化。失败返回 null。</summary>
    GameObject Instantiate(string prefabPath);

    /// <summary>直接对已加载的 Prefab 实例化（走缓存判断），失败返回 null。</summary>
    GameObject Instantiate(GameObject prefab);

    /// <summary>释放单个资产本体（非场景实例）。对 GameObject 类资产为卸载 Prefab。</summary>
    void Release(Object asset);

    /// <summary>释放全部缓存资产。</summary>
    void ClearCache();
}

#endregion

public class ResourceService : MonoBehaviour, IResourceService
{
    #region 单例

    private static ResourceService _instance;
    private static readonly object _lock = new object();
    private static bool _quitting;

    public static IResourceService Instance
    {
        get
        {
            if (_quitting) return null;

            lock (_lock)
            {
                if (_instance != null) return _instance;

                _instance = FindExisting();
                if (_instance != null) return _instance;

                var go = new GameObject($"[{nameof(ResourceService)}]");
                _instance = go.AddComponent<ResourceService>();
                DontDestroyOnLoad(go);
                return _instance;
            }
        }
    }

    private static ResourceService FindExisting()
    {
#if UNITY_2023_1_OR_NEWER
        return FindFirstObjectByType<ResourceService>(FindObjectsInactive.Include);
#else
        return FindObjectOfType<ResourceService>();
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

    void OnApplicationQuit()
    {
        _quitting = true;
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
        ClearCache();
    }

    #endregion

    #region 缓存

    // path -> 已加载资产。key 使用规范化后的路径，避免 "Foo" 与 "Foo.prefab" 重复。
    private readonly Dictionary<string, Object> _cache = new Dictionary<string, Object>(128);

    private static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        return path.Replace('\\', '/').TrimStart('/');
    }

    private bool TryGetCached(string key, out Object asset)
    {
        if (_cache.TryGetValue(key, out asset) && asset != null)
            return true;

        // 缓存里被 Unity 卸载了（比如切场景后 UnloadUnusedAssets），移除失效项
        if (asset != null || _cache.ContainsKey(key))
            _cache.Remove(key);

        asset = null;
        return false;
    }

    #endregion

    #region 同步加载

    public T Load<T>(string path) where T : Object
    {
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError($"[ResourceService] Load<{typeof(T).Name}> 路径为空。");
            return null;
        }

        var key = Normalize(path);
        if (TryGetCached(key, out var cached))
            return cached as T;

        var asset = Resources.Load<T>(key);
        if (asset == null)
        {
            Debug.LogError($"[ResourceService] 加载失败：{key} (类型 {typeof(T).Name})");
            return null;
        }

        _cache[key] = asset;
        return asset;
    }

    public T[] LoadAll<T>(string folderPath) where T : Object
    {
        if (string.IsNullOrEmpty(folderPath))
        {
            Debug.LogError($"[ResourceService] LoadAll<{typeof(T).Name}> 路径为空。");
            return Array.Empty<T>();
        }

        // LoadAll 不做逐项缓存（语义是“整批”），但会把结果对象加入缓存，
        // 后续单路径 Load 命中同一对象时不会重复加载。
        var key = Normalize(folderPath);
        var assets = Resources.LoadAll<T>(key);
        if (assets == null || assets.Length == 0)
        {
            Debug.LogWarning($"[ResourceService] LoadAll<{typeof(T).Name}> 无结果：{key}");
            return Array.Empty<T>();
        }

        foreach (var a in assets)
        {
            if (a == null) continue;
            var assetPath = key + "/" + a.name;
            _cache[assetPath] = a;
        }
        return assets;
    }

    #endregion

    #region 异步加载

    public IEnumerator LoadAsync<T>(string path, Action<T> onLoaded) where T : Object
    {
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError($"[ResourceService] LoadAsync<{typeof(T).Name}> 路径为空。");
            onLoaded?.Invoke(null);
            yield break;
        }

        var key = Normalize(path);
        if (TryGetCached(key, out var cached))
        {
            onLoaded?.Invoke(cached as T);
            yield break;
        }

        var request = Resources.LoadAsync<T>(key);
        yield return request;

        var asset = request.asset as T;
        if (asset == null)
        {
            Debug.LogError($"[ResourceService] 异步加载失败：{key} (类型 {typeof(T).Name})");
            onLoaded?.Invoke(null);
            yield break;
        }

        _cache[key] = asset;
        onLoaded?.Invoke(asset);
    }

    #endregion

    #region 实例化

    public GameObject Instantiate(string prefabPath)
    {
        var prefab = Load<GameObject>(prefabPath);
        return prefab != null ? Object.Instantiate(prefab) : null;
    }

    public GameObject Instantiate(GameObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogError("[ResourceService] Instantiate 传入的 prefab 为 null。");
            return null;
        }
        return Object.Instantiate(prefab);
    }

    #endregion

    #region 释放

    public void Release(Object asset)
    {
        if (asset == null) return;

        // 从缓存中按引用移除
        string foundKey = null;
        foreach (var kv in _cache)
        {
            if (ReferenceEquals(kv.Value, asset))
            {
                foundKey = kv.Key;
                break;
            }
        }
        if (foundKey != null) _cache.Remove(foundKey);

        // Resources 下加载的资产用 UnloadAsset 卸载本体。
        // 注意：如果场景中仍有实例引用它（例如 MonoBehaviour 持有），卸载会失败或导致丢失，
        // 所以调用前应确认所有实例已销毁。
        if (asset is not GameObject)   // GameObject(Prefab) 通过 Destroy 实例即可，本体无需手动卸载
            Resources.UnloadAsset(asset);
    }

    public void ClearCache()
    {
        _cache.Clear();
        // 只清空缓存不强制 UnloadUnusedAssets；是否回收交给 GameBootstrap / 切场景时统一处理。
    }

    #endregion
}