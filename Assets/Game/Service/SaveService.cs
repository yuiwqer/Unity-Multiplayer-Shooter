// SaveService.cs
// 存档服务：负责将游戏数据序列化为 JSON 并持久化，支持全局数据和模块级数据。
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// 全局存档数据结构（音量、最高分等）
[Serializable]
public class GlobalSaveData
{
    public float masterVolume = 1f;
    public float bgmVolume = 1f;
    public float sfxVolume = 1f;
    public int highScore = 0;
    public string lastSceneName = "main_menu";
}

// 业务实体存档接口。实现此接口的 Handler 可以被存档。
public interface ISaveable
{
    string SaveKey { get; }
    string SerializeState();
    void DeserializeState(string json);
}

public class SaveService : MonoBehaviour
{
    #region 单例
    private static SaveService _instance;
    private static readonly object _lock = new object();
    private static bool _quitting;

    public static SaveService Instance
    {
        get
        {
            if (_quitting) return null;
            lock (_lock)
            {
                if (_instance != null) return _instance;
                _instance = FindObjectOfType<SaveService>();
                if (_instance != null) return _instance;

                var go = new GameObject($"[{nameof(SaveService)}]");
                _instance = go.AddComponent<SaveService>();
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
    void OnApplicationQuit() { _quitting = true; SaveGlobalData(); }
    void OnDestroy() { if (_instance == this) _instance = null; }
    #endregion

    private string SaveFilePath => Path.Combine(Application.persistentDataPath, "global_save.json");
    public GlobalSaveData GlobalData { get; private set; } = new GlobalSaveData();

    // 当前场景需要持久化的实体：Key -> 数据
    private readonly Dictionary<string, string> _entityDataCache = new Dictionary<string, string>();

    void Start()
    {
        LoadGlobalData();
    }

    #region 全局数据读写

    public void LoadGlobalData()
    {
        if (File.Exists(SaveFilePath))
        {
            try
            {
                string json = File.ReadAllText(SaveFilePath);
                GlobalData = JsonUtility.FromJson<GlobalSaveData>(json);
                Debug.Log($"[SaveService] 全局存档加载成功：{SaveFilePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveService] 加载存档失败: {e.Message}");
                GlobalData = new GlobalSaveData();
            }
        }
        else
        {
            GlobalData = new GlobalSaveData();
        }
    }

    public void SaveGlobalData()
    {
        try
        {
            string json = JsonUtility.ToJson(GlobalData, true);
            File.WriteAllText(SaveFilePath, json);
            Debug.Log($"[SaveService] 全局存档已保存。");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveService] 保存存档失败: {e.Message}");
        }
    }

    #endregion

    #region 实体数据读写 (由 Handler 主动上报)

    /// <summary>
    /// 注册需要存档的实体。
    /// </summary>
    public void RegisterSaveable(ISaveable saveable)
    {
        if (saveable == null || string.IsNullOrEmpty(saveable.SaveKey)) return;
        _entityDataCache[saveable.SaveKey] = saveable.SerializeState();
    }

    /// <summary>
    /// 恢复实体数据。
    /// </summary>
    public void RestoreSaveable(ISaveable saveable)
    {
        if (saveable == null || string.IsNullOrEmpty(saveable.SaveKey)) return;
        if (_entityDataCache.TryGetValue(saveable.SaveKey, out var json))
        {
            saveable.DeserializeState(json);
        }
    }

    #endregion

    #region 调试工具
    [ContextMenu("Delete Save File")]
    public void DeleteSave()
    {
        if (File.Exists(SaveFilePath)) File.Delete(SaveFilePath);
        _entityDataCache.Clear();
        Debug.Log("[SaveService] 存档已删除。");
    }
    #endregion
}