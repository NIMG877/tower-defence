using NUnit.Framework;
using UnityEngine;

namespace MapData.Tests
{
    public class MapDataManagerInitTests
    {
        [Test]
        public void Initialize_with_no_levelData_yields_empty_matrix()
        {
            var mgr = new MapDataManager();
            mgr.Initialize();
            // iSize/jSize default to 0; matrix is null. This is a "no data" state.
            Assert.AreEqual(0, mgr.iSize);
            Assert.AreEqual(0, mgr.jSize);
            Assert.IsNull(mgr.Tiles);
        }

        [Test]
        public void Initialize_with_levelData_builds_matrix_from_MapData()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.iSize = 4;
            ld.jSize = 3;
            ld.MapData = new System.Collections.Generic.List<Tile>
            {
                new Tile { i = 0, j = 0, highland = true },
                new Tile { i = 2, j = 1, passableType = 3, deadly = true },
                new Tile { i = 1, j = 2, portalOutI = 0, portalOutJ = 0 },
            };

            var mgr = new MapDataManager();
            mgr.AttachLevelData(ld);
            mgr.Initialize();

            Assert.AreEqual(4, mgr.iSize);
            Assert.AreEqual(3, mgr.jSize);
            Assert.IsNotNull(mgr.Tiles);
            Assert.AreEqual(4, mgr.Tiles.GetLength(0));
            Assert.AreEqual(3, mgr.Tiles.GetLength(1));

            Assert.IsTrue(mgr.Tiles[0, 0].highland);
            Assert.IsFalse(mgr.Tiles[0, 0].deadly);

            Assert.IsTrue(mgr.Tiles[2, 1].deadly);
            Assert.AreEqual(3, mgr.Tiles[2, 1].passableType);

            Assert.AreEqual(0, mgr.Tiles[1, 2].portalOutI);
            Assert.AreEqual(0, mgr.Tiles[1, 2].portalOutJ);

            // unlisted cell: default
            Assert.IsFalse(mgr.Tiles[3, 2].highland);
            Assert.IsFalse(mgr.Tiles[3, 2].canSet);
            Assert.AreEqual(0, mgr.Tiles[3, 2].passableType);
            // material array parallel — also allocated
            Assert.IsNotNull(mgr.TileMaterials);
            Assert.AreEqual(4, mgr.TileMaterials.GetLength(0));
            Assert.AreEqual(3, mgr.TileMaterials.GetLength(1));
            Assert.IsNull(mgr.TileMaterials[3, 2]);

            Object.DestroyImmediate(ld);
        }

        [Test]
        public void Initialize_skips_out_of_range_entries_without_throwing()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.iSize = 2;
            ld.jSize = 2;
            ld.MapData = new System.Collections.Generic.List<Tile>
            {
                new Tile { i = 5, j = 5, highland = true },   // out of range
                new Tile { i = -1, j = 0 },                  // out of range
                new Tile { i = 0, j = 0, passableType = 2 },  // in range
            };

            var mgr = new MapDataManager();
            mgr.AttachLevelData(ld);

            Assert.DoesNotThrow(() => mgr.Initialize());
            Assert.AreEqual(2, mgr.Tiles[0, 0].passableType);
            Assert.IsFalse(mgr.Tiles[1, 1].highland); // unlisted cell unaffected

            Object.DestroyImmediate(ld);
        }
    }
}