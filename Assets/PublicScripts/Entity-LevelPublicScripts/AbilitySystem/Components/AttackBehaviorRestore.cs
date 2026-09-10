using System;
using System.Collections.Generic;

namespace AbilitySystem.Components
{
    [RegisterComponent("AttackBehaviorRestore")]
    public class AttackBehaviorRestore : AbilityComponentBase
    {
        private Func<bool> _toSelf;
        private Func<string> _blackboardKey;
        private Func<string> _inputKey;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            var bb = ctx.sharedBlackboard;
            _toSelf = p.GetBoolLazy("toSelf", true, bb);
            _blackboardKey = p.GetStringLazy("blackboardKey", "", bb);
            _inputKey = p.GetStringLazy("inputKey", "", bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (ctx.sharedBlackboard == null) return;
            string inputKey = _inputKey();
            if (string.IsNullOrEmpty(inputKey)) return;

            List<AttackBehaviorSnapshot> snapshots =
                ctx.sharedBlackboard.Get<List<AttackBehaviorSnapshot>>(inputKey, null);
            if (snapshots == null) return;

            List<Entity> targets = ResolveTargets(ctx);
            if (targets == null || targets.Count == 0) return;
            var targetSet = new HashSet<Entity>(targets);

            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                AttackBehaviorSnapshot snapshot = snapshots[i];
                if (snapshot == null || !targetSet.Contains(snapshot.Target)) continue;
                if (snapshot.Target != null && snapshot.Target.Attack != null)
                {
                    snapshot.Target.Attack.DamageType = snapshot.DamageType;
                    snapshot.Target.Attack.TargetPriority = snapshot.TargetPriority;
                }
                snapshots.RemoveAt(i);
            }

            if (snapshots.Count == 0) ctx.sharedBlackboard.Remove(inputKey);
            else ctx.sharedBlackboard.Set(inputKey, snapshots);
        }

        private List<Entity> ResolveTargets(AbilityContext ctx)
        {
            if (_toSelf())
            {
                return ctx.entity != null ? new List<Entity> { ctx.entity } : null;
            }
            string key = _blackboardKey();
            if (string.IsNullOrEmpty(key)) return null;
            return ctx.sharedBlackboard.Get<List<Entity>>(key, null);
        }
    }
}
