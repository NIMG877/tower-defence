using System;
using System.Collections.Generic;

namespace AbilitySystem.Components
{
    /// <summary>
    /// 切换目标实体的逻辑状态机状态（Entity.StateMachine.TrySetState 的数据化），
    /// 表现层（AnimationMachine）经 StateChanged 被动播对应槽位动画。
    /// 目标解析双通道：blackboardKey 优先（黑板 List&lt;Entity&gt;），未配则 toSelf。
    /// Cast 可通过 castMode 选择 OneShot（默认，播完回 Idle）或 Sustained（循环到显式转出）。
    /// state 名非法是配置错误 → OneShotWarn 跳过；切换失败（优先级不够/ban 命中/已 Die）
    /// 是合法竞争结果 → 静默。7 态全开放：Attack/Die 的专属通道语义（攻击帧回调登记/
    /// Entity.Die 完整死亡链）由配置侧负责。
    /// </summary>
    [RegisterComponent("SetEntityState")]
    public class SetEntityState : AbilityComponentBase
    {
        private Func<string> _state;
        private Func<string> _castMode;
        private Func<bool> _force;
        private Func<bool> _toSelf;
        private Func<string> _blackboardKey;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            Blackboard bb = ctx.sharedBlackboard;
            _state = p.GetStringLazy("state", "", bb);
            _castMode = p.GetStringLazy("castMode", nameof(CastMode.OneShot), bb);
            _force = p.GetBoolLazy("force", false, bb);
            _toSelf = p.GetBoolLazy("toSelf", true, bb);
            _blackboardKey = p.GetStringLazy("blackboardKey", "", bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            List<Entity> targets = ResolveTargets(ctx);
            if (targets == null) return;

            string stateName = _state();
            if (!Enum.TryParse(stateName, out EntityState state))
            {
                OneShotWarn.WarnOnce(
                    "set-entity-state:" + stateName,
                    $"SetEntityState: unknown state '{stateName}'; expected one of " +
                    "Default/Idle/Move/Attack/Start/Cast/Die. Skipping.");
                return;
            }

            CastMode castMode = CastMode.OneShot;
            if (state == EntityState.Cast)
            {
                string castModeName = _castMode();
                if (string.Equals(castModeName, nameof(CastMode.OneShot), StringComparison.OrdinalIgnoreCase))
                {
                    castMode = CastMode.OneShot;
                }
                else if (string.Equals(castModeName, nameof(CastMode.Sustained), StringComparison.OrdinalIgnoreCase))
                {
                    castMode = CastMode.Sustained;
                }
                else
                {
                    OneShotWarn.WarnOnce(
                        "set-entity-state:cast-mode:" + castModeName,
                        $"SetEntityState: unknown castMode '{castModeName}'; expected OneShot or Sustained. Skipping.");
                    return;
                }
            }

            bool force = _force();
            for (int i = 0; i < targets.Count; i++)
            {
                Entity target = targets[i];
                if (target == null) continue;
                if (state == EntityState.Cast)
                    target.StateMachine.TrySetCastState(force, castMode);
                else
                    target.StateMachine.TrySetState(state, force);
            }
        }

        private List<Entity> ResolveTargets(AbilityContext ctx)
        {
            string key = _blackboardKey();
            if (!string.IsNullOrEmpty(key))
                return ctx.sharedBlackboard?.Get<List<Entity>>(key, null);
            return _toSelf() && ctx.entity != null ? new List<Entity> { ctx.entity } : null;
        }
    }
}
