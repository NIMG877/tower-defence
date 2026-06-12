namespace AbilitySystem.Components
{
    /// <summary>
    /// Base class for <see cref="IAbilityComponent"/> implementations that don't
    /// need <c>OnTick</c> or <c>OnTeardown</c>. <c>OnInit</c> and <c>OnTrigger</c>
    /// stay abstract (each component is defined by them); <c>OnTick</c> and
    /// <c>OnTeardown</c> have empty virtual defaults so most components don't
    /// have to write the empty bodies.
    ///
    /// <para>Ticking components (those that need <see cref="ITickingComponent"/>)
    /// inherit this AND implement <c>ITickingComponent</c> — the runner detects
    /// them by the interface check, not by class hierarchy.</para>
    /// </summary>
    public abstract class AbilityComponentBase : IAbilityComponent
    {
        public abstract void OnInit(AbilityContext ctx, ParamList parameters);
        public abstract void OnTrigger(AbilityContext ctx);
        public virtual void OnTick(AbilityContext ctx, float dt) { }
        public virtual void OnTeardown(AbilityContext ctx) { }
    }
}
