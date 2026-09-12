using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MapData.Tests
{
    /// <summary>
    /// Parity test: <see cref="MapPathFinder.AStar{T}"/> (new pure-function home)
    /// must produce byte-identical output to <see cref="MapDataManager.AStarWayFinding"/>
    /// (the pre-refactor instance-method implementation).
    /// These tests run BEFORE the MapDataManager refactor so the old code path
    /// is still alive and the parity is meaningful.
    /// </summary>
    public class MapPathFinderTests
    {
        const float EntityR = 0.1f; // EntityManager.MovableEntityR，与 AStarWayFinding 传参保持 parity

        // Build a fresh MapDataManager + LevelData, attached. Returns the manager
        // (already Initialized). Caller is responsible for DestroyImmediate.
        static MapDataManager MakeManager(LevelData ld)
        {
            var mgr = new MapDataManager();
            mgr.AttachLevelData(ld);
            mgr.Initialize();
            return mgr;
        }

        // Convert a sparse Tile list into the dense Tile[iSize,jSize]
        // array the editor passes to MapPathFinder.AStar. Unspecified cells stay default.
        static Tile[,] DenseGrid(LevelData ld)
        {
            var g = new Tile[ld.iSize, ld.jSize];
            if (ld.MapData == null) return g;
            foreach (var e in ld.MapData)
            {
                if (e.i < 0 || e.i >= ld.iSize || e.j < 0 || e.j >= ld.jSize) continue;
                g[e.i, e.j] = e;
            }
            return g;
        }

        static void AssertPathsEqual(MoveParameters[] a, MoveParameters[] b)
        {
            if (a == null && b == null) return;
            Assert.IsNotNull(a, "expected non-null path from MapDataManager");
            Assert.IsNotNull(b, "expected non-null path from MapPathFinder");
            Assert.AreEqual(a.Length, b.Length, "path length mismatch");
            for (int k = 0; k < a.Length; k++)
            {
                Assert.AreEqual(a[k].targetPosition, b[k].targetPosition,
                    $"position mismatch at step {k}");
                Assert.AreEqual(a[k].whetherToEnterPortal, b[k].whetherToEnterPortal,
                    $"portal flag mismatch at step {k}");
            }
        }

        [Test]
        public void Parity_straight_path_in_open_grid()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.iSize = 3;
            ld.jSize = 3;
            ld.MapData = new List<Tile>(); // all default = passable

            var mgr = MakeManager(ld);
            var grid = DenseGrid(ld);

            var start = new Vector2(0.5f, 0.5f);
            var end   = new Vector2(2.5f, 0.5f);

            var fromMgr = mgr.AStarWayFinding(start, end, 0);
            var fromPF  = MapPathFinder.AStar(grid, ld.iSize, ld.jSize,
                                                              start, end, EntityR, 0);

            AssertPathsEqual(fromMgr, fromPF);

            Object.DestroyImmediate(ld);
        }

        [Test]
        public void Parity_grid_with_wall_routes_around()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.iSize = 5;
            ld.jSize = 5;
            // wall vertical strip at j=2
            ld.MapData = new List<Tile>();
            for (int i = 0; i < 5; i++)
                ld.MapData.Add(new Tile { i = i, j = 2, passableType = 3 });

            var mgr = MakeManager(ld);
            var grid = DenseGrid(ld);

            var start = new Vector2(0.5f, 2.5f);
            var end   = new Vector2(4.5f, 2.5f);

            var fromMgr = mgr.AStarWayFinding(start, end, 0);
            var fromPF  = MapPathFinder.AStar(grid, ld.iSize, ld.jSize,
                                                              start, end, EntityR, 0);

            AssertPathsEqual(fromMgr, fromPF);
            Assert.IsNotNull(fromPF, "should find a path around the wall");
            // route should not contain (2,2) which is a wall center
            for (int k = 0; k < fromPF.Length; k++)
            {
                Assert.AreNotEqual(2.5f, fromPF[k].targetPosition.x,
                    $"wall cell x=2.5 should not appear in path (step {k})");
            }

            Object.DestroyImmediate(ld);
        }

        [Test]
        public void Parity_portal_traversal()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.iSize = 4;
            ld.jSize = 4;
            ld.MapData = new List<Tile>
            {
                new Tile { i = 0, j = 0, portalOutI = 3, portalOutJ = 3 },
            };

            var mgr = MakeManager(ld);
            var grid = DenseGrid(ld);

            var start = new Vector2(0.5f, 0.5f);
            var end   = new Vector2(3.5f, 3.5f);

            var fromMgr = mgr.AStarWayFinding(start, end, 0);
            var fromPF  = MapPathFinder.AStar(grid, ld.iSize, ld.jSize,
                                                              start, end, EntityR, 0);

            AssertPathsEqual(fromMgr, fromPF);
            Assert.IsNotNull(fromPF);
            // both implementations should mark the portal traversal
            bool portalSeen = false;
            for (int k = 0; k < fromPF.Length; k++)
                if (fromPF[k].whetherToEnterPortal) { portalSeen = true; break; }
            Assert.IsTrue(portalSeen, "portal entry should appear in unified path");

            Object.DestroyImmediate(ld);
        }

        [Test]
        public void Parity_unreachable_target_returns_null()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.iSize = 3;
            ld.jSize = 3;
            ld.MapData = new List<Tile>();
            // wall off right column
            for (int i = 0; i < 3; i++)
                ld.MapData.Add(new Tile { i = i, j = 2, passableType = 3 });

            var mgr = MakeManager(ld);
            var grid = DenseGrid(ld);

            var start = new Vector2(0.5f, 0.5f);
            var end   = new Vector2(2.5f, 0.5f); // unreachable (wall at col 2)

            var fromMgr = mgr.AStarWayFinding(start, end, 0);
            var fromPF  = MapPathFinder.AStar(grid, ld.iSize, ld.jSize,
                                                              start, end, EntityR, 0);

            AssertPathsEqual(fromMgr, fromPF);
            Assert.IsNull(fromMgr, "old MapDataManager should also return null for unreachable target");

            Object.DestroyImmediate(ld);
        }

        /// <summary>
        /// 回归测试:cp 偏离格心(off-grid continuous coords)时,A* 必须能正确返回路径。
        /// 历史 bug:主循环用 peek-expand-pop 顺序,start cell 的 heuristic 用 startPoint
        /// 连续坐标,子节点 heuristic 用 cell center,两者不一致,priority 倒置 → 子节点被
        /// 立刻 pop 掉没人 expand → 返回 null。修复后主循环改成 pop-expand,这个测试通过。
        /// </summary>
        [Test]
        public void Off_grid_checkpoints_find_path()
        {
            // 5x5 网格,中央十字 H2 + 4 个 H1 deadly,4 个 G 角落区域互不相通
            // (1,1) (1,3) (3,1) (3,3) 是 H1 deadly,(0,2)/(1,2)/(2,0..3)/(3,2)/(4,2) 是 H2
            // cp 都偏离格心 ~0.05~0.15(模拟编辑器拖拽手感)
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.iSize = 5;
            ld.jSize = 5;
            ld.MapData = new List<Tile>
            {
                new Tile { i = 4, j = 3, passableType = 0 },
                new Tile { i = 4, j = 0, passableType = 0 },
                new Tile { i = 3, j = 0, passableType = 0 },
                new Tile { i = 3, j = 1, passableType = 1, deadly = true },
                new Tile { i = 3, j = 3, passableType = 1, deadly = true },
                new Tile { i = 3, j = 4, passableType = 0 },
                new Tile { i = 1, j = 1, passableType = 1, deadly = true },
                new Tile { i = 0, j = 1, passableType = 0 },
                new Tile { i = 0, j = 0, passableType = 0 },
                new Tile { i = 0, j = 3, passableType = 0 },
                new Tile { i = 1, j = 3, passableType = 1, deadly = true },
                new Tile { i = 1, j = 4, passableType = 0 },
                new Tile { i = 1, j = 0, passableType = 0 },
                new Tile { i = 4, j = 4, passableType = 0 },
                new Tile { i = 0, j = 4, passableType = 0 },
                new Tile { i = 4, j = 2, highland = true,  passableType = 2 },
                new Tile { i = 3, j = 2, highland = true,  passableType = 2 },
                new Tile { i = 2, j = 2, highland = true,  passableType = 2 },
                new Tile { i = 1, j = 2, highland = true,  passableType = 2 },
                new Tile { i = 0, j = 2, highland = true,  passableType = 2 },
                new Tile { i = 2, j = 0, highland = true,  passableType = 2 },
                new Tile { i = 2, j = 1, highland = true,  passableType = 2 },
                new Tile { i = 2, j = 3, highland = true,  passableType = 2 },
                new Tile { i = 2, j = 4, passableType = 0 }, // 注意:不是 highland
                // (4, 1) 没有 entry → 默认 passableType=0,可走
            };

            // 偏离格心 ~0.05~0.15(模拟编辑器拖拽手感)
            var cp0 = new Vector2(2.9492025f, 4.1116734f);   // cell (4,3) 内,偏左上
            var cp1 = new Vector2(4.0580845f, 2.9941313f);   // cell (3,4) 内,偏右上
            var cp2 = new Vector2(2.9133780f, -0.056303456f);// cell (0,3) 内,偏右上

            var mgr = new MapDataManager();
            mgr.AttachLevelData(ld);
            mgr.Initialize();

            // seg0: cell (4,3) → cell (3,4),必须经 (4,4) 绕开 (3,3) H1
            var seg0 = mgr.AStarWayFinding(cp0, cp1, 0);
            Assert.IsNotNull(seg0, "off-grid seg0 returned null (peek-expand-pop bug?)");
            Assert.AreEqual(3, seg0.Length, "seg0 should have 3 points: cp0, waypoint (4,4), cp1");
            Assert.AreEqual(cp0, seg0[0].targetPosition);
            Assert.AreEqual(new Vector2(4, 4), seg0[1].targetPosition);
            Assert.AreEqual(cp1, seg0[2].targetPosition);

            // seg1: cell (3,4) → cell (0,3),必须沿 j=4 整列下来,中间 (4,2)(1,4)(0,4) 三个 waypoint
            var seg1 = mgr.AStarWayFinding(cp1, cp2, 0);
            Assert.IsNotNull(seg1, "off-grid seg1 returned null (peek-expand-pop bug?)");
            Assert.AreEqual(5, seg1.Length, "seg1 should have 5 points: cp1, 3 waypoints (4,2)(4,1)(4,0), cp2");
            Assert.AreEqual(cp1, seg1[0].targetPosition);
            Assert.AreEqual(new Vector2(4, 2), seg1[1].targetPosition);
            Assert.AreEqual(new Vector2(4, 1), seg1[2].targetPosition);
            Assert.AreEqual(new Vector2(4, 0), seg1[3].targetPosition);
            Assert.AreEqual(cp2, seg1[4].targetPosition);

            Object.DestroyImmediate(ld);
        }
    }
}