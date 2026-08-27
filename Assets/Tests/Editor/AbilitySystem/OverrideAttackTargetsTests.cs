using System.Collections.Generic;
using AbilitySystem.Components;
using NUnit.Framework;

namespace AbilitySystem.Tests
{
    /// <summary>攻击候选三步流水线（write_blackboard 副本快照 → filter →
    /// override_attack_targets 提交）的 EditMode 契约测试。列表元素用 null 占位：
    /// 两个组件都只搬运 Entity 引用、不触碰 Entity 成员，无需场景装配。</summary>
    public class OverrideAttackTargetsTests
    {
        [Test]
        public void WriteBlackboard_EventTargets_WritesCopyNotReference()
        {
            var live = new List<Entity> { null, null };
            var evt = new BeforeTargetSelectEvent { targets = live };
            var bb = new Blackboard();
            var ctx = new AbilityContext { currentEvent = evt, sharedBlackboard = bb };

            var comp = new WriteBlackboard();
            comp.OnInit(ctx, Params(("key", "cands"), ("source", "event"), ("path", "targets")));
            comp.OnTrigger(ctx);

            List<Entity> written = bb.Get<List<Entity>>("cands", null);
            Assert.That(written, Is.Not.Null);
            Assert.That(written.Count, Is.EqualTo(2));
            Assert.That(written, Is.Not.SameAs(live), "黑板拿到的是副本，不与 live 候选共享引用");
            written.Add(null);
            Assert.That(live.Count, Is.EqualTo(2), "改副本不得影响 live 候选");
        }

        [Test]
        public void WriteBlackboard_EventScalars_ReadBeforeTargetSelectFields()
        {
            var evt = new BeforeTargetSelectEvent { selectMaxNum = 3, selectMinNum = 1, sameComp = true };
            var bb = new Blackboard();
            var ctx = new AbilityContext { currentEvent = evt, sharedBlackboard = bb };

            var comp = new WriteBlackboard();
            comp.OnInit(ctx, Params(("key", "k"), ("source", "event"), ("path", "selectmaxnum")));
            comp.OnTrigger(ctx);
            Assert.That(bb.Get<object>("k", null), Is.EqualTo(3));

            comp = new WriteBlackboard();
            comp.OnInit(ctx, Params(("key", "k"), ("source", "event"), ("path", "samecomp")));
            comp.OnTrigger(ctx);
            Assert.That(bb.Get<object>("k", null), Is.EqualTo(true));
        }

        [Test]
        public void Override_ReplacesLiveCandidatesInPlace()
        {
            var live = new List<Entity> { null, null, null };
            var evt = new BeforeTargetSelectEvent { targets = live };
            var src = new List<Entity> { null };
            var bb = new Blackboard();
            bb.Set("src", src);
            var ctx = new AbilityContext { currentEvent = evt, sharedBlackboard = bb };

            var comp = new OverrideAttackTargets();
            comp.OnInit(ctx, Params(("blackboardKey", "src")));
            comp.OnTrigger(ctx);

            Assert.That(evt.targets, Is.SameAs(live), "清空重填同一列表对象，AttackBase 无需回写");
            Assert.That(live.Count, Is.EqualTo(1));
            Assert.That(live[0], Is.SameAs(src[0]));
        }

        [Test]
        public void Override_EmptySource_ClearsCandidates()
        {
            // 覆盖为空是合法语义：如"技能期间只打精英"且场上无精英 → 本次索敌无目标。
            var live = new List<Entity> { null, null };
            var evt = new BeforeTargetSelectEvent { targets = live };
            var bb = new Blackboard();
            bb.Set("src", new List<Entity>());
            var ctx = new AbilityContext { currentEvent = evt, sharedBlackboard = bb };

            var comp = new OverrideAttackTargets();
            comp.OnInit(ctx, Params(("blackboardKey", "src")));
            comp.OnTrigger(ctx);

            Assert.That(live.Count, Is.EqualTo(0));
        }

        [Test]
        public void Override_WrongEvent_WarnsAndSkips()
        {
            var live = new List<Entity> { null, null };
            var evt = new AttackInterruptEvent(); // 非 BeforeTargetSelectEvent
            var bb = new Blackboard();
            bb.Set("src", new List<Entity> { null });
            var ctx = new AbilityContext { currentEvent = evt, sharedBlackboard = bb };

            var comp = new OverrideAttackTargets();
            comp.OnInit(ctx, Params(("blackboardKey", "src")));
            comp.OnTrigger(ctx);

            Assert.That(live.Count, Is.EqualTo(2), "触发时机配错时告警跳过，不碰任何列表");
        }

        [Test]
        public void Override_MissingListKey_WarnsAndSkips()
        {
            var live = new List<Entity> { null, null };
            var evt = new BeforeTargetSelectEvent { targets = live };
            var bb = new Blackboard();
            var ctx = new AbilityContext { currentEvent = evt, sharedBlackboard = bb };

            var comp = new OverrideAttackTargets();
            comp.OnInit(ctx, Params(("blackboardKey", "no_such_key")));
            comp.OnTrigger(ctx);

            Assert.That(live.Count, Is.EqualTo(2), "源键缺失（流水线少 write 步骤）告警跳过");
        }

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
