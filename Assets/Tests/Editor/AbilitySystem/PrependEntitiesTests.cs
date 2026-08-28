using System.Collections.Generic;
using AbilitySystem.Components;
using NUnit.Framework;
using UnityEngine;

namespace AbilitySystem.Tests
{
    /// <summary>PrependEntities（源列表去重前插到目标列表最前，原地保对象）的 EditMode 契约测试。
    /// 去重断言需要可区分的 Entity 引用（null==null 区分不了），用空 GameObject 挂 Entity。</summary>
    public class PrependEntitiesTests
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
        public void Prepend_SourceEntities_GoInFrontInOrder()
        {
            Entity b1 = NewEntity(), b2 = NewEntity(), c1 = NewEntity(), c2 = NewEntity();
            var target = new List<Entity> { c1, c2 };
            var bb = new Blackboard();
            bb.Set("cands", target);
            bb.Set("bubbles", new List<Entity> { b1, b2 });
            var ctx = new AbilityContext { sharedBlackboard = bb };

            var comp = new PrependEntities();
            comp.OnInit(ctx, Params(("blackboardKey", "cands"), ("sourceKey", "bubbles")));
            comp.OnTrigger(ctx);

            Assert.That(target, Is.SameAs(bb.Get<List<Entity>>("cands", null)), "原地修改，目标列表对象不变");
            Assert.That(target, Is.EqualTo(new List<Entity> { b1, b2, c1, c2 }));
        }

        [Test]
        public void Prepend_Dedup_IsPromoteToFront_NotDuplicate()
        {
            // 视界内泡泡本就在候选里：提权语义 = 移到最前 + 原位移除，而非保留在后面或重复计入。
            Entity b1 = NewEntity(), b2 = NewEntity(), c1 = NewEntity();
            var target = new List<Entity> { c1, b1 };
            var bb = new Blackboard();
            bb.Set("cands", target);
            bb.Set("bubbles", new List<Entity> { b1, b2, b1 }); // 源内重复同样只取一次
            var ctx = new AbilityContext { sharedBlackboard = bb };

            var comp = new PrependEntities();
            comp.OnInit(ctx, Params(("blackboardKey", "cands"), ("sourceKey", "bubbles")));
            comp.OnTrigger(ctx);

            Assert.That(target, Is.EqualTo(new List<Entity> { b1, b2, c1 }));
        }

        [Test]
        public void Prepend_SkipsNullSourceEntries_TargetNullsUntouched()
        {
            Entity b1 = NewEntity(), c1 = NewEntity();
            var target = new List<Entity> { null, c1 };
            var bb = new Blackboard();
            bb.Set("cands", target);
            bb.Set("bubbles", new List<Entity> { null, b1 });
            var ctx = new AbilityContext { sharedBlackboard = bb };

            var comp = new PrependEntities();
            comp.OnInit(ctx, Params(("blackboardKey", "cands"), ("sourceKey", "bubbles")));
            comp.OnTrigger(ctx);

            Assert.That(target, Is.EqualTo(new List<Entity> { b1, null, c1 }));
        }

        [Test]
        public void Prepend_EmptySource_IsNoOp()
        {
            Entity c1 = NewEntity();
            var target = new List<Entity> { c1 };
            var bb = new Blackboard();
            bb.Set("cands", target);
            bb.Set("bubbles", new List<Entity>());
            var ctx = new AbilityContext { sharedBlackboard = bb };

            var comp = new PrependEntities();
            comp.OnInit(ctx, Params(("blackboardKey", "cands"), ("sourceKey", "bubbles")));
            comp.OnTrigger(ctx);

            Assert.That(target, Is.EqualTo(new List<Entity> { c1 }));
        }

        [Test]
        public void Prepend_MissingTargetKey_WarnsAndSkips()
        {
            Entity b1 = NewEntity();
            var source = new List<Entity> { b1 };
            var bb = new Blackboard();
            bb.Set("bubbles", source);
            var ctx = new AbilityContext { sharedBlackboard = bb };

            var comp = new PrependEntities();
            comp.OnInit(ctx, Params(("blackboardKey", "no_such_key"), ("sourceKey", "bubbles")));
            comp.OnTrigger(ctx);

            Assert.That(source, Is.EqualTo(new List<Entity> { b1 }), "目标键缺失（流水线少 write 步骤）告警跳过");
        }

        [Test]
        public void Prepend_MissingSourceKey_WarnsAndSkips()
        {
            Entity c1 = NewEntity();
            var target = new List<Entity> { c1 };
            var bb = new Blackboard();
            bb.Set("cands", target);
            var ctx = new AbilityContext { sharedBlackboard = bb };

            var comp = new PrependEntities();
            comp.OnInit(ctx, Params(("blackboardKey", "cands"), ("sourceKey", "no_such_key")));
            comp.OnTrigger(ctx);

            Assert.That(target, Is.EqualTo(new List<Entity> { c1 }), "源键缺失告警跳过，不碰目标列表");
        }

        private Entity NewEntity()
        {
            var go = new GameObject("prepend_test_entity");
            _scratch.Add(go);
            return go.AddComponent<Entity>();
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
