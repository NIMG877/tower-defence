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
        const float EntityR = 0.25f;

        // Build a fresh MapDataManager + LevelData, attached. Returns the manager
        // (already Initialized). Caller is responsible for DestroyImmediate.
        static MapDataManager MakeManager(LevelData ld)
        {
            var mgr = new MapDataManager();
            mgr.AttachLevelData(ld);
            mgr.Initialize();
            return mgr;
        }

        // Convert a sparse BlockDataEntry list into the dense BlockDataEntry[iSize,jSize]
        // array the editor passes to MapPathFinder.AStar. Unspecified cells stay default.
        static BlockDataEntry[,] DenseGrid(LevelData ld)
        {
            var g = new BlockDataEntry[ld.iSize, ld.jSize];
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
            ld.MapData = new List<BlockDataEntry>(); // all default = passable

            var mgr = MakeManager(ld);
            var grid = DenseGrid(ld);

            var start = new Vector2(0.5f, 0.5f);
            var end   = new Vector2(2.5f, 0.5f);

            var fromMgr = mgr.AStarWayFinding(start, end, 0);
            var fromPF  = MapPathFinder.AStar<BlockDataEntry>(grid, ld.iSize, ld.jSize,
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
            ld.MapData = new List<BlockDataEntry>();
            for (int i = 0; i < 5; i++)
                ld.MapData.Add(new BlockDataEntry { i = i, j = 2, passableType = 3 });

            var mgr = MakeManager(ld);
            var grid = DenseGrid(ld);

            var start = new Vector2(0.5f, 2.5f);
            var end   = new Vector2(4.5f, 2.5f);

            var fromMgr = mgr.AStarWayFinding(start, end, 0);
            var fromPF  = MapPathFinder.AStar<BlockDataEntry>(grid, ld.iSize, ld.jSize,
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
            ld.MapData = new List<BlockDataEntry>
            {
                new BlockDataEntry { i = 0, j = 0, portalOutI = 3, portalOutJ = 3 },
            };

            var mgr = MakeManager(ld);
            var grid = DenseGrid(ld);

            var start = new Vector2(0.5f, 0.5f);
            var end   = new Vector2(3.5f, 3.5f);

            var fromMgr = mgr.AStarWayFinding(start, end, 0);
            var fromPF  = MapPathFinder.AStar<BlockDataEntry>(grid, ld.iSize, ld.jSize,
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
            ld.MapData = new List<BlockDataEntry>();
            // wall off right column
            for (int i = 0; i < 3; i++)
                ld.MapData.Add(new BlockDataEntry { i = i, j = 2, passableType = 3 });

            var mgr = MakeManager(ld);
            var grid = DenseGrid(ld);

            var start = new Vector2(0.5f, 0.5f);
            var end   = new Vector2(2.5f, 0.5f); // unreachable (wall at col 2)

            var fromMgr = mgr.AStarWayFinding(start, end, 0);
            var fromPF  = MapPathFinder.AStar<BlockDataEntry>(grid, ld.iSize, ld.jSize,
                                                              start, end, EntityR, 0);

            AssertPathsEqual(fromMgr, fromPF);
            Assert.IsNull(fromMgr, "old MapDataManager should also return null for unreachable target");

            Object.DestroyImmediate(ld);
        }
    }
}