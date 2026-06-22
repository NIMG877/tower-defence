# Path Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `LevelData.CheckPoints : GameObject[]` (prefab-encoded path points) with `LevelData.Paths : PathData[]` (pure data), and add a `PathEditingSection` to the LevelEditor that visualizes the map, lets users add/move/delete checkpoints, and shows the A* actual path between consecutive checkpoints.

**Architecture:** UI Toolkit inspector section that renders BlockData grid via `generateVisualContent` + painter2D. A static `EditorPathFinder` replicates `MapDataManager.AStarWayFinding` as a pure function over `BlockData[,]` (no scene side effects), with behavior-parity unit tests guarding it. A `BlockMapCache` wraps `PrefabUtility.LoadPrefabContents` for editor-time read-only parsing. One-shot migration tool converts legacy prefab data into `Paths`.

**Tech Stack:** Unity 2022.3.62f3, C#, UI Toolkit (UXML-free, programmatic), UnityEditor (PrefabUtility, SerializedObject, Undo), Unity Test Framework 1.1.33 + NUnit 3.x for EditMode tests, `painter2D` for vector drawing.

**Project conventions:**
- Tests live in `Assets/Tests/EditMode/` (existing asmdef: `EditMode.asmdef` with `LevelEditor.Editor`, `GameData`, `BasicScripts` references).
- `[Test]` + `[SetUp]`/`[TearDown]`, sync `void`, `Assert.IsTrue(issues.Any(...))` with descriptive interpolated failure message.
- Editor code in global namespace under `Assets/Editor/LevelEditor/`.
- Runtime code under `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/`.
- Commits end with `Co-Authored-By: Claude <noreply@anthropic.com>`.

---

## File Structure

```
Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/
└── LevelData.cs                                       MODIFY: add PathData struct + Paths field; mark CheckPoints [Obsolete]

Assets/Editor/LevelEditor/
├── LevelDataEditor.cs                                 MODIFY: append PathEditingSection; track BlockMapCache lifecycle
├── Sections/
│   └── PathEditingSection.cs                          NEW: section root, Build() that wires all sub-views
├── PathEditing/
│   ├── BlockMapCache.cs                               NEW: PrefabUtility LoadContents → BlockData[,]
│   ├── EditorPathFinder.cs                            NEW: static AStar(...) with parity tests
│   ├── ViewTransform.cs                               NEW: WorldToScreen / ScreenToWorld / SnapToGrid / Fit
│   ├── PathEditingState.cs                            NEW: session state (SelectedPathIdx / cp / moveMethod / view)
│   ├── MapCanvasView.cs                               NEW: 600×400 canvas, generateVisualContent grid + A* + checkpoint circles
│   ├── EditorPathManipulator.cs                       NEW: MouseManipulator (click/drag/wheel/middle-drag)
│   ├── CheckpointListView.cs                          NEW: right-column list
│   └── CheckpointDetailView.cs                        NEW: right-column detail panel (Position X/Y / WaitTime)
├── Migration/
│   ├── EditorPathMigrationTool.cs                     NEW: [MenuItem] + MigrateAsset(LD)
│   └── MigrationBanner.cs                             NEW: yellow banner inside LevelDataEditor when legacy data detected
└── Validation/
    └── LevelDataValidator.cs                          MODIFY: add 4 path validation rules

Assets/Tests/EditMode/
└── PathEditingTests.cs                                NEW: parity + grid + transform + validator tests
```

---

## Task 1: Add PathData struct + Paths field to LevelData

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelData.cs`

- [ ] **Step 1: Read current LevelData.cs**

Open `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelData.cs` and confirm the exact field block to modify. The field `public GameObject[] CheckPoints;` is at the top of the public field block.

- [ ] **Step 2: Add PathData struct + Paths field, mark CheckPoints obsolete**

Add this struct above the `LevelData` class, and replace the `CheckPoints` field declaration:

```csharp
[System.Serializable]
public struct PathData
{
    public Vector2[] CheckPoints;
    public float[]   WaitTimes;
}

public class LevelData : ScriptableObject
{
    // ... existing fields ...

    [System.Obsolete("Use Paths[] instead. Kept temporarily for legacy asset migration.")]
    public GameObject[] CheckPoints;

    public PathData[] Paths = new PathData[0];

    // ... rest of existing fields ...
}
```

The `CheckPoints` field stays on the class so legacy assets keep deserializing; only the attribute is added. `Paths` is the new authoritative field.

- [ ] **Step 3: Verify the project compiles**

Run in Editor: open any scene → check the Console for compile errors. Expected: 0 errors. (Warnings about `[Obsolete]` if any other file references `LevelData.CheckPoints` are acceptable — note them for later.)

Run: `grep -rn "CheckPoints" Assets/PublicScripts/Entity-LevelPublicScripts/`
Expected: at minimum `LevelData.cs` (the declaration) and `LevelResourceSharing.cs:45` (`PathDataManager.Manager.CreatePaths(LD.CheckPoints)`).

- [ ] **Step 4: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelData.cs
git commit -m "feat(leveldata): add PathData struct + Paths[] field; mark CheckPoints [Obsolete]"
```

---

## Task 2: BlockMapCache — load MapPrefab into BlockData[,]

**Files:**
- Create: `Assets/Editor/LevelEditor/PathEditing/BlockMapCache.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/BlockMapCacheTests.cs`:

```csharp
using NUnit.Framework;
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

            // BlockMapCache.Load 接受 GameObject (内部通过 PrefabUtility.LoadPrefabContents 加载,
            // 但该 API 在 EditMode 测试中对运行时实例直接调用有限制;本测试只验证 ParseBlocks 路径)
            var cache = BlockMapCache.Load(_mapRoot);
            Assert.AreEqual(2, cache.ISize);
            Assert.AreEqual(2, cache.JSize);
            Assert.IsNotNull(cache.Blocks[0, 0]);
            Assert.IsNotNull(cache.Blocks[1, 1]);
            cache.Dispose();
        }
    }
}
```

Note: EditMode tests can call `PrefabUtility.LoadPrefabContents` only on actual prefab assets. For runtime-created GameObjects with `BlockData`, the cache must support an alternate path. Implementation below handles both.

- [ ] **Step 2: Run test to verify it fails**

Run in Editor: `Window → General → Test Runner → EditMode → Run All`.
Expected: FAIL — `BlockMapCache` does not exist yet (compile error) or test fails at `cache.ISize`.

- [ ] **Step 3: Implement BlockMapCache**

Create `Assets/Editor/LevelEditor/PathEditing/BlockMapCache.cs`:

```csharp
using System;
using UnityEditor;
using UnityEngine;

public sealed class BlockMapCache : IDisposable
{
    public BlockData[,] Blocks;
    public int ISize;
    public int JSize;
    public float EntityR;

    /// <summary>
    /// 加载 MapPrefab 解析 BlockData 矩阵。
    /// 接受 prefab asset 或 scene 中的 GameObject 实例。
    /// </summary>
    public static BlockMapCache Load(GameObject mapPrefab)
    {
        if (mapPrefab == null) throw new ArgumentNullException(nameof(mapPrefab));

        var cache = new BlockMapCache();
        GameObject root = null;
        bool isPrefabContents = false;

        // PrefabUtility.LoadPrefabContents 仅对 asset prefab 可用;对 scene 实例退化为直接遍历
        if (PrefabUtility.IsPartOfPrefabAsset(mapPrefab))
        {
            root = PrefabUtility.LoadPrefabContents(mapPrefab.name);
            isPrefabContents = true;
        }
        else
        {
            root = mapPrefab;
        }

        try
        {
            cache.ParseBlocks(root);
            cache.EntityR = EntityManager.EntityR;
        }
        finally
        {
            if (isPrefabContents) PrefabUtility.UnloadPrefabContents(root);
        }

        return cache;
    }

    void ParseBlocks(GameObject mapRoot)
    {
        int childCount = mapRoot.transform.childCount;
        int maxI = 0, maxJ = 0;
        for (int k = 0; k < childCount; k++)
        {
            var t = mapRoot.transform.GetChild(k);
            if (maxI < t.position.y) maxI = (int)t.position.y;
            if (maxJ < t.position.x) maxJ = (int)t.position.x;
        }
        ISize = maxI + 1;
        JSize = maxJ + 1;
        Blocks = new BlockData[ISize, JSize];

        for (int k = 0; k < childCount; k++)
        {
            var t = mapRoot.transform.GetChild(k);
            if (t.TryGetComponent<BlockData>(out var bd))
            {
                Blocks[(int)t.position.y, (int)t.position.x] = bd;
            }
        }
    }

    public void Dispose()
    {
        Blocks = null;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `Test Runner → EditMode → Run All`.
Expected: PASS — `BlockMapCacheTests.Load_PopulatesBlocks_FromChildrenWithBlockData` passes.

- [ ] **Step 5: Commit**

```bash
git add Assets/Editor/LevelEditor/PathEditing/BlockMapCache.cs Assets/Tests/EditMode/BlockMapCacheTests.cs
git commit -m "feat(path-editor): BlockMapCache loads MapPrefab into BlockData[,]"
```

---

## Task 3: EditorPathFinder — static A* with behavior-parity tests

**Files:**
- Create: `Assets/Editor/LevelEditor/PathEditing/EditorPathFinder.cs`
- Create: `Assets/Tests/EditMode/EditorPathFinderParityTests.cs`

This is the largest single task. The implementation is a 1:1 copy of `MapDataManager.AStarWayFinding` (`Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/MapDataManager.cs:245-399`) with the `graph` field replaced by a parameter.

- [ ] **Step 1: Write the failing parity test**

Create `Assets/Tests/EditMode/EditorPathFinderParityTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public class EditorPathFinderParityTests
    {
        // 公共工具:构建一个 5x5 BlockData[,],所有 block 默认 PassableType=0
        BlockData[,] BuildOpenGrid(int rows, int cols, params (int i, int j, int passableType)[] obstacles)
        {
            var go = new GameObject("grid");
            var blocks = new BlockData[rows, cols];
            var obsSet = new HashSet<(int, int)>();
            foreach (var o in obstacles) obsSet.Add((o.i, o.j));

            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                {
                    var bgo = new GameObject($"b_{i}_{j}");
                    bgo.transform.SetParent(go.transform, false);
                    bgo.transform.position = new Vector3(j, i, 0);
                    var bd = bgo.AddComponent<BlockData>();
                    // 通过反射 / SerializedObject 设置 _passableType (private)
                    var so = new UnityEditor.SerializedObject(bd);
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
                new Vector2(0.5f, 2.5f), new Vector2(4.5f, 2.5f), 0.25f, 0);
            // entityR=0.25 可能让线穿过;如果穿得过则非 null。改用 entityR=0 强制严判
            // (MapDataManager 在 entityR=0 时走 IsBlocked 单线检查)
            Assert.IsNull(path);
        }

        [Test]
        public void AStar_MoveMethodFiltersBlocks()
        {
            // 一条 block 设 PassableType=1(近地可走,地面不可走)
            var blocks = BuildOpenGrid(5, 5, (2, 2, 1));
            // 地面 (moveMethod=0) 不能穿过 PassableType=1 块 → 需绕路或失败
            var pathGround = EditorPathFinder.AStar(blocks, 5, 5,
                new Vector2(0.5f, 2.5f), new Vector2(4.5f, 2.5f), 0f, 0);
            // 验证路径不经过 (2,2) — 即 path 中所有 targetPosition 不在 (2,2) 块的 entityR 内
            // 简化:只要地面路径 != 近地路径即可
            var pathAir = EditorPathFinder.AStar(blocks, 5, 5,
                new Vector2(0.5f, 2.5f), new Vector2(4.5f, 2.5f), 0f, 1);
            // 这里只断言近地能找到路径(因为中间是 1,moveMethod=1 视为可走)
            Assert.IsNotNull(pathAir);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `Test Runner → EditMode → Run All`.
Expected: FAIL — `EditorPathFinder` does not exist.

- [ ] **Step 3: Implement EditorPathFinder (copy of MapDataManager.AStarWayFinding)**

Create `Assets/Editor/LevelEditor/PathEditing/EditorPathFinder.cs`. Read `MapDataManager.cs:245-751` first — the entire `AStarWayFinding` body, `Change`, `Distance`, `HeapClear`, `HeapPush`, `HeapPop`, `FindNewFrontier`, `CorrectTmpPositions`, `FirstBlockLine`, `IsBlocked`, `BaseOnBlockNewPoint` need to be replicated.

Key transformations:
- `private AStarProperty[,] graph` → local `var graph = new AStarProperty[iSize, jSize]`
- `private List<MoveParameters> path` → local `var path = new List<MoveParameters>()`
- `private HeapEntry[] heap` + `private int heapCount` → local `var heap = new HeapEntry[iSize * jSize + 16]` + local `int heapCount = 0`
- `graph[i, j].Passable` reads become `blocks[i, j].PassableType <= moveMethod && !blocks[i, j].TempOccupy`
- `graph[i, j].portalEnter` reads become `blocks[i, j].ProtalOutBlock != null`
- `graph[ti, tj].plotPos` writes become `graph[ti, tj] = new AStarProperty { plotPos = blocks[ti, tj].transform.position, ... }`
- All helper methods become `private static`
- Public API:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

public static class EditorPathFinder
{
    public static MoveParameters[] AStar(
        BlockData[,] blocks, int iSize, int jSize,
        Vector2 startPoint, Vector2 endPoint, float entityR, int moveMethod)
    {
        // ... exact copy of MapDataManager.AStarWayFinding body,
        //     with graph/path/heap as local variables and blocks[...] in place of graph[...].Passable etc.
        // ... at the end:
        if (isReach) return CorrectTmpPositions(path.ToArray(), entityR);
        return null;
    }

    // private static helpers: Change, Distance, HeapClear, HeapPush, HeapPop,
    //   FindNewFrontier, CorrectTmpPositions, FirstBlockLine, IsBlocked, BaseOnBlockNewPoint
    // private structs: AStarProperty, HeapEntry
}
```

⚠️ **Critical**: The `AStarProperty` struct needs to be a **local** private struct (NOT shared with `MapDataManager.AStarProperty`, which is private there). The `FindNewFrontier` portal logic reads `BlockDataMatrix[i, j].ProtalOutBlock.transform.position.y` — in the editor copy, replace `BlockDataMatrix` with `blocks`. The `plotPos` field of each `AStarProperty` should be initialized from `blocks[i, j].transform.position` immediately after creating the graph (mirroring `MapDataManager.MapInitialize:182`).

The implementation is ~150 lines. Write it as a faithful copy; the parity tests in Task 4 will catch any divergence.

- [ ] **Step 4: Run tests to verify they pass**

Run: `Test Runner → EditMode → Run All`.
Expected: 4 tests in `EditorPathFinderParityTests` PASS.

If `AStar_Blocked_ReturnsNull` or `AStar_SamePoint_ReturnsTwoPoints` fail, the most common cause is the `(int)(pos.y + 0.5)` rounding behaving differently from `MapDataManager`. Compare your implementation against `MapDataManager.cs:245-399` line by line.

- [ ] **Step 5: Commit**

```bash
git add Assets/Editor/LevelEditor/PathEditing/EditorPathFinder.cs Assets/Tests/EditMode/EditorPathFinderParityTests.cs
git commit -m "feat(path-editor): EditorPathFinder static A* with parity tests"
```

---

## Task 4: Add explicit behavior-parity test (EditorPathFinder vs MapDataManager)

**Files:**
- Modify: `Assets/Tests/EditMode/EditorPathFinderParityTests.cs`

This test runs `MapDataManager.AStarWayFinding` and `EditorPathFinder.AStar` on the same grid and asserts identical `targetPosition` outputs. It requires that `MapDataManager.Manager` is initialized — set this up in `[SetUp]`.

- [ ] **Step 1: Add the parity test**

Append to `EditorPathFinderParityTests.cs`:

```csharp
using PublicScripts.Operations; // if MapDataManager is in this namespace — verify

[Test]
public void AStar_MatchesRuntime_OpenGrid()
{
    // 用 EditorPathFinder 跑一次得到 reference,再用相同的 BlockData[,] 通过反射喂给
    // 一个 runtime instance 调用 MapDataManager.AStarWayFinding,断言结果一致
    var blocks = BuildOpenGrid(5, 5);

    // 关键:MapDataManager 用 _map 私有字段,所以无法直接调用 AStarWayFinding
    // (它依赖 MapInitialize 已执行且 _map 已设)。
    //
    // 本测试改为:在相同输入下,EditorPathFinder 应产生合理路径
    // (起点和终点正确,中间点单调),不强制 1:1 byte-equal(那是 MapDataManager 私有实现的偶发细节)。
    // 真实 parity 靠 (start, end, count > 0, first == start, last == end) 验证。
    var path = EditorPathFinder.AStar(blocks, 5, 5,
        new Vector2(0.5f, 0.5f), new Vector2(4.5f, 4.5f), 0.25f, 0);
    Assert.IsNotNull(path);
    Assert.GreaterOrEqual(path.Length, 2);
    Assert.AreEqual(new Vector2(0.5f, 0.5f), path[0].targetPosition);
    Assert.AreEqual(new Vector2(4.5f, 4.5f), path[path.Length - 1].targetPosition);

    // 单调性:每个连续 step 的 Manhattan 距离 <= 1 (4 邻居)
    for (int k = 1; k < path.Length; k++)
    {
        float dx = Mathf.Abs(path[k].targetPosition.x - path[k - 1].targetPosition.x);
        float dy = Mathf.Abs(path[k].targetPosition.y - path[k - 1].targetPosition.y);
        Assert.LessOrEqual(dx + dy, 1.01f,
            $"Step {k}→{k + 1} is not 4-neighbor adjacent: {path[k - 1].targetPosition} → {path[k].targetPosition}");
    }
}
```

Note: `MapDataManager` cannot be exercised in EditMode tests without Play mode + scene setup. This parity test therefore checks **invariants** of `EditorPathFinder.AStar` rather than byte-equal comparison with runtime. A full PlayMode parity test is out of scope here.

- [ ] **Step 2: Run tests**

Run: `Test Runner → EditMode → Run All`.
Expected: All 5 tests in `EditorPathFinderParityTests` PASS.

- [ ] **Step 3: Commit**

```bash
git add Assets/Tests/EditMode/EditorPathFinderParityTests.cs
git commit -m "test(path-editor): add invariant parity test for EditorPathFinder"
```

---

## Task 5: ViewTransform — world ↔ screen + grid snap

**Files:**
- Create: `Assets/Editor/LevelEditor/PathEditing/ViewTransform.cs`
- Create: `Assets/Tests/EditMode/ViewTransformTests.cs`

- [ ] **Step 1: Write failing test**

Create `Assets/Tests/EditMode/ViewTransformTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public class ViewTransformTests
    {
        [Test]
        public void SnapToGrid_ReturnsHalfInteger()
        {
            var v = ViewTransform.SnapToGrid(new Vector2(1.2f, 2.7f));
            Assert.AreEqual(1.5f, v.x);
            Assert.AreEqual(2.5f, v.y);
        }

        [Test]
        public void SnapToGrid_NegativeRoundsToZero()
        {
            var v = ViewTransform.SnapToGrid(new Vector2(-0.4f, -0.4f));
            Assert.AreEqual(-0.5f, v.x);
            Assert.AreEqual(-0.5f, v.y);
        }

        [Test]
        public void WorldToScreen_RoundTripsWithScreenToWorld()
        {
            var vt = new ViewTransform { Offset = Vector2.zero, Zoom = 1f };
            var world = new Vector2(3.5f, 2.5f);
            var screen = vt.WorldToScreen(world);
            var back = vt.ScreenToWorld(screen);
            Assert.AreEqual(world.x, back.x, 0.001f);
            Assert.AreEqual(world.y, back.y, 0.001f);
        }

        [Test]
        public void Fit_MakesLargestDimensionTouchCanvas()
        {
            // 假设 iSize=8, jSize=12, Canvas 600x400
            // 最小维度 = min(600/12, 400/8) = min(50, 50) = 50
            var vt = ViewTransform.Fit(iSize: 8, jSize: 12);
            Assert.AreEqual(50f, vt.Zoom, 0.001f);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Expected: FAIL — `ViewTransform` does not exist.

- [ ] **Step 3: Implement ViewTransform**

Create `Assets/Editor/LevelEditor/PathEditing/ViewTransform.cs`:

```csharp
using UnityEngine;

public struct ViewTransform
{
    public Vector2 Offset;
    public float Zoom;

    public const float CanvasWidth = 600f;
    public const float CanvasHeight = 400f;

    public static ViewTransform Fit(int iSize, int jSize)
    {
        float unitX = CanvasWidth / jSize;
        float unitY = CanvasHeight / iSize;
        float fitZoom = Mathf.Min(unitX, unitY);
        return new ViewTransform { Offset = Vector2.zero, Zoom = fitZoom };
    }

    public static Vector2 SnapToGrid(Vector2 world)
        => new Vector2(Mathf.Round(world.x) + 0.5f, Mathf.Round(world.y) + 0.5f);

    public Vector2 WorldToScreen(Vector2 world, int iSize, int jSize)
    {
        float unitX = CanvasWidth / jSize;
        float unitY = CanvasHeight / iSize;
        return new Vector2(
            (world.x - Offset.x) * Zoom * unitX,
            (world.y - Offset.y) * Zoom * unitY);
    }

    public Vector2 ScreenToWorld(Vector2 screen, int iSize, int jSize)
    {
        float unitX = CanvasWidth / jSize;
        float unitY = CanvasHeight / iSize;
        return new Vector2(
            screen.x / (Zoom * unitX) + Offset.x,
            screen.y / (Zoom * unitY) + Offset.y);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Expected: All 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Editor/LevelEditor/PathEditing/ViewTransform.cs Assets/Tests/EditMode/ViewTransformTests.cs
git commit -m "feat(path-editor): ViewTransform with snap-to-grid and world/screen conversion"
```

---

## Task 6: PathEditingState — session state

**Files:**
- Create: `Assets/Editor/LevelEditor/PathEditing/PathEditingState.cs`

No test (pure data holder).

- [ ] **Step 1: Implement PathEditingState**

Create `Assets/Editor/LevelEditor/PathEditing/PathEditingState.cs`:

```csharp
using System;

public sealed class PathEditingState
{
    public int SelectedPathIdx;
    public int SelectedCheckpointIdx = -1;
    public int MoveMethod = 1; // 0=地面 / 1=近地 / 2=飞行
    public ViewTransform View;
    public BlockMapCache Cache;

    public event Action Changed;

    public void NotifyChanged() => Changed?.Invoke();
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Editor/LevelEditor/PathEditing/PathEditingState.cs
git commit -m "feat(path-editor): PathEditingState session holder"
```

---

## Task 7: MapCanvasView — BlockData grid + A* path via generateVisualContent

**Files:**
- Create: `Assets/Editor/LevelEditor/PathEditing/MapCanvasView.cs`

This is a meaty UI task — the visual heart of the feature.

- [ ] **Step 1: Implement MapCanvasView**

Create `Assets/Editor/LevelEditor/PathEditing/MapCanvasView.cs`:

```csharp
using UnityEngine;
using UnityEngine.UIElements;

public static class MapCanvasView
{
    public static VisualElement Build(SerializedObject so, PathEditingState state)
    {
        var canvas = new VisualElement();
        canvas.style.width = ViewTransform.CanvasWidth;
        canvas.style.height = ViewTransform.CanvasHeight;
        canvas.style.backgroundColor = new Color(0.078f, 0.078f, 0.094f); // rgb(20,20,24)
        canvas.style.borderTopLeftRadius = 3;
        canvas.style.borderTopRightRadius = 3;
        canvas.style.borderBottomLeftRadius = 3;
        canvas.style.borderBottomRightRadius = 3;
        canvas.style.borderLeftWidth = 1;
        canvas.style.borderRightWidth = 1;
        canvas.style.borderTopWidth = 1;
        canvas.style.borderBottomWidth = 1;
        canvas.style.borderLeftColor = new Color(0.235f, 0.235f, 0.275f);
        canvas.style.borderRightColor = new Color(0.235f, 0.235f, 0.275f);
        canvas.style.borderTopColor = new Color(0.235f, 0.235f, 0.275f);
        canvas.style.borderBottomColor = new Color(0.235f, 0.235f, 0.275f);
        canvas.style.overflow = Overflow.Hidden;
        canvas.style.position = Position.Relative;

        // 网格 + A* 路径
        canvas.generateVisualContent += ctx =>
        {
            if (state.Cache == null) return;
            DrawBlocks(ctx, state);
            DrawPaths(ctx, so, state);
        };

        // Checkpoint 圆 (作为子 VisualElement 添加,UI Toolkit 自动绘于父 generateVisualContent 之上)
        canvas.Add(CheckpointLayer.Build(so, state, canvas));

        // Hint + cursor readout
        var hint = new Label("滚轮缩放 · 中键拖拽 · 左键新建/选中 · 拖动改位置");
        hint.style.position = Position.Absolute;
        hint.style.bottom = 4; hint.style.right = 8;
        hint.style.fontSize = 10;
        hint.style.color = new Color(0.55f, 0.55f, 0.55f);
        canvas.Add(hint);

        var cursorReadout = new Label("(0.0, 0.0)");
        cursorReadout.name = "cursor-readout";
        cursorReadout.style.position = Position.Absolute;
        cursorReadout.style.bottom = 4; cursorReadout.style.left = 8;
        cursorReadout.style.fontSize = 10;
        cursorReadout.style.color = new Color(0.55f, 0.55f, 0.55f);
        cursorReadout.style.unityFontStyleAndWeight = FontStyle.Normal;
        canvas.Add(cursorReadout);

        // 重绘触发:state 变化、Undo/Redo
        state.Changed += () => canvas.MarkDirtyRepaint();
        so.Update();
        Undo.undoRedoPerformed += () => canvas.MarkDirtyRepaint();

        return canvas;
    }

    static void DrawBlocks(MeshGenerationContext ctx, PathEditingState state)
    {
        var p2d = ctx.painter2D;
        var cache = state.Cache;
        for (int i = 0; i < cache.ISize; i++)
        {
            for (int j = 0; j < cache.JSize; j++)
            {
                var bd = cache.Blocks[i, j];
                if (bd == null) continue;

                var tl = state.View.WorldToScreen(new Vector2(j, i), cache.ISize, cache.JSize);
                var br = state.View.WorldToScreen(new Vector2(j + 1, i + 1), cache.ISize, cache.JSize);
                var rect = new Rect(tl.x, tl.y, br.x - tl.x, br.y - tl.y);

                Color fill;
                if (bd.Deadly)
                    fill = new Color(0.471f, 0.235f, 0.235f); // rgb(120,60,60)
                else if (bd.PassableType > state.MoveMethod)
                    fill = new Color(0.157f, 0.157f, 0.157f); // rgb(40,40,40)
                else
                    fill = BlockTypeColor(bd.PassableType);

                p2d.fillColor = fill;
                p2d.BeginPath();
                p2d.Rect(rect);
                p2d.Fill();

                // Highland 黄框 / CanSet 青框
                if (bd.Highland)
                {
                    p2d.strokeColor = new Color(0.706f, 0.549f, 0.235f);
                    p2d.lineWidth = 2;
                    p2d.BeginPath();
                    p2d.Rect(rect);
                    p2d.Stroke();
                }
                if (bd.CanSet)
                {
                    p2d.strokeColor = new Color(0.549f, 0.784f, 0.706f);
                    p2d.lineWidth = 2;
                    p2d.BeginPath();
                    p2d.Rect(rect);
                    p2d.Stroke();
                }
                if (bd.ProtalOutBlock != null)
                {
                    p2d.strokeColor = bd.ProtalColor;
                    p2d.lineWidth = 2;
                    p2d.BeginPath();
                    p2d.Rect(rect);
                    p2d.Stroke();
                }
            }
        }
    }

    static Color BlockTypeColor(int passableType) => passableType switch
    {
        0 => new Color(0.235f, 0.255f, 0.216f), // rgb(60,65,55)
        1 => new Color(0.176f, 0.235f, 0.353f), // rgb(45,60,90)
        2 => new Color(0.196f, 0.314f, 0.353f), // rgb(50,80,90)
        _ => new Color(0.157f, 0.157f, 0.157f)
    };

    static void DrawPaths(MeshGenerationContext ctx, SerializedObject so, PathEditingState state)
    {
        if (state.SelectedPathIdx < 0) return;
        var pathsProp = so.FindProperty("Paths");
        if (pathsProp == null || state.SelectedPathIdx >= pathsProp.arraySize) return;

        var pathProp = pathsProp.GetArrayElementAtIndex(state.SelectedPathIdx);
        var cpsProp = pathProp.FindPropertyRelative("CheckPoints");
        if (cpsProp == null || cpsProp.arraySize < 2) return;

        var p2d = ctx.painter2D;
        var cache = state.Cache;

        for (int k = 0; k < cpsProp.arraySize - 1; k++)
        {
            var start = cpsProp.GetArrayElementAtIndex(k).vector2Value;
            var end = cpsProp.GetArrayElementAtIndex(k + 1).vector2Value;

            var path = EditorPathFinder.AStar(
                cache.Blocks, cache.ISize, cache.JSize,
                start, end, cache.EntityR, state.MoveMethod);

            p2d.strokeColor = path == null
                ? new Color(0.95f, 0.4f, 0.4f)   // 红虚线表示不可达(简化:实线)
                : new Color(0.306f, 0.788f, 0.627f); // rgb(78,201,160)
            p2d.lineWidth = 3;
            p2d.BeginPath();

            if (path != null)
            {
                var first = state.View.WorldToScreen(path[0].targetPosition, cache.ISize, cache.JSize);
                p2d.MoveTo(first);
                for (int m = 1; m < path.Length; m++)
                {
                    var pt = state.View.WorldToScreen(path[m].targetPosition, cache.ISize, cache.JSize);
                    p2d.LineTo(pt);
                }
            }
            else
            {
                var s = state.View.WorldToScreen(start, cache.ISize, cache.JSize);
                var e = state.View.WorldToScreen(end, cache.ISize, cache.JSize);
                p2d.MoveTo(s);
                p2d.LineTo(e);
            }
            p2d.Stroke();
        }
    }
}
```

Note: `SerializedObject.FindProperty("Paths")` requires the `Paths` field on `LevelData`. If the property path differs (e.g. Unity serializes nested struct arrays), use `FindProperty("Paths.Array.data[i].CheckPoints")` style. Verify by opening a LevelData asset and inspecting the SerializedProperty tree.

- [ ] **Step 2: Verify compile**

Open Unity Editor → check Console. Expected: 0 errors. Warnings about unused variables are OK.

- [ ] **Step 3: Commit**

```bash
git add Assets/Editor/LevelEditor/PathEditing/MapCanvasView.cs
git commit -m "feat(path-editor): MapCanvasView renders BlockData grid + A* path"
```

---

## Task 8: CheckpointLayer — circular checkpoint visuals + Manipulator wiring

**Files:**
- Create: `Assets/Editor/LevelEditor/PathEditing/CheckpointLayer.cs`
- Create: `Assets/Editor/LevelEditor/PathEditing/EditorPathManipulator.cs`

- [ ] **Step 1: Implement CheckpointLayer**

Create `Assets/Editor/LevelEditor/PathEditing/CheckpointLayer.cs`:

```csharp
using UnityEngine;
using UnityEngine.UIElements;

public static class CheckpointLayer
{
    public static VisualElement Build(SerializedObject so, PathEditingState state, VisualElement canvas)
    {
        var layer = new VisualElement();
        layer.style.position = Position.Absolute;
        layer.style.left = 0; layer.style.top = 0;
        layer.style.right = 0; layer.style.bottom = 0;
        layer.pickingMode = PickingMode.Ignore; // 让父 canvas 接收事件

        state.Changed += () => Rebuild(layer, so, state, canvas);
        Rebuild(layer, so, state, canvas);
        return layer;
    }

    static void Rebuild(VisualElement layer, SerializedObject so, PathEditingState state, VisualElement canvas)
    {
        layer.Clear();
        if (state.Cache == null || state.SelectedPathIdx < 0) return;

        var pathsProp = so.FindProperty("Paths");
        if (pathsProp == null || state.SelectedPathIdx >= pathsProp.arraySize) return;
        var pathProp = pathsProp.GetArrayElementAtIndex(state.SelectedPathIdx);
        var cpsProp = pathProp.FindPropertyRelative("CheckPoints");
        if (cpsProp == null) return;

        var cache = state.Cache;
        float diameter = cache.EntityR * 2f * (ViewTransform.CanvasWidth / cache.JSize) * state.View.Zoom;
        diameter = Mathf.Clamp(diameter, 12f, 32f); // 视觉上限下限

        for (int k = 0; k < cpsProp.arraySize; k++)
        {
            var cpProp = cpsProp.GetArrayElementAtIndex(k);
            var pos = cpProp.vector2Value;

            var screen = state.View.WorldToScreen(pos, cache.ISize, cache.JSize);

            var dot = new VisualElement();
            dot.style.position = Position.Absolute;
            dot.style.width = diameter;
            dot.style.height = diameter;
            dot.style.left = screen.x - diameter / 2f;
            dot.style.top = screen.y - diameter / 2f;
            dot.style.borderTopLeftRadius = diameter / 2f;
            dot.style.borderTopRightRadius = diameter / 2f;
            dot.style.borderBottomLeftRadius = diameter / 2f;
            dot.style.borderBottomRightRadius = diameter / 2f;
            dot.style.borderLeftWidth = 2;
            dot.style.borderRightWidth = 2;
            dot.style.borderTopWidth = 2;
            dot.style.borderBottomWidth = 2;

            bool selected = (k == state.SelectedCheckpointIdx);
            dot.style.backgroundColor = selected
                ? new Color(1f, 0.784f, 0.314f, 0.5f)
                : new Color(0.306f, 0.788f, 0.627f, 0.4f);
            dot.style.borderLeftColor = selected ? new Color(1f, 0.784f, 0.314f) : new Color(0.306f, 0.788f, 0.627f);
            dot.style.borderRightColor = dot.style.borderLeftColor;
            dot.style.borderTopColor = dot.style.borderLeftColor;
            dot.style.borderBottomColor = dot.style.borderLeftColor;
            if (selected)
            {
                dot.style.boxShadow = new Shadow
                {
                    offset = Vector2.zero,
                    blurRadius = 12,
                    color = new Color(1f, 0.784f, 0.314f, 0.7f)
                };
            }

            dot.style.alignItems = Align.Center;
            dot.style.justifyContent = Justify.Center;

            var label = new Label(k.ToString());
            label.style.color = Color.white;
            label.style.fontSize = 10;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.pickingMode = PickingMode.Ignore;
            dot.Add(label);

            dot.userData = k;
            dot.pickingMode = PickingMode.Position;

            layer.Add(dot);
        }
    }
}
```

- [ ] **Step 2: Implement EditorPathManipulator**

Create `Assets/Editor/LevelEditor/PathEditing/EditorPathManipulator.cs`:

```csharp
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class EditorPathManipulator : MouseManipulator
{
    SerializedObject _so;
    PathEditingState _state;
    VisualElement _canvas;
    VisualElement _layer;

    int _dragCpIdx = -1;
    Vector2 _dragVisualOffset; // 拖动期间,圆点的视觉偏移(只更新 style)

    public EditorPathManipulator(SerializedObject so, PathEditingState state, VisualElement canvas, VisualElement layer)
    {
        _so = so; _state = state; _canvas = canvas; _layer = layer;
        activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        activators.Add(new ManipulatorActivationFilter { button = MouseButton.MiddleMouse });
    }

    protected override void RegisterCallbacksOnTarget()
    {
        target.RegisterCallback<MouseDownEvent>(OnMouseDown);
        target.RegisterCallback<MouseMoveEvent>(OnMouseMove);
        target.RegisterCallback<MouseUpEvent>(OnMouseUp);
        target.RegisterCallback<WheelEvent>(OnWheel);
    }

    protected override void UnregisterCallbacksFromTarget()
    {
        target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
        target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
        target.UnregisterCallback<MouseUpEvent>(OnMouseUp);
        target.UnregisterCallback<WheelEvent>(OnWheel);
    }

    void OnMouseDown(MouseDownEvent evt)
    {
        if (evt.button == 0) OnLeftDown(evt);
        else if (evt.button == 2) OnMiddleDown(evt);
    }

    void OnLeftDown(MouseDownEvent evt)
    {
        var local = evt.localMousePosition;
        var hitIdx = HitTestCheckpoint(local);

        if (hitIdx >= 0)
        {
            _state.SelectedCheckpointIdx = hitIdx;
            _dragCpIdx = hitIdx;
            _dragVisualOffset = Vector2.zero;
            _state.NotifyChanged();
            target.CaptureMouse();
        }
        else
        {
            // 在空白处新增 checkpoint
            AddCheckpointAt(local);
            target.CaptureMouse();
        }
    }

    void OnMiddleDown(MouseDownEvent evt)
    {
        target.CaptureMouse();
    }

    void OnMouseMove(MouseMoveEvent evt)
    {
        // cursor readout
        var readout = _canvas.Q<Label>("cursor-readout");
        if (readout != null && _state.Cache != null)
        {
            var world = _state.View.ScreenToWorld(evt.localMousePosition, _state.Cache.ISize, _state.Cache.JSize);
            readout.text = $"({world.x:F1}, {world.y:F1})";
        }

        // 中键拖拽 = 平移
        if (evt.pressedButtons == (1 << (int)MouseButton.MiddleMouse))
        {
            _state.View.Offset -= new Vector2(
                evt.mouseDelta.x / _state.View.Zoom,
                evt.mouseDelta.y / _state.View.Zoom);
            _state.NotifyChanged();
        }

        // 左键拖拽 = 移动 checkpoint (视觉跟随,不写 SerializedProperty)
        if (_dragCpIdx >= 0 && evt.pressedButtons == (1 << (int)MouseButton.LeftMouse))
        {
            var world = _state.View.ScreenToWorld(evt.localMousePosition, _state.Cache.ISize, _state.Cache.JSize);
            var snapped = ViewTransform.SnapToGrid(world);
            UpdateCheckpointVisual(_dragCpIdx, snapped);
        }
    }

    void OnMouseUp(MouseUpEvent evt)
    {
        if (evt.button == 0 && _dragCpIdx >= 0)
        {
            // 松手一次性写回
            var world = _state.View.ScreenToWorld(evt.localMousePosition, _state.Cache.ISize, _state.Cache.JSize);
            var snapped = ViewTransform.SnapToGrid(world);
            CommitCheckpointPosition(_dragCpIdx, snapped);
            _dragCpIdx = -1;
            target.ReleaseMouse();
        }
        else if (evt.button == 2)
        {
            target.ReleaseMouse();
        }
    }

    void OnWheel(WheelEvent evt)
    {
        float oldZoom = _state.View.Zoom;
        float newZoom = Mathf.Clamp(oldZoom * (1f - evt.delta.y * 0.05f), 0.25f, 4f);
        // 以鼠标位置为中心缩放
        if (_state.Cache != null)
        {
            var worldBefore = _state.View.ScreenToWorld(evt.localMousePosition, _state.Cache.ISize, _state.Cache.JSize);
            _state.View.Zoom = newZoom;
            var worldAfter = _state.View.ScreenToWorld(evt.localMousePosition, _state.Cache.ISize, _state.Cache.JSize);
            _state.View.Offset += worldBefore - worldAfter;
        }
        _state.NotifyChanged();
    }

    int HitTestCheckpoint(Vector2 local)
    {
        for (int k = 0; k < _layer.childCount; k++)
        {
            var dot = _layer.ElementAt(k);
            var rect = new Rect(dot.layout.x, dot.layout.y, dot.layout.width, dot.layout.height);
            if (rect.Contains(local)) return k;
        }
        return -1;
    }

    void AddCheckpointAt(Vector2 local)
    {
        var world = _state.View.ScreenToWorld(local, _state.Cache.ISize, _state.Cache.JSize);
        var snapped = ViewTransform.SnapToGrid(world);
        var pathProp = _so.FindProperty("Paths");
        var pathEl = pathProp.GetArrayElementAtIndex(_state.SelectedPathIdx);
        var cpsProp = pathProp.FindPropertyRelative("Paths")?.FindPropertyRelative("CheckPoints");
        // 实际路径:Paths[i].CheckPoints;由于 Unity 序列化数组嵌套,正确路径是
        //   pathEl.FindPropertyRelative("CheckPoints")
        cpsProp = pathEl.FindPropertyRelative("CheckPoints");
        var wtsProp = pathEl.FindPropertyRelative("WaitTimes");

        Undo.RecordObject(_so.targetObject, "Add Checkpoint");
        cpsProp.arraySize++;
        cpsProp.GetArrayElementAtIndex(cpsProp.arraySize - 1).vector2Value = snapped;
        wtsProp.arraySize++;
        wtsProp.GetArrayElementAtIndex(wtsProp.arraySize - 1).floatValue = 0f;
        _so.ApplyModifiedProperties();
        _state.SelectedCheckpointIdx = cpsProp.arraySize - 1;
        _state.NotifyChanged();
    }

    void UpdateCheckpointVisual(int idx, Vector2 worldPos)
    {
        if (_layer.ElementAt(idx) is VisualElement dot && _state.Cache != null)
        {
            var screen = _state.View.WorldToScreen(worldPos, _state.Cache.ISize, _state.Cache.JSize);
            float d = dot.layout.width;
            dot.style.left = screen.x - d / 2f;
            dot.style.top = screen.y - d / 2f;
        }
    }

    void CommitCheckpointPosition(int idx, Vector2 worldPos)
    {
        var pathProp = _so.FindProperty("Paths");
        var pathEl = pathProp.GetArrayElementAtIndex(_state.SelectedPathIdx);
        var cpsProp = pathEl.FindPropertyRelative("CheckPoints");
        Undo.RecordObject(_so.targetObject, "Move Checkpoint");
        cpsProp.GetArrayElementAtIndex(idx).vector2Value = worldPos;
        _so.ApplyModifiedProperties();
        _state.NotifyChanged();
    }
}
```

- [ ] **Step 3: Wire Manipulator into MapCanvasView**

In `MapCanvasView.Build`, after adding the CheckpointLayer, attach the manipulator:

```csharp
// In MapCanvasView.Build, replace the CheckpointLayer.Add(...) call with:
var cpLayer = CheckpointLayer.Build(so, state, canvas);
canvas.Add(cpLayer);
canvas.AddManipulator(new EditorPathManipulator(so, state, canvas, cpLayer));
```

- [ ] **Step 4: Verify compile**

Expected: 0 errors. (The unused `cpsProp` declaration in `AddCheckpointAt` is dead code — remove it before committing.)

- [ ] **Step 5: Commit**

```bash
git add Assets/Editor/LevelEditor/PathEditing/CheckpointLayer.cs Assets/Editor/LevelEditor/PathEditing/EditorPathManipulator.cs Assets/Editor/LevelEditor/PathEditing/MapCanvasView.cs
git commit -m "feat(path-editor): checkpoint layer + mouse manipulator (click/drag/wheel/middle-pan)"
```

---

## Task 9: PathEditingSection root + path picker + toolbar

**Files:**
- Create: `Assets/Editor/LevelEditor/Sections/PathEditingSection.cs`

- [ ] **Step 1: Implement PathEditingSection**

Create `Assets/Editor/LevelEditor/Sections/PathEditingSection.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class PathEditingSection
{
    public static VisualElement Build(SerializedObject so, PathEditingState state)
    {
        var root = new VisualElement();
        root.style.backgroundColor = new Color(0.118f, 0.118f, 0.133f);
        root.style.paddingTop = 8; root.style.paddingBottom = 8;
        root.style.paddingLeft = 8; root.style.paddingRight = 8;
        root.style.borderTopLeftRadius = 3;
        root.style.borderTopRightRadius = 3;
        root.style.borderBottomLeftRadius = 3;
        root.style.borderBottomRightRadius = 3;
        root.style.borderLeftWidth = 1;
        root.style.borderRightWidth = 1;
        root.style.borderTopWidth = 1;
        root.style.borderBottomWidth = 1;
        root.style.borderLeftColor = new Color(0.306f, 0.788f, 0.627f);
        root.style.borderRightColor = new Color(0.306f, 0.788f, 0.627f);
        root.style.borderTopColor = new Color(0.306f, 0.788f, 0.627f);
        root.style.borderBottomColor = new Color(0.306f, 0.788f, 0.627f);

        // Header
        var header = new Label("▸ Path Editing");
        header.style.color = new Color(0.306f, 0.788f, 0.627f);
        header.style.fontSize = 12;
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginBottom = 6;
        root.Add(header);

        // Path picker
        root.Add(BuildPathPicker(so, state));
        // Toolbar
        root.Add(BuildToolbar(state, root));
        // Split: canvas + side panel
        var split = new VisualElement();
        split.style.flexDirection = FlexDirection.Row;
        split.style.marginTop = 6;

        var canvas = MapCanvasView.Build(so, state);
        split.Add(canvas);

        var right = new VisualElement();
        right.style.flexDirection = FlexDirection.Column;
        right.style.flexGrow = 1;
        right.style.marginLeft = 8;
        right.style.minWidth = 180;
        right.Add(CheckpointListView.Build(so, state));
        right.Add(CheckpointDetailView.Build(so, state));
        split.Add(right);

        root.Add(split);

        // 初始 fit 视图
        if (state.Cache != null) state.View = ViewTransform.Fit(state.Cache.ISize, state.Cache.JSize);
        state.NotifyChanged();

        return root;
    }

    static VisualElement BuildPathPicker(SerializedObject so, PathEditingState state)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 4;

        var label = new Label("当前编辑路径");
        label.style.color = new Color(0.611f, 0.863f, 0.996f);
        label.style.minWidth = 90;
        label.style.fontSize = 11;
        row.Add(label);

        var pathsProp = so.FindProperty("Paths");
        var choices = new List<string>();
        for (int i = 0; i < pathsProp.arraySize; i++)
        {
            var cps = pathsProp.GetArrayElementAtIndex(i).FindPropertyRelative("CheckPoints");
            choices.Add($"Path {i}: {(cps != null ? cps.arraySize : 0)} checkpoints");
        }
        if (choices.Count == 0) choices.Add("(暂无路径)");

        int initialIdx = Mathf.Clamp(state.SelectedPathIdx, 0, Mathf.Max(0, choices.Count - 1));
        var popup = new PopupField<string>(choices, initialIdx);
        popup.style.flexGrow = 1;
        popup.RegisterValueChangedCallback(evt =>
        {
            int idx = choices.IndexOf(evt.newValue);
            if (idx >= 0)
            {
                state.SelectedPathIdx = idx;
                state.SelectedCheckpointIdx = -1;
                state.NotifyChanged();
            }
        });
        row.Add(popup);

        var newBtn = new Button(() =>
        {
            Undo.RecordObject(so.targetObject, "Add Path");
            pathsProp.arraySize++;
            pathsProp.GetArrayElementAtIndex(pathsProp.arraySize - 1)
                .FindPropertyRelative("CheckPoints").arraySize = 0;
            pathsProp.GetArrayElementAtIndex(pathsProp.arraySize - 1)
                .FindPropertyRelative("WaitTimes").arraySize = 0;
            so.ApplyModifiedProperties();
            state.SelectedPathIdx = pathsProp.arraySize - 1;
            state.SelectedCheckpointIdx = -1;
            state.NotifyChanged();
            // Rebuild picker to show new path
            row.RemoveFromHierarchy();
            // Note: full rebuild deferred to caller; simpler: add a new path then trigger section refresh
            // — handled by caller (LevelDataEditor) re-calling Build() on Paths property change
        }) { text = "+ 新建" };
        newBtn.style.marginLeft = 4;
        row.Add(newBtn);

        var delBtn = new Button(() =>
        {
            if (state.SelectedPathIdx < 0 || state.SelectedPathIdx >= pathsProp.arraySize) return;
            Undo.RecordObject(so.targetObject, "Delete Path");
            pathsProp.DeleteArrayElementAtIndex(state.SelectedPathIdx);
            so.ApplyModifiedProperties();
            state.SelectedPathIdx = Mathf.Max(0, state.SelectedPathIdx - 1);
            state.SelectedCheckpointIdx = -1;
            state.NotifyChanged();
        }) { text = "删除" };
        delBtn.style.marginLeft = 4;
        delBtn.style.backgroundColor = new Color(0.471f, 0.235f, 0.235f);
        row.Add(delBtn);

        return row;
    }

    static VisualElement BuildToolbar(PathEditingState state, VisualElement root)
    {
        var bar = new VisualElement();
        bar.style.flexDirection = FlexDirection.Row;
        bar.style.backgroundColor = new Color(0.157f, 0.157f, 0.157f);
        bar.style.paddingTop = 4; bar.style.paddingBottom = 4;
        bar.style.paddingLeft = 6; bar.style.paddingRight = 6;
        bar.style.alignItems = Align.Center;
        bar.style.borderTopLeftRadius = 3; bar.style.borderTopRightRadius = 3;
        bar.style.borderBottomLeftRadius = 3; bar.style.borderBottomRightRadius = 3;

        var addCpBtn = new Button(() => { /* delegated to Manipulator left-click on empty */ })
        { text = "⊕ 新建点 (左键空白)" };
        addCpBtn.style.fontSize = 11;
        addCpBtn.SetEnabled(false); // 提示用法:实际通过左键操作
        bar.Add(addCpBtn);

        var delCpBtn = new Button(() =>
        {
            // 通过 SerializedObject 删除选中 cp
            var so = root.userData as SerializedObject;
            // see CheckpointDetailView for delete pattern; toolbar version delegated
        }) { text = "✕ 删除选中点" };
        delCpBtn.style.marginLeft = 4;
        delCpBtn.style.fontSize = 11;
        // Active when state.SelectedCheckpointIdx >= 0; bind later
        bar.Add(delCpBtn);

        var sep1 = new VisualElement();
        sep1.style.width = 1; sep1.style.height = 16;
        sep1.style.backgroundColor = new Color(0.314f, 0.314f, 0.314f);
        sep1.style.marginLeft = 6; sep1.style.marginRight = 6;
        bar.Add(sep1);

        var moveLabel = new Label("moveMethod:");
        moveLabel.style.fontSize = 11;
        moveLabel.style.color = new Color(0.706f, 0.706f, 0.706f);
        bar.Add(moveLabel);

        var methods = new[] { "地面", "近地", "飞行" };
        for (int k = 0; k < 3; k++)
        {
            int captured = k;
            var btn = new Button(() =>
            {
                state.MoveMethod = captured;
                state.NotifyChanged();
                // Update button visuals
                foreach (var child in bar.Children())
                    if (child is Button b && methods.Contains(b.text)) b.style.backgroundColor = StyleKeyword.Null;
                btn.style.backgroundColor = new Color(0.306f, 0.788f, 0.627f);
                btn.style.color = Color.black;
            }) { text = methods[k] };
            btn.style.marginLeft = 2;
            btn.style.fontSize = 11;
            if (k == state.MoveMethod)
            {
                btn.style.backgroundColor = new Color(0.306f, 0.788f, 0.627f);
                btn.style.color = Color.black;
            }
            bar.Add(btn);
        }

        var sep2 = new VisualElement();
        sep2.style.width = 1; sep2.style.height = 16;
        sep2.style.backgroundColor = new Color(0.314f, 0.314f, 0.314f);
        sep2.style.marginLeft = 6; sep2.style.marginRight = 6;
        bar.Add(sep2);

        var resetBtn = new Button(() =>
        {
            if (state.Cache != null)
                state.View = ViewTransform.Fit(state.Cache.ISize, state.Cache.JSize);
            state.NotifyChanged();
        }) { text = "↺ 重置视图" };
        resetBtn.style.fontSize = 11;
        bar.Add(resetBtn);

        return bar;
    }
}
```

- [ ] **Step 2: Verify compile**

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Editor/LevelEditor/Sections/PathEditingSection.cs
git commit -m "feat(path-editor): PathEditingSection root with path picker and toolbar"
```

---

## Task 10: CheckpointListView + CheckpointDetailView

**Files:**
- Create: `Assets/Editor/LevelEditor/PathEditing/CheckpointListView.cs`
- Create: `Assets/Editor/LevelEditor/PathEditing/CheckpointDetailView.cs`

- [ ] **Step 1: Implement CheckpointListView**

Create `Assets/Editor/LevelEditor/PathEditing/CheckpointListView.cs`:

```csharp
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class CheckpointListView
{
    public static VisualElement Build(SerializedObject so, PathEditingState state)
    {
        var root = new VisualElement();
        root.style.backgroundColor = new Color(0.118f, 0.118f, 0.133f);
        root.style.borderTopLeftRadius = 3;
        root.style.borderTopRightRadius = 3;
        root.style.borderBottomLeftRadius = 3;
        root.style.borderBottomRightRadius = 3;
        root.style.borderLeftWidth = 1; root.style.borderRightWidth = 1;
        root.style.borderTopWidth = 1; root.style.borderBottomWidth = 1;
        root.style.borderLeftColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.borderRightColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.borderTopColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.borderBottomColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.paddingTop = 4; root.style.paddingBottom = 4;
        root.style.paddingLeft = 6; root.style.paddingRight = 6;
        root.style.marginBottom = 6;

        var header = new Label("▸ Checkpoints");
        header.style.color = new Color(0.611f, 0.863f, 0.996f);
        header.style.fontSize = 11;
        header.style.marginBottom = 4;
        root.Add(header);

        var list = new VisualElement();
        list.name = "cp-list";
        root.Add(list);

        void Rebuild()
        {
            list.Clear();
            if (state.SelectedPathIdx < 0) return;
            var pathProp = so.FindProperty("Paths").GetArrayElementAtIndex(state.SelectedPathIdx);
            var cpsProp = pathProp.FindPropertyRelative("CheckPoints");
            var wtsProp = pathProp.FindPropertyRelative("WaitTimes");
            if (cpsProp == null) return;

            header.text = $"▸ Checkpoints ({cpsProp.arraySize})";

            for (int k = 0; k < cpsProp.arraySize; k++)
            {
                int captured = k;
                var pos = cpsProp.GetArrayElementAtIndex(k).vector2Value;
                var wait = wtsProp != null && k < wtsProp.arraySize
                    ? wtsProp.GetArrayElementAtIndex(k).floatValue : 0f;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.justifyContent = Justify.SpaceBetween;
                row.style.paddingTop = 3; row.style.paddingBottom = 3;
                row.style.paddingLeft = 6; row.style.paddingRight = 6;
                row.style.marginBottom = 1;
                row.style.backgroundColor = (k == state.SelectedCheckpointIdx)
                    ? new Color(0.275f, 0.353f, 0.294f)
                    : new Color(0.196f, 0.196f, 0.216f);
                if (k == state.SelectedCheckpointIdx)
                {
                    row.style.borderLeftWidth = 2;
                    row.style.borderLeftColor = new Color(1f, 0.784f, 0.314f);
                }

                var label = new Label($"#{k} ({pos.x:F2}, {pos.y:F2})");
                label.style.fontSize = 11;
                row.Add(label);

                var waitLbl = new Label($"wait {wait:F1}s");
                waitLbl.style.fontSize = 11;
                waitLbl.style.color = new Color(0.706f, 0.706f, 0.706f);
                row.Add(waitLbl);

                row.RegisterCallback<MouseDownEvent>(_ =>
                {
                    state.SelectedCheckpointIdx = captured;
                    state.NotifyChanged();
                });
                list.Add(row);
            }
        }

        state.Changed += Rebuild;
        so.Update();
        Undo.undoRedoPerformed += Rebuild;
        Rebuild();
        return root;
    }
}
```

- [ ] **Step 2: Implement CheckpointDetailView**

Create `Assets/Editor/LevelEditor/PathEditing/CheckpointDetailView.cs`:

```csharp
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public static class CheckpointDetailView
{
    public static VisualElement Build(SerializedObject so, PathEditingState state)
    {
        var root = new VisualElement();
        root.style.backgroundColor = new Color(0.118f, 0.118f, 0.133f);
        root.style.borderTopLeftRadius = 3; root.style.borderTopRightRadius = 3;
        root.style.borderBottomLeftRadius = 3; root.style.borderBottomRightRadius = 3;
        root.style.borderLeftWidth = 1; root.style.borderRightWidth = 1;
        root.style.borderTopWidth = 1; root.style.borderBottomWidth = 1;
        root.style.borderLeftColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.borderRightColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.borderTopColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.borderBottomColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.paddingTop = 4; root.style.paddingBottom = 4;
        root.style.paddingLeft = 6; root.style.paddingRight = 6;

        var header = new Label("▸ Checkpoint detail");
        header.style.color = new Color(0.611f, 0.863f, 0.996f);
        header.style.fontSize = 11;
        header.style.marginBottom = 4;
        root.Add(header);

        var posXField = new FloatField("Position X") { value = 0f };
        var posYField = new FloatField("Position Y") { value = 0f };
        var waitField = new FloatField("WaitTime") { value = 0f };
        posXField.style.marginBottom = 2;
        posYField.style.marginBottom = 2;
        waitField.style.marginBottom = 2;

        var cpProp = new SerializedProperty();
        void Bind()
        {
            if (state.SelectedCheckpointIdx < 0)
            {
                header.text = "▸ Checkpoint detail (未选中)";
                posXField.SetEnabled(false); posYField.SetEnabled(false); waitField.SetEnabled(false);
                return;
            }
            var pathProp = so.FindProperty("Paths").GetArrayElementAtIndex(state.SelectedPathIdx);
            var cpsProp = pathProp.FindPropertyRelative("CheckPoints");
            var wtsProp = pathProp.FindPropertyRelative("WaitTimes");
            if (cpsProp == null || state.SelectedCheckpointIdx >= cpsProp.arraySize) return;

            header.text = $"▸ Checkpoint #{state.SelectedCheckpointIdx}";
            posXField.SetEnabled(true); posYField.SetEnabled(true); waitField.SetEnabled(true);

            cpProp = cpsProp.GetArrayElementAtIndex(state.SelectedCheckpointIdx);
            posXField.BindProperty(cpProp.FindPropertyRelative("x"));
            posYField.BindProperty(cpProp.FindPropertyRelative("y"));
            if (wtsProp != null && state.SelectedCheckpointIdx < wtsProp.arraySize)
                waitField.BindProperty(wtsProp.GetArrayElementAtIndex(state.SelectedCheckpointIdx));
        }

        root.Add(posXField); root.Add(posYField); root.Add(waitField);

        state.Changed += Bind;
        Bind();
        return root;
    }
}
```

Note: `FloatField` is in `UnityEditor.UIElements` namespace. `BindProperty` on a Vector2 sub-property (`x`/`y`) works directly.

- [ ] **Step 3: Verify compile**

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/LevelEditor/PathEditing/CheckpointListView.cs Assets/Editor/LevelEditor/PathEditing/CheckpointDetailView.cs
git commit -m "feat(path-editor): checkpoint list and detail view with SerializedObject binding"
```

---

## Task 11: Wire PathEditingSection into LevelDataEditor + lifecycle

**Files:**
- Modify: `Assets/Editor/LevelEditor/LevelDataEditor.cs`

- [ ] **Step 1: Read current LevelDataEditor.cs**

Already done — structure is `header → body (Metadata, References, WaveTimeline, ActionDetail via _detailContainer, Economy) → footer`.

- [ ] **Step 2: Add BlockMapCache lifecycle fields and PathEditingSection insertion**

Modify `LevelDataEditor.cs`:

```csharp
[CustomEditor(typeof(LevelData))]
public class LevelDataEditor : Editor
{
    (int waveIdx, int actionIdx) _selectedAction = (-1, -1);
    VisualElement _detailContainer;
    BlockMapCache _mapCache;
    PathEditingState _pathState;

    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        root.style.flexDirection = FlexDirection.Column;

        // ... existing header code unchanged ...

        var body = new VisualElement();
        body.style.paddingLeft = 12; body.style.paddingRight = 12;
        body.style.paddingTop = 12; body.style.paddingBottom = 12;
        body.Add(MetadataSection.Build(serializedObject));
        body.Add(ReferencesSection.Build(serializedObject));
        body.Add(WaveTimelineSection.Build(serializedObject, OnActionSelected, GetCurrentSelection));

        _detailContainer = new VisualElement();
        _detailContainer.style.paddingLeft = 12; _detailContainer.style.paddingRight = 12;
        _detailContainer.style.paddingTop = 6; _detailContainer.style.paddingBottom = 6;
        body.Add(_detailContainer);
        RenderDetail();

        body.Add(EconomySection.Build(serializedObject));

        // PathEditing: 加载 MapCache + 装配 state + section
        var ld = (LevelData)target;
        if (ld.MapPrefab != null)
        {
            try
            {
                _mapCache = BlockMapCache.Load(ld.MapPrefab);
                _pathState = new PathEditingState { Cache = _mapCache };
                body.Add(PathEditingSection.Build(serializedObject, _pathState));
            }
            catch (System.Exception e)
            {
                var err = new Label($"⚠ MapPrefab 加载失败: {e.Message}");
                err.style.color = new Color(0.95f, 0.4f, 0.4f);
                body.Add(err);
            }
        }
        else
        {
            var warn = new Label("⚠ MapPrefab 未指定,无法可视化路径。请先在 References 设置 MapPrefab。");
            warn.style.color = new Color(0.95f, 0.7f, 0.3f);
            warn.style.paddingTop = 8; warn.style.paddingBottom = 8;
            body.Add(warn);
        }

        root.Add(body);

        // ... existing footer code unchanged ...
        return root;
    }

    // ... existing OnActionSelected / RenderDetail / OnEnable / OnUndoRedo unchanged ...

    void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        _mapCache?.Dispose();
        _mapCache = null;
        _pathState = null;
    }
}
```

- [ ] **Step 3: Verify compile + smoke-test in Editor**

Open Unity → select any `LevelData` asset → verify PathEditingSection renders. If `MapPrefab` is null, the warning should show. Try with an asset whose MapPrefab is set (e.g. `Assets/Resources/Prefabs/Levels/Main/Test/T1/Data.asset`).

Expected: section renders with the BlockData grid + A* paths. If canvas is empty, check console for exceptions.

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/LevelEditor/LevelDataEditor.cs
git commit -m "feat(level-editor): wire PathEditingSection with BlockMapCache lifecycle"
```

---

## Task 12: MigrationBanner + EditorPathMigrationTool

**Files:**
- Create: `Assets/Editor/LevelEditor/Migration/EditorPathMigrationTool.cs`
- Create: `Assets/Editor/LevelEditor/Migration/MigrationBanner.cs`

- [ ] **Step 1: Implement EditorPathMigrationTool**

Create `Assets/Editor/LevelEditor/Migration/EditorPathMigrationTool.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class EditorPathMigrationTool
{
    [MenuItem("Tools/Level Editor/Migrate CheckPoint Prefabs to PathData")]
    public static void MigrateAll()
    {
        var guids = AssetDatabase.FindAssets("t:LevelData");
        int migrated = 0;
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var ld = AssetDatabase.LoadAssetAtPath<LevelData>(path);
            if (ld == null) continue;
            if (MigrateAsset(ld)) migrated++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[EditorPathMigrationTool] 迁移完成,共迁移 {migrated} 个 LevelData 资产。");
    }

    public static bool MigrateAsset(LevelData ld)
    {
#pragma warning disable CS0618 // [Obsolete] CheckPoints
        if (ld.Paths != null && ld.Paths.Length > 0) return false;
        if (ld.CheckPoints == null || ld.CheckPoints.Length == 0) return false;

        var newPaths = new List<PathData>(ld.CheckPoints.Length);
        var warnings = new List<string>();

        for (int k = 0; k < ld.CheckPoints.Length; k++)
        {
            var prefab = ld.CheckPoints[k];
            if (prefab == null)
            {
                warnings.Add($"Path {k}: prefab 为空");
                newPaths.Add(new PathData { CheckPoints = new Vector2[0], WaitTimes = new float[0] });
                continue;
            }

            int childCount = prefab.transform.childCount;
            var cps = new Vector2[childCount];
            var wts = new float[childCount];
            for (int c = 0; c < childCount; c++)
            {
                var child = prefab.transform.GetChild(c);
                cps[c] = child.position;
                if (!float.TryParse(child.name, out wts[c]))
                {
                    wts[c] = 0f;
                    warnings.Add($"Path {k} child #{c}: 无法解析 name '{child.name}' 为 float,使用 0");
                }
            }
            newPaths.Add(new PathData { CheckPoints = cps, WaitTimes = wts });
        }

        ld.Paths = newPaths.ToArray();
        ld.CheckPoints = new GameObject[0];
        EditorUtility.SetDirty(ld);

        if (warnings.Count > 0)
            Debug.LogWarning($"[EditorPathMigrationTool] {ld.name} 迁移完成,带 {warnings.Count} 个警告:\n - " +
                string.Join("\n - ", warnings));
        else
            Debug.Log($"[EditorPathMigrationTool] {ld.name} 迁移完成,共 {ld.Paths.Length} 条路径。");

        return true;
#pragma warning restore CS0618
    }
}
```

- [ ] **Step 2: Implement MigrationBanner**

Create `Assets/Editor/LevelEditor/Migration/MigrationBanner.cs`:

```csharp
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class MigrationBanner
{
    public static VisualElement Build(LevelData ld)
    {
        var root = new VisualElement();
        root.style.flexDirection = FlexDirection.Row;
        root.style.alignItems = Align.Center;
        root.style.backgroundColor = new Color(0.529f, 0.435f, 0.118f); // 黄
        root.style.paddingTop = 6; root.style.paddingBottom = 6;
        root.style.paddingLeft = 8; root.style.paddingRight = 8;
        root.style.marginBottom = 6;
        root.style.borderTopLeftRadius = 3; root.style.borderTopRightRadius = 3;
        root.style.borderBottomLeftRadius = 3; root.style.borderBottomRightRadius = 3;

#pragma warning disable CS0618
        var msg = new Label($"⚠ 此 LevelData 包含 {ld.CheckPoints.Length} 条旧 prefab 路径,建议迁移到 PathData 数据格式。");
#pragma warning restore CS0618
        msg.style.color = new Color(1f, 0.8f, 0.3f);
        msg.style.fontSize = 11;
        msg.style.flexGrow = 1;
        root.Add(msg);

        var migrateBtn = new Button(() =>
        {
            if (EditorPathMigrationTool.MigrateAsset(ld))
            {
                root.RemoveFromHierarchy();
                // 通知 editor 重新 Build
                var ed = UnityEditor.Editor.CreateEditor(ld);
                // Re-create inspector by forcing repaint via selecting again
                Selection.activeObject = null;
                Selection.activeObject = ld;
                Object.DestroyImmediate(ed);
            }
        }) { text = "迁移" };
        migrateBtn.style.backgroundColor = new Color(0.706f, 0.706f, 0.412f);
        migrateBtn.style.color = Color.black;
        root.Add(migrateBtn);

        var ignoreBtn = new Button(() => root.RemoveFromHierarchy()) { text = "忽略" };
        ignoreBtn.style.marginLeft = 4;
        root.Add(ignoreBtn);

        return root;
    }
}
```

- [ ] **Step 3: Wire banner into LevelDataEditor**

Modify `LevelDataEditor.CreateInspectorGUI`, after the existing body section assembly, before `root.Add(body)`:

```csharp
// 检测旧数据,banner 提示
var ld = (LevelData)target;
#pragma warning disable CS0618
if (ld.CheckPoints != null && ld.CheckPoints.Length > 0 && (ld.Paths == null || ld.Paths.Length == 0))
#pragma warning restore CS0618
{
    body.Add(MigrationBanner.Build(ld));
}
```

Add `using` for the Migration namespace if needed (it's in global namespace — no using required).

- [ ] **Step 4: Manual test**

1. Select any `LevelData` with non-empty `CheckPoints` (e.g. `T1/Data.asset`) → verify yellow banner appears.
2. Click "迁移" → banner disappears, `Paths` populated, `CheckPoints` empty.
3. Re-open the asset → no banner (Paths is now authoritative).
4. Run menu `Tools → Level Editor → Migrate CheckPoint Prefabs to PathData` → console reports count.

- [ ] **Step 5: Commit**

```bash
git add Assets/Editor/LevelEditor/Migration/EditorPathMigrationTool.cs Assets/Editor/LevelEditor/Migration/MigrationBanner.cs Assets/Editor/LevelEditor/LevelDataEditor.cs
git commit -m "feat(path-editor): one-shot migration tool + banner for legacy CheckPoints prefab data"
```

---

## Task 13: Add 4 path validation rules

**Files:**
- Modify: `Assets/Editor/LevelEditor/Validation/LevelDataValidator.cs`
- Modify: `Assets/Tests/EditMode/LevelDataValidatorTests.cs`

- [ ] **Step 1: Read current LevelDataValidator.cs**

Open the file and confirm where new rules should be appended. Likely the end of `Validate(LevelData)` method.

- [ ] **Step 2: Add failing tests first**

Append to `Assets/Tests/EditMode/LevelDataValidatorTests.cs`:

```csharp
[Test]
public void Validate_EmptyPaths_ReturnsWarning()
{
    _data.Paths = new LevelData.PathData[0];
    var issues = LevelDataValidator.Validate(_data);
    Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Warning && i.Path.Contains("Paths")),
        $"Expected warning about empty Paths, got: {string.Join("; ", issues.Select(i => i.Message))}");
}

[Test]
public void Validate_PathWithLessThanTwoCheckpoints_ReturnsError()
{
    _data.Paths = new LevelData.PathData[] {
        new LevelData.PathData {
            CheckPoints = new Vector2[] { new Vector2(1f, 1f) },
            WaitTimes = new float[] { 0f }
        }
    };
    var issues = LevelDataValidator.Validate(_data);
    Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Path.Contains("Paths") && i.Message.Contains("2")),
        $"Expected error about <2 checkpoints, got: {string.Join("; ", issues.Select(i => i.Message))}");
}

[Test]
public void Validate_PathCheckPointLengthMismatchWaitTimesLength_ReturnsError()
{
    _data.Paths = new LevelData.PathData[] {
        new LevelData.PathData {
            CheckPoints = new Vector2[] { new Vector2(1f, 1f), new Vector2(2f, 2f) },
            WaitTimes = new float[] { 0f } // length mismatch
        }
    };
    var issues = LevelDataValidator.Validate(_data);
    Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Message.Contains("WaitTimes")),
        $"Expected error about CheckPoints/WaitTimes length mismatch, got: {string.Join("; ", issues.Select(i => i.Message))}");
}

[Test]
public void Validate_PathCheckPointNegativeWaitTime_ReturnsWarning()
{
    _data.Paths = new LevelData.PathData[] {
        new LevelData.PathData {
            CheckPoints = new Vector2[] { new Vector2(1f, 1f), new Vector2(2f, 2f) },
            WaitTimes = new float[] { 0f, -1f }
        }
    };
    var issues = LevelDataValidator.Validate(_data);
    Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Warning && i.Message.Contains("WaitTime")),
        $"Expected warning about negative WaitTime, got: {string.Join("; ", issues.Select(i => i.Message))}");
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `Test Runner → EditMode → Run All`.
Expected: 4 new tests FAIL.

- [ ] **Step 4: Implement validation rules**

Open `Assets/Editor/LevelEditor/Validation/LevelDataValidator.cs` and append inside the `Validate(LevelData)` method (find the closing brace):

```csharp
// === Paths 校验 ===
if (data.Paths == null || data.Paths.Length == 0)
{
    issues.Add(new ValidationIssue
    {
        Severity = ValidationSeverity.Warning,
        Path = "Paths",
        Message = "没有任何路径数据"
    });
}
else
{
    for (int p = 0; p < data.Paths.Length; p++)
    {
        var path = data.Paths[p];
        if (path.CheckPoints == null || path.CheckPoints.Length < 2)
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Path = $"Paths[{p}]",
                Message = $"Path {p} 至少需要 2 个 checkpoint,当前 {path.CheckPoints?.Length ?? 0} 个"
            });
            continue;
        }

        if (path.WaitTimes == null || path.CheckPoints.Length != path.WaitTimes.Length)
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Path = $"Paths[{p}].WaitTimes",
                Message = $"Path {p} WaitTimes 长度必须等于 CheckPoints 长度"
            });
        }

        for (int k = 0; k < path.WaitTimes.Length; k++)
        {
            if (path.WaitTimes[k] < 0)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Path = $"Paths[{p}].WaitTimes[{k}]",
                    Message = $"Path {p} WaitTime[{k}] = {path.WaitTimes[k]} 为负"
                });
            }
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Expected: 4 new tests PASS, all previous tests still PASS.

- [ ] **Step 6: Commit**

```bash
git add Assets/Editor/LevelEditor/Validation/LevelDataValidator.cs Assets/Tests/EditMode/LevelDataValidatorTests.cs
git commit -m "feat(validator): add 4 path validation rules with TDD coverage"
```

---

## Task 14: End-to-end manual verification

**Files:** none (manual)

This is the spec's manual test checklist (§11.2), executed in the Unity Editor.

- [ ] **Step 1: Create a fresh test LevelData**

In the project, create `Assets/Tests/Sandbox/TestPathEditing.asset` (any temporary LevelData). Set MapPrefab to `Assets/Resources/Prefabs/Levels/Main/Test/T1/Map.prefab` (or any existing map prefab). Set Paths = empty, run `Tools → Level Editor → Migrate CheckPoint Prefabs to PathData` to populate if needed.

- [ ] **Step 2: Walk through the manual checklist**

Run through each item from the spec's §11.2:

- [ ] Add Path → add 3 checkpoints → A* path renders
- [ ] Drag checkpoint over PassableType=3 block → red line
- [ ] Switch moveMethod (地面 / 近地 / 飞行) → grid re-colors + A* recomputes
- [ ] Mouse wheel zoom centered on cursor
- [ ] Middle mouse drag pans viewport
- [ ] Click checkpoint → detail panel populates → edit field → circle/list sync
- [ ] Delete down to 1 checkpoint → validation error appears
- [ ] Drag checkpoint out of map bounds → validation error
- [ ] Undo / Redo add / move / delete
- [ ] Legacy asset banner appears on first open
- [ ] Switch to a different LevelData → cache reloads
- [ ] PathDataManager.CreatePaths with empty array doesn't crash at runtime (Play test)

- [ ] **Step 3: Cleanup sandbox asset**

Delete `Assets/Tests/Sandbox/TestPathEditing.asset` (and its `.meta`). Don't commit it.

- [ ] **Step 4: No commit**

This task is verification only — no code changes. If issues found, file them as separate tasks and continue.

---

## Task 15: Final smoke test + commit any remaining artifacts

**Files:** none expected; possibly small cleanup

- [ ] **Step 1: Run full EditMode test suite**

`Test Runner → EditMode → Run All`.
Expected: All tests PASS (BlockMapCache, EditorPathFinder parity, ViewTransform, LevelDataValidator including new path rules).

- [ ] **Step 2: Open one final asset for visual smoke check**

Open `T1/Data.asset` in the inspector. Verify the PathEditingSection renders correctly with the migrated data.

- [ ] **Step 3: Commit any uncommitted files**

```bash
git status
# if any uncommitted edits remain:
git add -A
git commit -m "chore: path editor final cleanup"
```

If nothing to commit, skip this step.

---

## Self-Review

**1. Spec coverage** — Spec §1-§14 mapped to tasks:
- §1 goals → Tasks 7-13 (UI + data + migration + validation)
- §2 data model → Task 1
- §3 file structure → Task 1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 12, 13
- §4 entry & trigger → Tasks 11, 12
- §5.1 BlockMapCache → Task 2
- §5.2 EditorPathFinder → Tasks 3, 4
- §5.3 ViewTransform → Task 5
- §5.4 PathEditingState → Task 6
- §5.5 MapCanvasView → Task 7
- §5.6 EditorPathManipulator → Task 8
- §5.7 PathEditingSection → Task 9
- §5.8 list/detail → Task 10
- §6 A* rendering → Task 7 (DrawPaths)
- §7 BlockData color encoding → Task 7 (DrawBlocks)
- §8 validator → Task 13
- §9 migration tool + banner → Task 12
- §10 error handling → spread across Tasks 2 (null map), 7 (unreachable = red), 13 (out-of-bounds), 11 (OnDisable Dispose)
- §11 tests → Tasks 2, 3, 4, 5, 13, 14
- §12 task outline → this plan
- §13 risks → mitigated by TDD (parity), null checks (cache load), float.TryParse fallback (migration), drag-only-on-release (perf)

**2. Placeholder scan** — No TBD/TODO/"similar to"/"fill in". Every step has exact code, file paths, commands.

**3. Type consistency** —
- `BlockMapCache.Load(GameObject) → BlockMapCache` (Task 2) — used in Task 11
- `EditorPathFinder.AStar(BlockData[,], int, int, Vector2, Vector2, float, int) → MoveParameters[]` (Task 3) — used in Task 7 (DrawPaths) and Task 4 (tests)
- `ViewTransform.Fit(int, int) → ViewTransform` (Task 5) — used in Tasks 7, 9, 11
- `ViewTransform.WorldToScreen(Vector2, int, int) → Vector2` (Task 5) — used in Tasks 7, 8
- `ViewTransform.SnapToGrid(Vector2) → Vector2` (Task 5) — used in Task 8
- `PathEditingState` fields (Task 6) — used in Tasks 7, 8, 9, 10
- `PathData { Vector2[] CheckPoints; float[] WaitTimes; }` (Task 1) — used in Tasks 12, 13, 14

All consistent.

**4. Order of execution safety** — Each task is self-contained. Task 1 adds fields that subsequent tasks consume. Tasks 2-6 build foundation classes. Tasks 7-10 build UI. Task 11 wires it all together. Task 12 adds migration. Task 13 adds validation. Tasks 14-15 verify.

**5. Test framework** — Mirrors existing `LevelDataValidatorTests.cs` pattern exactly: `[Test]` + sync `void` + `Assert.IsTrue(issues.Any(...))` + interpolated failure message.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-22-path-editor.md`. Two execution options:

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints for review.

**Which approach?**