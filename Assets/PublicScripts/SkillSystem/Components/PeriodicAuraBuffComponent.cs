using System;
using System.Collections.Generic;

namespace SkillSystem.Components
{
    [RegisterComponent("PeriodicAuraBuff")]
    public class PeriodicAuraBuffComponent : ITickingComponent
    {
        private float _radius;
        private string _buffTypesRaw, _buffValuesRaw, _effectName, _buffId;
        private float _priority = -10f;
        private bool _toAllies = true;
        private readonly List<Entity> _tracked = new List<Entity>();
        private readonly List<Buff> _trackedBuffs = new List<Buff>();

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _radius = p.GetFloat("radius", 0f);
            _buffTypesRaw = p.GetString("buffTypes", "");
            _buffValuesRaw = p.GetString("buffValues", "");
            _effectName = p.GetString("effectName", "");
            _buffId = p.GetString("buffId", "aura_buff");
            _priority = p.GetFloat("priority", -10f);
            _toAllies = p.GetBool("toAllies", true);
        }

        public void OnTrigger(SkillContext ctx) { }
        public void OnTeardown(SkillContext ctx)
        {
            for (int i = 0; i < _tracked.Count; i++)
                _tracked[i].buffController?.DestroyBuff(_trackedBuffs[i]);
            _tracked.Clear();
            _trackedBuffs.Clear();
        }

        public void OnTick(SkillContext ctx, float dt)
        {
            if (ctx.entity == null) return;
            int camp = ctx.entity.Camp;
            var inRange = EntityManager.Manager.EntitySelector_Radius(
                (ctx.entity.Movement.Position.x, ctx.entity.Movement.Position.y),
                camp, _toAllies, _radius, false);

            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                if (!inRange.Contains(_tracked[i]))
                {
                    _tracked[i].buffController?.DestroyBuff(_trackedBuffs[i]);
                    _tracked.RemoveAt(i);
                    _trackedBuffs.RemoveAt(i);
                }
            }
            for (int i = 0; i < inRange.Count; i++)
            {
                if (!_tracked.Contains(inRange[i]) && inRange[i].buffController != null)
                {
                    var types = ParseBuffTypes(_buffTypesRaw);
                    var vals = ParseFloats(_buffValuesRaw);
                    var b = inRange[i].buffController.CreateBuff(types, null, _buffId, vals, _priority, true);
                    _tracked.Add(inRange[i]);
                    _trackedBuffs.Add(b);
                }
            }
        }

        private static BuffType[] ParseBuffTypes(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return Array.Empty<BuffType>();
            var parts = csv.Split(',');
            var arr = new BuffType[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                arr[i] = (BuffType)Enum.Parse(typeof(BuffType), parts[i].Trim());
            return arr;
        }
        private static float[] ParseFloats(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return Array.Empty<float>();
            var parts = csv.Split(',');
            var arr = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                arr[i] = float.Parse(parts[i].Trim());
            return arr;
        }
    }
}
