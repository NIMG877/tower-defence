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
        private Func<string> _orderLogic;
        private Func<string> _outputKey;
        private bool _hasDamageType;
        private bool _hasOrderLogic;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            var bb = ctx.sharedBlackboard;
            _toSelf = p.GetBoolLazy("toSelf", true, bb);
            _blackboardKey = p.GetStringLazy("blackboardKey", "", bb);
            _damageType = p.GetIntLazy("damageType", 0, bb);
            _orderLogic = p.GetStringLazy("orderLogic", "", bb);
            _outputKey = p.GetStringLazy("outputKey", "", bb);
            _hasDamageType = p.HasKey("damageType");
            _hasOrderLogic = p.HasKey("orderLogic");
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            List<Entity> targets = ResolveTargets(ctx);
            if (targets == null || targets.Count == 0) return;

            bool applyOrderLogic = _hasOrderLogic;
            OrderLogic parsedOrder = default;
            if (applyOrderLogic && !Enum.TryParse(_orderLogic(), true, out parsedOrder))
            {
                Debug.LogWarning($"AttackBehaviorOverride: unknown OrderLogic '{_orderLogic()}'; skipping order override");
                applyOrderLogic = false;
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
                    target.AttackBase.EntityOrderLogic));

                if (_hasDamageType) target.AttackBase.DamageType = _damageType();
                if (applyOrderLogic) target.AttackBase.EntityOrderLogic = parsedOrder;
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
        public readonly OrderLogic OrderLogic;

        public AttackBehaviorSnapshot(Entity target, int damageType, OrderLogic orderLogic)
        {
            Target = target;
            DamageType = damageType;
            OrderLogic = orderLogic;
        }
    }
}
