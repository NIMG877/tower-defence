using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AbilitySystem.Tests
{
    /// <summary>KeyEqual/KeyNotEqual（键对键同一性比较）的 EditMode 契约测试。
    /// 生产形态：WriteBlackboard path=origin 写 List&lt;Entity&gt;（单元素），
    /// SpawnEntity 召唤者协议存裸 Entity——单元素列表解包后按引用比较。</summary>
    public class ConditionEvaluatorTests
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
        public void KeyEqual_BareEntities_ReferenceEquality()
        {
            Entity summoner = NewEntity();
            var bb = new Blackboard();
            bb.Set("origin", NewEntity());
            bb.Set("summoner", summoner);

            Assert.That(Eval(Unit("origin", "summoner"), bb), Is.False, "不同实体引用不相等");
            bb.Set("origin", summoner);
            Assert.That(Eval(Unit("origin", "summoner"), bb), Is.True, "同一引用相等");
        }

        [Test]
        public void KeyEqual_UnwrapsSingletonEntityList_VsBareEntity()
        {
            // 生产形态：origin 侧是 ToEntityList 的单元素列表，summoner 侧是裸 Entity。
            Entity summoner = NewEntity();
            var bb = new Blackboard();
            bb.Set("origin", new List<Entity> { summoner });
            bb.Set("summoner", summoner);

            Assert.That(Eval(Unit("origin", "summoner"), bb), Is.True, "单元素列表解包后与裸实体比较");
        }

        [Test]
        public void KeyEqual_MissingKey_EitherSide_IsUnequal()
        {
            Entity summoner = NewEntity();
            var bb = new Blackboard();
            bb.Set("summoner", summoner);

            Assert.That(Eval(Unit("origin", "summoner"), bb), Is.False, "左键缺失 = 不等（缺失=不等惯例）");

            var bothMissing = new Blackboard();
            Assert.That(Eval(Unit("origin", "summoner"), bothMissing), Is.False, "双侧缺失仍不等，null==null 不成立");
        }

        [Test]
        public void KeyEqual_NullOriginMember_IsUnequal()
        {
            // origin 为 null 的事件（无归属伤害）：ToEntityList 写入 null 成员。
            Entity summoner = NewEntity();
            var bb = new Blackboard();
            bb.Set("origin", new List<Entity> { null });
            bb.Set("summoner", summoner);

            Assert.That(Eval(Unit("origin", "summoner"), bb), Is.False);
        }

        [Test]
        public void KeyEqual_MultiElementList_DoesNotMatchSingleEntity()
        {
            Entity a = NewEntity(), b = NewEntity();
            var bb = new Blackboard();
            bb.Set("origin", new List<Entity> { a, b });
            bb.Set("summoner", a);

            Assert.That(Eval(Unit("origin", "summoner"), bb), Is.False, "多元素列表不解包，不与单实体相等");
        }

        [Test]
        public void KeyEqual_MissingRightKey_IsConfigError_ReturnsFalse()
        {
            // KeyEqual 没配 rightKey 是配线笔误（不是运行期缺数据），报错并判否。
            Entity summoner = NewEntity();
            var bb = new Blackboard();
            bb.Set("origin", summoner);
            bb.Set("summoner", summoner);

            Assert.That(Eval(new ConditionUnit { op = ConditionOp.KeyEqual, leftKey = "origin", rightKey = "" }, bb),
                Is.False);
        }

        [Test]
        public void KeyEqual_ValueTypes_BoxedEquality()
        {
            var bb = new Blackboard();
            bb.Set("count", 3f);
            bb.Set("expect", 3f);

            Assert.That(Eval(Unit("count", "expect"), bb), Is.True, "值类型按装箱 Equals 值相等");

            bb.Set("expect", 4f);
            Assert.That(Eval(Unit("count", "expect"), bb), Is.False);
        }

        [Test]
        public void KeyNotEqual_NegatesKeyEqual()
        {
            Entity a = NewEntity(), b = NewEntity();
            var bb = new Blackboard();
            bb.Set("origin", a);
            bb.Set("summoner", b);

            Assert.That(Eval(NotUnit("origin", "summoner"), bb), Is.True, "不同引用 → NotEqual 成立");

            bb.Set("summoner", a);
            Assert.That(Eval(NotUnit("origin", "summoner"), bb), Is.False, "同引用 → NotEqual 不成立");

            bb.Remove("summoner");
            Assert.That(Eval(NotUnit("origin", "summoner"), bb), Is.True, "缺失 = 不等 → NotEqual 成立");
        }

        private static ConditionUnit Unit(string leftKey, string rightKey) =>
            new ConditionUnit { op = ConditionOp.KeyEqual, leftKey = leftKey, rightKey = rightKey };

        private static ConditionUnit NotUnit(string leftKey, string rightKey) =>
            new ConditionUnit { op = ConditionOp.KeyNotEqual, leftKey = leftKey, rightKey = rightKey };

        private static bool Eval(ConditionUnit unit, Blackboard bb)
        {
            return ConditionEvaluator.Evaluate(
                new List<ConditionGroup>
                {
                    new ConditionGroup { units = new List<ConditionUnit> { unit } },
                },
                new ConditionEvalContext { sharedBlackboard = bb });
        }

        private Entity NewEntity()
        {
            var go = new GameObject("cond_test_entity");
            _scratch.Add(go);
            return go.AddComponent<Entity>();
        }
    }
}
