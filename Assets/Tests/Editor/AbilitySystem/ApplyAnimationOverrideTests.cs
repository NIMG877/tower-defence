using System.Collections.Generic;
using AbilitySystem.Components;
using NUnit.Framework;
using UnityEngine;

namespace AbilitySystem.Tests
{
    /// <summary>ApplyAnimationOverride once 模式（注册待用一次性覆盖，不切状态）的 EditMode 契约测试。
    /// 机器 headless（无 Spine skeleton）：注册路径不需要 skeleton；断言经 outputKey 黑板记录观测。</summary>
    public class ApplyAnimationOverrideTests
    {
        private readonly List<GameObject> _scratch = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _scratch.Count; i++)
            {
                Object.DestroyImmediate(_scratch[i]);
            }
            _scratch.Clear();
        }

        [Test]
        public void Once_RegistersHandlePerTarget_AndRecordsToOutputKey()
        {
            Entity e1 = NewEntityWithMachine(), e2 = NewEntityWithMachine();
            var bb = new Blackboard();
            bb.Set("targets", new List<Entity> { e1, e2 });
            var ctx = new AbilityContext { sharedBlackboard = bb };

            var comp = new ApplyAnimationOverride();
            comp.OnInit(ctx, Params(
                ("mode", "once"),
                ("toSelf", "false"),
                ("blackboardKey", "targets"),
                ("slots", "AttackBegin,AttackRemote,AttackEnd"),
                ("resources", "Talent_Begin,Talent_Attack,Talent_End"),
                ("outputKey", "recs")));
            comp.OnTrigger(ctx);

            List<AnimationOverrideRecord> recs = bb.Get<List<AnimationOverrideRecord>>("recs", null);
            Assert.That(recs, Is.Not.Null, "outputKey 记录待写回黑板");
            Assert.That(recs.Count, Is.EqualTo(2));
            Assert.That(recs[0].Target, Is.SameAs(e1));
            Assert.That(recs[0].Handle, Is.Not.Null, "一次性覆盖条目返回可撤销 handle");
            Assert.That(recs[1].Target, Is.SameAs(e2));
            Assert.That(recs[1].Handle, Is.Not.Null);
        }

        [Test]
        public void Once_TargetWithoutMachine_IsSkipped()
        {
            Entity withMachine = NewEntityWithMachine();
            Entity bare = NewEntity(); // 无 AnimationMachine
            var bb = new Blackboard();
            bb.Set("targets", new List<Entity> { bare, withMachine });
            var ctx = new AbilityContext { sharedBlackboard = bb };

            var comp = new ApplyAnimationOverride();
            comp.OnInit(ctx, Params(
                ("mode", "once"),
                ("toSelf", "false"),
                ("blackboardKey", "targets"),
                ("slots", "Idle"),
                ("resources", "anim_i"),
                ("outputKey", "recs")));
            comp.OnTrigger(ctx);

            List<AnimationOverrideRecord> recs = bb.Get<List<AnimationOverrideRecord>>("recs", null);
            Assert.That(recs.Count, Is.EqualTo(1), "无机器目标跳过，不产生记录");
            Assert.That(recs[0].Target, Is.SameAs(withMachine));
        }

        private Entity NewEntity()
        {
            var go = new GameObject("anim_override_test_entity");
            _scratch.Add(go);
            return go.AddComponent<Entity>();
        }

        private Entity NewEntityWithMachine()
        {
            Entity entity = NewEntity();
            entity.entityAM = entity.gameObject.AddComponent<AnimationMachine>();
            return entity;
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
