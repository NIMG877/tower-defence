namespace AbilitySystem.Components
{
    [RegisterComponent("SetAbnormalState")]
    public class SetAbnormalStateComponent : IAbilityComponent
    {
        private int _stateIndex;
        private bool _add = true;

        public void OnInit(AbilityContext ctx, ParamList p)
        {
            _stateIndex = p.GetInt("stateIndex", 0);
            _add = p.GetBool("add", true);
        }

        public void OnTrigger(AbilityContext ctx)
        {
            if (ctx.entity == null || ctx.entity.buffController == null) return;
            if (_add) ctx.entity.buffController.AddAbnormalState(-10f, _stateIndex);
            else ctx.entity.buffController.TryRemoveAbnormalState(_stateIndex);
        }

        public void OnTick(AbilityContext ctx, float dt) { }
        public void OnTeardown(AbilityContext ctx) { }
    }
}
