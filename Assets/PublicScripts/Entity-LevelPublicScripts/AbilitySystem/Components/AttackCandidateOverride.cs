using System;
using System.Collections.Generic;

namespace AbilitySystem.Components
{
    /// <summary>
    /// 用黑板实体列表整表覆盖攻击索敌候选：清空事件的 live 候选列表，再把源列表
    /// 全部填入（Clear + AddRange——AttackBase 后续继续使用同一列表对象，无需回写）。
    /// 覆盖为空列表是合法语义（如"技能期间只打精英"且场上无精英→本次索敌无目标）。
    ///
    /// <para>用法：规则触发器设为 OnBeforeTargetSelect。典型三步流水线：
    /// write_blackboard（source=event path=targets，把候选副本快照挂黑板）→
    /// filter_targets（在副本上筛）→ 本组件（把筛后副本覆盖回 live 候选）。
    /// 本组件是流水线里唯一触碰 live 列表的步骤。</para>
    /// </summary>
    [RegisterComponent("AttackCandidateOverride")]
    public class AttackCandidateOverride : AbilityComponentBase
    {
        private Func<string> _blackboardKey;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            _blackboardKey = p.GetStringLazy("blackboardKey", "", ctx.sharedBlackboard);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (ctx.sharedBlackboard == null) return;

            if (!(ctx.currentEvent is BeforeTargetSelectEvent evt))
            {
                OneShotWarn.WarnOnce("attack-candidate-override-event",
                    "AttackCandidateOverride: current event is not BeforeTargetSelectEvent " +
                    "(rule must trigger on OnBeforeTargetSelect); skipping.");
                return;
            }

            string key = _blackboardKey();
            if (string.IsNullOrEmpty(key))
            {
                OneShotWarn.WarnOnce("attack-candidate-override-key",
                    "AttackCandidateOverride: blackboardKey is required; skipping.");
                return;
            }

            List<Entity> source = ctx.sharedBlackboard.Get<List<Entity>>(key, null);
            if (source == null)
            {
                OneShotWarn.WarnOnce("attack-candidate-override:" + key,
                    $"AttackCandidateOverride: blackboard key '{key}' holds no entity list; skipping.");
                return;
            }

            evt.targets.Clear();
            evt.targets.AddRange(source);
        }
    }
}
