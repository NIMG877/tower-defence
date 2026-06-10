namespace SkillSystem
{
    public class PreWarmEvent : SkillEvent { }
    public class InitializeEvent : SkillEvent { }
    public class BeforeAttackEvent : SkillEvent
    {
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
    public class AttackSuccessfullyEvent : SkillEvent { }
    public class AttackInterruptEvent : SkillEvent { }
    public class BeforeHurtEvent : SkillEvent
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
    public class AfterHurtEvent : SkillEvent
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
    public class AttackAnimBeginEvent : SkillEvent { }
    public class BeforeDieAnimationEvent : SkillEvent { }
    public class SkillBeginEvent : SkillEvent
    {
        public SkillRuntime skill;
    }
    public class SkillEndEvent : SkillEvent
    {
        public SkillRuntime skill;
    }
}
