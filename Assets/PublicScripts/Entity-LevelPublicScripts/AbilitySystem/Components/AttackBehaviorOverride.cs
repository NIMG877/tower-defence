using System;
using System.Collections.Generic;
using UnityEngine;

namespace AbilitySystem.Components
{
    [RegisterComponent("AttackBehaviorOverride")]
    public class AttackBehaviorOverride : AbilityComponentBase
    {
        private Func<bool> _toSelf;
        private Func<string> _blackboardKey;
        private Func<int> _damageType;
        private Func<string> _targetPriority;
        private Func<string> _outputKey;
        private bool _hasDamageType;
        private bool _hasTargetPriority;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            var bb = ctx.sharedBlackboard;
            _toSelf = p.GetBoolLazy("toSelf", true, bb);
            _blackboardKey = p.GetStringLazy("blackboardKey", "", bb);
            _damageType = p.GetIntLazy("damageType", 0, bb);
            _targetPriority = p.GetStringLazy("targetPriority", "", bb);
            _outputKey = p.GetStringLazy("outputKey", "", bb);
            _hasDamageType = p.HasKey("damageType");
            _hasTargetPriority = p.HasKey("targetPriority");
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            List<Entity> targets = ResolveTargets(ctx);
            if (targets == null || targets.Count == 0) return;

            bool applyTargetPriority = _hasTargetPriority;
            OrderLogic parsedTargetPriority = default;
            if (applyTargetPriority && !Enum.TryParse(_targetPriority(), true, out parsedTargetPriority))
            {
                Debug.LogWarning($"AttackBehaviorOverride: unknown OrderLogic '{_targetPriority()}'; skipping target priority override");
                applyTargetPriority = false;
            }

            string outputKey = _outputKey();
            List<AttackBehaviorSnapshot> snapshots = null;
            if (!string.IsNullOrEmpty(outputKey) && ctx.sharedBlackboard != null)
            {
                snapshots = ctx.sharedBlackboard.Get<List<AttackBehaviorSnapshot>>(outputKey, null)
                    ?? new List<AttackBehaviorSnapshot>();
            }

            for (int i = 0; i < targets.Count; i++)
            {
                Entity target = targets[i];
                if (target == null || target.AttackBase == null) continue;

                snapshots?.Add(new AttackBehaviorSnapshot(
                    target,
                    target.AttackBase.DamageType,
                    target.AttackBase.TargetPriority));

                if (_hasDamageType) target.AttackBase.DamageType = _damageType();
                if (applyTargetPriority) target.AttackBase.TargetPriority = parsedTargetPriority;
            }

            if (snapshots != null)
            {
                ctx.sharedBlackboard.Set(outputKey, snapshots);
            }
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

    public sealed class AttackBehaviorSnapshot
    {
        public readonly Entity Target;
        public readonly int DamageType;
        public readonly OrderLogic TargetPriority;

        public AttackBehaviorSnapshot(Entity target, int damageType, OrderLogic targetPriority)
        {
            Target = target;
            DamageType = damageType;
            TargetPriority = targetPriority;
        }
    }
}
