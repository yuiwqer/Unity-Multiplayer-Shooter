// ModuleConfigSO.cs
// 配置驱动多态模块的 SO 基类。
//
// 设计要点：
//   1. 以 moduleName 作为唯一 id，ModuleService 用它建立 name → SO 的查找表。
//   2. 每个 SO 资产绑定一个 Handler 类型 + 一个 Prefab，实例化后注入 SO 给 Handler。
//   3. 提供显式生命周期方法（OnConfigLoaded / OnConfigUnloaded），
//      由 ModuleService 在加载/卸载时调用；不要依赖 SO 的 OnEnable/OnDisable
//      （它们在编辑器下行为不稳定，触发时机依赖资产是否被引用）。
//   4. HandlerType 抽象属性实现“类型多态”：Service 不关心具体子类，
//      根据 SO 返回的 Type 实例化对应 Handler。
//   5. 子类通过 [CreateAssetMenu] 创建独立资产，天然回避 Unity 多态序列化的坑。
//
// 使用约定：
//   - SO 是项目资产，不要持有场景对象引用。
//   - OnConfigLoaded 中建立运行时数据结构（字典、缓存等），
//     OnConfigUnloaded 中清理，避免污染编辑器数据。
//   - 所有业务状态（冷却、进度等）应由 Handler 持有，SO 保持只读配置职责。

using System;
using UnityEngine;
using UnityEngine.Serialization;

public abstract class ModuleConfigSO : ScriptableObject
{
    #region 基础配置

    [Header("模块标识")]
    [Tooltip("模块唯一 id。ModuleService 以此为 key 建立 name → SO 查找表。不允许为空或重复。")]
    [SerializeField] private string moduleName;

    [Header("Handler 绑定")]
    [Tooltip("Handler 所在的 Prefab。为 null 时该模块不会被 Service 实例化。")]
    [SerializeField] private GameObject prefab;

    [Tooltip("Handler 组件类型（可选）。为 null 时 Service 会在 Prefab 上查找第一个 ModuleHandler 子类。")]
    [SerializeField] private string handlerTypeName;

    #endregion

    #region 运行时访问

    /// <summary>模块唯一 id（即 name）。</summary>
    public string ModuleName => moduleName;

    /// <summary>Handler 所在的 Prefab。</summary>
    public GameObject Prefab => prefab;

    /// <summary>Handler 的运行时类型。优先用序列化字符串解析，避免子类每处都 override。</summary>
    public virtual Type HandlerType
    {
        get
        {
            if (string.IsNullOrEmpty(handlerTypeName)) return null;
            return Type.GetType(handlerTypeName);
        }
    }

    #endregion

    #region 编辑器校验

#if UNITY_EDITOR
    protected virtual void OnValidate()
    {
        // 子类可以 override 补充自定义校验，务必调用 base
        if (string.IsNullOrWhiteSpace(moduleName))
            Debug.LogWarning($"[{name}] ModuleConfigSO: moduleName 为空。", this);

        if (prefab == null)
            Debug.LogWarning($"[{name}] ModuleConfigSO: prefab 未指定。", this);
    }
#endif

    #endregion

    #region 生命周期（由 ModuleService 显式调用）

    /// <summary>
    /// 配置被加载后调用一次（ModuleService.Awake 阶段）。
    /// 在此处建立运行时数据结构、解析配置；不要在此处实例化任何场景对象。
    /// </summary>
    public virtual void OnConfigLoaded() { }

    /// <summary>
    /// 配置被卸载前调用一次（ModuleService.OnDestroy 阶段）。
    /// 用于清理 OnConfigLoaded 中建立的运行时状态。
    /// </summary>
    public virtual void OnConfigUnloaded() { }

    #endregion
}