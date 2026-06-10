namespace SkillSystem.Components
{
    [RegisterComponent("SetAttackCombo")]
    public class SetAttackCombo : ISkillComponent
    {
        private int _cumboSet = 1; // absolute value assigned to event.cumbo (default 1 = no change)

        public void OnInit(SkillContext ctx, ParamList parameters)
        {
            _cumboSet = parameters.GetInt("cumbo", 1);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (!(ctx.currentEvent is BeforeAttackEvent bae)) return;
            bae.cumbo = _cumboSet;
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
