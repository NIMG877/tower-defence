using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode
{
    public class EditorPathFinderParityTests
    {
        // 公共工具:构建一个 BlockData[,],所有 block 默认 PassableType=0(地面可走)
        BlockData[,] BuildOpenGrid(int rows, int cols,
            params (int i, int j, int passableType)[] obstacles)
        {
            var rootGo = new GameObject("grid");
            var blocks = new BlockData[rows, cols];
            var obsSet = new HashSet<(int, int)>();
            foreach (var o in obstacles) obsSet.Add((o.i, o.j));

            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                {
                    var bgo = new GameObject($"b_{i}_{j}");
                    bgo.transform.SetParent(rootGo.transform, false);
                    bgo.transform.position = new Vector3(j, i, 0);
                    var bd = bgo.AddComponent<BlockData>();
                    // 通过 SerializedObject 设置 private _passableType
                    var so = new SerializedObject(bd);
                    so.FindProperty("_passableType").intValue = obsSet.Contains((i, j)) ? 3 : 0;
                    so.ApplyModifiedProperties();
                    blocks[i, j] = bd;
                }
            return blocks;
        }

        [TearDown]
        public void TearDown()
        {
            // 清理测试中创建的 GameObject
            var roots = Object.FindObjectsOfType<GameObject>();
            foreach (var r in roots)
            {
                if (r.name == "grid") Object.DestroyImmediate(r);
            }
        }

        [Test]
        public void AStar_SamePoint_ReturnsTwoPoints()
        {
            var blocks = BuildOpenGrid(5, 5);
            var path = EditorPathFinder.AStar(blocks, 5, 5,
                new Vector2(1.5f, 1.5f), new Vector2(1.5f, 1.5f), 0.25f, 0);
            Assert.IsNotNull(path);
            Assert.AreEqual(2, path.Length);
            Assert.AreEqual(new Vector2(1.5f, 1.5f), path[0].targetPosition);
            Assert.AreEqual(new Vector2(1.5f, 1.5f), path[1].targetPosition);
        }

        [Test]
        public void AStar_OpenPath_ReturnsAtLeastTwoPoints()
        {
            var blocks = BuildOpenGrid(5, 5);
            var path = EditorPathFinder.AStar(blocks, 5, 5,
                new Vector2(0.5f, 0.5f), new Vector2(4.5f, 4.5f), 0.25f, 0);
            Assert.IsNotNull(path);
            Assert.GreaterOrEqual(path.Length, 2);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), path[0].targetPosition);
        }

        [Test]
        public void AStar_Blocked_ReturnsNull()
        {
            // 把 (2,0)(2,1)(2,2)(2,3)(2,4) 都设成不可走,形成垂直墙
            var blocks = BuildOpenGrid(5, 5,
                (2, 0, 3), (2, 1, 3), (2, 2, 3), (2, 3, 3), (2, 4, 3));
            var path = EditorPathFinder.AStar(blocks, 5, 5,
                new Vector2(0.5f, 2.5f), new Vector2(4.5f, 2.5f), 0f, 0);
            // entityR=0 强制严判(MapDataManager 在 entityR=0 时走 IsBlocked 单线检查)
            Assert.IsNull(path);
        }

        [Test]
        public void AStar_MoveMethodFiltersBlocks()
        {
            // 一条 block 设 PassableType=1(近地可走,地面不可走)
            var blocks = BuildOpenGrid(5, 5, (2, 2, 1));
            // 地面 (moveMethod=0) 不能穿过 PassableType=1 块
            // 近地 (moveMethod=1) 可以穿过 PassableType=1 块
            var pathAir = EditorPathFinder.AStar(blocks, 5, 5,
                new Vector2(0.5f, 2.5f), new Vector2(4.5f, 2.5f), 0f, 1);
            Assert.IsNotNull(pathAir);
        }
    }
}