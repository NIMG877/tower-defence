using System;
using System.Collections.Generic;

namespace AbilitySystem.Components
{
    [RegisterComponent("DestroyAbnormalState")]
    public class DestroyAbnormalState : AbilityComponentBase
    {
        private Func<string> _inputTargetKey;
        private Func<string> _inputStateKey;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            Blackboard bb = ctx.sharedBlackboard;
            _inputTargetKey = p.GetStringLazy("inputTarget", "", bb);
            _inputStateKey = p.GetStringLazy("inputState", "", bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            string targetKey = _inputTargetKey();
            string stateKey = _inputStateKey();
            if (ctx.sharedBlackboard == null || string.IsNullOrEmpty(targetKey) || string.IsNullOrEmpty(stateKey)) return;

            var targets = ctx.sharedBlackboard.Get<List<Entity>>(targetKey, null);
            var states = ctx.sharedBlackboard.Get<List<int>>(stateKey, null);
            if (targets == null || states == null) return;

            int count = Math.Min(targets.Count, states.Count);
            for (int i = 0; i < count; i++)
            {
                Entity target = targets[i];
                int type = states[i];
                if (target == null || target.buffController == null || !BuffController.IsValidAbnormalType(type)) continue;
                target.buffController.TryRemoveAbnormalState(type);
            }

            ctx.sharedBlackboard.Remove(targetKey);
            ctx.sharedBlackboard.Remove(stateKey);
        }
    }
}
