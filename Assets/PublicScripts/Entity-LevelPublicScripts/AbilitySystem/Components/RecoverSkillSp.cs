using System;
using UnityEngine;

namespace AbilitySystem.Components
{
    /// <summary>
    /// 立即向本实体的选中技能恢复 SP（"部署后立即获得 N 点技力"类天赋）。
    /// 单参数 <c>amount</c>。SP 恢复语义（上限/蓄能）由
    /// <see cref="EntityAbilityRunner.RecoverSkillSp"/> 统一处理；无已构建技能时
    /// 在该处报错跳过。
    /// </summary>
    [RegisterComponent("RecoverSkillSp")]
    public class RecoverSkillSp : AbilityComponentBase
    {
        private Func<int> _amount;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            _amount = p.GetIntLazy("amount", 0, ctx.sharedBlackboard);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (ctx.entity == null)
            {
                Debug.LogError("[RecoverSkillSp] detached execution has no entity; skipping.");
                return;
            }
            ctx.entity.AbilityRunner.RecoverSkillSp(_amount());
        }
    }
}
