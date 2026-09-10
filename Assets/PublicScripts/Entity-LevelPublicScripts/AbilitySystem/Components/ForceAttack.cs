using System;
using System.Collections.Generic;

namespace AbilitySystem.Components
{
    /// <summary>
    /// 让目标实体立即执行一次空目标强制攻击（forceChange、不可打断）：只播
    /// 攻击动画与 OnAttackSuccessfully 节拍（SP 消耗、事件桥照常），不索敌、
    /// 不造成直接伤害。配合 apply_animation_override(once) 播技能专属动画——
    /// 注册在前，同帧 TrySetAttackState 解析即消费待用覆盖。
    /// </summary>
    [RegisterComponent("ForceAttack")]
    public class ForceAttack : AbilityComponentBase
    {
        private Func<bool> _toSelf;
        private Func<string> _blackboardKey;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            var bb = ctx.sharedBlackboard;
            _toSelf = p.GetBoolLazy("toSelf", true, bb);
            _blackboardKey = p.GetStringLazy("blackboardKey", "", bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            List<Entity> targets = ResolveTargets(ctx);
            if (targets == null) return;

            for (int i = 0; i < targets.Count; i++)
            {
                targets[i]?.Attack?.TryToAttack(Array.Empty<Entity>(), true, false);
            }
        }

        private List<Entity> ResolveTargets(AbilityContext ctx)
        {
            if (_toSelf())
            {
                return ctx.entity != null ? new List<Entity> { ctx.entity } : null;
            }
            string key = _blackboardKey();
            if (string.IsNullOrEmpty(key) || ctx.sharedBlackboard == null) return null;
            return ctx.sharedBlackboard.Get<List<Entity>>(key, null);
        }
    }
}
