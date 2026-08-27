using System;
using System.Collections.Generic;
using System.Globalization;

namespace AbilitySystem.Components
{
    /// <summary>
    /// Filters entity lists with OR groups of AND conditions. Two modes:
    /// "attackCandidates" (default) subscribes to the entities' AttackBase.OnBeforeTargetSelect
    /// and filters the candidate list produced by AttackBase.AttackTargetSelect;
    /// "list" filters the Blackboard List&lt;Entity&gt; at blackboardKey in place.
    /// </summary>
    [RegisterComponent("EntityFilter")]
    public class EntityFilter : AbilityComponentBase
    {
        private Func<bool> _toSelf;
        private Func<string> _blackboardKey;
        private Func<string> _mode;
        private Func<string[]> _fields;
        private Func<string[]> _ops;
        private Func<string[]> _values;
        private Func<int[]> _groups;
        private readonly List<AttackBase> _subscribedAttacks = new List<AttackBase>();

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            Blackboard bb = ctx.sharedBlackboard;
            _toSelf = p.GetBoolLazy("toSelf", true, bb);
            _blackboardKey = p.GetStringLazy("blackboardKey", "", bb);
            _mode = p.GetStringLazy("mode", "attackCandidates", bb);
            _fields = p.GetStringArrayLazy<string>("fields", null, bb);
            _ops = p.GetStringArrayLazy<string>("ops", null, bb);
            _values = p.GetStringArrayLazy<string>("values", null, bb);
            _groups = p.GetIntArrayLazy("groups", null, bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (Normalize(_mode()) == "list")
            {
                FilterBlackboardList(ctx);
                return;
            }

            if (ctx.currentEvent is AbilityEndEvent)
            {
                UnsubscribeAll();
                return;
            }

            List<Entity> targets = ResolveTargets(ctx);
            if (targets == null) return;

            for (int i = 0; i < targets.Count; i++)
            {
                AttackBase attack = targets[i]?.AttackBase;
                if (attack == null || _subscribedAttacks.Contains(attack)) continue;
                attack.OnBeforeTargetSelect += FilterTargets;
                _subscribedAttacks.Add(attack);
            }
        }

        public override void OnTeardown(AbilityContext ctx)
        {
            UnsubscribeAll();
        }

        private void FilterTargets(List<Entity> targets, ref int selectMaxNum, ref int selectMinNum, ref bool sameCamp)
        {
            RemoveNonMatching(targets);
        }

        /// <summary>list 模式：对黑板上的 List&lt;Entity&gt; 原地过滤（典型用法是接在
        /// select_targets 的 outputEntitiesKey 之后做链式筛选）。键缺失/存的不是列表
        /// 属资产配线错误，一次性告警后跳过。</summary>
        private void FilterBlackboardList(AbilityContext ctx)
        {
            string key = _blackboardKey();
            if (string.IsNullOrEmpty(key))
            {
                OneShotWarn.WarnOnce("entity-filter-list-key",
                    "EntityFilter: mode 'list' requires blackboardKey; skipping.");
                return;
            }
            if (ctx.sharedBlackboard == null) return;

            List<Entity> targets = ctx.sharedBlackboard.Get<List<Entity>>(key, null);
            if (targets == null)
            {
                OneShotWarn.WarnOnce("entity-filter-list:" + key,
                    $"EntityFilter: blackboard key '{key}' holds no entity list; skipping.");
                return;
            }
            RemoveNonMatching(targets);
        }

        private void RemoveNonMatching(List<Entity> targets)
        {
            string[] fields = _fields();
            string[] ops = _ops();
            string[] values = _values();
            int[] groups = _groups();
            int count = Math.Min(Math.Min(fields.Length, ops.Length), Math.Min(values.Length, groups.Length));
            if (count == 0) return;

            for (int i = targets.Count - 1; i >= 0; i--)
            {
                if (!MatchesAnyGroup(targets[i], fields, ops, values, groups, count))
                {
                    targets.RemoveAt(i);
                }
            }
        }

        private static bool MatchesAnyGroup(
            Entity entity,
            string[] fields,
            string[] ops,
            string[] values,
            int[] groups,
            int count)
        {
            if (entity == null) return false;

            var groupResults = new Dictionary<int, bool>();
            for (int i = 0; i < count; i++)
            {
                int group = groups[i];
                bool passed = Evaluate(entity, fields[i], ops[i], values[i]);
                if (groupResults.TryGetValue(group, out bool current))
                {
                    groupResults[group] = current && passed;
                }
                else
                {
                    groupResults[group] = passed;
                }
            }

            foreach (KeyValuePair<int, bool> result in groupResults)
            {
                if (result.Value) return true;
            }
            return false;
        }

        private static bool Evaluate(Entity entity, string field, string op, string expected)
        {
            string normalizedField = Normalize(field);
            if (normalizedField == "idc")
            {
                return entity.EntityData != null
                    && CompareStrings(entity.EntityData.ID.ID_C, op, expected);
            }

            if (!TryGetFieldValue(entity, normalizedField, out float actual))
            {
                OneShotWarn.WarnOnce(
                    "entity-filter-field:" + field,
                    $"EntityFilter: unknown field '{field}'; its condition fails.");
                return false;
            }
            if (!TryParseNumber(field, expected, out float parsed)) return false;

            switch (Normalize(op))
            {
                case "none":
                    return true;
                case "equal":
                case "eq":
                    return actual == parsed;
                case "notequal":
                case "ne":
                    return actual != parsed;
                case "greater":
                case "gt":
                    return actual > parsed;
                case "greaterorequal":
                case "ge":
                    return actual >= parsed;
                case "less":
                case "lt":
                    return actual < parsed;
                case "lessorequal":
                case "le":
                    return actual <= parsed;
                default:
                    OneShotWarn.WarnOnce(
                        "entity-filter-op:" + op,
                        $"EntityFilter: unknown op '{op}'; its condition fails.");
                    return false;
            }
        }

        /// <summary>字符串字段（IdC）比较：仅 Equal/NotEqual，排序类 op 属配线错误。</summary>
        private static bool CompareStrings(string actual, string op, string expected)
        {
            switch (Normalize(op))
            {
                case "none":
                    return true;
                case "equal":
                case "eq":
                    return Normalize(actual) == Normalize(expected);
                case "notequal":
                case "ne":
                    return Normalize(actual) != Normalize(expected);
                default:
                    OneShotWarn.WarnOnce(
                        "entity-filter-op-string:" + op,
                        $"EntityFilter: op '{op}' cannot compare string field 'IdC'; its condition fails.");
                    return false;
            }
        }

        /// <summary>数值字段的比较值解析：解析失败即资产笔误，一次性告警并让该条件失败。</summary>
        private static bool TryParseNumber(string field, string raw, out float value)
        {
            if (!string.IsNullOrEmpty(raw)
                && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
            value = 0;
            OneShotWarn.WarnOnce(
                "entity-filter-value:" + field + ":" + raw,
                $"EntityFilter: field '{field}' expects a number but got '{raw}'; its condition fails.");
            return false;
        }

        private static bool TryGetFieldValue(Entity entity, string field, out float value)
        {
            switch (Normalize(field))
            {
                case "monsterstatus":
                    value = entity.EntityData != null ? entity.EntityData.MonsterStatus : 0;
                    return entity.EntityData != null;
                case "idn":
                    value = entity.EntityData != null ? entity.EntityData.ID.ID_N : 0;
                    return entity.EntityData != null;
                case "camp":
                    value = entity.Camp;
                    return true;
                case "currenthp":
                    value = entity.Stats.CurrentHp;
                    return true;
                case "currenthprate":
                    value = entity.Stats.CurrentHpRate;
                    return true;
                case "maxhp":
                    value = entity.Stats.MaxHpS;
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }

        private void UnsubscribeAll()
        {
            for (int i = 0; i < _subscribedAttacks.Count; i++)
            {
                AttackBase attack = _subscribedAttacks[i];
                if (attack != null) attack.OnBeforeTargetSelect -= FilterTargets;
            }
            _subscribedAttacks.Clear();
        }

        private List<Entity> ResolveTargets(AbilityContext ctx)
        {
            if (_toSelf()) return ctx.entity != null ? new List<Entity> { ctx.entity } : null;
            string key = _blackboardKey();
            if (string.IsNullOrEmpty(key) || ctx.sharedBlackboard == null) return null;
            return ctx.sharedBlackboard.Get<List<Entity>>(key, null);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Trim().ToLowerInvariant();
        }
    }
}
