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
    public class BeforeAttackEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnBeforeAttack;
        public Entity target;
        public float multiplyer = 1f;
        public float defPenetrate;
        public float mgrPenetrate;
        public float defPenetrate_value;
        public float mgrPenetrate_value;
        public int cumbo = 1;
        public int damageType;
        public int applyType;
    }
    public class AfterAttackEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAfterAttack;
        public Entity target;
        public float multiplyer;
        public float defPenetrate;
        public float mgrPenetrate;
        public float defPenetrate_value;
        public float mgrPenetrate_value;
        public int damageType;
        public int applyType;
        public bool isDeadly;
    }
    public class BeforeTakeDamageEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnBeforeTakeDamage;
        public Entity target;
        public float multiplyer = 1f;
        public float defPenetrate;
        public float mgrPenetrate;
        public float defPenetrate_value;
        public float mgrPenetrate_value;
        public int damageType;
        public int applyType;
    }
    public class AfterTakeDamageEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAfterTakeDamage;
        public Entity target;
        public float multiplyer;
        public float defPenetrate;
        public float mgrPenetrate;
        public float defPenetrate_value;
        public float mgrPenetrate_value;
        public int damageType;
        public int applyType;
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
    public class BeforeHurtEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnBeforeHurt;
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
    public class AfterHurtEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAfterHurt;
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
    public class AttackAnimBeginEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAttackAnimBegin;
    }
    public class BeforeDieAnimationEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnBeforeDieAnimation;
    }
    public class SkillBeginEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnSkillBegin;
        public SkillRuntime skill;
    }
    public class SkillEndEvent : SkillEvent
    {
        public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnSkillEnd;
        public SkillRuntime skill;
    }
}
