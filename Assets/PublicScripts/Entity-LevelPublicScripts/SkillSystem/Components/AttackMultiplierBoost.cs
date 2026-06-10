namespace SkillSystem.Components
{
    [RegisterComponent("AttackMultiplierBoost")]
    public class AttackMultiplierBoost : ISkillComponent
    {
        private float _multiplier = 1f;

        public void OnInit(SkillContext ctx, ParamList parameters)
        {
            _multiplier = parameters.GetFloat("multiplier", 1f);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (!(ctx.currentEvent is BeforeAttackEvent bae)) return;
            bae.multiplyer *= _multiplier;
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
