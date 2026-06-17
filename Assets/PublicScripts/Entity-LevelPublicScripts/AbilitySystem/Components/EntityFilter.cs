using System;
using System.Collections.Generic;

namespace AbilitySystem.Components
{
    /// <summary>
    /// Filters attack-target candidates with OR groups of AND conditions.
    /// </summary>
    [RegisterComponent("EntityFilter")]
    public class EntityFilter : AbilityComponentBase
    {
        private Func<bool> _toSelf;
        private Func<string> _blackboardKey;
        private Func<string[]> _fields;
        private Func<string[]> _ops;
        private Func<float[]> _values;
        private Func<int[]> _groups;
        private readonly List<AttackBase> _subscribedAttacks = new List<AttackBase>();

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            Blackboard bb = ctx.sharedBlackboard;
            _toSelf = p.GetBoolLazy("toSelf", true, bb);
            _blackboardKey = p.GetStringLazy("blackboardKey", "", bb);
            _fields = p.GetStringArrayLazy<string>("fields", null, bb);
            _ops = p.GetStringArrayLazy<string>("ops", null, bb);
            _values = p.GetFloatArrayLazy("values", null, bb);
            _groups = p.GetIntArrayLazy("groups", null, bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
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
            string[] fields = _fields();
            string[] ops = _ops();
            float[] values = _values();
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
            float[] values,
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

        private static bool Evaluate(Entity entity, string field, string op, float expected)
        {
            if (!TryGetFieldValue(entity, field, out float actual))
            {
                OneShotWarn.WarnOnce(
                    "entity-filter-field:" + field,
                    $"EntityFilter: unknown field '{field}'; its condition fails.");
                return false;
            }

            switch (Normalize(op))
            {
                case "none":
                    return true;
                case "equal":
                case "eq":
                    return actual == expected;
                case "notequal":
                case "ne":
                    return actual != expected;
                case "greater":
                case "gt":
                    return actual > expected;
                case "greaterorequal":
                case "ge":
                    return actual >= expected;
                case "less":
                case "lt":
                    return actual < expected;
                case "lessorequal":
                case "le":
                    return actual <= expected;
                default:
                    OneShotWarn.WarnOnce(
                        "entity-filter-op:" + op,
                        $"EntityFilter: unknown op '{op}'; its condition fails.");
                    return false;
            }
        }

        private static bool TryGetFieldValue(Entity entity, string field, out float value)
        {
            switch (Normalize(field))
            {
                case "monsterstatus":
                    value = entity.EntityData != null ? entity.EntityData.MonsterStatus : 0;
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
