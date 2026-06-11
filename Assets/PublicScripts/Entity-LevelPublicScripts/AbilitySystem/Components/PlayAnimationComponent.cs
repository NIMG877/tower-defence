namespace AbilitySystem.Components
{
    [RegisterComponent("PlayAnimation")]
    public class PlayAnimationComponent : IAbilityComponent
    {
        private int _targetState = 1; // 1 = Idle
        private bool _force = true;

        public void OnInit(AbilityContext ctx, ParamList p)
        {
            _targetState = p.GetInt("targetState", 1);
            _force = p.GetBool("force", true);
        }

        public void OnTrigger(AbilityContext ctx)
        {
            if (ctx.entity == null || ctx.entity.entityAM == null) return;
            ctx.entity.entityAM.TrySetState((EntityState)_targetState, _force);
        }

        public void OnTick(AbilityContext ctx, float dt) { }
        public void OnTeardown(AbilityContext ctx) { }
    }
}
