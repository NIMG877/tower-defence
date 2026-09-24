using System;

namespace AbilitySystem.Components
{
    /// <summary>
    /// Restores the target Entity's <see cref="EntityVision.Range"/> from
    /// <see cref="EntityVision.BaseRange"/>, undoing whatever
    /// <see cref="AttackRangeOverride"/> set during the skill window.
    /// Designer wires this on the end trigger (e.g. <c>OnAbilityEnd</c>); the
    /// setter re-runs <c>RangeCaculator</c> so the new (base) range is orientation-correct.
    ///
    /// <para>Params mirror the override component, minus <c>range</c>:</para>
    /// <list type="bullet">
    ///   <item><c>toSelf</c> (bool, default <c>true</c>) — when true, target is
    ///   <c>ctx.entity</c>; when false, target is read from the blackboard.</item>
    ///   <item><c>targetEntity</c> (string, default <c>""</c>) — blackboard key holding
    ///   the <see cref="Entity"/> to restore. Empty / unset = no target when
    ///   <c>toSelf=false</c> (component is a no-op).</item>
    /// </list>
    /// </summary>
    [RegisterComponent("AttackRangeRestore")]
    public class AttackRangeRestore : AbilityComponentBase
    {
        private Func<bool> _toSelf;
        private Func<string> _targetEntityKey;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            var bb = ctx.sharedBlackboard;
            _toSelf          = p.GetBoolLazy ("toSelf",       true, bb);
            _targetEntityKey = p.GetStringLazy("targetEntity", "",   bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            Entity target = ResolveTarget(ctx);
            if (target == null) return;
            // Re-assigning BaseRange to Range is the documented "restore" gesture —
            // it re-runs MapDataManager.RangeCaculator, so orientation is honored
            // (`Vision.Range = Vision.BaseRange`).
            target.Vision.Range = target.Vision.BaseRange;
        }

        // Same shape as AttackRangeOverride.ResolveTarget; duplicated rather than
        // hoisted to AbilityComponentBase because each component should remain independently
        // readable (only 2 callers, and the line is short).
        private Entity ResolveTarget(AbilityContext ctx)
        {
            if (_toSelf()) return ctx.entity;
            var key = _targetEntityKey();
            if (string.IsNullOrEmpty(key)) return null;
            return ctx.sharedBlackboard?.Get<Entity>(key, null);
        }
    }
}
