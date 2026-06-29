using System;
using System.Collections.Generic;

namespace AbilitySystem.Components
{
    [RegisterComponent("DestroyEntity")]
    public class DestroyEntity : AbilityComponentBase
    {
        private Func<bool> _toSelf;
        private Func<string> _inputTargetKey;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            Blackboard bb = ctx.sharedBlackboard;
            _toSelf = p.GetBoolLazy("toSelf", true, bb);
            _inputTargetKey = p.GetStringLazy("blackboardKey", "", bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            List<Entity> targets = ResolveTargets(ctx);
            if (targets == null) return;

            var seen = new HashSet<Entity>();
            for (int i = 0; i < targets.Count; i++)
            {
                Entity target = targets[i];
                if (target == null || !seen.Add(target) || target.Stats == null || !target.Stats.IsActive) continue;
                target.Die();
            }
        }

        private List<Entity> ResolveTargets(AbilityContext ctx)
        {
            string key = _inputTargetKey();
            if (!string.IsNullOrEmpty(key))
                return ctx.sharedBlackboard?.Get<List<Entity>>(key, null);
            return _toSelf() && ctx.entity != null ? new List<Entity> { ctx.entity } : null;
        }
    }
}
