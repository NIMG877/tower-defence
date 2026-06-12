using System.Collections.Generic;

namespace AbilitySystem
{
    // The runtime context for one dispatch pass. sharedBlackboard is
    // the per-Entity shared blackboard (EntityAbilityRunner.sharedBlackboard
    // is injected at dispatch time). entity and currentEvent are reserved
    // for future ConditionOp extensions; this spec does not read them.
    public class ConditionEvalContext
    {
        // sharedBlackboard is intentionally uninitialized: caller MUST set it.
        // A forgotten assignment would silently use a fresh empty Blackboard and
        // break the per-Entity handoff. The one call site (EntitySkillRunner)
        // always sets it explicitly.
        public Blackboard sharedBlackboard;
        public Entity entity;
        public AbilityEvent currentEvent;
    }

    public static class ConditionEvaluator
    {
        // AND/OR evaluation:
        //   - empty / null groups -> true (unconditional pass)
        //   - otherwise, iterate groups; first group to pass wins (OR)
        public static bool Evaluate(List<ConditionGroup> groups, ConditionEvalContext ctx)
        {
            if (groups == null || groups.Count == 0) return true;

            for (int g = 0; g < groups.Count; g++)
            {
                if (EvaluateGroup(groups[g], ctx)) return true;
            }
            return false;
        }

        // AND across units:
        //   - empty / null units -> true
        //   - otherwise, all units must pass (AND)
        private static bool EvaluateGroup(ConditionGroup group, ConditionEvalContext ctx)
        {
            if (group == null) return true;
            if (group.units == null || group.units.Count == 0) return true;

            for (int u = 0; u < group.units.Count; u++)
            {
                if (!EvaluateUnit(group.units[u], ctx)) return false;
            }
            return true;
        }

        // Single comparison. Unknown ops log a one-shot warning and
        // return true (matches the legacy default: return true).
        private static bool EvaluateUnit(ConditionUnit unit, ConditionEvalContext ctx)
        {
            if (unit == null) return true;
            if (unit.op == ConditionOp.None) return true;

            switch (unit.op)
            {
                case ConditionOp.Equal:
                    return ctx.sharedBlackboard.Get<string>(unit.leftKey) == unit.rightValue;
                case ConditionOp.NotEqual:
                    return ctx.sharedBlackboard.Get<string>(unit.leftKey) != unit.rightValue;
                case ConditionOp.Greater:
                    return CompareNumeric(ctx, unit.leftKey, unit.rightValue) >  0;
                case ConditionOp.GreaterOrEqual:
                    return CompareNumeric(ctx, unit.leftKey, unit.rightValue) >= 0;
                case ConditionOp.Less:
                    return CompareNumeric(ctx, unit.leftKey, unit.rightValue) <  0;
                case ConditionOp.LessOrEqual:
                    return CompareNumeric(ctx, unit.leftKey, unit.rightValue) <= 0;
                default:
                    return WarnUnknownOpAndPass(unit.op);
            }
        }

        private static int CompareNumeric(ConditionEvalContext ctx, string leftKey, string rightValueStr)
        {
            var left = ctx.sharedBlackboard.Get<string>(leftKey, "");
            if (float.TryParse(left, out var l) && float.TryParse(rightValueStr, out var r))
                return l.CompareTo(r);
            return string.Compare(left, rightValueStr, System.StringComparison.Ordinal);
        }

        // One-shot warning per unknown op value across the application
        // lifetime. Backwards-compatible: unknown op -> pass.
        private static bool WarnUnknownOpAndPass(ConditionOp op)
        {
            OneShotWarn.WarnOnce(
                "cond-op:" + (int)op,
                $"ConditionEvaluator: unknown ConditionOp {(int)op} treated as 'pass'. " +
                "If this fires, a new op was added without a case in Evaluate.");
            return true;
        }
    }
}
