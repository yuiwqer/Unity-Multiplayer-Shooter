// Events.cs
// 全局事件定义集中地。
//
// 组织规范：
//   1. 所有跨模块通信的事件类集中放在本文件（或按领域拆成 Events.Combat.cs / Events.UI.cs 等部分类文件）。
//      集中放的好处：一眼能看全所有事件，避免“事件定义散落在各模块里、找不到发送方/接收方”。
//   2. 事件类用 readonly struct，不是 class：
//        - 零 GC（值类型，Publish 时按值传递，不产生堆分配）
//        - 不可变（防止接收方误改事件内容传播给后续订阅者）
//      如果事件包含引用类型字段（如 Unit 引用），struct 只保证字段不可重赋值，不保证引用对象不可变——这是可接受的，
//      事件本身就是"快照 + 引用"的语义。
//   3. 命名约定：XxxEvent。动词开头表示"命令"（ReloadCommandEvent），过去式表示"通知"（UnitDiedEvent）。
//   4. 不要给事件加方法。事件是纯数据载体，行为属于接收方。
//   5. 事件类型即 EventService 的 key，不要在不同命名空间下定义同名事件——会撞 key。
//
// 新增事件时：
//   - 在对应领域区块追加 struct
//   - 在发送方（Handler / Service）里 Publish
//   - 在接收方 OnModuleInitialize 里 Subscribe、OnModuleDispose 里 Unsubscribe

using UnityEngine;

namespace Game.Events
{
    #region 战斗 / 武器

    /// <summary>命令：请求装填。由输入系统或 UI 发送，WeaponHandler 接收。</summary>
    public readonly struct Events
    {
        /// <summary>发起装填的武器模块 id。为空表示"当前激活武器"。</summary>
        public readonly string WeaponModuleName;

        /// <summary>是否强制装填（忽略"弹匣未满"等条件）。</summary>
        public readonly bool Force;

        public Events(string weaponModuleName = null, bool force = false)
        {
            WeaponModuleName = weaponModuleName;
            Force = force;
        }
    }

    /// <summary>通知：某单位死亡。UnitHandler 发送，Buff/Skill/AI 等接收。</summary>
    public readonly struct UnitDiedEvent
    {
        public readonly int UnitId;
        public readonly string UnitConfigName;
        public readonly Vector3 DeathPosition;

        public UnitDiedEvent(int unitId, string unitConfigName, Vector3 deathPosition)
        {
            UnitId = unitId;
            UnitConfigName = unitConfigName;
            DeathPosition = deathPosition;
        }
    }

    /// <summary>通知：某单位受到伤害。用于伤害数字、受击特效、Buff 触发等。</summary>
    public readonly struct DamageAppliedEvent
    {
        public readonly int TargetUnitId;
        public readonly int SourceUnitId;
        public readonly float Amount;
        public readonly bool IsCritical;

        public DamageAppliedEvent(int targetUnitId, int sourceUnitId, float amount, bool isCritical)
        {
            TargetUnitId = targetUnitId;
            SourceUnitId = sourceUnitId;
            Amount = amount;
            IsCritical = isCritical;
        }
    }

    #endregion

    #region 技能 / Buff

    /// <summary>通知：技能释放完成（含冷却开始）。</summary>
    public readonly struct SkillCastEvent
    {
        public readonly string SkillModuleName;
        public readonly int CasterUnitId;
        public readonly int TargetUnitId;

        public SkillCastEvent(string skillModuleName, int casterUnitId, int targetUnitId)
        {
            SkillModuleName = skillModuleName;
            CasterUnitId = casterUnitId;
            TargetUnitId = targetUnitId;
        }
    }

    /// <summary>通知：Buff 到期或被驱散。</summary>
    public readonly struct BuffRemovedEvent
    {
        public readonly string BuffModuleName;
        public readonly int TargetUnitId;
        public readonly bool Expired;   // true=自然到期，false=被驱散/手动移除

        public BuffRemovedEvent(string buffModuleName, int targetUnitId, bool expired)
        {
            BuffModuleName = buffModuleName;
            TargetUnitId = targetUnitId;
            Expired = expired;
        }
    }

    #endregion

    #region 游戏流程

    /// <summary>通知：场景加载完成。SceneService 发送。</summary>
    public readonly struct SceneLoadedEvent
    {
        public readonly string SceneName;

        public SceneLoadedEvent(string sceneName)
        {
            SceneName = sceneName;
        }
    }

    /// <summary>通知：游戏暂停状态变化。</summary>
    public readonly struct GamePausedEvent
    {
        public readonly bool IsPaused;

        public GamePausedEvent(bool isPaused)
        {
            IsPaused = isPaused;
        }
    }

    public readonly struct AmmoChangedEvent
    {
        public readonly int CurrentAmmo;
        public readonly int MaxAmmo;
        public AmmoChangedEvent(int current, int max) { CurrentAmmo = current; MaxAmmo = max; }
    }
    #endregion
}