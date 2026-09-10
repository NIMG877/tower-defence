using System.Collections.Generic;
using UnityEngine;

namespace AbilitySystem
{
    public class PreWarmEvent : AbilityEvent
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnPreWarm;
    }
    public class InitializeEvent : AbilityEvent
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnInitialize;
    }

    // Shared fields for events that carry a damage-magnitude payload (target + multipliers + types).
    public abstract class DamageEventBase : AbilityEvent
    {
        public Entity target;
        public float multiplyer;
        public float defPenetrate;
        public float mgrPenetrate;
        public float defPenetrate_value;
        public float mgrPenetrate_value;
        public int damageType;
        public int applyType;
    }

    public class BeforeAttackEvent : DamageEventBase
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnBeforeAttack;
        public int cumbo;
    }
    public class AfterAttackEvent : DamageEventBase
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnAfterAttack;
        public bool isDeadly;
    }
    public class BeforeTakeDamageEvent : DamageEventBase
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnBeforeTakeDamage;
    }
    public class AfterTakeDamageEvent : DamageEventBase
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnAfterTakeDamage;
        public bool isDeadly;
    }
    public class AttackSuccessfullyEvent : AbilityEvent
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnAttackSuccessfully;
    }
    public class AttackInterruptEvent : AbilityEvent
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnAttackInterrupt;
    }
    public class AttackIdleEvent : AbilityEvent
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnAttackIdle;
    }

    // 索敌候选确定后、数量裁剪前派发（EntityAttack.AttackTargetSelect 内经
    // EntityAbilityRunner 桥接）。targets 为候选列表本体（引用，订阅方可直接
    // 增删；write_blackboard 以 source=event path=targets 提取的是副本）；
    // 三个标量派发后回写。
    public class BeforeTargetSelectEvent : AbilityEvent
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnBeforeTargetSelect;
        public List<Entity> targets;
        public int selectMaxNum;
        public int selectMinNum;
        public bool sameComp;
    }

    // Shared fields for events that carry an incoming-damage payload (origin + damage + multipliers + types).
    public abstract class HurtEventBase : AbilityEvent
    {
        public Entity origin;
        public float damage;
        public float multiplyer;
        public float defPenetrate;
        public float mgrPenetrate;
        public float defPenetrate_value;
        public float mgrPenetrate_value;
        public int damageType;
        public int applyType;
        public bool isDeadly;
    }

    public class BeforeHurtEvent : HurtEventBase
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnBeforeHurt;
    }
    public class AfterHurtEvent : HurtEventBase
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnAfterHurt;
    }
    public class AttackAnimBeginEvent : AbilityEvent
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnAttackAnimBegin;
    }
    public class BeforeDieAnimationEvent : AbilityEvent
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnBeforeDieAnimation;
    }
    // 召唤物死亡，由宿主侧 WatchSummonDeath 桥接派发。target=死亡实体，
    // position=死亡时刻位置快照（延迟后实体可能已被池回收，勿再读实体现场）。
    public class SummonDeathEvent : AbilityEvent
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnSummonDeath;
        public Entity target;
        public Vector2 position;
    }
    // 子弹（视觉载弹）抵达目标点销毁时，由宿主侧 FireBullets 桥接派发。
    // position=销毁时刻实际落点（抛物线含随机偏移，非瞄准点）。
    public class BulletLandedEvent : AbilityEvent
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnBulletLanded;
        public Vector2 position;
    }
    public class TickEvent : AbilityEvent
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnTick;
        public float deltaTime;
    }
    public class AbilityBeginEvent : AbilityEvent
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnAbilityBegin;
        public AbilityRuntime ability;
    }
    public class AbilityEndEvent : AbilityEvent
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnAbilityEnd;
        public AbilityRuntime ability;
    }
    public class AbilityAddedEvent : AbilityEvent
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnAbilityAdded;
        public AbilityRuntime ability;
    }
    public class AbilityRemovedEvent : AbilityEvent
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnAbilityRemoved;
        public AbilityRuntime ability;
    }
}
