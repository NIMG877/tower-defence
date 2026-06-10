namespace SkillSystem.Components
{
    [RegisterComponent("SelfDamageOnEvent")]
    public class SelfDamageOnEventComponent : ISkillComponent
    {
        private float _damage;
        private int _damageType = 3; // 3 = true damage in existing convention

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _damage = p.GetFloat("damage", 0f);
            _damageType = p.GetInt("damageType", 3);
        }

        public void OnTrigger(SkillContext ctx)
        {
            var atd = (AfterTakeDamageEvent)ctx.currentEvent;
            if (!atd.isDeadly) return;
            if (ctx.entity == null) return;
            ctx.entity.TakeDamage(ctx.entity, _damage, 1f, 0, 0, 0, 0, _damageType, 0);
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
