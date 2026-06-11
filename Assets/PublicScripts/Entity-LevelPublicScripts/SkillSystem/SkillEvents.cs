namespace SkillSystem
{
    public class PreWarmEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnPreWarm;
    }
    public class InitializeEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnInitialize;
    }

    // Shared fields for events that carry a damage-magnitude payload (target + multipliers + types).
    public abstract class DamageEventBase : SkillEvent
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
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnBeforeAttack;
        public int cumbo;
    }
    public class AfterAttackEvent : DamageEventBase
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAfterAttack;
        public bool isDeadly;
    }
    public class BeforeTakeDamageEvent : DamageEventBase
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnBeforeTakeDamage;
    }
    public class AfterTakeDamageEvent : DamageEventBase
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAfterTakeDamage;
        public bool isDeadly;
    }
    public class AttackSuccessfullyEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAttackSuccessfully;
    }
    public class AttackInterruptEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAttackInterrupt;
    }

    // Shared fields for events that carry an incoming-damage payload (origin + damage + multipliers + types).
    public abstract class HurtEventBase : SkillEvent
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
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnBeforeHurt;
    }
    public class AfterHurtEvent : HurtEventBase
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAfterHurt;
    }
    public class AttackAnimBeginEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAttackAnimBegin;
    }
    public class BeforeDieAnimationEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnBeforeDieAnimation;
    }
    public class AbilityBeginEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAbilityBegin;
        public AbilityRuntime ability;
    }
    public class AbilityEndEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAbilityEnd;
        public AbilityRuntime ability;
    }
    public class AbilityAddedEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAbilityAdded;
        public AbilityRuntime ability;
    }
    public class AbilityRemovedEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAbilityRemoved;
        public AbilityRuntime ability;
    }
}
