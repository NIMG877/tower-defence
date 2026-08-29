using System;
using System.Collections.Generic;
using AbilitySystem.Components;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AbilitySystem.Tests
{
    /// <summary>SelectLandingPoints 选点算法（纯函数 seams）的 EditMode 契约测试。
    /// 语义：敌人优先（范围内敌群去泡泡后随机取，原位不加偏移）→ 不足补格点
    /// （地面层先于高台层，层内随机不重复，每点 ±offset 偏移）→ 格点用尽循环
    /// 回敌人（可重复）。随机源注入 <c>Func&lt;float&gt;</c>（[0,1]），测试用常量
    /// 序列驱动；OnTrigger 装配（Vision/MapDataManager）依赖场景，EditMode 不覆盖。
    /// </summary>
    public class SelectLandingPointsTests
    {
        private const float Offset = 0.24f;

        // r=0.9999f 使 Fisher-Yates 的 j=(int)(r*(i+1))%(i+1)==i（小数组下恒等序），
        // 且索引抽取 (int)(r*n)%n==n-1——组合出"保持输入顺序"的可断言序列。
        private static Func<float> Identity() => () => 0.9999f;
        private static Func<float> Const(float v) => () => v;

        [Test]
        public void EnoughEnemies_PicksFourAtExactPositions_NoOffset()
        {
            var enemies = new[] { new Vector2(1, 1), new Vector2(2, 5), new Vector2(3, 9), new Vector2(7, 2), new Vector2(8, 8) };
            var cells = new (int x, int y)[] { (0, 0), (10, 0), (0, 10), (10, 10), (5, 5) };

            List<Vector2> picked = SelectLandingPoints.PickLandingPoints(
                cells, AllGround(cells), enemies, 4, Offset, Identity());

            Assert.That(picked, Is.EqualTo(new List<Vector2> { new(1, 1), new(2, 5), new(3, 9), new(7, 2) }),
                "敌人数够：随机取 4 个敌人原位作落点，不加偏移、不消费格点");
        }

        [Test]
        public void FewerEnemies_FillsFromGroundCells_BeforePlatformCells()
        {
            var enemies = new[] { new Vector2(1, 1), new Vector2(2, 2) };
            var cells = new (int x, int y)[] { (0, 0), (1, 0), (4, 4), (5, 4), (6, 4) };
            var isHighland = new[] { false, false, true, true, true };

            List<Vector2> picked = SelectLandingPoints.PickLandingPoints(
                cells, isHighland, enemies, 4, Offset, Const(0.5f));

            Assert.That(picked, Is.EqualTo(new List<Vector2> { new(1, 1), new(2, 2), new(0, 0), new(1, 0) })
                .AsCollection, "2 敌人补 2 地面格（r=0.5 偏移恰为 0），高台格不消费");
        }

        [Test]
        public void NoEnemies_GroundTierFirst_ThenPlatform()
        {
            var cells = new (int x, int y)[] { (0, 0), (1, 0), (4, 4), (5, 4), (6, 4) };
            var isHighland = new[] { false, false, true, true, true };

            List<Vector2> picked = SelectLandingPoints.PickLandingPoints(
                cells, isHighland, Array.Empty<Vector2>(), 3, Offset, Identity());

            Assert.That(picked, Is.EqualTo(new List<Vector2> { new(0, 0), new(1, 0), new(4, 4) }),
                "地面层用尽才轮到高台层；层内按恒等随机序取前 N 个不同格");
        }

        [Test]
        public void CellPoints_GetSignedRandomOffset()
        {
            var cells = new (int x, int y)[] { (5, 5) };

            List<Vector2> low = SelectLandingPoints.PickLandingPoints(
                cells, AllGround(cells), Array.Empty<Vector2>(), 1, Offset, Const(0f));
            List<Vector2> high = SelectLandingPoints.PickLandingPoints(
                cells, AllGround(cells), Array.Empty<Vector2>(), 1, Offset, Const(1f));

            Assert.That(low[0], Is.EqualTo(new Vector2(5 - Offset, 5 - Offset)), "r=0 → 双轴 -offset");
            Assert.That(high[0], Is.EqualTo(new Vector2(5 + Offset, 5 + Offset)), "r=1 → 双轴 +offset");
        }

        [Test]
        public void CellsExhausted_CyclesBackToEnemies_WithRepetition()
        {
            var enemies = new[] { new Vector2(2, 2) };
            var cells = new (int x, int y)[] { (5, 5) };

            List<Vector2> picked = SelectLandingPoints.PickLandingPoints(
                cells, AllGround(cells), enemies, 4, Offset, Const(0.5f));

            Assert.That(picked, Is.EqualTo(new List<Vector2> { new(2, 2), new(5, 5), new(2, 2), new(5, 5) }),
                "去重池用尽后从敌人重新选起：敌人↔格点交替、允许重复");
        }

        [Test]
        public void NoEnemies_SingleCell_RepeatsCellToFill()
        {
            var cells = new (int x, int y)[] { (5, 5) };

            List<Vector2> picked = SelectLandingPoints.PickLandingPoints(
                cells, AllGround(cells), Array.Empty<Vector2>(), 3, Offset, Const(0.5f));

            Assert.That(picked, Is.EqualTo(new List<Vector2> { new(5, 5), new(5, 5), new(5, 5) }),
                "只有格点可重复时循环只走格点（敌人池为空被跳过）");
        }

        [Test]
        public void ConfigErrors_ReturnNull()
        {
            var cells = new (int x, int y)[] { (0, 0) };

            Assert.That(SelectLandingPoints.PickLandingPoints(null, null, Array.Empty<Vector2>(), 4, Offset, Identity()), Is.Null,
                "Range 为 null（半径视野）是配线错误");
            Assert.That(SelectLandingPoints.PickLandingPoints(cells, AllGround(cells), Array.Empty<Vector2>(), 0, Offset, Identity()), Is.Null,
                "count<=0 是配线错误");
            Assert.That(SelectLandingPoints.PickLandingPoints(Array.Empty<(int, int)>(), Array.Empty<bool>(), Array.Empty<Vector2>(), 4, Offset, Identity()), Is.Null,
                "无格点且无敌人：无可选来源");
        }

        [Test]
        public void ExtractEnemyPositions_DropsExcludedEntities()
        {
            var go0 = new GameObject("e0"); go0.transform.position = new Vector3(1, 1);
            var go1 = new GameObject("e1"); go1.transform.position = new Vector3(2, 2);
            var go2 = new GameObject("e2"); go2.transform.position = new Vector3(3, 3);
            try
            {
                var enemies = new List<Entity> { go0.AddComponent<Entity>(), go1.AddComponent<Entity>(), go2.AddComponent<Entity>() };
                var excluded = new List<Entity> { enemies[1] };

                Vector2[] positions = SelectLandingPoints.ExtractEnemyPositions(enemies, excluded);

                Assert.That(positions, Is.EqualTo(new[] { new Vector2(1, 1), new Vector2(3, 3) }),
                    "排除名单（熔岩泡泡花名册）中的实体不进敌池");
            }
            finally
            {
                Object.DestroyImmediate(go0); Object.DestroyImmediate(go1); Object.DestroyImmediate(go2);
            }
        }

        private static bool[] AllGround((int x, int y)[] cells)
        {
            return new bool[cells.Length];
        }
    }
}
