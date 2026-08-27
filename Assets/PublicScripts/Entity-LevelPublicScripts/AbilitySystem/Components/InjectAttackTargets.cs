using System;
using System.Collections.Generic;

namespace AbilitySystem.Components
{
    /// <summary>
    /// 把黑板实体列表注入攻击索敌候选的最前：先移除候选里已有的列表实体，再整体
    /// 插到索引 0——注入的实体无条件获得最高目标优先级（含视野外实体）。只作用于
    /// 自身的索敌（OnBeforeTargetSelect 经 runner 桥接派发，天然按实体隔离）。
    /// 与 EntityFilter（只能剔除、不能注入）互补。
    ///
    /// <para>用法：规则触发器设为 OnBeforeTargetSelect。触发时读保留键
    /// BlackboardKeys.AttackCandidates（仅派发窗口内存在，缺失=触发时机配错，
    /// 告警跳过）与源 blackboardKey 列表；源列表为空是无操作。</para>
    /// </summary>
    [RegisterComponent("InjectAttackTargets")]
    public class InjectAttackTargets : AbilityComponentBase
    {
        private Func<string> _blackboardKey;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            _blackboardKey = p.GetStringLazy("blackboardKey", "", ctx.sharedBlackboard);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (ctx.sharedBlackboard == null) return;

            List<Entity> candidates = ctx.sharedBlackboard.Get<List<Entity>>(BlackboardKeys.AttackCandidates, null);
            if (candidates == null)
            {
                OneShotWarn.WarnOnce("inject-attack-targets-window",
                    "InjectAttackTargets: attackCandidates key missing (rule must trigger on OnBeforeTargetSelect); skipping.");
                return;
            }

            List<Entity> inject = ReadInjectList(ctx);
            if (inject == null || inject.Count == 0) return;

            // 先移除候选里已有的列表实体（避免重复），再整体插到最前。
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                if (inject.Contains(candidates[i]))
                {
                    candidates.RemoveAt(i);
                }
            }
            candidates.InsertRange(0, inject);
        }

        private List<Entity> ReadInjectList(AbilityContext ctx)
        {
            string key = _blackboardKey();
            if (string.IsNullOrEmpty(key)) return null;
            return ctx.sharedBlackboard.Get<List<Entity>>(key, null);
        }
    }
}
