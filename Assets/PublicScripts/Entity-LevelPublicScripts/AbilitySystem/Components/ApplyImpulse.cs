using System;
using System.Collections.Generic;
using UnityEngine;

namespace AbilitySystem.Components
{
    [RegisterComponent("ApplyImpulse")]
    public class ApplyImpulse : AbilityComponentBase
    {
        private Func<string> _targetMode;
        private Func<string> _blackboardKey;
        private Func<int> _strengthLevel;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            Blackboard bb = ctx.sharedBlackboard;
            _targetMode = p.GetStringLazy("targetMode", "eventTarget", bb);
            _blackboardKey = p.GetStringLazy("blackboardKey", "", bb);
            _strengthLevel = p.GetIntLazy("strengthLevel", 0, bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            Entity origin = ctx.entity;
            if (origin == null) return;

            List<Entity> targets = ResolveTargets(ctx);
            var seen = new HashSet<Entity>();
            for (int i = 0; i < targets.Count; i++)
            {
                Entity target = targets[i];
                if (target == null || target.MoveBase == null || !seen.Add(target)) continue;

                Vector2 direction = target.EntityPosition - origin.EntityPosition;
                if (direction.sqrMagnitude <= 0f) continue;
                target.MoveBase.TryToAddImpulse(direction.normalized, _strengthLevel());
            }
        }

        private List<Entity> ResolveTargets(AbilityContext ctx)
        {
            switch (Normalize(_targetMode()))
            {
                case "self":
                    return ctx.entity == null ? new List<Entity>() : new List<Entity> { ctx.entity };
                case "blackboard":
                case "blackboardentities":
                    string key = _blackboardKey();
                    return string.IsNullOrEmpty(key) || ctx.sharedBlackboard == null
                        ? new List<Entity>()
                        : ctx.sharedBlackboard.Get<List<Entity>>(key, null) ?? new List<Entity>();
                default:
                    Entity target = ctx.currentEvent is DamageEventBase damageEvent
                        ? damageEvent.target
                        : null;
                    return target == null ? new List<Entity>() : new List<Entity> { target };
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Trim().ToLowerInvariant();
        }
    }
}
