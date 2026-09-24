using System;
using UnityEngine;

namespace AbilitySystem.Components
{
    /// <summary>
    /// Overrides the target Entity's <see cref="EntityVision.Range"/> with a
    /// component-supplied range. Used in pairs with <see cref="AttackRangeRestore"/>:
    /// designer wires the override on the begin trigger (e.g. <c>OnAbilityBegin</c>) and
    /// the restore on the end trigger (e.g. <c>OnAbilityEnd</c>), and the Vision snapshot
    /// reverts when the skill window closes.
    ///
    /// <para>Params:</para>
    /// <list type="bullet">
    ///   <item><c>toSelf</c> (bool, default <c>true</c>) — when true, target is
    ///   <c>ctx.entity</c>; when false, target is read from the blackboard.</item>
    ///   <item><c>targetEntity</c> (string, default <c>""</c>) — blackboard key holding
    ///   the <see cref="Entity"/> to rewrite. Empty / unset = no target when
    ///   <c>toSelf=false</c> (component is a no-op).</item>
    ///   <item><c>range</c> (Vector2Int[] via <see cref="ParamList.GetVector2IntArrayLazy"/>,
    ///   default <c>null</c>) — literal value must be a JSON int-array
    ///   <c>"[[x,y],[x,y],...]"</c> (parsed via <c>ComponentConfig.ParseVector2IntArray</c>,
    ///   i.e. <c>JsonConvert.DeserializeObject&lt;int[][]&gt;</c>; working example:
    ///   spot_s1.asset's <c>range</c> param); fromBlackboard=true accepts
    ///   <c>Vector2Int[]</c> / <c>Vector2Int</c> / <c>string</c>. Null or empty
    ///   range = no-op.</item>
    /// </list>
    /// </summary>
    [RegisterComponent("AttackRangeOverride")]
    public class AttackRangeOverride : AbilityComponentBase
    {
        private Func<bool> _toSelf;
        private Func<string> _targetEntityKey;
        private Func<Vector2Int[]> _range;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            var bb = ctx.sharedBlackboard;
            _toSelf         = p.GetBoolLazy         ("toSelf",       true, bb);
            _targetEntityKey = p.GetStringLazy      ("targetEntity", "",   bb);
            _range          = p.GetVector2IntArrayLazy("range",       null, bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            Entity target = ResolveTarget(ctx);
            if (target == null) return;
            var range = _range();
            if (range == null || range.Length == 0) return;
            target.Vision.Range = ToTupleRange(range);
        }

        // Mirror of ApplyBuff.ResolveTargets: when toSelf, ctx.entity wins; otherwise
        // BB[key] is the Entity reference. Returns null (caller no-ops) when toSelf=false
        // and the key is empty / missing — the upstream writer hasn't run yet, or the
        // designer left it unconfigured.
        private Entity ResolveTarget(AbilityContext ctx)
        {
            if (_toSelf()) return ctx.entity;
            var key = _targetEntityKey();
            if (string.IsNullOrEmpty(key)) return null;
            return ctx.sharedBlackboard?.Get<Entity>(key, null);
        }

        // EntityVision.Range setter takes (int x, int y)[]; Vector2Int[] is designer-friendly
        // but not assignable. Cheap shape conversion.
        private static (int x, int y)[] ToTupleRange(Vector2Int[] range)
        {
            var arr = new (int x, int y)[range.Length];
            for (int i = 0; i < range.Length; i++) arr[i] = (range[i].x, range[i].y);
            return arr;
        }
    }
}
