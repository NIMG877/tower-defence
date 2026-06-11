namespace AbilitySystem.Components
{
    [RegisterComponent("CampDamageModifier")]
    public class CampDamageModifierComponent : IAbilityComponent
    {
        private int _requiredCamp = 2;
        private float _multiplier = 1f;

        public void OnInit(AbilityContext ctx, ParamList p)
        {
            _requiredCamp = p.GetInt("requiredCamp", 2);
            _multiplier = p.GetFloat("multiplier", 1f);
        }

        public void OnTrigger(AbilityContext ctx)
        {
            var btd = (BeforeTakeDamageEvent)ctx.currentEvent;
            if (btd.target == null) return;
            // Camp is on the target's Movement.Camp or the attack origin; expose via Blackboard if needed.
            if (ctx.sharedBlackboard != null && ctx.sharedBlackboard.Get<int>("attackerCamp") == _requiredCamp)
                btd.multiplyer = _multiplier;
        }

        public void OnTick(AbilityContext ctx, float dt) { }
        public void OnTeardown(AbilityContext ctx) { }
    }
}
