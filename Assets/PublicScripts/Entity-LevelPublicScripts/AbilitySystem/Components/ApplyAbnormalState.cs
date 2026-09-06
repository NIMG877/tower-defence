using System;
using System.Collections.Generic;

namespace AbilitySystem.Components
{
    [RegisterComponent("ApplyAbnormalState")]
    public class ApplyAbnormalState : AbilityComponentBase
    {
        private Func<string> _mode;
        private Func<int[]> _types;
        private Func<float[]> _times;
        private Func<bool> _toSelf;
        private Func<string> _inputTargetKey;
        private Func<string> _outputTargetKey;
        private Func<string> _outputStateKey;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            Blackboard bb = ctx.sharedBlackboard;
            _mode = p.GetStringLazy("mode", "normal", bb);
            _types = p.GetIntArrayLazy("abnormalTypes", null, bb);
            _times = p.GetFloatArrayLazy("abnormalTimes", null, bb);
            _toSelf = p.GetBoolLazy("toSelf", true, bb);
            _inputTargetKey = p.GetStringLazy("blackboardKey", "", bb);
            _outputTargetKey = p.GetStringLazy("outputTarget", "", bb);
            _outputStateKey = p.GetStringLazy("outputState", "", bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            List<Entity> targets = ResolveTargets(ctx);
            if (targets == null) return;

            int[] types = _types();
            float[] times = _times();
            if (types.Length != times.Length)
            {
                OneShotWarn.WarnOnce(
                    "apply-abnormal-length",
                    $"ApplyAbnormalState: abnormalTypes/abnormalTimes length mismatch ({types.Length}/{times.Length}); skipping.");
                return;
            }

            string mode = Normalize(_mode());
            if (mode == "aura")
            {
                SyncAura(ctx, targets, types, times);
                return;
            }
            if (mode != "normal")
            {
                OneShotWarn.WarnOnce(
                    "apply-abnormal-mode:" + mode,
                    $"ApplyAbnormalState: unknown mode '{_mode()}'; expected 'normal' or 'aura'.");
                return;
            }

            ApplyNormal(ctx, targets, types, times);
        }

        private void ApplyNormal(AbilityContext ctx, List<Entity> targets, int[] types, float[] times)
        {
            bool write = ctx.sharedBlackboard != null
                && !string.IsNullOrEmpty(_outputTargetKey())
                && !string.IsNullOrEmpty(_outputStateKey());
            var roundTargets = new List<Entity>();
            var roundStates = new List<int>();

            for (int i = 0; i < targets.Count; i++)
            {
                Entity target = targets[i];
                if (target == null || target.buffController == null) continue;
                for (int j = 0; j < types.Length; j++)
                {
                    if (!IsValidType(types[j])) continue;
                    target.buffController.AddAbnormalState(times[j], types[j]);
                    if (!write) continue;
                    roundTargets.Add(target);
                    roundStates.Add(types[j]);
                }
            }

            if (!write) return;
            var outputTargets = ctx.sharedBlackboard.Get<List<Entity>>(_outputTargetKey(), null) ?? new List<Entity>();
            var outputStates = ctx.sharedBlackboard.Get<List<int>>(_outputStateKey(), null) ?? new List<int>();
            outputTargets.AddRange(roundTargets);
            outputStates.AddRange(roundStates);
            ctx.sharedBlackboard.Set(_outputTargetKey(), outputTargets);
            ctx.sharedBlackboard.Set(_outputStateKey(), outputStates);
        }

        private void SyncAura(AbilityContext ctx, List<Entity> targets, int[] types, float[] times)
        {
            string targetKey = _outputTargetKey();
            string stateKey = _outputStateKey();
            if (ctx.sharedBlackboard == null || string.IsNullOrEmpty(_inputTargetKey())
                || string.IsNullOrEmpty(targetKey) || string.IsNullOrEmpty(stateKey))
            {
                OneShotWarn.WarnOnce(
                    "apply-abnormal-aura-keys",
                    "ApplyAbnormalState: mode='aura' requires blackboardKey, outputTarget, and outputState.");
                return;
            }

            var oldTargets = ctx.sharedBlackboard.Get<List<Entity>>(targetKey, null) ?? new List<Entity>();
            var oldStates = ctx.sharedBlackboard.Get<List<int>>(stateKey, null) ?? new List<int>();
            var nextTargets = new List<Entity>();
            var nextStates = new List<int>();
            var desired = new HashSet<StatePair>();

            for (int i = 0; i < targets.Count; i++)
            {
                Entity target = targets[i];
                if (target == null || target.buffController == null) continue;
                for (int j = 0; j < types.Length; j++)
                {
                    int type = types[j];
                    if (!IsValidType(type)) continue;
                    var pair = new StatePair(target, type);
                    if (!desired.Add(pair)) continue;
                    target.buffController.AddAbnormalState(times[j], type);
                    nextTargets.Add(target);
                    nextStates.Add(type);
                }
            }

            int oldCount = Math.Min(oldTargets.Count, oldStates.Count);
            for (int i = 0; i < oldCount; i++)
            {
                Entity target = oldTargets[i];
                int type = oldStates[i];
                if (target == null || target.buffController == null || !IsValidType(type)) continue;
                if (!desired.Contains(new StatePair(target, type)))
                    target.buffController.TryRemoveAbnormalState(type);
            }

            ctx.sharedBlackboard.Remove(targetKey);
            ctx.sharedBlackboard.Remove(stateKey);
            ctx.sharedBlackboard.Set(targetKey, nextTargets);
            ctx.sharedBlackboard.Set(stateKey, nextStates);
        }

        public override void OnTeardown(AbilityContext ctx)
        {
            if (Normalize(_mode()) != "aura" || ctx.sharedBlackboard == null) return;
            RemoveRecorded(ctx.sharedBlackboard, _outputTargetKey(), _outputStateKey());
        }

        private List<Entity> ResolveTargets(AbilityContext ctx)
        {
            string key = _inputTargetKey();
            if (!string.IsNullOrEmpty(key))
                return ctx.sharedBlackboard?.Get<List<Entity>>(key, null);
            return _toSelf() && ctx.entity != null ? new List<Entity> { ctx.entity } : null;
        }

        private static void RemoveRecorded(Blackboard bb, string targetKey, string stateKey)
        {
            if (string.IsNullOrEmpty(targetKey) || string.IsNullOrEmpty(stateKey)) return;
            var targets = bb.Get<List<Entity>>(targetKey, null);
            var states = bb.Get<List<int>>(stateKey, null);
            if (targets != null && states != null)
            {
                int count = Math.Min(targets.Count, states.Count);
                for (int i = 0; i < count; i++)
                {
                    Entity target = targets[i];
                    int type = states[i];
                    if (target != null && target.buffController != null && IsValidType(type))
                        target.buffController.TryRemoveAbnormalState(type);
                }
            }
            bb.Remove(targetKey);
            bb.Remove(stateKey);
        }

        private static bool IsValidType(int type)
        {
            if (BuffController.IsValidAbnormalType(type)) return true;
            OneShotWarn.WarnOnce(
                "apply-abnormal-type:" + type,
                $"ApplyAbnormalState: abnormal type {type} is not a valid abnormal state type; skipping.");
            return false;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Trim().ToLowerInvariant();
        }

        private readonly struct StatePair : IEquatable<StatePair>
        {
            private readonly Entity _entity;
            private readonly int _type;

            public StatePair(Entity entity, int type)
            {
                _entity = entity;
                _type = type;
            }

            public bool Equals(StatePair other) => _entity == other._entity && _type == other._type;
            public override bool Equals(object obj) => obj is StatePair other && Equals(other);
            public override int GetHashCode() => ((_entity != null ? _entity.GetHashCode() : 0) * 397) ^ _type;
        }
    }
}
