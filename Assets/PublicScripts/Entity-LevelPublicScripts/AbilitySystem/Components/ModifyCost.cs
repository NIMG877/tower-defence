using System;
using UnityEngine;

namespace AbilitySystem.Components
{
    /// <summary>
    /// 修改关卡部署费用。单个参数 <c>amount</c>：正数获得费用，负数消耗费用。
    /// 立即生效的全局变更（不走实体/黑板），区间裁剪由
    /// <see cref="LevelResourceManager.ChangeCost"/> 负责，飘字展示实际生效量。
    /// </summary>
    [RegisterComponent("ModifyCost")]
    public class ModifyCost : AbilityComponentBase
    {
        private Func<int> _amount;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            _amount = p.GetIntLazy("amount", 0, ctx.sharedBlackboard);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            int applied = LevelResourceManager.Manager.ChangeCost(_amount());
            ShowCostText(ctx, applied);
        }

        /// <summary>
        /// 飘字锚定触发实体，正负各用一种样式；获得费用时播 "get_cost" 音效。
        /// 生效量为 0（满费/零费 clamp）则无变化可报，不显示。
        /// virtual 仅为 EditMode 测试提供无 UI/音频替身
        /// （EditMode 下 <see cref="MyUI.LevelMessagePanel"/> 单例无法构造）。
        /// </summary>
        protected virtual void ShowCostText(AbilityContext ctx, int applied)
        {
            if (applied == 0)
                return;
            MyUI.LevelMessagePanel.Panel.ShowText(
                ctx.entity.transform.position,
                applied > 0 ? MyUI.CombatTextKind.AddCost : MyUI.CombatTextKind.ReduceCost,
                Mathf.Abs(applied));
            if (applied > 0)
                AudioManager.Manager.PlayAudio("get_cost");
        }
    }
}
