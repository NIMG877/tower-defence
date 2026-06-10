using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkillSystem.Components
{
    /// <summary>
    /// Applies buffs to one or more entities. When <c>blackboardKey</c> is empty,
    /// the existing single-target behavior is preserved (self or event target).
    /// When <c>blackboardKey</c> is set, the component reads a <c>List&lt;Entity&gt;</c>
    /// from that key and applies the buff to each entry.
    /// See docs/superpowers/specs/2026-06-08-skill-blackboard-component-pattern.md.
    /// </summary>
    [RegisterComponent("ApplyBuff")]
    public class ApplyBuffComponent : ISkillComponent
    {
        private string _buffId = "skill_buff";
        private float _priority = -10f;
        private bool _toSelf = true;
        private string _inputKey;            // blackboard key (optional)
        private BuffType[] _types = Array.Empty<BuffType>();
        private float[] _values = Array.Empty<float>();

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _types        = BuffParamParser.ParseBuffTypes(p.GetString("buffTypes", ""));
            _values       = BuffParamParser.ParseFloats(p.GetString("buffValues", ""));
            _buffId       = p.GetString("buffId", "skill_buff");
            _priority     = p.GetFloat("priority", -10f);
            _toSelf       = p.GetBool("toSelf", true);
            _inputKey     = p.GetString("blackboardKey", "");
        }

        public void OnTrigger(SkillContext ctx)
        {
            Debug.Log($"ApplyBuffComponent triggered: buffId={_buffId}, types=[{string.Join(", ", _types)}], values=[{string.Join(", ", _values)}], priority={_priority}, toSelf={_toSelf}, inputKey={_inputKey}");
            if (ctx.entity == null) return;
            if (_types.Length == 0) return;

            List<Entity> targets = ResolveTargets(ctx);
            if (targets == null) return;

            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || t.buffController == null) continue;
                t.buffController.CreateBuff(_types, null, _buffId, _values, _priority, true);
            }
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }

        // Blackboard-read path: when blackboardKey is set, the target list is read
        // from the key. If the key is missing/empty, the component skips silently —
        // this means an upstream writer hasn't run yet, which is normal in some
        // dispatch orders.
        private List<Entity> ResolveTargets(SkillContext ctx)
        {
            if (!string.IsNullOrEmpty(_inputKey))
            {
                return ctx.blackboard.Get<List<Entity>>(_inputKey, null);
            }

            // Original single-target behavior preserved for backward compatibility.
            var t = _toSelf
                ? ctx.entity
                : (ctx.currentEvent is BeforeTakeDamageEvent btd ? btd.target : null);
            if (t == null || t.buffController == null) return null;
            return new List<Entity> { t };
        }
    }
}
