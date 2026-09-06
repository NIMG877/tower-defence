using AbilitySystem.Components;
using NUnit.Framework;
using UnityEngine;

namespace AbilitySystem.Tests
{
    /// <summary>ModifyCost（关卡费用增减，单一 amount 参数）的 EditMode 契约测试。
    /// LevelResourceManager 是进程级单例，用 SetCostMessage 复位后断言数值；
    /// 编辑器测试中 _start=false，ChangeCost 不触 UI；飘字缝被测试子类覆写，
    /// 记录实际生效量供断言。</summary>
    public class ModifyCostTests
    {
        private const int MaxCost = 99;

        private sealed class RecordingModifyCost : ModifyCost
        {
            public int LastApplied;
            public int ShowTextCalls;

            protected override void ShowCostText(AbilityContext ctx, int applied)
            {
                LastApplied = applied;
                ShowTextCalls++;
            }
        }

        [SetUp]
        public void SetUp()
        {
            LevelResourceManager.Manager.SetCostMessage(0, MaxCost, 1f);
        }

        [Test]
        public void PositiveAmount_GainsCost()
        {
            RecordingModifyCost comp = NewComp(6);
            comp.OnTrigger(NewCtx());

            Assert.That(CurrentCost(), Is.EqualTo(6));
            Assert.That(comp.LastApplied, Is.EqualTo(6));
            Assert.That(comp.ShowTextCalls, Is.EqualTo(1));
        }

        [Test]
        public void NegativeAmount_SpendsCost()
        {
            LevelResourceManager.Manager.ChangeCost(20);
            RecordingModifyCost comp = NewComp(-2);
            comp.OnTrigger(NewCtx());

            Assert.That(CurrentCost(), Is.EqualTo(18));
            Assert.That(comp.LastApplied, Is.EqualTo(-2));
        }

        [Test]
        public void SpendBelowZero_ClampsToZero()
        {
            LevelResourceManager.Manager.ChangeCost(3);
            RecordingModifyCost comp = NewComp(-10);
            comp.OnTrigger(NewCtx());

            Assert.That(CurrentCost(), Is.EqualTo(0));
            Assert.That(comp.LastApplied, Is.EqualTo(-3));
        }

        [Test]
        public void GainAboveMax_ClampsToMaxCost()
        {
            RecordingModifyCost comp = NewComp(1000);
            comp.OnTrigger(NewCtx());

            Assert.That(CurrentCost(), Is.EqualTo(MaxCost));
            Assert.That(comp.LastApplied, Is.EqualTo(MaxCost));
        }

        [Test]
        public void ZeroApplied_NoFloatingText()
        {
            // 已在 max：回费 clamp 到 0 生效量，无变化可报，不飘字。
            LevelResourceManager.Manager.ChangeCost(MaxCost);
            RecordingModifyCost comp = NewComp(8);
            comp.OnTrigger(NewCtx());

            Assert.That(CurrentCost(), Is.EqualTo(MaxCost));
            Assert.That(comp.LastApplied, Is.EqualTo(0));
            Assert.That(comp.ShowTextCalls, Is.EqualTo(0));
        }

        [Test]
        public void OpName_IsAutoRegistered_AsSnakeCase()
        {
            // fang_s1 资产用 op: modify_cost；组件 Pascal 名是别名，二者都解析到同一 canonical。
            Assert.That(AbilityStepOpRegistry.ResolveCanonical("ModifyCost"), Is.EqualTo("modify_cost"));
            Assert.That(AbilityStepOpRegistry.ResolveCanonical("modify_cost"), Is.EqualTo("modify_cost"));
        }

        private static int CurrentCost() => LevelResourceManager.Manager.CostMessage.currentCost;

        private static RecordingModifyCost NewComp(int amount)
        {
            var ctx = NewCtx();
            var comp = new RecordingModifyCost();
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
