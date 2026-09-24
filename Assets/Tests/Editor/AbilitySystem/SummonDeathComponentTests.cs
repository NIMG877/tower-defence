using System.Collections.Generic;
using NUnit.Framework;

namespace AbilitySystem.Tests
{
    /// <summary>召唤物死亡桥相关注册与事件枚举的契约测试。组件本体依赖
    /// Entity/EntityAttack/BuffController（随实体场景装配），
    /// 与既有组件同样不做 EditMode 单测，由 PlayMode 验证。</summary>
    public class SummonDeathComponentTests
    {
        [Test]
        public void SummonDeathBridgeOps_AreRegisteredUnderCanonicalSnakeCase()
        {
            Assert.That(AbilityStepOpRegistry.IsRegistered("watch_summon_death"), Is.True);
            Assert.That(AbilityStepOpRegistry.IsRegistered("update_buff"), Is.True);
            Assert.That(AbilityStepOpRegistry.IsRegistered("attack_candidate_override"), Is.True);

            // PascalCase 组件名是兼容别名：可解析，但不出现在 RegisteredOps。
            Assert.That(AbilityStepOpRegistry.ResolveCanonical("WatchSummonDeath"), Is.EqualTo("watch_summon_death"));
            Assert.That(AbilityStepOpRegistry.ResolveCanonical("UpdateBuff"), Is.EqualTo("update_buff"));
            Assert.That(AbilityStepOpRegistry.ResolveCanonical("AttackCandidateOverride"), Is.EqualTo("attack_candidate_override"));
            Assert.That(AbilityStepOpRegistry.RegisteredOps, Does.Contain("watch_summon_death"));
            Assert.That(AbilityStepOpRegistry.RegisteredOps, Does.Not.Contain("WatchSummonDeath"));
        }

        [Test]
        public void SummonDeathEvent_MapsToOnSummonDeath()
        {
            var evt = new SummonDeathEvent();
            Assert.That(evt.TriggerEvent, Is.EqualTo(TriggerEvent.OnSummonDeath));
        }

        [Test]
        public void TriggerEvent_OnSummonDeath_AppendedAfterOnTick()
        {
            // 既有 asset 按枚举序号序列化；后续事件只能追加在其后，不得改变既有触发序号（当前 OnSummonDeath=17）。
            Assert.That((int)TriggerEvent.OnTick, Is.EqualTo(16));
            Assert.That((int)TriggerEvent.OnSummonDeath, Is.EqualTo(17));
        }

        [Test]
        public void ConditionEvaluator_ReadsNumericAndFlagBlackboardKeys()
        {
            // 黑板是 object 存储：条件键可能是数字计数（write_blackboard add 维护），
            // 也可能是字符串标志（random_roll 的 "True"/"False"）。按 object 读出转
            // 字符串比较，数字键不得在求值处抛 InvalidCastException。
            var bb = new Blackboard();
            bb.Set("count", 3);
            bb.Set("rate", 0.6f);
            bb.Set("flag", "True");
            var ctx = new ConditionEvalContext { sharedBlackboard = bb };

            Assert.That(Evaluate(ctx, ConditionOp.Equal, "count", "3"), Is.True);
            Assert.That(Evaluate(ctx, ConditionOp.NotEqual, "count", "0"), Is.True);
            Assert.That(Evaluate(ctx, ConditionOp.Greater, "count", "2"), Is.True);
            Assert.That(Evaluate(ctx, ConditionOp.LessOrEqual, "count", "2"), Is.False);
            Assert.That(Evaluate(ctx, ConditionOp.LessOrEqual, "rate", "0.6"), Is.True);
            Assert.That(Evaluate(ctx, ConditionOp.Equal, "flag", "True"), Is.True);
            // 缺键 = ""：Equal 任何非空 rightValue 为假，NotEqual 为真。
            Assert.That(Evaluate(ctx, ConditionOp.Equal, "missing", "True"), Is.False);
            Assert.That(Evaluate(ctx, ConditionOp.NotEqual, "missing", "True"), Is.True);
        }

        private static bool Evaluate(ConditionEvalContext ctx, ConditionOp op, string leftKey, string rightValue)
        {
            var groups = new List<ConditionGroup>
            {
                new ConditionGroup
                {
                    units = new List<ConditionUnit>
                    {
                        new ConditionUnit { op = op, leftKey = leftKey, rightValue = rightValue },
                    },
                },
            };
            return ConditionEvaluator.Evaluate(groups, ctx);
        }
    }
}
