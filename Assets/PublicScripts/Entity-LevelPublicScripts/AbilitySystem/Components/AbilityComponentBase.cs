namespace AbilitySystem.Components
{
    /// <summary>
    /// Base class for ability components. <c>OnInit</c> and <c>OnTrigger</c>
    /// are abstract (each component is defined by them); <c>OnTick</c> and
    /// <c>OnTeardown</c> have empty virtual defaults so most components don't
    /// have to write the empty bodies. The runner's per-frame <c>Tick</c> loop
    /// calls <c>OnTick</c> on every component; un-overridden cases are no-ops.
    /// </summary>
    public abstract class AbilityComponentBase
    {
        public abstract void OnInit(AbilityContext ctx, ParamList parameters);
        public abstract void OnTrigger(AbilityContext ctx);
        public virtual void OnTick(AbilityContext ctx, float dt) { }
        public virtual void OnTeardown(AbilityContext ctx) { }
    }
}
