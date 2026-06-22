using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode
{
    public class BlockMapCacheTests
    {
        GameObject _mapRoot;

        [TearDown]
        public void TearDown()
        {
            if (_mapRoot != null) Object.DestroyImmediate(_mapRoot);
        }

        [Test]
        public void Load_PopulatesBlocks_FromChildrenWithBlockData()
        {
            _mapRoot = new GameObject("TestMap");
            // 2x2 layout: positions (0,0)(1,0)(0,1)(1,1)
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                {
                    var go = new GameObject($"Block_{i}_{j}");
                    go.transform.SetParent(_mapRoot.transform, false);
                    go.transform.position = new Vector3(j, i, 0);
                    go.AddComponent<BlockData>();
                }

            // BlockMapCache.Load accepts a scene GameObject instance
            // (PrefabUtility.LoadPrefabContents is only for prefab assets).
            var cache = BlockMapCache.Load(_mapRoot);
            Assert.AreEqual(2, cache.ISize);
            Assert.AreEqual(2, cache.JSize);
            Assert.IsNotNull(cache.Blocks[0, 0]);
            Assert.IsNotNull(cache.Blocks[1, 1]);
            // EntityR cached from EntityManager.EntityR (default 0.25)
            Assert.AreEqual(0.25f, cache.EntityR, 0.001f);
            cache.Dispose();
        }
    }
}