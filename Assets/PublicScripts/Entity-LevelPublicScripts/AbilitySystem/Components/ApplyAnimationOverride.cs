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

        // once registers a one-shot entry: no state transition, the entry is
        // consumed whole the first time the machine naturally plays any of its
        // covered slots (see AnimationMachine.AddOneShotOverride). override
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
            if (slots == null || resources == null)
            {
                Debug.LogWarning("ApplyAnimationOverride: slots/resources param missing; override not applied");
                return null;
            }
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
                if (!Enum.TryParse(slots[i], true, out AnimationSlot parsed) || !Enum.IsDefined(typeof(AnimationSlot), parsed))
                {
                    Debug.LogWarning($"ApplyAnimationOverride: unknown animation slot '{slots[i]}'; skipping");
                    continue;
                }
                result[parsed] = resources[i];
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
