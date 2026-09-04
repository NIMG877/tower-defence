using AbilitySystem.Components;
using NUnit.Framework;
using UnityEngine;

namespace AbilitySystem.Tests
{
    /// <summary>ModifyCost（关卡费用增减，单一 amount 参数）的 EditMode 契约测试。
    /// LevelResourceManager 是进程级单例，用 SetCostMessage 复位后断言数值；
    /// 编辑器测试中 _start=false，ChangeCost 不触 UI。</summary>
    public class ModifyCostTests
    {
        private const int MaxCost = 99;

        [SetUp]
        public void SetUp()
        {
            LevelResourceManager.Manager.SetCostMessage(0, MaxCost, 1f);
        }

        [Test]
        public void PositiveAmount_GainsCost()
        {
            var comp = NewComp(6);
            comp.OnTrigger(NewCtx());

            Assert.That(CurrentCost(), Is.EqualTo(6));
        }

        [Test]
        public void NegativeAmount_SpendsCost()
        {
            LevelResourceManager.Manager.ChangeCost(20);
            var comp = NewComp(-2);
            comp.OnTrigger(NewCtx());

            Assert.That(CurrentCost(), Is.EqualTo(18));
        }

        [Test]
        public void SpendBelowZero_ClampsToZero()
        {
            LevelResourceManager.Manager.ChangeCost(3);
            var comp = NewComp(-10);
            comp.OnTrigger(NewCtx());

            Assert.That(CurrentCost(), Is.EqualTo(0));
        }

        [Test]
        public void GainAboveMax_ClampsToMaxCost()
        {
            var comp = NewComp(1000);
            comp.OnTrigger(NewCtx());

            Assert.That(CurrentCost(), Is.EqualTo(MaxCost));
        }

        [Test]
        public void OpName_IsAutoRegistered_AsSnakeCase()
        {
            // fang_s1 资产用 op: modify_cost；组件 Pascal 名是别名，二者都解析到同一 canonical。
            Assert.That(AbilityStepOpRegistry.ResolveCanonical("ModifyCost"), Is.EqualTo("modify_cost"));
            Assert.That(AbilityStepOpRegistry.ResolveCanonical("modify_cost"), Is.EqualTo("modify_cost"));
        }

        private static int CurrentCost() => LevelResourceManager.Manager.CostMessage.currentCost;

        private static ModifyCost NewComp(int amount)
        {
            var ctx = NewCtx();
            var comp = new ModifyCost();
            comp.OnInit(ctx, Params(("amount", amount.ToString())));
            return comp;
        }

        private static AbilityContext NewCtx() =>
            new AbilityContext { sharedBlackboard = new Blackboard() };

        private static ParamList Params(params (string key, string value)[] kv)
        {
            var entries = new ParamEntry[kv.Length];
            for (int i = 0; i < kv.Length; i++)
            {
                entries[i] = new ParamEntry
                {
                    key = kv[i].key,
                    value = kv[i].value,
                    fromBlackboard = false,
                    type = ParamValueType.String,
                };
            }
            return new ParamList { entries = entries };
        }
    }
}
