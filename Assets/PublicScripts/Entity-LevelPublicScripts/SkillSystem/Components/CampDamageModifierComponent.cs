namespace SkillSystem.Components
{
    [RegisterComponent("CampDamageModifier")]
    public class CampDamageModifierComponent : ISkillComponent
    {
        private int _requiredCamp = 2;
        private float _multiplier = 1f;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _requiredCamp = p.GetInt("requiredCamp", 2);
            _multiplier = p.GetFloat("multiplier", 1f);
        }

        public void OnTrigger(SkillContext ctx)
        {
            var btd = (BeforeTakeDamageEvent)ctx.currentEvent;
            if (btd.target == null) return;
            // Camp is on the target's Movement.Camp or the attack origin; expose via Blackboard if needed
            if (ctx.blackboard != null && ctx.blackboard.Get<int>("attackerCamp") == _requiredCamp)
                btd.multiplyer = _multiplier;
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
