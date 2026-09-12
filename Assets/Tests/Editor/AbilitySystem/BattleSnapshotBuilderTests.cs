using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AbilitySystem.Tests
{
    /// <summary>
    /// BattleSnapshotBuilder 纯函数部分（BuildCross）的测试。带单例依赖的 Build(Entity)
    /// 需要活动战局，归 Unity PlayMode 实测（见 plan-llm-generated-ability 验证清单）。
    /// </summary>
    public class BattleSnapshotBuilderTests
    {
        private static BattleSnapshotBuilder.SnapshotEntity E(string id, int camp, float x, float y, float hpRate)
        {
            return new BattleSnapshotBuilder.SnapshotEntity
            {
                id = id,
                camp = camp,
                hpRate = hpRate,
                pos = new BattleSnapshotBuilder.SnapshotPos { x = x, y = y },
            };
        }

        [Test]
        public void Cross_CountsEnemiesAndAllies_AndFindsNearestLowest()
        {
            var entities = new List<BattleSnapshotBuilder.SnapshotEntity>
            {
                E("a1", camp: 1, x: 1, y: 0, hpRate: 1f),
                E("e1", camp: 2, x: 3, y: 4, hpRate: 0.5f),   // 距原点 5
                E("e2", camp: 2, x: 0, y: 1, hpRate: 0.2f),   // 距原点 1
            };

            BattleSnapshotBuilder.SnapshotCross cross = BattleSnapshotBuilder.BuildCross(Vector2.zero, entities, selfCamp: 1);

            Assert.AreEqual(2, cross.enemyCount);
            Assert.AreEqual(1, cross.allyCount, "allyCount 不含自身（自身不在传入列表里）");
            Assert.AreEqual(1f, cross.nearestEnemyDistance);
            Assert.AreEqual(0.2f, cross.lowestEnemyHpRate);
            Assert.AreEqual("e2", cross.lowestEnemyId);
        }

        [Test]
        public void Cross_NoEnemies_KeepsDefaults()
        {
            var entities = new List<BattleSnapshotBuilder.SnapshotEntity>
            {
                E("a1", camp: 1, x: 2, y: 2, hpRate: 1f),
            };

            BattleSnapshotBuilder.SnapshotCross cross = BattleSnapshotBuilder.BuildCross(new Vector2(0, 0), entities, selfCamp: 1);

            Assert.AreEqual(0, cross.enemyCount);
            Assert.AreEqual(1, cross.allyCount);
            Assert.AreEqual(-1f, cross.nearestEnemyDistance);
            Assert.AreEqual(1f, cross.lowestEnemyHpRate);
            Assert.IsNull(cross.lowestEnemyId);
        }

        [Test]
        public void Cross_NullList_AndNullEntries_AreSafe()
        {
            BattleSnapshotBuilder.SnapshotCross fromNull = BattleSnapshotBuilder.BuildCross(Vector2.zero, null, 1);
            Assert.AreEqual(0, fromNull.enemyCount);
            Assert.AreEqual(-1f, fromNull.nearestEnemyDistance);

            var withNull = new List<BattleSnapshotBuilder.SnapshotEntity> { null, E("e1", 2, 1, 1, 0.9f) };
            BattleSnapshotBuilder.SnapshotCross cross = BattleSnapshotBuilder.BuildCross(Vector2.zero, withNull, 1);
            Assert.AreEqual(1, cross.enemyCount);
        }
    }
}
