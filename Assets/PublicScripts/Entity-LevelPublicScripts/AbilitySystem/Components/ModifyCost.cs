using System;

namespace AbilitySystem.Components
{
    /// <summary>
    /// 修改关卡部署费用。单个参数 <c>amount</c>：正数获得费用，负数消耗费用。
    /// 立即生效的全局变更（不走实体/黑板），区间裁剪与 UI 刷新由
    /// <see cref="LevelResourceManager.ChangeCost"/> 负责。
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
            LevelResourceManager.Manager.ChangeCost(_amount());
        }
    }
}
