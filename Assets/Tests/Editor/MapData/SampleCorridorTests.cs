using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEngine;

namespace MapData.Tests
{
    /// <summary>
    /// Direct tests on <c>SampleCorridor</c> via reflection. Validates that
    /// the corridor-scan correctly returns true (blocked) on every cell the
    /// line passes through, regardless of sampling strategy. The legacy
    /// mid-point sampler misses chess-board obstacles; DDA does not.
    /// </summary>
    public class SampleCorridorTests
    {
        // AStarProperty 是 MapPathFinder 嵌套 private struct,用反射绕过可见性。
        static readonly Type AStarPropertyType =
            typeof(MapPathFinder).GetNestedType("AStarProperty", BindingFlags.NonPublic);
        static readonly FieldInfo PassableField =
            AStarPropertyType.GetField("Passable", BindingFlags.Public | BindingFlags.Instance);

        static readonly MethodInfo SampleCorridorMethod =
            typeof(MapPathFinder).GetMethod("SampleCorridor",
                BindingFlags.NonPublic | BindingFlags.Static);

        static Array MakeGraph(int iSize, int jSize, params (int i, int j)[] walls)
        {
            // Array.CreateInstance 需要 Type → Array 是抽象基类,实际用二维数组类型
            var g = Array.CreateInstance(AStarPropertyType, iSize, jSize);
            // 默认 Passable = false(struct default)。先全部填 true。
            for (int i = 0; i < iSize; i++)
            {
                for (int j = 0; j < jSize; j++)
                {
                    var box = g.GetValue(i, j); // boxing struct
                    PassableField.SetValue(box, true);
                    g.SetValue(box, i, j); // unbox + re-box
                }
            }
            foreach (var (i, j) in walls)
            {
                var box = g.GetValue(i, j);
                PassableField.SetValue(box, false);
                g.SetValue(box, i, j);
            }
            return g;
        }

        static bool CallSampleCorridor(Array graph, Vector2 a, Vector2 b)
        {
            Assert.IsNotNull(SampleCorridorMethod, "SampleCorridor not found on MapPathFinder");
            return (bool)SampleCorridorMethod.Invoke(null, new object[] { graph, a, b });
        }

        [Test]
        public void Corridor_clear_returns_false()
        {
            var g = MakeGraph(5, 5);
            Assert.IsFalse(CallSampleCorridor(g, new Vector2(0.5f, 0.5f), new Vector2(4.5f, 0.5f)),
                "all-passable corridor should report not-blocked");
        }

        [Test]
        public void Corridor_with_wall_at_end_returns_true()
        {
            var g = MakeGraph(3, 3, (2, 1));
            Assert.IsTrue(CallSampleCorridor(g, new Vector2(0.5f, 1.5f), new Vector2(2.5f, 1.5f)),
                "wall at endpoint should report blocked");
        }

        /// <summary>
        /// 棋盘式障碍:(0,0)(2,0)(4,0) 全 impassable,中间 (1,0)(3,0) passable。
        /// 直线 (0.5,0.5)→(4.5,0.5) 穿过所有 5 个 cell 的中心,必然命中 3 个墙。
        /// 老算法的 mid-point 采样落在 (1,0)(3,0) 上 → 报 "clear" → 这是已知 bug。
        /// DDA 逐格走 → 必然命中 (0,0) 第一个 → 报 blocked。
        /// </summary>
        [Test]
        public void Corridor_chess_board_obstacles_returns_true()
        {
            var g = MakeGraph(5, 1, (0, 0), (2, 0), (4, 0));
            Assert.IsTrue(CallSampleCorridor(g, new Vector2(0.5f, 0.5f), new Vector2(4.5f, 0.5f)),
                "chess-board obstacles should report blocked (DDA, not mid-point sampling)");
        }

        /// <summary>
        /// 对角线穿 2x2 障碍块 (1,1):(0.5,0.5)→(3.5,3.5) 必然穿过 (1,1) 或 (2,2)。
        /// </summary>
        [Test]
        public void Corridor_diagonal_through_blocked_cell_returns_true()
        {
            var g = MakeGraph(4, 4, (1, 1));
            Assert.IsTrue(CallSampleCorridor(g, new Vector2(0.5f, 0.5f), new Vector2(3.5f, 3.5f)),
                "diagonal through a blocked cell should report blocked");
        }

        /// <summary>
        /// 退化:零长度走廊
        /// </summary>
        [Test]
        public void Corridor_zero_length_returns_false()
        {
            var g = MakeGraph(3, 3, (1, 1));
            Assert.IsFalse(CallSampleCorridor(g, new Vector2(1.5f, 1.5f), new Vector2(1.5f, 1.5f)),
                "zero-length corridor should be trivially clear");
        }
    }
}