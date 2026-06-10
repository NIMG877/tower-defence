using System;

namespace SkillSystem
{
    public class ConditionEvalContext
    {
        public Blackboard Blackboard = new Blackboard();
        public object Event; // optional; carries the dispatching SkillEvent when present
    }

    public static class ConditionEvaluator
    {
        public static bool Evaluate(ConditionConfig cond, ConditionEvalContext ctx)
        {
            if (cond == null) return true;
            if (cond.op == ConditionOp.None) return true;

            switch (cond.op)
            {
                case ConditionOp.HasBlackboardKey:
                    return ctx.Blackboard.Has(cond.leftKey);
                case ConditionOp.NotHasBlackboardKey:
                    return !ctx.Blackboard.Has(cond.leftKey);
                case ConditionOp.Equal:
                    return ctx.Blackboard.Get<string>(cond.leftKey) == cond.rightValue;
                case ConditionOp.NotEqual:
                    return ctx.Blackboard.Get<string>(cond.leftKey) != cond.rightValue;
                case ConditionOp.Greater:
                    return CompareNumeric(ctx, cond.leftKey, cond.rightValue) > 0;
                case ConditionOp.GreaterOrEqual:
                    return CompareNumeric(ctx, cond.leftKey, cond.rightValue) >= 0;
                case ConditionOp.Less:
                    return CompareNumeric(ctx, cond.leftKey, cond.rightValue) < 0;
                case ConditionOp.LessOrEqual:
                    return CompareNumeric(ctx, cond.leftKey, cond.rightValue) <= 0;
                // HasBuff / IsInAbnormalState — implemented in Phase 2 when SkillEvent carries BuffController ref
                default: return true;
            }
        }

        private static int CompareNumeric(ConditionEvalContext ctx, string leftKey, string rightValueStr)
        {
            var left = ctx.Blackboard.Get<string>(leftKey, "");
            if (float.TryParse(left, out var l) && float.TryParse(rightValueStr, out var r))
                return l.CompareTo(r);
            return string.Compare(left, rightValueStr, StringComparison.Ordinal);
        }
    }
}
