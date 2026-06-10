using System.Collections.Generic;
using UnityEngine;

namespace SkillSystem.Components
{
    [RegisterComponent("DamageRadiusFalloff")]
    public class DamageRadiusFalloffComponent : ISkillComponent
    {
        // sqrt(2) — diagonal of one EntityR cell. 2 * EntityR spans 2 cells.
        private const float Sqrt2 = 1.414f;

        private float _maxRadius = 1.5f;
        private float _baseDamage = 1f;
        private int _damageType = 0;
        private int _applyType = 1;
        private float _tier1R = 0.5f, _tier2R = 1f, _tier3R = 1.5f;
        private float _tier1Mul = 1f, _tier2Mul = 0.5f, _tier3Mul = 0.25f, _tier4Mul = 0.1f;
        private int _impulse1 = 5, _impulse2 = 4, _impulse3 = 3, _impulse4 = 2;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _maxRadius = p.GetFloat("maxRadius", 1.5f);
            _baseDamage = p.GetFloat("baseDamage", 1f);
            _damageType = p.GetInt("damageType", 0);
            _applyType = p.GetInt("applyType", 1);
            _tier1R = p.GetFloat("tier1Radius", 0.5f);
            _tier2R = p.GetFloat("tier2Radius", 1f);
            _tier3R = p.GetFloat("tier3Radius", 1.5f);
            _tier1Mul = p.GetFloat("tier1Mul", 1f);
            _tier2Mul = p.GetFloat("tier2Mul", 0.5f);
            _tier3Mul = p.GetFloat("tier3Mul", 0.25f);
            _tier4Mul = p.GetFloat("tier4Mul", 0.1f);
            _impulse1 = p.GetInt("impulse1", 5);
            _impulse2 = p.GetInt("impulse2", 4);
            _impulse3 = p.GetInt("impulse3", 3);
            _impulse4 = p.GetInt("impulse4", 2);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (ctx.entity == null) return;
            var pos = ctx.entity.Movement.Position;
            var monsters = EntityManager.Manager.EntitySelector_Radius((pos.x, pos.y), 2, false, _maxRadius, false);
            var turrets = EntityManager.Manager.EntitySelector_Radius((pos.x, pos.y), 1, false, _maxRadius, false);
            var all = new List<Entity>(monsters);
            all.AddRange(turrets);
            float r0 = EntityManager.EntityR;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                float r = Vector2.Distance(t.EntityPosition, ctx.entity.Movement.Position);
                float mul; int impulse;
                if (r <= r0) { mul = _tier1Mul; impulse = _impulse1; }
                else if (r <= 2 * r0) { mul = _tier2Mul; impulse = _impulse2; }
                else if (r <= Sqrt2 + r0) { mul = _tier3Mul; impulse = _impulse3; }
                else { mul = _tier4Mul; impulse = _impulse4; }
                t.TakeDamage(ctx.entity, _baseDamage, mul, 0, 0, 0, 0, _damageType, _applyType);
                var dir = (t.EntityPosition - ctx.entity.Movement.Position);
                if (dir.sqrMagnitude > 0.0001f) t.MoveBase?.TryToAddImpulse(dir.normalized, impulse);
            }
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
