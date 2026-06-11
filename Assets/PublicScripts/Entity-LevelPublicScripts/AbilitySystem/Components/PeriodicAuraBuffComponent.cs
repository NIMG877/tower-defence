using System;
using System.Collections.Generic;

namespace AbilitySystem.Components
{
    [RegisterComponent("PeriodicAuraBuff")]
    public class PeriodicAuraBuffComponent : ITickingComponent
    {
        private float _radius;
        private string _effectName, _buffId;
        private float _priority = -10f;
        private bool _toAllies = true;
        private BuffType[] _types = Array.Empty<BuffType>();
        private float[] _values = Array.Empty<float>();
        private readonly List<Entity> _tracked = new List<Entity>();
        private readonly List<Buff> _trackedBuffs = new List<Buff>();
        // Parallel HashSet so per-tick membership checks are O(1) instead of O(n).
        // Kept in lockstep with _tracked by Add/Remove in OnTick and Clear in OnTeardown.
        private readonly HashSet<Entity> _trackedSet = new HashSet<Entity>();

        public void OnInit(AbilityContext ctx, ParamList p)
        {
            _radius = p.GetFloat("radius", 0f);
            _types = BuffParamParser.ParseBuffTypes(p.GetString("buffTypes", ""));
            _values = BuffParamParser.ParseFloats(p.GetString("buffValues", ""));
            _effectName = p.GetString("effectName", "");
            _buffId = p.GetString("buffId", "aura_buff");
            _priority = p.GetFloat("priority", -10f);
            _toAllies = p.GetBool("toAllies", true);
        }

        public void OnTrigger(AbilityContext ctx) { }
        public void OnTeardown(AbilityContext ctx)
        {
            for (int i = 0; i < _tracked.Count; i++)
                _tracked[i].buffController?.DestroyBuff(_trackedBuffs[i]);
            _tracked.Clear();
            _trackedBuffs.Clear();
            _trackedSet.Clear();
        }

        public void OnTick(AbilityContext ctx, float dt)
        {
            if (ctx.entity == null) return;
            int camp = ctx.entity.Camp;
            var inRange = EntityManager.Manager.EntitySelector_Radius(
                (ctx.entity.Movement.Position.x, ctx.entity.Movement.Position.y),
                camp, _toAllies, _radius, false);

            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                var tracked = _tracked[i];
                if (!inRange.Contains(tracked))
                {
                    tracked.buffController?.DestroyBuff(_trackedBuffs[i]);
                    _tracked.RemoveAt(i);
                    _trackedBuffs.RemoveAt(i);
                    _trackedSet.Remove(tracked);
                }
            }
            for (int i = 0; i < inRange.Count; i++)
            {
                var e = inRange[i];
                if (!_trackedSet.Contains(e) && e.buffController != null)
                {
                    var b = e.buffController.CreateBuff(_types, null, _buffId, _values, _priority, true);
                    _tracked.Add(e);
                    _trackedBuffs.Add(b);
                    _trackedSet.Add(e);
                }
            }
        }
    }
}
