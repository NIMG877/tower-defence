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
