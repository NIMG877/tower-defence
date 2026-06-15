using System;
using System.Collections.Generic;

namespace AbilitySystem.Components
{
    [RegisterComponent("ChargeAttackReservePool")]
    public class ChargeAttackReservePool : AbilityComponentBase
    {
        private Func<bool> _toSelf;
        private Func<string> _blackboardKey;
        private Func<int> _capacity;
        private Func<int> _minMonsterStatus;
        private Func<int> _maxMonsterStatus;
        private bool _hasMonsterStatusFilter;
        private readonly List<ChargeReserveHandle> _handles = new List<ChargeReserveHandle>();

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            var bb = ctx.sharedBlackboard;
            _toSelf = p.GetBoolLazy("toSelf", true, bb);
            _blackboardKey = p.GetStringLazy("blackboardKey", "", bb);
            _capacity = p.GetIntLazy("capacity", 1, bb);
            _minMonsterStatus = p.GetIntLazy("minMonsterStatus", int.MinValue, bb);
            _maxMonsterStatus = p.GetIntLazy("maxMonsterStatus", int.MaxValue, bb);
            _hasMonsterStatusFilter = p.HasKey("minMonsterStatus") || p.HasKey("maxMonsterStatus");
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (ctx.currentEvent is AbilityEndEvent)
            {
                RemoveAll();
                return;
            }

            List<Entity> targets = ResolveTargets(ctx);
            if (targets == null) return;
            for (int i = 0; i < targets.Count; i++)
            {
                ChargeAttack attack = targets[i] != null ? targets[i].GetComponent<ChargeAttack>() : null;
                if (attack == null || HasHandleFor(attack)) continue;
                ChargeReserveHandle handle = attack.AddReservePool(this, _capacity(), CanUseOnTarget);
                if (handle != null) _handles.Add(handle);
            }
        }

        public override void OnTeardown(AbilityContext ctx)
        {
            RemoveAll();
        }

        private bool CanUseOnTarget(Entity target)
        {
            if (!_hasMonsterStatusFilter) return true;
            if (target == null || target.EntityData == null) return false;
            int status = target.EntityData.MonsterStatus;
            return status >= _minMonsterStatus() && status <= _maxMonsterStatus();
        }

        private bool HasHandleFor(ChargeAttack attack)
        {
            for (int i = 0; i < _handles.Count; i++)
            {
                if (_handles[i] != null && _handles[i].Attack == attack) return true;
            }
            return false;
        }

        private void RemoveAll()
        {
            for (int i = 0; i < _handles.Count; i++)
            {
                ChargeReserveHandle handle = _handles[i];
                handle?.Attack?.RemoveReservePool(handle);
            }
            _handles.Clear();
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
