using System;
using System.Collections.Generic;
using UnityEngine;

namespace AbilitySystem.Components
{
    [RegisterComponent("ChargeAttackDamageModifier")]
    public class ChargeAttackDamageModifier : AbilityComponentBase
    {
        private Func<bool> _toSelf;
        private Func<string> _blackboardKey;
        private Func<float> _multiplier;
        private readonly List<ChargeAttack> _subscribedAttacks = new List<ChargeAttack>();

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            var bb = ctx.sharedBlackboard;
            _toSelf = p.GetBoolLazy("toSelf", true, bb);
            _blackboardKey = p.GetStringLazy("blackboardKey", "", bb);
            _multiplier = p.GetFloatLazy("multiplier", 1f, bb);
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
                ChargeAttack attack = targets[i] != null ? targets[i].GetComponent<ChargeAttack>() : null;
                if (attack == null || _subscribedAttacks.Contains(attack)) continue;
                attack.OnBeforeChargeTakeDamage += ModifyDamage;
                _subscribedAttacks.Add(attack);
            }
        }

        public override void OnTeardown(AbilityContext ctx)
        {
            UnsubscribeAll();
        }

        private void ModifyDamage(Entity target, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int damageType, int applyType)
        {
            multiplyer *= _multiplier();
        }

        private void UnsubscribeAll()
        {
            for (int i = 0; i < _subscribedAttacks.Count; i++)
            {
                ChargeAttack attack = _subscribedAttacks[i];
                if (attack != null) attack.OnBeforeChargeTakeDamage -= ModifyDamage;
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
    }
}
