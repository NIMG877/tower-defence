namespace AbilitySystem.Components
{
    [RegisterComponent("SelfDamageOnEvent")]
    public class SelfDamageOnEventComponent : IAbilityComponent
    {
        private float _damage;
        private int _damageType = 3; // 3 = true damage in existing convention

        public void OnInit(AbilityContext ctx, ParamList p)
        {
            _damage = p.GetFloat("damage", 0f);
            _damageType = p.GetInt("damageType", 3);
        }

        public void OnTrigger(AbilityContext ctx)
        {
            var atd = (AfterTakeDamageEvent)ctx.currentEvent;
            if (!atd.isDeadly) return;
            if (ctx.entity == null) return;
            ctx.entity.TakeDamage(ctx.entity, _damage, 1f, 0, 0, 0, 0, _damageType, 0);
        }

        public void OnTick(AbilityContext ctx, float dt) { }
        public void OnTeardown(AbilityContext ctx) { }
    }
}
