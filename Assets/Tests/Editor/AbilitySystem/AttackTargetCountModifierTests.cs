using AbilitySystem.Components;
using NUnit.Framework;

namespace AbilitySystem.Tests
{
    /// <summary>AttackTargetCountModifier（改写本次攻击 selectMaxNum/selectMinNum）
    /// 的 EditMode 契约测试。直接构造 BeforeTargetSelectEvent 挂 ctx.currentEvent，
    /// 断言事件字段被改写；ref 回写由 EntityAbilityRunner.OnBeforeTargetSelect 桥负责，
    /// 不在本测试范围。</summary>
    public class AttackTargetCountModifierTests
    {
        [Test]
        public void AddOne_SelectMaxNum_IncreasesThisAttackTargetCount()
        {
            var evt = NewEvent();
            var comp = NewComp(("fields", "selectmaxnum"), ("methods", "add"), ("values", "1"));

            comp.OnTrigger(Ctx(evt));

            Assert.That(evt.selectMaxNum, Is.EqualTo(2));
            Assert.That(evt.selectMinNum, Is.EqualTo(0));
        }

        [Test]
        public void SetMethod_AssignsAbsoluteValue()
        {
            var evt = NewEvent();
            var comp = NewComp(("fields", "selectmaxnum"), ("methods", "set"), ("values", "3"));

            comp.OnTrigger(Ctx(evt));

            Assert.That(evt.selectMaxNum, Is.EqualTo(3));
        }

        [Test]
        public void SelectMinNum_FieldRewritesMinOnly()
        {
            var evt = NewEvent();
            var comp = NewComp(("fields", "selectminnum"), ("methods", "add"), ("values", "2"));

            comp.OnTrigger(Ctx(evt));

            Assert.That(evt.selectMinNum, Is.EqualTo(2));
            Assert.That(evt.selectMaxNum, Is.EqualTo(1));
        }

        [Test]
        public void NonTargetSelectEvent_SkipsWithoutChange()
        {
            var evt = NewEvent();
            var ctx = Ctx(evt);
            ctx.currentEvent = new AttackSuccessfullyEvent();
            var comp = NewComp(("fields", "selectmaxnum"), ("methods", "add"), ("values", "1"));

            comp.OnTrigger(ctx);

            Assert.That(evt.selectMaxNum, Is.EqualTo(1));
        }

        [Test]
        public void UnknownField_SkipsWithoutChange()
        {
            var evt = NewEvent();
            var comp = NewComp(("fields", "nope"), ("methods", "add"), ("values", "1"));

            comp.OnTrigger(Ctx(evt));

            Assert.That(evt.selectMaxNum, Is.EqualTo(1));
        }

        [Test]
        public void OpName_IsAutoRegistered_AsSnakeCase()
        {
            // ansel_t1 资产用 op: attack_target_count_modifier；组件 Pascal 名是别名。
            Assert.That(AbilityStepOpRegistry.ResolveCanonical("AttackTargetCountModifier"), Is.EqualTo("attack_target_count_modifier"));
            Assert.That(AbilityStepOpRegistry.ResolveCanonical("attack_target_count_modifier"), Is.EqualTo("attack_target_count_modifier"));
        }

        private static BeforeTargetSelectEvent NewEvent() =>
            new BeforeTargetSelectEvent
            {
                targets = new System.Collections.Generic.List<Entity>(),
                selectMaxNum = 1,
                selectMinNum = 0,
                sameComp = true,
            };

        private static AttackTargetCountModifier NewComp(params (string key, string value)[] kv)
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
            var comp = new AttackTargetCountModifier();
            comp.OnInit(
                new AbilityContext { sharedBlackboard = new Blackboard() },
                new ParamList { entries = entries });
            return comp;
        }

        private static AbilityContext Ctx(AbilityEvent evt) =>
            new AbilityContext { sharedBlackboard = new Blackboard(), currentEvent = evt };
    }
}
