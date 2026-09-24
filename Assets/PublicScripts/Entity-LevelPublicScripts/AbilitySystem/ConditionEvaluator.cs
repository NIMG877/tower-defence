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
        // break the per-Entity handoff. Every call site (EntityAbilityRunner
        // dispatch, AbilityStepRuntime step conditions) sets it explicitly.
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
                    return ReadAsString(ctx.sharedBlackboard, unit.leftKey) == unit.rightValue;
                case ConditionOp.NotEqual:
                    return ReadAsString(ctx.sharedBlackboard, unit.leftKey) != unit.rightValue;
                case ConditionOp.Greater:
                    return CompareNumeric(ctx, unit.leftKey, unit.rightValue) >  0;
                case ConditionOp.GreaterOrEqual:
                    return CompareNumeric(ctx, unit.leftKey, unit.rightValue) >= 0;
                case ConditionOp.Less:
                    return CompareNumeric(ctx, unit.leftKey, unit.rightValue) <  0;
                case ConditionOp.LessOrEqual:
                    return CompareNumeric(ctx, unit.leftKey, unit.rightValue) <= 0;
                case ConditionOp.KeyEqual:
                    return KeyEquals(ctx, unit.leftKey, unit.rightKey);
                case ConditionOp.KeyNotEqual:
                    return !KeyEquals(ctx, unit.leftKey, unit.rightKey);
                default:
                    return WarnUnknownOpAndPass(unit.op);
            }
        }

        // 键对键同一性比较：op(leftKey, rightKey)，两侧都从黑板按 object 读出。
        // 生产形态不对称：SpawnEntity 召唤者协议存裸 Entity，而 WriteBlackboard
        // path=origin 存 List<Entity>（ToEntityList 单元素）——单元素列表解包后
        // 比较，多元素列表不与单实体相等。缺键（null）任一侧 = 不等（沿用
        // "缺失 = 不等"）；实体即引用相等（池化实体跨次取出仍是同一实例）。
        // rightKey 留空是配线笔误（KeyEqual 没配右侧），报错并判否——这与
        // 运行期缺数据不同，后者静默按不等处理。
        private static bool KeyEquals(ConditionEvalContext ctx, string leftKey, string rightKey)
        {
            if (string.IsNullOrEmpty(rightKey))
            {
                UnityEngine.Debug.LogError(
                    $"ConditionEvaluator: KeyEqual/KeyNotEqual requires rightKey (leftKey='{leftKey}').");
                return false;
            }
            object left = UnwrapSingletonEntityList(ctx.sharedBlackboard.Get<object>(leftKey, null));
            object right = UnwrapSingletonEntityList(ctx.sharedBlackboard.Get<object>(rightKey, null));
            if (left == null || right == null) return false;
            return left.Equals(right);
        }

        private static object UnwrapSingletonEntityList(object v)
        {
            if (v is List<Entity> list)
                return list.Count == 1 ? list[0] : (list.Count == 0 ? null : v);
            return v;
        }

        private static int CompareNumeric(ConditionEvalContext ctx, string leftKey, string rightValueStr)
        {
            var left = ReadAsString(ctx.sharedBlackboard, leftKey);
            if (float.TryParse(left, out var l) && float.TryParse(rightValueStr, out var r))
                return l.CompareTo(r);
            return string.Compare(left, rightValueStr, System.StringComparison.Ordinal);
        }

        // 黑板是 object 存储：条件键可能是数字计数（write_blackboard add 维护），
        // 也可能是字符串标志（random_roll 的 "True"/"False"）。统一按 object 读出
        // 后转字符串比较（不能按 string 硬取，数字键会类型不符）。
        // 缺键返回 ""，与既有 "缺失 = 不等" 语义一致。
        private static string ReadAsString(Blackboard bb, string key)
        {
            object v = bb.Get<object>(key, null);
            if (v == null) return "";
            return v is System.IFormattable formattable
                ? formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture)
                : v.ToString();
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
