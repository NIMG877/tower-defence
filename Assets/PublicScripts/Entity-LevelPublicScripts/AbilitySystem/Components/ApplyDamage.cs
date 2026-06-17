using System;
using System.Collections.Generic;

namespace AbilitySystem.Components
{
    [RegisterComponent("ApplyDamage")]
    public class ApplyDamage : AbilityComponentBase
    {
        private Func<string> _targetMode;
        private Func<string> _blackboardKey;
        private Func<string> _baseValueMode;
        private Func<float> _baseValue;
        private Func<float> _multiplier;
        private Func<float> _defPenetrate;
        private Func<float> _mgrPenetrate;
        private Func<float> _defPenetrateValue;
        private Func<float> _mgrPenetrateValue;
        private Func<int> _damageType;
        private Func<int> _applyType;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            Blackboard bb = ctx.sharedBlackboard;
            _targetMode = p.GetStringLazy("targetMode", "eventTarget", bb);
            _blackboardKey = p.GetStringLazy("blackboardKey", "", bb);
            _baseValueMode = p.GetStringLazy("baseValueMode", "attack", bb);
            _baseValue = p.GetFloatLazy("baseValue", 0f, bb);
            _multiplier = p.GetFloatLazy("multiplier", 1f, bb);
            _defPenetrate = p.GetFloatLazy("defPenetrate", 0f, bb);
            _mgrPenetrate = p.GetFloatLazy("mgrPenetrate", 0f, bb);
            _defPenetrateValue = p.GetFloatLazy("defPenetrateValue", 0f, bb);
            _mgrPenetrateValue = p.GetFloatLazy("mgrPenetrateValue", 0f, bb);
            _damageType = p.GetIntLazy("damageType", 0, bb);
            _applyType = p.GetIntLazy("applyType", 2, bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (ctx.entity == null) return;

            List<Entity> targets = ResolveTargets(ctx);
            float damage = Normalize(_baseValueMode()) == "fixed" ? _baseValue() : ctx.entity.Stats.AttackS;

            for (int i = 0; i < targets.Count; i++)
            {
                Entity target = targets[i];
                if (target == null) continue;
                target.TakeDamage(
                    ctx.entity,
                    damage,
                    _multiplier(),
                    _defPenetrate(),
                    _mgrPenetrate(),
                    _defPenetrateValue(),
                    _mgrPenetrateValue(),
                    _damageType(),
                    _applyType());
            }
        }

        private List<Entity> ResolveTargets(AbilityContext ctx)
        {
            switch (Normalize(_targetMode()))
            {
                case "self":
                    return new List<Entity> { ctx.entity };
                case "blackboard":
                case "blackboardentities":
                    string key = _blackboardKey();
                    return string.IsNullOrEmpty(key) || ctx.sharedBlackboard == null
                        ? new List<Entity>()
                        : ctx.sharedBlackboard.Get<List<Entity>>(key, null) ?? new List<Entity>();
                default:
                    Entity eventTarget = ctx.currentEvent is DamageEventBase damageEvent ? damageEvent.target : null;
                    return eventTarget == null ? new List<Entity>() : new List<Entity> { eventTarget };
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Trim().ToLowerInvariant();
        }
    }
}
