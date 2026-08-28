using System;
using System.Collections.Generic;
using UnityEngine;

namespace AbilitySystem.Components
{
    [RegisterComponent("ApplyAnimationOverride")]
    public class ApplyAnimationOverride : AbilityComponentBase
    {
        private Func<bool> _toSelf;
        private Func<string> _blackboardKey;
        private Func<string> _mode;
        private Func<string[]> _slots;
        private Func<string[]> _resources;
        private Func<string> _outputKey;
        private Func<int> _priority;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            var bb = ctx.sharedBlackboard;
            _toSelf = p.GetBoolLazy("toSelf", true, bb);
            _blackboardKey = p.GetStringLazy("blackboardKey", "", bb);
            _mode = p.GetStringLazy("mode", "once", bb);
            _slots = p.GetStringArrayLazy<string>("slots", null, bb);
            _resources = p.GetStringArrayLazy<string>("resources", null, bb);
            _outputKey = p.GetStringLazy("outputKey", "", bb);
            _priority = p.GetIntLazy("priority", 0, bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            List<Entity> targets = ResolveTargets(ctx);
            if (targets == null || targets.Count == 0) return;

            AnimationOverride animations = BuildOverride();
            if (animations == null) return;

            string mode = _mode();
            if (string.Equals(mode, "once", StringComparison.OrdinalIgnoreCase))
            {
                Register(ctx, targets, animations, true);
                return;
            }
            if (string.Equals(mode, "override", StringComparison.OrdinalIgnoreCase))
            {
                Register(ctx, targets, animations, false);
                return;
            }

            Debug.LogWarning($"ApplyAnimationOverride: unknown mode '{mode}'; expected 'once' or 'override'");
        }

        // once registers a one-shot entry: no state transition, each covered slot
        // is consumed the next time the machine naturally plays it. override
        // registers a persistent entry. Both record revocable handles to outputKey.
        private void Register(AbilityContext ctx, List<Entity> targets, AnimationOverride animations, bool oneShot)
        {
            string outputKey = _outputKey();
            List<AnimationOverrideRecord> records = null;
            if (!string.IsNullOrEmpty(outputKey) && ctx.sharedBlackboard != null)
            {
                records = ctx.sharedBlackboard.Get<List<AnimationOverrideRecord>>(outputKey, null)
                    ?? new List<AnimationOverrideRecord>();
            }

            for (int i = 0; i < targets.Count; i++)
            {
                Entity target = targets[i];
                if (target == null || target.entityAM == null) continue;
                AnimationOverrideHandle handle = oneShot
                    ? target.entityAM.AddOneShotOverride(this, animations, _priority())
                    : target.entityAM.AddOverride(this, animations, _priority());
                records?.Add(new AnimationOverrideRecord(target, handle));
            }

            if (records != null)
            {
                ctx.sharedBlackboard.Set(outputKey, records);
            }
        }

        private AnimationOverride BuildOverride()
        {
            string[] slots = _slots();
            string[] resources = _resources();
            int count = Math.Min(slots.Length, resources.Length);
            if (slots.Length != resources.Length)
            {
                Debug.LogWarning(
                    $"ApplyAnimationOverride: slots/resources length mismatch " +
                    $"({slots.Length}/{resources.Length}); applying first {count} entries");
            }
            if (count == 0) return null;

            var result = new AnimationOverride();
            bool hasValidEntry = false;
            for (int i = 0; i < count; i++)
            {
                if (string.IsNullOrEmpty(resources[i])) continue;
                if (!TrySetSlot(result, slots[i], resources[i]))
                {
                    Debug.LogWarning($"ApplyAnimationOverride: unknown animation slot '{slots[i]}'; skipping");
                    continue;
                }
                hasValidEntry = true;
            }
            return hasValidEntry ? result : null;
        }

        private List<Entity> ResolveTargets(AbilityContext ctx)
        {
            if (_toSelf())
            {
                return ctx.entity != null ? new List<Entity> { ctx.entity } : null;
            }
            string key = _blackboardKey();
            if (string.IsNullOrEmpty(key) || ctx.sharedBlackboard == null) return null;
            return ctx.sharedBlackboard.Get<List<Entity>>(key, null);
        }

        private static bool TrySetSlot(AnimationOverride animations, string slot, string resource)
        {
            if (!Enum.TryParse(slot, true, out AnimationSlot parsed)) return false;
            switch (parsed)
            {
                case AnimationSlot.Default: animations.Default = resource; break;
                case AnimationSlot.Idle: animations.Idle = resource; break;
                case AnimationSlot.Move: animations.Move = resource; break;
                case AnimationSlot.JumpBegin: animations.JumpBegin = resource; break;
                case AnimationSlot.JumpLoop: animations.JumpLoop = resource; break;
                case AnimationSlot.JumpEnd: animations.JumpEnd = resource; break;
                case AnimationSlot.Start: animations.Start = resource; break;
                case AnimationSlot.Die: animations.Die = resource; break;
                case AnimationSlot.AttackBegin: animations.AttackBegin = resource; break;
                case AnimationSlot.AttackEnd: animations.AttackEnd = resource; break;
                case AnimationSlot.AttackRemote: animations.AttackRemote = resource; break;
                case AnimationSlot.AttackClose: animations.AttackClose = resource; break;
                case AnimationSlot.ChargeBegin: animations.ChargeBegin = resource; break;
                case AnimationSlot.Charge: animations.Charge = resource; break;
                case AnimationSlot.ChargeEnd: animations.ChargeEnd = resource; break;
                default: return false;
            }
            return true;
        }
    }

    public sealed class AnimationOverrideRecord
    {
        public readonly Entity Target;
        public readonly AnimationOverrideHandle Handle;

        public AnimationOverrideRecord(Entity target, AnimationOverrideHandle handle)
        {
            Target = target;
            Handle = handle;
        }
    }
}
