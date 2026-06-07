namespace SkillSystem.Components
{
    [RegisterComponent("PlayAnimation")]
    public class PlayAnimationComponent : ISkillComponent
    {
        private int _targetState = 1; // 1 = Idle
        private bool _force = true;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _targetState = p.GetInt("targetState", 1);
            _force = p.GetBool("force", true);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (ctx.entity == null || ctx.entity.entityAM == null) return;
            ctx.entity.entityAM.TrySetState((EntityState)_targetState, _force);
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
