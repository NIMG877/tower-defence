using NUnit.Framework;
using UnityEngine;

namespace MapData.Tests
{
    public class EditorPathFinderTests
    {
        static BlockState[,] MakeGrid(int rows, int cols, System.Action<int, int, BlockState[,]> paint = null)
        {
            var g = new BlockState[rows, cols];
            if (paint != null) paint(rows, cols, g);
            return g;
        }

        [Test]
        public void AStar_finds_straight_path_in_open_grid()
        {
            // 3x3 all passable
            var grid = MakeGrid(3, 3);
            var path = EditorPathFinder.AStar(grid, 3, 3, new Vector2(0.5f, 0.5f), new Vector2(2.5f, 0.5f), 0.25f, 0);
            Assert.IsNotNull(path);
            Assert.GreaterOrEqual(path.Length, 2);
        }

        [Test]
        public void AStar_returns_null_when_target_blocked()
        {
            // 3x3 with the right column walled off
            var grid = MakeGrid(3, 3, (rows, cols, g) =>
            {
                for (int i = 0; i < rows; i++) g[i, 2].passableType = 3;
            });
            var path = EditorPathFinder.AStar(grid, 3, 3, new Vector2(0.5f, 0.5f), new Vector2(2.5f, 0.5f), 0.25f, 0);
            Assert.IsNull(path);
        }

        [Test]
        public void AStar_traverses_portal_when_target_passable()
        {
            // 3x3 with a portal at (0,0) → (2,2)
            var grid = MakeGrid(3, 3, (rows, cols, g) =>
            {
                g[0, 0].portalOutI = 2;
                g[0, 0].portalOutJ = 2;
                g[2, 2].portalOutI = -1; // target cell is not itself a portal source
            });
            // Surround (2,2) so the only way in is the portal: actually we want a path
            // from outside the (2,2) cell. Easiest: portal at (0,0) with normal grid.
            var path = EditorPathFinder.AStar(grid, 3, 3, new Vector2(0.5f, 0.5f), new Vector2(2.5f, 2.5f), 0.25f, 0);
            Assert.IsNotNull(path);
            // Look for the portal-traversal marker (whetherToEnterPortal == true) somewhere
            // in the returned path. The portal source (0.5, 0.5) is the start, so the
            // portal entry is at the next step.
            bool foundPortal = false;
            for (int k = 0; k < path.Length; k++)
            {
                if (path[k].whetherToEnterPortal) { foundPortal = true; break; }
            }
            Assert.IsTrue(foundPortal, "Path should traverse portal (0,0) → (2,2)");
        }
    }
}
