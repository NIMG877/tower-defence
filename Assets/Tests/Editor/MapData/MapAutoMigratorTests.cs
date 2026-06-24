using NUnit.Framework;
using UnityEngine;

namespace MapData.Tests
{
    public class MapAutoMigratorTests
    {
        [Test]
        public void ReadFromPrefab_returns_empty_for_null_prefab()
        {
            var entries = MapAutoMigrator.ReadFromPrefab(null);
            Assert.IsNotNull(entries);
            Assert.AreEqual(0, entries.Count);
        }

        [Test]
        public void ReadFromPrefab_returns_empty_for_prefab_with_no_children()
        {
            var root = new GameObject("MapRoot");
            try
            {
                var entries = MapAutoMigrator.ReadFromPrefab(root);
                Assert.IsNotNull(entries);
                Assert.AreEqual(0, entries.Count);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ReadFromPrefab_skips_children_without_BlockData()
        {
            var root = new GameObject("MapRoot");
            try
            {
                var c0 = new GameObject("c0");
                c0.transform.SetParent(root.transform, false);
                // no BlockData attached (and BlockData.cs is gone)
                var entries = MapAutoMigrator.ReadFromPrefab(root);
                Assert.AreEqual(0, entries.Count);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MigrateLevelData_returns_false_when_already_migrated()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.MapData = new System.Collections.Generic.List<Tile>
            {
                new Tile { i = 0, j = 0 },
            };
            try
            {
                Assert.IsFalse(MapAutoMigrator.MigrateLevelData(ld));
            }
            finally
            {
                Object.DestroyImmediate(ld);
            }
        }

        [Test]
        public void MigrateLevelData_returns_false_when_no_prefab()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.MapPrefab = null;
            ld.MapData = new System.Collections.Generic.List<Tile>();
            try
            {
                Assert.IsFalse(MapAutoMigrator.MigrateLevelData(ld));
            }
            finally
            {
                Object.DestroyImmediate(ld);
            }
        }
    }
}