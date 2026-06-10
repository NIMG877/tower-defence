namespace SkillSystem.Components
{
    [RegisterComponent("LockHpShield")]
    public class LockHpShieldComponent : ISkillComponent
    {
        private bool _active;
        private float _threshold;
        private float _selfDamage = 0f; // damage applied when shield saves the entity

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _active = p.GetBool("active", true);
            _threshold = p.GetFloat("threshold", 0f);
            _selfDamage = p.GetFloat("selfDamageOnSave", 0f);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (!_active) return;
            var bhe = (BeforeHurtEvent)ctx.currentEvent;
            if (ctx.entity == null) return;
            float hp = ctx.entity.Stats.CurrentHp;
            float dmg = bhe.damage;
            if (hp - dmg < _threshold)
            {
                float actual = hp - _threshold;
                if (actual < 0) actual = 0;
                bhe.damage = actual;
                if (_selfDamage > 0f)
                    ctx.entity.TakeDamage(ctx.entity, _selfDamage, 1f, 0, 0, 0, 0, 3, 0);
            }
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
