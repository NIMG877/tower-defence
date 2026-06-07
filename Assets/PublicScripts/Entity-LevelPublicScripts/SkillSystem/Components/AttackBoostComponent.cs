namespace SkillSystem.Components
{
    [RegisterComponent("AttackBoost")]
    public class AttackBoostComponent : ISkillComponent
    {
        private float _multiplier = 1f;
        private int _cumboSet = 1; // absolute value assigned to event.cumbo (default 1 = no change)

        public void OnInit(SkillContext ctx, ParamList parameters)
        {
            _multiplier = parameters.GetFloat("multiplier", 1f);
            _cumboSet = parameters.GetInt("cumbo", 1);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (!(ctx.currentEvent is BeforeAttackEvent bae)) return;
            bae.multiplyer *= _multiplier;
            bae.cumbo = _cumboSet;
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
