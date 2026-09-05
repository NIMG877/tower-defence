# Map Data Storage Refactor — Implementation Plan

> 文档状态：历史实施计划存档，非当前有效文档。当前实现以代码与 docs/ 现行文档为准。


> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace per-tile `BlockData` MonoBehaviour with a data-on-`LevelData` storage model; add a map-editing tab to the existing path editor; refactor `MapDataManager` to read from the new data store; keep `MapPrefab` purely as an optional visual model asset.

**Architecture:** Two parallel paths: (1) edit-time: editor paints into `LevelData.MapData` via `SerializedProperty`; prefab is not read for editing. (2) run-time: `MapDataManager` reads `LevelData.MapData`, optionally instantiates prefab for visuals + material cache, builds `BlockState[,]`. The editor and runtime share two struct shapes: `BlockDataEntry` (serialized on `LevelData`) and `BlockState` (runtime).

**Tech Stack:** Unity 6.x, UI Toolkit (UIElements), `UnityEditor.SerializedProperty`, NUnit via `com.unity.test-framework` 1.1.33, `PrefabUtility` for prefab edits.

**Spec:** [docs/superpowers/specs/2026-06-23-map-data-storage-design.md](docs/superpowers/specs/2026-06-23-map-data-storage-design.md)

---

## File Structure

### New files (created in this plan)

| Path                                                                                                       | Responsibility                                                                  |
|------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------|
| `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/BlockDataEntry.cs`                          | Serializable struct on `LevelData` (spec §3.1)                                  |
| `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/BlockState.cs`                              | Runtime struct in `BlockStateMatrix` (spec §3.2)                                |
| `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/MapDataMigration.cs`                        | Static helper: read legacy `BlockData` MBs, produce `BlockDataEntry[]`          |
| `Assets/Editor/LevelEditor/Sections/MapEditorSection.cs`                                                  | Shared scaffolding (cache, view, canvas bg, base manipulator) for the tabs     |
| `Assets/Editor/LevelEditor/Sections/MapEditTab.cs`                                                        | Map-paint workflow (spec §4.3)                                                  |
| `Assets/Editor/LevelEditor/Sections/PathEditTab.cs`                                                       | Path-paint workflow (moved from `PathEditingSection`)                           |
| `Assets/Editor/LevelEditor/PathEditing/MapAutoMigrator.cs`                                                | Editor-time one-shot migration (prefab contents → LevelData.MapData + cleanup) |
| `Assets/Tests/Editor/MapData.Tests.Editor.asmdef`                                                         | Test assembly definition (UTF)                                                  |
| `Assets/Tests/Editor/MapData/BlockDataEntryTests.cs`                                                      | Tests for `BlockDataEntry` (construction, ToBlockState, equality)               |
| `Assets/Tests/Editor/MapData/BlockStateTests.cs`                                                          | Tests for `BlockState` (defaults, mutation)                                     |
| `Assets/Tests/Editor/MapData/MapDataManagerInitTests.cs`                                                  | Tests for `MapDataManager.Initialize` (matrix build from `LevelData.MapData`)   |
| `Assets/Tests/Editor/MapData/EditorPathFinderTests.cs`                                                    | Tests for `EditorPathFinder.AStar` portal traversal                             |
| `Assets/Tests/Editor/MapData/MapAutoMigratorTests.cs`                                                     | Tests for `MapAutoMigrator.Migrate` (uses an in-memory prefab clone)            |

### Modified files

| Path                                                                                                       | Change                                                                          |
|------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------|
| `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelData.cs`                                | Add `iSize`, `jSize`, `MapData` fields                                          |
| `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/MapDataManager.cs`                           | Replace `BlockData[,]` with `BlockState[,]`; add `AttachLevelData`              |
| `Assets/Editor/LevelEditor/LevelDataEditor.cs`                                                            | Replace `PathEditingSection` slot with `MapEditorSection`                       |
| `Assets/Editor/LevelEditor/PathEditing/BlockMapCache.cs`                                                  | Load from `LevelData.MapData` (drop prefab dependency)                          |
| `Assets/Editor/LevelEditor/PathEditing/MapCanvasView.cs`                                                  | Drive rendering from `BlockDataEntry`                                           |
| `Assets/Editor/LevelEditor/PathEditing/EditorPathFinder.cs`                                               | Take `BlockState[,]` instead of `BlockData[,]`                                  |
| `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/StaticScript/InteractableStatic.cs`        | `BlockData` → `BlockState`                                                      |
| `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/MoveScripts/MoveBase.cs`                   | `BlockData` → `BlockState`                                                      |
| `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs`                         | `BlockData` → `BlockState`, null-check `material.color`                         |
| `Assets/Resources/Prefabs/Monsters/Origin/Wdslm/Scripts/Machine/MachineTalent1.cs`                        | `BlockData` → `BlockState`                                                      |

### Deleted files

| Path                                                                                                       | Reason                                                                          |
|------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------|
| `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/BlockData.cs`                                | Replaced by `BlockDataEntry` + `BlockState`                                     |
| `Assets/Editor/LevelEditor/Sections/PathEditingSection.cs`                                                 | Replaced by `MapEditorSection` + `PathEditTab`                                  |

### Unchanged (referenced for context)

- `Assets/Editor/LevelEditor/PathEditing/EditorPathManipulator.cs` — operates on `(i, j)`, no change.
- `Assets/Editor/LevelEditor/PathEditing/PathEditingState.cs` — generic `Changed` event, reused.
- `Assets/Editor/LevelEditor/PathEditing/ViewTransform.cs` — generic zoom/pan, reused.

---

## Phase 0: Test Infrastructure

### Task 0.1: Create test assembly

**Files:**
- Create: `Assets/Tests/Editor/MapData.Tests.Editor.asmdef`

The project has `com.unity.test-framework` 1.1.33 in `Packages/manifest.json` but no user-code test assemblies. Create one for this refactor.

- [ ] **Step 1: Create directory**

```bash
mkdir -p Assets/Tests/Editor/MapData
```

- [ ] **Step 2: Write the asmdef**

`Assets/Tests/Editor/MapData.Tests.Editor.asmdef`:

```json
{
    "name": "MapData.Tests.Editor",
    "rootNamespace": "MapData.Tests",
    "references": [
        "BasicScripts",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 3: Verify Unity recognizes the test assembly**

Open the project in Unity, wait for compile, then in Unity Test Runner (`Window > General > Test Runner`) the assembly should appear under **EditMode** with 0 tests. No tests yet, but the assembly must be discoverable.

- [ ] **Step 4: Commit**

```bash
git add Assets/Tests/Editor/MapData.Tests.Editor.asmdef
git commit -m "test(map-data): add MapData.Tests.Editor asmdef for NUnit tests"
```

---

## Phase 1: Data Model Foundation

### Task 1.1: Add `BlockDataEntry` struct (serialized on `LevelData`)

**Files:**
- Create: `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/BlockDataEntry.cs`
- Test: `Assets/Tests/Editor/MapData/BlockDataEntryTests.cs`

Spec reference: §3.1. `BlockDataEntry` is a `[System.Serializable]` struct holding the per-tile data that lives on `LevelData`. Mirror of today's `BlockData` MB fields (minus runtime state).

- [ ] **Step 1: Write the failing test**

`Assets/Tests/Editor/MapData/BlockDataEntryTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace MapData.Tests
{
    public class BlockDataEntryTests
    {
        [Test]
        public void Default_construct_has_all_passable_zero_and_no_portal()
        {
            var e = new BlockDataEntry { i = 3, j = 7 };
            Assert.AreEqual(3, e.i);
            Assert.AreEqual(7, e.j);
            Assert.AreEqual(0, e.passableType);
            Assert.IsFalse(e.highland);
            Assert.IsFalse(e.canSet);
            Assert.IsFalse(e.deadly);
            Assert.AreEqual(-1, e.portalOutI);
            Assert.AreEqual(-1, e.portalOutJ);
            Assert.AreEqual(default(Color), e.portalColor);
        }

        [Test]
        public void ToBlockState_copies_all_fields_and_resets_material_to_null()
        {
            var e = new BlockDataEntry
            {
                i = 1, j = 2,
                highland = true,
                canSet = true,
                passableType = 2,
                deadly = true,
                portalOutI = 5,
                portalOutJ = 6,
                portalColor = new Color(0.1f, 0.2f, 0.3f, 0.4f),
            };
            var s = e.ToBlockState();
            Assert.IsTrue(s.highland);
            Assert.IsTrue(s.canSet);
            Assert.AreEqual(2, s.passableType);
            Assert.IsTrue(s.deadly);
            Assert.AreEqual(5, s.portalOutI);
            Assert.AreEqual(6, s.portalOutJ);
            Assert.AreEqual(new Color(0.1f, 0.2f, 0.3f, 0.4f), s.portalColor);
            Assert.IsNull(s.material);
        }

        [Test]
        public void Equality_matches_field_by_field()
        {
            var a = new BlockDataEntry { i = 1, j = 2, passableType = 3, portalOutI = 4, portalOutJ = 5 };
            var b = new BlockDataEntry { i = 1, j = 2, passableType = 3, portalOutI = 4, portalOutJ = 5 };
            var c = new BlockDataEntry { i = 1, j = 2, passableType = 3, portalOutI = 4, portalOutJ = 6 };
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Open the project in Unity, then run via Test Runner > EditMode > `MapData.Tests.Editor.BlockDataEntryTests`.

Expected: **3 tests fail to compile** (`BlockDataEntry` does not exist).

- [ ] **Step 3: Write the implementation**

`Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/BlockDataEntry.cs`:

```csharp
using UnityEngine;

/// <summary>
/// Per-tile data stored on <see cref="LevelData"/>. Sparse — only cells with
/// non-default state have an entry. Mirrors the old <c>BlockData</c> MB fields
/// minus runtime state. See spec §3.1.
/// </summary>
[System.Serializable]
public struct BlockDataEntry
{
    public int   i;
    public int   j;
    public bool  highland;
    public bool  canSet;
    public int   passableType;   // 0 = ground walk, 1 = ground+low-air, 2 = +high-air, 3 = impassable
    public bool  deadly;
    public int   portalOutI;     // -1 = no portal
    public int   portalOutJ;     // -1 = no portal
    public Color portalColor;

    public BlockState ToBlockState() => new BlockState
    {
        highland    = highland,
        canSet      = canSet,
        passableType = passableType,
        deadly      = deadly,
        portalOutI  = portalOutI,
        portalOutJ  = portalOutJ,
        portalColor = portalColor,
        material    = null,
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run the same 3 tests. Expected: **3 tests pass**.

- [ ] **Step 5: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/BlockDataEntry.cs \
        Assets/Tests/Editor/MapData/BlockDataEntryTests.cs
git commit -m "feat(map-data): add BlockDataEntry struct (LevelData-serialized)"
```

---

### Task 1.2: Add `BlockState` struct (runtime)

**Files:**
- Create: `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/BlockState.cs`
- Test: `Assets/Tests/Editor/MapData/BlockStateTests.cs`

Spec reference: §3.2. The runtime mirror of `BlockDataEntry` with the additional `material` and `tempOccupy` fields.

- [ ] **Step 1: Write the failing test**

`Assets/Tests/Editor/MapData/BlockStateTests.cs`:

```csharp
using NUnit.Framework;

namespace MapData.Tests
{
    public class BlockStateTests
    {
        [Test]
        public void Default_construct_is_ground_walkable_with_no_portal_and_null_material()
        {
            var s = default(BlockState);
            Assert.IsFalse(s.highland);
            Assert.IsFalse(s.canSet);
            Assert.IsFalse(s.deadly);
            Assert.AreEqual(0, s.passableType);
            Assert.AreEqual(-1, s.portalOutI);
            Assert.AreEqual(-1, s.portalOutJ);
            Assert.IsNull(s.material);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run via Test Runner. Expected: **1 test fails to compile** (`BlockState` does not exist).

- [ ] **Step 3: Write the implementation**

`Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/BlockState.cs`:

```csharp
using UnityEngine;

/// <summary>
/// Runtime state for a single map cell. Stored in
/// <c>MapDataManager.BlockStateMatrix</c>. See spec §3.2.
/// </summary>
public struct BlockState
{
    public bool     highland;
    public bool     canSet;
    public int      passableType;
    public bool     deadly;
    public int      portalOutI;
    public int      portalOutJ;
    public Color    portalColor;
    public Material material;     // null if no prefab instantiated
}
```

- [ ] **Step 4: Run test to verify it passes**

Run. Expected: **1 test passes**.

- [ ] **Step 5: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/BlockState.cs \
        Assets/Tests/Editor/MapData/BlockStateTests.cs
git commit -m "feat(map-data): add BlockState struct (runtime mirror of BlockDataEntry)"
```

---

### Task 1.3: Add `MapData / iSize / jSize` to `LevelData`

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelData.cs`

Spec reference: §3.1 storage. No behavior change yet — these fields are just declared and default to empty/zero.

- [ ] **Step 1: Read the current `LevelData.cs`**

Read `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelData.cs` to find the class body.

- [ ] **Step 2: Add the new fields**

Add three fields to the `LevelData` class. Pick a sensible location — right after the `MapPrefab` field is the natural spot (visual proximity to related data). The exact insertion point is the line *before* the next field (`EnvironmentalControlDevice` per the spec §3 of the design report).

Insertion:

```csharp
    [Header("Map data (new)")]
    public int iSize;
    public int jSize;
    public List<BlockDataEntry> MapData = new List<BlockDataEntry>();
```

Also add the `using` directive at the top of the file (after the existing `using` lines):

```csharp
using System.Collections.Generic;
```

- [ ] **Step 3: Verify Unity recompiles cleanly**

Open the project in Unity. The console should be clean. `MapData` defaults to an empty list; existing `.asset` files that don't declare `MapData` will deserialize to an empty list (Unity's default for missing serialized fields with default initializers).

- [ ] **Step 4: Manual smoke test**

In the Unity Project window, find the MC-1 `LevelData` asset (`Assets/Resources/Prefabs/Levels/Main/AdventureOfMinecraft/MC-1/MC-1.asset`). Select it — the inspector should still render without errors. The new fields are visible (iSize=0, jSize=0, MapData=empty) but the rest of the UI is unchanged.

- [ ] **Step 5: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelData.cs
git commit -m "feat(level-data): add iSize/jSize/MapData fields to LevelData"
```

---

## Phase 2: Runtime Refactor

### Task 2.1: `MapDataManager` reads from `LevelData.MapData`

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/MapDataManager.cs`

Spec reference: §5.1, §5.2. This task changes `MapInitialize` to read from `LevelData` (via `AttachLevelData`). The prefab is still walked for material caching, but data comes from `LevelData.MapData`. We swap the matrix type from `BlockData[,]` to `BlockState[,]` in this task — that's the cleanest cut, and tests catch the call-site breakage that follows in Task 2.3.

- [ ] **Step 1: Read the current `MapDataManager.cs`**

Read `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/MapDataManager.cs` to identify the exact lines to change: the field declaration (`BlockDataMatrix`), the `MapInitialize` method body, and the call sites inside the file that reference `BlockDataMatrix[i, j]`.

- [ ] **Step 2: Write the failing test**

`Assets/Tests/Editor/MapData/MapDataManagerInitTests.cs`:

```csharp
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
            Assert.IsNull(mgr.BlockStateMatrix);
        }

        [Test]
        public void Initialize_with_levelData_builds_matrix_from_MapData()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.iSize = 4;
            ld.jSize = 3;
            ld.MapData = new System.Collections.Generic.List<BlockDataEntry>
            {
                new BlockDataEntry { i = 0, j = 0, highland = true },
                new BlockDataEntry { i = 2, j = 1, passableType = 3, deadly = true },
                new BlockDataEntry { i = 1, j = 2, portalOutI = 0, portalOutJ = 0, portalColor = Color.red },
            };

            var mgr = new MapDataManager();
            mgr.AttachLevelData(ld);
            mgr.Initialize();

            Assert.AreEqual(4, mgr.iSize);
            Assert.AreEqual(3, mgr.jSize);
            Assert.IsNotNull(mgr.BlockStateMatrix);
            Assert.AreEqual(4, mgr.BlockStateMatrix.GetLength(0));
            Assert.AreEqual(3, mgr.BlockStateMatrix.GetLength(1));

            Assert.IsTrue(mgr.BlockStateMatrix[0, 0].highland);
            Assert.IsFalse(mgr.BlockStateMatrix[0, 0].deadly);

            Assert.IsTrue(mgr.BlockStateMatrix[2, 1].deadly);
            Assert.AreEqual(3, mgr.BlockStateMatrix[2, 1].passableType);

            Assert.AreEqual(0, mgr.BlockStateMatrix[1, 2].portalOutI);
            Assert.AreEqual(0, mgr.BlockStateMatrix[1, 2].portalOutJ);
            Assert.AreEqual(Color.red, mgr.BlockStateMatrix[1, 2].portalColor);

            // unlisted cell: default
            Assert.IsFalse(mgr.BlockStateMatrix[3, 2].highland);
            Assert.IsFalse(mgr.BlockStateMatrix[3, 2].canSet);
            Assert.AreEqual(0, mgr.BlockStateMatrix[3, 2].passableType);
            Assert.IsNull(mgr.BlockStateMatrix[3, 2].material);

            Object.DestroyImmediate(ld);
        }

        [Test]
        public void Initialize_skips_out_of_range_entries_without_throwing()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.iSize = 2;
            ld.jSize = 2;
            ld.MapData = new System.Collections.Generic.List<BlockDataEntry>
            {
                new BlockDataEntry { i = 5, j = 5, highland = true },   // out of range
                new BlockDataEntry { i = -1, j = 0 },                  // out of range
                new BlockDataEntry { i = 0, j = 0, passableType = 2 },  // in range
            };

            var mgr = new MapDataManager();
            mgr.AttachLevelData(ld);

            Assert.DoesNotThrow(() => mgr.Initialize());
            Assert.AreEqual(2, mgr.BlockStateMatrix[0, 0].passableType);
            Assert.IsFalse(mgr.BlockStateMatrix[1, 1].highland); // unlisted cell unaffected

            Object.DestroyImmediate(ld);
        }
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run via Test Runner. Expected: **3 tests fail to compile** (`MapDataManager` no longer has `BlockDataMatrix`, has no `AttachLevelData`, etc.).

- [ ] **Step 4: Modify `MapDataManager.cs`**

Apply the following edits to `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/MapDataManager.cs`:

**4a.** Replace the field declaration (around line 53):

```csharp
// before
public BlockData[,] BlockDataMatrix;

// after
public BlockState[,] BlockStateMatrix;
private LevelData _levelData;
```

**4b.** Add `AttachLevelData` method (place it next to `CreateMap`):

```csharp
public void AttachLevelData(LevelData levelData)
{
    _levelData = levelData;
}
```

**4c.** Replace the body of `MapInitialize` (around lines 147-187). Keep the graph + heap + EntityManager init, but read from `_levelData` instead of walking `BlockData` MBs. New body:

```csharp
public void MapInitialize()
{
    iSize = _levelData != null ? _levelData.iSize : 0;
    jSize = _levelData != null ? _levelData.jSize : 0;
    BlockStateMatrix = (iSize > 0 && jSize > 0) ? new BlockState[iSize, jSize] : null;

    if (_levelData != null)
    {
        foreach (var entry in _levelData.MapData)
        {
            if (entry.i < 0 || entry.i >= iSize || entry.j < 0 || entry.j >= jSize) continue;
            BlockStateMatrix[entry.i, entry.j] = entry.ToBlockState();
        }
    }

    if (_map != null)
    {
        for (int k = 0; k < _map.transform.childCount; k++)
        {
            var child = _map.transform.GetChild(k);
            int ci = (int)child.position.y;
            int cj = (int)child.position.x;
            if (ci < 0 || ci >= iSize || cj < 0 || cj >= jSize) continue;
            var mr = child.GetComponent<MeshRenderer>();
            if (mr == null) continue;
            var s = BlockStateMatrix[ci, cj];
            s.material = mr.material;
            BlockStateMatrix[ci, cj] = s;
        }
    }

    graph = (iSize > 0 && jSize > 0) ? new AStarProperty[iSize, jSize] : null;
    heap = (iSize > 0 && jSize > 0) ? new HeapEntry[iSize * jSize + 16] : null;
    if (graph != null)
    {
        for (int i = 0; i < iSize; i++)
        {
            for (int j = 0; j < jSize; j++)
            {
                graph[i, j].plotPos = new Vector2(j, i);
                graph[i, j].portalEnter = BlockStateMatrix[i, j].portalOutI != -1;
            }
        }
    }
    EntityManager.Manager.BlockEntitysInitialize(iSize, jSize);
}
```

**4d.** Add `ref BlockState GetPosBlockRef` (replace the existing `GetPosBlock`):

```csharp
public ref BlockState GetPosBlockRef(int i, int j)
{
    return ref BlockStateMatrix[i, j];
}

public BlockState GetPosBlock(int i, int j)
{
    if (i < 0 || j < 0 || i >= iSize || j >= jSize) return default;
    return BlockStateMatrix[i, j];
}
```

**4e.** Replace internal `BlockDataMatrix` references inside this file. In the file, find all `BlockDataMatrix` occurrences and rename to `BlockStateMatrix`. Specific call sites inside `MapDataManager.cs` (the ones that read fields off it):

- Line ~63: `BlockDataMatrix[i, j].Highland` → `BlockStateMatrix[i, j].highland`
- Line ~63: `BlockDataMatrix[i, j].CanSet` → `BlockStateMatrix[i, j].canSet`
- Line ~78: `BlockDataMatrix[i, j].Highland` → `BlockStateMatrix[i, j].highland`
- Line ~78: `BlockDataMatrix[i, j].Deadly` → `BlockStateMatrix[i, j].deadly`
- Line ~78: `BlockDataMatrix[i, j].CanSet` → `BlockStateMatrix[i, j].canSet`
- Line ~286: `BlockDataMatrix[i, j].PassableType <= moveMethod && !BlockDataMatrix[i, j].TempOccupy` → `BlockStateMatrix[i, j].passableType <= moveMethod` (the `&& !TempOccupy` clause is dropped per spec §10.4 — `_tempOccupy` is dead code with no writers in the codebase)
- Line ~332: same as 286
- Line ~441, 483: same
- Line ~607-608: portal target lookup. Replace
  ```csharp
  int ti = (int)BlockDataMatrix[i, j].ProtalOutBlock.transform.position.y;
  int tj = (int)BlockDataMatrix[i, j].ProtalOutBlock.transform.position.x;
  ```
  with
  ```csharp
  int ti = BlockStateMatrix[i, j].portalOutI;
  int tj = BlockStateMatrix[i, j].portalOutJ;
  ```
- Line ~658: same as 607
- Line ~792, 843: `BlockDataMatrix[p1.y, p1.x].transform.position` → `new Vector2(p1.x + 0.5f, p1.y + 0.5f)` (the cell center is now just `(j + 0.5, i + 0.5)` — no more Transform needed)
- Line ~441: `BlockDataMatrix[i, j].PassableType <= 0` → `BlockStateMatrix[i, j].passableType <= 0`

- [ ] **Step 5: Run test to verify they pass**

Run the 3 new tests. Expected: **3 tests pass**.

- [ ] **Step 6: Verify the project compiles in Unity**

Open the project. The console will show errors at other call sites (`InteractableStatic`, `MoveBase`, `LevelMessagePanel`, `EditorPathFinder`, `BlockMapCache`, `MapCanvasView`, `MachineTalent1`) that still reference `BlockData`. These are intentionally unfixed until Tasks 2.2-2.4 — the compilation errors are expected here.

To allow Unity to keep going for verification, you can temporarily comment out the call sites in dependent files; the cleaner path is to immediately proceed to Task 2.2 and fix the consumers. **The recommended flow is to do 2.2 next, before re-running compile verification.**

- [ ] **Step 7: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/MapDataManager.cs \
        Assets/Tests/Editor/MapData/MapDataManagerInitTests.cs
git commit -m "refactor(map-data): MapDataManager reads BlockState[,] from LevelData.MapData"
```

---

### Task 2.2: Update runtime call sites — `InteractableStatic`, `MoveBase`, `LevelMessagePanel`, `MachineTalent1`

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/StaticScript/InteractableStatic.cs`
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/MoveScripts/MoveBase.cs`
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs`
- Modify: `Assets/Resources/Prefabs/Monsters/Origin/Wdslm/Scripts/Machine/MachineTalent1.cs`

Spec reference: §5.3. Each call site is small (1-3 lines). The pattern is: `BlockData X` → `BlockState X`, field capitalization fix (`Highland` → `highland`, etc.), and `GetPosBlock` returns `BlockState` (no longer `BlockData`).

- [ ] **Step 1: Update `InteractableStatic.cs`**

Find each occurrence of `BlockData bD` and `bD.PassableType`. Replace with `BlockState bS` and `bS.passableType`. There are 4 lines (45, 53, 61, 69 per the spec). Also update the call `MapDataManager.Manager.GetPosBlock(...)` return type — if local variable was `BlockData`, change to `BlockState`.

```csharp
// before
BlockData bD = MapDataManager.Manager.GetPosBlock(ii, jj);
if (bD == null || bD.PassableType > 0) return;

// after
BlockState bS = MapDataManager.Manager.GetPosBlock(ii, jj);
if (bS.passableType > 0) return;
```

(Note: `GetPosBlock` no longer returns null for in-range cells — it returns `default(BlockState)` which has `passableType == 0`.)

- [ ] **Step 2: Update `MoveBase.cs:131`**

Find the `Highland` read and switch to the lowercase struct field:

```csharp
// before
if (ii != -1 && MapDataManager.Manager.GetPosBlock(ii, jj).Highland)

// after
if (ii != -1 && MapDataManager.Manager.GetPosBlock(ii, jj).highland)
```

- [ ] **Step 3: Update `LevelMessagePanel.cs` (the two material-color sites at lines 1736, 1745)**

Find each `BlockData` reference and switch to `BlockState`. Add a null-check on `material` before touching `.color`:

```csharp
// before
bD.Material.color = ...;

// after
if (bS.material != null) bS.material.color = ...;
```

If the existing call site already uses a `ref`-style pattern (e.g. via `GetPosBlockRef` or by index), use that — the new `ref BlockState GetPosBlockRef(int, int)` accessor is the supported path for mutation.

- [ ] **Step 4: Update `MachineTalent1.cs:144`**

Find the `map[i, j].PassableType` reference. The `map` field type may need updating from `BlockData[,]` to `BlockState[,]` (check the field declaration in that file). Update field type and lowercase the field access:

```csharp
// before
public BlockData[,] map;  // (or local)
// ...
if (map[i, j].PassableType <= _thisMove.MoveMethod)

// after
public BlockState[,] map;
// ...
if (map[i, j].passableType <= _thisMove.MoveMethod)
```

- [ ] **Step 5: Compile-check Unity**

Open the project. Expected: **runtime code now compiles cleanly** (the editor side `BlockMapCache`, `MapCanvasView`, `EditorPathFinder` still have errors — Tasks 2.3 and 2.4 fix them).

- [ ] **Step 6: Re-run Phase 1 + 2.1 tests**

Run all EditMode tests. Expected: **4 tests pass** (BlockDataEntry 3 + BlockState 1 + MapDataManagerInit 3 = 7 total).

- [ ] **Step 7: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/StaticScript/InteractableStatic.cs \
        Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/MoveScripts/MoveBase.cs \
        Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs \
        Assets/Resources/Prefabs/Monsters/Origin/Wdslm/Scripts/Machine/MachineTalent1.cs
git commit -m "refactor(map-data): migrate runtime call sites from BlockData to BlockState"
```

---

### Task 2.3: Update `EditorPathFinder` to take `BlockState[,]`

**Files:**
- Modify: `Assets/Editor/LevelEditor/PathEditing/EditorPathFinder.cs`
- Test: `Assets/Tests/Editor/MapData/EditorPathFinderTests.cs`

Spec reference: §5.4. The A* is a 1:1 port of `MapDataManager.AStarWayFinding` — only the data type and the portal cross-reference change.

- [ ] **Step 1: Write the failing test**

`Assets/Tests/Editor/MapData/EditorPathFinderTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run via Test Runner. Expected: **3 tests fail to compile** (signature change in `EditorPathFinder.AStar`).

- [ ] **Step 3: Modify `EditorPathFinder.cs`**

**3a.** Change the public signature:

```csharp
// before
public static MoveParameters[] AStar(BlockData[,] blocks, int iSize, int jSize, Vector2 start, Vector2 end, float entityR, int moveMethod)

// after
public static MoveParameters[] AStar(BlockState[,] blocks, int iSize, int jSize, Vector2 start, Vector2 end, float entityR, int moveMethod)
```

**3b.** Inside the method, rename `blocks` reads:
- `blocks[i, j].PassableType` → `blocks[i, j].passableType`
- `blocks[i, j].TempOccupy` references: **drop the `&& !TempOccupy` clauses** per spec §10.4. The `Reset` calls become:
  ```csharp
  graph[i, j].Reset(blocks[i, j].passableType <= moveMethod);
  ```
- `blocks[i, j].ProtalOutBlock != null` → `blocks[i, j].portalOutI != -1`
- `blocks[i, j].ProtalOutBlock.transform.position` → `(blocks[i, j].portalOutI, blocks[i, j].portalOutJ)` — the lookup lines were:
  ```csharp
  int ti = (int)blocks[i, j].ProtalOutBlock.transform.position.y;
  int tj = (int)blocks[i, j].ProtalOutBlock.transform.position.x;
  ```
  Replace with:
  ```csharp
  int ti = blocks[i, j].portalOutI;
  int tj = blocks[i, j].portalOutJ;
  ```
- Note: in the `JudgePointInUnWalkableBlock` nested function, `graph[ij.i, ij.j].Passable` is read from a `bool` (set elsewhere), and `blocks[i, j].ProtalOutBlock != null` is the only BlockData-shape read.

- [ ] **Step 4: Run test to verify they pass**

Run. Expected: **3 tests pass** (7 total in the suite).

- [ ] **Step 5: Commit**

```bash
git add Assets/Editor/LevelEditor/PathEditing/EditorPathFinder.cs \
        Assets/Tests/Editor/MapData/EditorPathFinderTests.cs
git commit -m "refactor(map-data): EditorPathFinder.AStar takes BlockState[,] + tests"
```

---

### Task 2.4: Update `BlockMapCache` to load from `LevelData.MapData`

**Files:**
- Modify: `Assets/Editor/LevelEditor/PathEditing/BlockMapCache.cs`

Spec reference: §3.3, §9.1. The cache no longer needs `PrefabUtility.LoadPrefabContents` — it builds its data directly from `LevelData.MapData` + `iSize/jSize`. The class still has `Blocks` and `ISize/JSize` for compatibility with the rest of the editor code that consumes it.

- [ ] **Step 1: Read `BlockMapCache.cs` to understand its current shape**

Already known: `public BlockData[,] Blocks; public int ISize; public int JSize;` and `static BlockMapCache Load(GameObject mapPrefab)`. The `Dispose()` calls `PrefabUtility.UnloadPrefabContents` only if it loaded an asset prefab.

- [ ] **Step 2: Change `Blocks` field type to `BlockDataEntry[,]`**

Note: this is `BlockDataEntry` (the serializable struct), not `BlockState` (the runtime struct) — the editor only needs serialized data, no `material` caching.

```csharp
// before
public BlockData[,] Blocks;
public int ISize;
public int JSize;

// after
public BlockDataEntry[,] Blocks;
public int ISize;
public int JSize;
```

- [ ] **Step 3: Change `Load` to take `LevelData`**

```csharp
// before
public static BlockMapCache Load(GameObject mapPrefab)

// after
public static BlockMapCache Load(LevelData levelData)
{
    var cache = new BlockMapCache();
    cache.ISize = levelData.iSize;
    cache.JSize = levelData.jSize;
    if (cache.ISize <= 0 || cache.JSize <= 0) return cache;

    cache.Blocks = new BlockDataEntry[cache.ISize, cache.JSize];
    foreach (var entry in levelData.MapData)
    {
        if (entry.i < 0 || entry.i >= cache.ISize) continue;
        if (entry.j < 0 || entry.j >= cache.JSize) continue;
        cache.Blocks[entry.i, entry.j] = entry;
    }
    return cache;
}
```

- [ ] **Step 4: Simplify `Dispose`**

Since we no longer `LoadPrefabContents`, the new `Dispose()` is a no-op (kept for API compatibility with `LevelDataEditor.OnDisable`):

```csharp
public void Dispose() { }
```

- [ ] **Step 5: Compile-check Unity**

Open the project. The `MapCanvasView.cs` and `LevelDataEditor.cs` still reference the old API — that's expected (next task fixes the canvas, and Task 3.1 fixes the editor).

- [ ] **Step 6: Commit**

```bash
git add Assets/Editor/LevelEditor/PathEditing/BlockMapCache.cs
git commit -m "refactor(map-data): BlockMapCache loads from LevelData.MapData"
```

---

### Task 2.5: Update `MapCanvasView` to draw from `BlockDataEntry[,]`

**Files:**
- Modify: `Assets/Editor/LevelEditor/PathEditing/MapCanvasView.cs`

Spec reference: §4.2 (shared rendering), §9.1.

- [ ] **Step 1: Read the current `MapCanvasView.cs`**

Read in full. Note the `DrawBlocks` function and the `BlockTypeColor(int)` helper.

- [ ] **Step 2: Replace `BlockData bd` with `BlockDataEntry bd`**

In the loop inside `DrawBlocks`, change the variable type and field accesses:

- `bd.PassableType` → `bd.passableType`
- `bd.Highland` → `bd.highland`
- `bd.CanSet` → `bd.canSet`
- `bd.Deadly` → `bd.deadly`
- `bd.ProtalOutBlock` → `(bd.portalOutI != -1)` (the canvas only needs to know "is this a portal-out source"; we don't render the target arrow in the editor canvas — that comes from `MapEditTab` portal mode)
- `bd.ProtalColor` → `bd.portalColor`

- [ ] **Step 3: Compile-check Unity**

Open the project. The remaining compilation errors should be only in `LevelDataEditor.cs` (it still constructs `BlockMapCache.Load(ld.MapPrefab)` and wires `PathEditingSection`).

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/LevelEditor/PathEditing/MapCanvasView.cs
git commit -m "refactor(map-data): MapCanvasView reads BlockDataEntry from cache"
```

---

### Task 2.6: Delete `BlockData.cs`

**Files:**
- Delete: `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/BlockData.cs`

- [ ] **Step 1: Verify no remaining references**

Run from the project root:

```bash
cd e:/Unity/projects/TD && grep -rn "BlockData\b" Assets --include="*.cs" --include="*.prefab" --include="*.asset"
```

Expected output: zero hits (the grep would also miss `BlockDataEntry` / `BlockDataManager` because of word boundary; the intent is to find bare `BlockData` references in code and YAML).

- [ ] **Step 2: Delete the file**

```bash
git rm Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/BlockData.cs
```

- [ ] **Step 3: Compile-check Unity**

Open the project. Expected: **the project compiles cleanly**. Editor still has UI errors from `LevelDataEditor` (it imports `PathEditingSection` which we're about to delete in Phase 3). Runtime should be fully clean.

- [ ] **Step 4: Re-run all EditMode tests**

Run via Test Runner. Expected: **7 tests pass**.

- [ ] **Step 5: Commit**

```bash
git commit -m "refactor(map-data): delete BlockData MonoBehaviour"
```

---

## Phase 3: New Editor Section

### Task 3.1: Create `MapEditorSection.cs` — shared scaffolding

**Files:**
- Create: `Assets/Editor/LevelEditor/Sections/MapEditorSection.cs`

Spec reference: §4.2. This is the foundation that both `MapEditTab` and `PathEditTab` plug into.

- [ ] **Step 1: Read the existing `PathEditingSection.cs` and `PathEditingState.cs`**

Read both files in full to understand the existing event/state pattern. `PathEditingState` exposes a `Changed` event that triggers re-render. Reuse it.

- [ ] **Step 2: Write the scaffolding file**

`Assets/Editor/LevelEditor/Sections/MapEditorSection.cs`:

```csharp
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Shared scaffolding for the [地图] / [路径] tabs in the LevelData inspector.
/// Owns the BlockMapCache, ViewTransform, and the canvas background that both
/// tabs render on top of. See spec §4.2.
/// </summary>
public static class MapEditorSection
{
    public static VisualElement Build(SerializedObject so, LevelData levelData, out IDisposable disposable)
    {
        disposable = null;
        var root = new VisualElement();
        root.style.flexDirection = FlexDirection.Column;
        root.AddToClassList("map-editor-section");

        var cache = BlockMapCache.Load(levelData);
        disposable = cache;

        if (levelData.iSize == 0 || levelData.jSize == 0)
        {
            var placeholder = new Label("⚠ MapData 为空:先在下方设置 iSize / jSize,再开始画地图。");
            placeholder.style.color = new Color(0.95f, 0.7f, 0.3f);
            placeholder.style.paddingTop = 8;
            placeholder.style.paddingBottom = 8;
            root.Add(placeholder);
            // Continue building — user might still want to set iSize/jSize.
        }

        // Size controls
        var sizeRow = new VisualElement();
        sizeRow.style.flexDirection = FlexDirection.Row;
        sizeRow.style.paddingTop = 4;
        sizeRow.style.paddingBottom = 4;
        sizeRow.Add(MakeLabeledIntField("iSize (rows)", so, "iSize", cache));
        sizeRow.Add(MakeLabeledIntField("jSize (cols)", so, "jSize", cache));
        root.Add(sizeRow);

        // Tab container
        var tabBar = new VisualElement();
        tabBar.style.flexDirection = FlexDirection.Row;
        tabBar.style.paddingTop = 8;
        var mapTabBtn = new Button { text = "地图" };
        var pathTabBtn = new Button { text = "路径" };
        mapTabBtn.style.flexGrow = 1;
        pathTabBtn.style.flexGrow = 1;
        tabBar.Add(mapTabBtn);
        tabBar.Add(pathTabBtn);
        root.Add(tabBar);

        // Body
        var body = new VisualElement();
        body.style.paddingTop = 4;
        root.Add(body);

        var state = new PathEditingState { Cache = cache };
        VisualElement currentTab = null;

        void ShowTab(System.Func<VisualElement> buildTab, Button activeBtn, Button inactiveBtn)
        {
            body.Clear();
            currentTab = buildTab();
            body.Add(currentTab);
            activeBtn.style.backgroundColor = new Color(0.35f, 0.45f, 0.65f);
            inactiveBtn.style.backgroundColor = new StyleColor(StyleKeyword.Null);
            state.NotifyChanged();
        }

        mapTabBtn.clicked += () => ShowTab(
            () => MapEditTab.Build(so, cache, state),
            mapTabBtn, pathTabBtn);
        pathTabBtn.clicked += () => ShowTab(
            () => PathEditTab.Build(so, state),
            pathTabBtn, mapTabBtn);

        // Default: show map tab
        ShowTab(() => MapEditTab.Build(so, cache, state), mapTabBtn, pathTabBtn);

        return root;
    }

    static VisualElement MakeLabeledIntField(string label, SerializedObject so, string propName, BlockMapCache cache)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginRight = 12;
        var lbl = new Label(label);
        lbl.style.minWidth = 80;
        row.Add(lbl);
        var prop = so.FindProperty(propName);
        var field = new IntegerField { value = prop.intValue };
        field.style.width = 60;
        field.RegisterValueChangedCallback(evt =>
        {
            Undo.RecordObject(so.targetObject, $"Change {propName}");
            prop.intValue = Mathf.Max(0, evt.newValue);
            so.ApplyModifiedProperties();
            // Note: cache is stale until user reloads section. The section
            // could be torn down and rebuilt on size change; for simplicity
            // we leave the rebuild to the user closing/reopening the inspector.
        });
        row.Add(field);
        return row;
    }
}
```

**Important**: this step will fail to compile because `MapEditTab` and `PathEditTab` don't exist yet. That's fine — they're created in Tasks 3.2 and 3.3.

- [ ] **Step 3: Commit (compilation will fail; that's expected)**

```bash
git add Assets/Editor/LevelEditor/Sections/MapEditorSection.cs
git commit -m "feat(level-editor): add MapEditorSection scaffolding for map+path tabs"
```

---

### Task 3.2: Create `MapEditTab.cs` — map paint workflow

**Files:**
- Create: `Assets/Editor/LevelEditor/Sections/MapEditTab.cs`

Spec reference: §4.3.

This is the largest new file. The paint workflow uses a `MouseManipulator` (lifted from `EditorPathManipulator`) but writes to `LevelData.MapData` instead of `LevelData.Paths`.

- [ ] **Step 1: Create the file**

`Assets/Editor/LevelEditor/Sections/MapEditTab.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Map-paint tab. See spec §4.3. Single-field brush with portal two-click mode.
/// Writes to <c>LevelData.MapData</c> via SerializedProperty + Undo.
/// </summary>
public static class MapEditTab
{
    enum PortalMode { Off, SetPortalOut }

    class BrushState
    {
        public bool highland;
        public bool canSet;
        public int  passableType;
        public bool deadly;
        public PortalMode portalMode = PortalMode.Off;
        public (int i, int j)? pendingPortalSource;
    }

    public static VisualElement Build(SerializedObject so, BlockMapCache cache, PathEditingState state)
    {
        var root = new VisualElement();
        root.style.flexDirection = FlexDirection.Row;

        // === Brush panel (left) ===
        var panel = new VisualElement();
        panel.style.width = 200;
        panel.style.paddingRight = 8;
        panel.style.borderRightWidth = 1;
        panel.style.borderRightColor = new Color(0.3f, 0.3f, 0.3f);
        root.Add(panel);

        var brush = new BrushState();
        panel.Add(MakeToggle("Highland", brush.highland, v => brush.highland = v));
        panel.Add(MakeToggle("CanSet",   brush.canSet,   v => brush.canSet = v));

        var passableRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
        passableRow.Add(new Label("PassableType") { style = { minWidth = 100 } });
        var passableField = new IntegerField { value = brush.passableType };
        passableField.style.flexGrow = 1;
        passableField.RegisterValueChangedCallback(evt => brush.passableType = Mathf.Clamp(evt.newValue, 0, 3));
        passableRow.Add(passableField);
        panel.Add(passableRow);

        panel.Add(MakeToggle("Deadly", brush.deadly, v => brush.deadly = v));

        var portalLabel = new Label("Portal mode");
        panel.Add(portalLabel);
        var portalEnum = new EnumField(brush.portalMode);
        portalEnum.RegisterValueChangedCallback(evt => brush.portalMode = (PortalMode)evt.newValue);
        panel.Add(portalEnum);

        panel.Add(new Label("提示:点击格子应用画刷;portal 模式两段式。右键 = 清除。"));

        // === Canvas (right) ===
        var canvasContainer = new VisualElement();
        canvasContainer.style.flexGrow = 1;
        canvasContainer.style.minHeight = 400;
        root.Add(canvasContainer);

        var view = new ViewTransform();
        var canvas = new VisualElement();
        canvas.style.flexGrow = 1;
        canvasContainer.Add(canvas);

        var status = new Label("(i, j): -");
        status.style.paddingTop = 4;
        canvasContainer.Add(status);

        void Repaint()
        {
            canvas.generateVisualContent = null;
            canvas.generateVisualContent = ctx => MapCanvasView.DrawBlocks(ctx, state, view);
            canvas.MarkDirtyRepaint();
        }
        state.Changed += Repaint;
        Repaint();

        // === Manipulator ===
        var manip = new MapEditManipulator(so, cache, view, brush, status, state, Repaint);
        canvas.AddManipulator(manip);

        // Repaint when undo/redo changes MapData
        Undo.undoRedoPerformed += Repaint;

        // Cleanup on detach
        canvas.RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            state.Changed -= Repaint;
            Undo.undoRedoPerformed -= Repaint;
        });

        return root;
    }

    static Toggle MakeToggle(string label, bool initial, System.Action<bool> onChange)
    {
        var t = new Toggle(label) { value = initial };
        t.RegisterValueChangedCallback(evt => onChange(evt.newValue));
        return t;
    }

    // === Manipulator ===
    class MapEditManipulator : MouseManipulator
    {
        readonly SerializedObject _so;
        readonly BlockMapCache _cache;
        readonly ViewTransform _view;
        readonly BrushState _brush;
        readonly Label _status;
        readonly PathEditingState _state;
        readonly System.Action _repaint;

        public MapEditManipulator(SerializedObject so, BlockMapCache cache, ViewTransform view,
            BrushState brush, Label status, PathEditingState state, System.Action repaint)
        {
            _so = so; _cache = cache; _view = view; _brush = brush;
            _status = status; _state = state; _repaint = repaint;
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

        void OnWheel(WheelEvent evt) { _view.Zoom(evt.delta.y); _repaint(); }

        (int i, int j)? ScreenToCell(Vector2 local)
        {
            if (_cache.Blocks == null) return null;
            var world = _view.ScreenToWorld(local, _cache.ISize, _cache.JSize);
            int i = (int)(world.y + 0.5f);
            int j = (int)(world.x + 0.5f);
            if (i < 0 || j < 0 || i >= _cache.ISize || j >= _cache.JSize) return null;
            return (i, j);
        }

        void OnMouseMove(MouseMoveEvent evt)
        {
            var cell = ScreenToCell(evt.localMousePosition);
            _status.text = cell.HasValue ? $"(i, j): ({cell.Value.i}, {cell.Value.j})" : "(i, j): -";
        }

        void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.button == 1) // right-click: clear
            {
                var cell = ScreenToCell(evt.localMousePosition);
                if (cell.HasValue) ClearCell(cell.Value);
                return;
            }
            if (evt.button != 0) return;
            var c = ScreenToCell(evt.localMousePosition);
            if (!c.HasValue) return;

            if (_brush.portalMode == PortalMode.SetPortalOut)
            {
                if (!_brush.pendingPortalSource.HasValue)
                {
                    _brush.pendingPortalSource = c;
                }
                else
                {
                    var src = _brush.pendingPortalSource.Value;
                    var dst = c.Value;
                    SetPortalOut(src, dst);
                    _brush.pendingPortalSource = null;
                }
                _repaint();
                return;
            }
            ApplyBrush(c.Value);
        }

        void OnMouseUp(MouseUpEvent evt) { /* drag-to-paint handled by repeated MouseDown if you want; future extension */ }

        // === SerializedProperty writes ===
        SerializedProperty MapDataProp() => _so.FindProperty("MapData");

        void ApplyBrush((int i, int j) cell)
        {
            Undo.RecordObject(_so.targetObject, "Paint Block");
            var mapData = MapDataProp();
            int idx = FindEntryIndex(mapData, cell);
            if (idx < 0)
            {
                idx = mapData.arraySize;
                mapData.InsertArrayElementAtIndex(idx);
            }
            var entry = mapData.GetArrayElementAtIndex(idx);
            entry.FindPropertyRelative("i").intValue = cell.i;
            entry.FindPropertyRelative("j").intValue = cell.j;
            entry.FindPropertyRelative("highland").boolValue = _brush.highland;
            entry.FindPropertyRelative("canSet").boolValue = _brush.canSet;
            entry.FindPropertyRelative("passableType").intValue = _brush.passableType;
            entry.FindPropertyRelative("deadly").boolValue = _brush.deadly;
            // portalOutI/J unchanged on regular paint
            _so.ApplyModifiedProperties();
            RefreshCacheFromSO();
            _state.NotifyChanged();
        }

        void SetPortalOut((int i, int j) src, (int i, int j) dst)
        {
            Undo.RecordObject(_so.targetObject, "Set Portal");
            var mapData = MapDataProp();
            int idx = FindEntryIndex(mapData, src);
            if (idx < 0)
            {
                idx = mapData.arraySize;
                mapData.InsertArrayElementAtIndex(idx);
                var e = mapData.GetArrayElementAtIndex(idx);
                e.FindPropertyRelative("i").intValue = src.i;
                e.FindPropertyRelative("j").intValue = src.j;
            }
            var entry = mapData.GetArrayElementAtIndex(idx);
            entry.FindPropertyRelative("portalOutI").intValue = dst.i;
            entry.FindPropertyRelative("portalOutJ").intValue = dst.j;
            _so.ApplyModifiedProperties();
            RefreshCacheFromSO();
            _state.NotifyChanged();
        }

        void ClearCell((int i, int j) cell)
        {
            Undo.RecordObject(_so.targetObject, "Clear Block");
            var mapData = MapDataProp();
            int idx = FindEntryIndex(mapData, cell);
            if (idx >= 0)
            {
                mapData.DeleteArrayElementAtIndex(idx);
                _so.ApplyModifiedProperties();
            }
            RefreshCacheFromSO();
            _state.NotifyChanged();
        }

        static int FindEntryIndex(SerializedProperty mapData, (int i, int j) cell)
        {
            for (int k = 0; k < mapData.arraySize; k++)
            {
                var e = mapData.GetArrayElementAtIndex(k);
                if (e.FindPropertyRelative("i").intValue == cell.i &&
                    e.FindPropertyRelative("j").intValue == cell.j) return k;
            }
            return -1;
        }

        void RefreshCacheFromSO()
        {
            // Rebuild Blocks[,] from the SO. Simple: re-read each entry.
            if (_cache.Blocks == null) return;
            // Reset to default first
            for (int i = 0; i < _cache.ISize; i++)
            for (int j = 0; j < _cache.JSize; j++)
                _cache.Blocks[i, j] = default;

            var mapData = MapDataProp();
            for (int k = 0; k < mapData.arraySize; k++)
            {
                var e = mapData.GetArrayElementAtIndex(k);
                int i = e.FindPropertyRelative("i").intValue;
                int j = e.FindPropertyRelative("j").intValue;
                if (i < 0 || j < 0 || i >= _cache.ISize || j >= _cache.JSize) continue;
                _cache.Blocks[i, j] = new BlockDataEntry
                {
                    i = i, j = j,
                    highland = e.FindPropertyRelative("highland").boolValue,
                    canSet = e.FindPropertyRelative("canSet").boolValue,
                    passableType = e.FindPropertyRelative("passableType").intValue,
                    deadly = e.FindPropertyRelative("deadly").boolValue,
                    portalOutI = e.FindPropertyRelative("portalOutI").intValue,
                    portalOutJ = e.FindPropertyRelative("portalOutJ").intValue,
                    portalColor = e.FindPropertyRelative("portalColor").colorValue,
                };
            }
        }
    }
}
```

- [ ] **Step 2: Verify the file compiles**

Open the project. The `PathEditTab` reference is unresolved — that's expected (next task).

- [ ] **Step 3: Commit**

```bash
git add Assets/Editor/LevelEditor/Sections/MapEditTab.cs
git commit -m "feat(level-editor): add MapEditTab with single-field brush + portal mode"
```

---

### Task 3.3: Create `PathEditTab.cs` — path paint workflow

**Files:**
- Create: `Assets/Editor/LevelEditor/Sections/PathEditTab.cs`

Spec reference: §4.4. Take the body of today's `PathEditingSection.cs` and adapt to live inside a tab. Most of the existing code moves verbatim.

- [ ] **Step 1: Read the current `PathEditingSection.cs`**

Read the full file. Note:
- The static `Build(SerializedObject so, PathEditingState state)` signature.
- The toolbar, canvas, manipulator, paths render — all of these move.
- The only adaptation needed is wrapping the existing logic in this new file with the same `Build(SerializedObject so, PathEditingState state)` signature, so `MapEditorSection` can call it.

- [ ] **Step 2: Create the file with the existing code**

`Assets/Editor/LevelEditor/Sections/PathEditTab.cs` — copy the body of `PathEditingSection.cs` into this new file. Change the class name from `PathEditingSection` to `PathEditTab`. Keep the `Build` signature the same.

```csharp
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class PathEditTab
{
    public static VisualElement Build(SerializedObject so, PathEditingState state)
    {
        // ... [exact body of PathEditingSection.Build] ...
    }
}
```

The `PathEditTab` class name replaces `PathEditingSection`. The internal logic, manipulator wiring, and `MapCanvasView.DrawBlocks` call are unchanged.

- [ ] **Step 3: Verify compile**

Open the project. Expected: **project compiles cleanly** (the only remaining error is `LevelDataEditor` still using the old `PathEditingSection.Build` — Task 3.4 fixes that).

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/LevelEditor/Sections/PathEditTab.cs
git commit -m "feat(level-editor): extract PathEditTab from PathEditingSection"
```

---

### Task 3.4: Wire `LevelDataEditor` to use `MapEditorSection`

**Files:**
- Modify: `Assets/Editor/LevelEditor/LevelDataEditor.cs`

Spec reference: §4.1, §9.1.

- [ ] **Step 1: Replace the path-editing block in `CreateInspectorGUI`**

In `LevelDataEditor.cs` (lines 47-71 in the version you read), replace:

```csharp
// before
if (ld.MapPrefab != null)
{
    try
    {
        _mapCache = BlockMapCache.Load(ld.MapPrefab);
        _pathState = new PathEditingState { Cache = _mapCache };
        body.Add(PathEditingSection.Build(serializedObject, _pathState));
    }
    catch (System.Exception e) { ... }
}
else { ... }

// after
// Map editor + path editor section (uses LevelData.MapData, prefab is optional)
_mapCache = BlockMapCache.Load(ld);
_pathState = new PathEditingState { Cache = _mapCache };
body.Add(MapEditorSection.Build(serializedObject, ld, out IDisposable sectionDisposable));
```

Wrap the new section call in a try/catch like before (defensive: any null-ref in tab build should be caught and reported as a warning label, not crash the inspector).

- [ ] **Step 2: Add field for the disposable**

Add to the class:

```csharp
IDisposable _sectionDisposable;
```

Store the disposable returned by `MapEditorSection.Build` in this field; dispose it in `OnDisable`.

- [ ] **Step 3: Update `OnDisable`**

```csharp
public override void OnDisable()
{
    Undo.undoRedoPerformed -= OnUndoRedo;
    _sectionDisposable?.Dispose();
    _sectionDisposable = null;
    _mapCache = null;
    _pathState = null;
}
```

- [ ] **Step 4: Verify compile**

Open the project. Expected: **project compiles cleanly** (the only error is `PathEditingSection` is no longer referenced anywhere — we delete it in Task 3.5).

- [ ] **Step 5: Manual smoke test — open a `LevelData` in the inspector**

In the Project window, select `Assets/Resources/Prefabs/Levels/Main/AdventureOfMinecraft/MC-1/MC-1.asset`. The inspector should render with:
- All existing sections (Metadata, References, Economy, WaveTimeline, ActionDetail).
- New `MapEditorSection` at the bottom showing the `[地图] / [路径]` tab bar.
- Default tab is `[地图]`. Canvas should be empty (because `MapData` is empty on MC-1's existing asset — it has BlockData on the prefab only).
- Switch to `[路径]`. The path editor UI should appear and behave the same as before (the old `PathEditingSection` is now `PathEditTab`).

- [ ] **Step 6: Commit**

```bash
git add Assets/Editor/LevelEditor/LevelDataEditor.cs
git commit -m "refactor(level-editor): LevelDataEditor wires MapEditorSection"
```

---

### Task 3.5: Delete `PathEditingSection.cs`

**Files:**
- Delete: `Assets/Editor/LevelEditor/Sections/PathEditingSection.cs`

- [ ] **Step 1: Verify no remaining references**

```bash
cd e:/Unity/projects/TD && grep -rn "PathEditingSection" Assets --include="*.cs"
```

Expected: zero hits.

- [ ] **Step 2: Delete the file**

```bash
git rm Assets/Editor/LevelEditor/Sections/PathEditingSection.cs
```

- [ ] **Step 3: Verify compile + run all tests**

Open the project. Expected: **compiles cleanly + 7 EditMode tests pass**.

- [ ] **Step 4: Commit**

```bash
git commit -m "refactor(level-editor): delete PathEditingSection (replaced by MapEditorSection+PathEditTab)"
```

### Task 3.6: Add warning strip to `MapEditorSection`

**Files:**
- Modify: `Assets/Editor/LevelEditor/PathEditing/BlockMapCache.cs`
- Modify: `Assets/Editor/LevelEditor/Sections/MapEditorSection.cs`

Spec reference: §7. A single warning strip at the top of `MapEditorSection` listing out-of-range entries, duplicate `(i, j)` entries, and the "prefab has legacy BlockData" warning.

- [ ] **Step 1: Add `GetWarnings()` to `BlockMapCache`**

In `BlockMapCache.cs`, add a public method that scans `MapData` for problems:

```csharp
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Returns a list of human-readable warnings about the current state of
/// MapData. Empty list means no warnings. See spec §7.
/// </summary>
public List<string> GetWarnings()
{
    var warnings = new List<string>();
    if (Blocks == null) return warnings;

    var seen = new HashSet<(int, int)>();
    var mapData = ((LevelData)_levelDataRef).MapData;
    foreach (var entry in mapData)
    {
        if (entry.i < 0 || entry.i >= ISize || entry.j < 0 || entry.j >= JSize)
        {
            warnings.Add($"entry (i={entry.i}, j={entry.j}) is out of range (grid is {ISize}×{JSize})");
            continue;
        }
        if (!seen.Add((entry.i, entry.j)))
        {
            warnings.Add($"duplicate entry at (i={entry.i}, j={entry.j})");
        }
    }

    if (_levelDataRef != null && ((LevelData)_levelDataRef).MapPrefab != null
        && HasLegacyBlockData(((LevelData)_levelDataRef).MapPrefab))
    {
        warnings.Add("MapPrefab still has legacy BlockData components — auto-clean on next save");
    }

    return warnings;
}

static bool HasLegacyBlockData(GameObject prefab)
{
    if (prefab == null) return false;
    foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
    {
        if (t.GetComponent<BlockData>() != null) return true;
    }
    return false;
}
```

Add a `LevelData _levelDataRef` field to `BlockMapCache` and store it in `Load`. Also add `public LevelData LevelData => _levelDataRef;` so the warnings method can access it. (The `BlockData` reference in `HasLegacyBlockData` is safe — this method only runs at editor time, and `BlockData.cs` may have been deleted by the time this task runs; the plan orders 2.6 (delete BlockData) BEFORE 3.6 — so we must adapt: change `HasLegacyBlockData` to use `MonoBehaviour` reflection or `GetComponentsInChildren<MonoBehaviour>()` and check the type name by string. Or run this code path only when `BlockData` is still defined — in practice, since this method is only called from the editor on a prefab, and the type is in `BasicScripts` asmdef which is referenced by the editor asmdef, this compiles fine as long as `BlockData.cs` hasn't been deleted. **If 2.6 already ran**, use the string-name approach:)

```csharp
static bool HasLegacyBlockData(GameObject prefab)
{
    if (prefab == null) return false;
    foreach (var mb in prefab.GetComponentsInChildren<MonoBehaviour>(true))
    {
        if (mb == null) continue; // missing-script entry
        if (mb.GetType().Name == "BlockData") return true;
    }
    return false;
}
```

Use this version (no `BlockData` type reference).

- [ ] **Step 2: Replace the placeholder label in `MapEditorSection` with a warnings container**

In `MapEditorSection.cs`, replace the `iSize == 0` placeholder block with a `warningsContainer` VisualElement that re-populates on `state.Changed`:

```csharp
// Replace the existing:
//     if (levelData.iSize == 0 || levelData.jSize == 0) { ...placeholder... }
// with:
var warningsContainer = new VisualElement();
warningsContainer.style.paddingTop = 4;
warningsContainer.style.paddingBottom = 4;
root.Add(warningsContainer);

void RefreshWarnings()
{
    warningsContainer.Clear();
    foreach (var msg in cache.GetWarnings())
    {
        var lbl = new Label("⚠ " + msg);
        lbl.style.color = new Color(0.95f, 0.7f, 0.3f);
        lbl.style.paddingTop = 2;
        warningsContainer.Add(lbl);
    }
}
state.Changed += RefreshWarnings;
RefreshWarnings();
```

- [ ] **Step 3: Compile-check Unity**

Open the project. Expected: **compiles cleanly**.

- [ ] **Step 4: Manual smoke test**

In the inspector for `MC-1.asset`:
- If migration ran (post-2.6), `MapData` is non-empty and there should be no "out of range" warnings.
- If you shrink `iSize` to 5, several entries become out-of-range — the warnings strip shows them.
- Right-click on a duplicated entry (impossible via UI, but you can manually add a duplicate by YAML edit) — not testable via UI, skip.

- [ ] **Step 5: Commit**

```bash
git add Assets/Editor/LevelEditor/PathEditing/BlockMapCache.cs \
        Assets/Editor/LevelEditor/Sections/MapEditorSection.cs
git commit -m "feat(level-editor): add warnings strip to MapEditorSection (spec §7)"
```

---

## Phase 4: Migration

### Task 4.1: Implement `MapAutoMigrator` and the escape-hatch menu item

**Files:**
- Create: `Assets/Editor/LevelEditor/PathEditing/MapAutoMigrator.cs`
- Test: `Assets/Tests/Editor/MapData/MapAutoMigratorTests.cs`

Spec reference: §6. The migrator is a static helper that takes a `LevelData`, reads the legacy `BlockData` MBs from its prefab, and produces the `BlockDataEntry[]` data. Two callers:
1. `LevelDataEditor.OnEnable` (auto, when `MapData` is empty).
2. A `MenuItem` escape hatch (when the user wants to re-run migration on a specific level).

For testing: the migrator can be tested with an in-memory constructed `LevelData` + a procedurally-built `GameObject` (no need for `PrefabUtility.LoadPrefabContents` in tests — that's a side effect of the production caller, not the migrator itself).

- [ ] **Step 1: Write the failing test**

`Assets/Tests/Editor/MapData/MapAutoMigratorTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace MapData.Tests
{
    public class MapAutoMigratorTests
    {
        [Test]
        public void Migrate_reads_BlockData_MBs_from_prefab_children_into_entries()
        {
            // Build a fake prefab in memory
            var root = new GameObject("MapRoot");
            try
            {
                var child0 = new GameObject("c0");
                child0.transform.SetParent(root.transform, false);
                child0.transform.position = new Vector3(0, 0, 0);
                var bd0 = child0.AddComponent<BlockData>();   // still exists at this point
                SetPrivate(bd0, "_highland", true);
                SetPrivate(bd0, "_canSet", true);
                SetPrivate(bd0, "_passableType", 2);

                var child1 = new GameObject("c1");
                child1.transform.SetParent(root.transform, false);
                child1.transform.position = new Vector3(3, 5, 0);

                var entries = MapAutoMigrator.ReadFromPrefab(root);
                Assert.AreEqual(1, entries.Count);
                Assert.AreEqual(0, entries[0].i);
                Assert.AreEqual(0, entries[0].j);
                Assert.IsTrue(entries[0].highland);
                Assert.IsTrue(entries[0].canSet);
                Assert.AreEqual(2, entries[0].passableType);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Migrate_skips_children_without_BlockData()
        {
            var root = new GameObject("MapRoot");
            try
            {
                var c0 = new GameObject("c0");
                c0.transform.SetParent(root.transform, false);
                // no BlockData attached
                var entries = MapAutoMigrator.ReadFromPrefab(root);
                Assert.AreEqual(0, entries.Count);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // helper to set a private serialized field via reflection
        static void SetPrivate(object obj, string name, object value)
        {
            var f = obj.GetType().GetField(name,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            f.SetValue(obj, value);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run via Test Runner. Expected: **2 tests fail to compile** (`MapAutoMigrator` does not exist; `BlockData` reference — this latter will go away once we delete `BlockData` in Task 2.6; the test file as written above uses `BlockData` as a transitional reference, but since `BlockData.cs` is already deleted by the time we get here, the test must be updated to use a helper that constructs a `BlockData` instance via reflection).

**Adjustment**: update the test to use `ScriptableObject.CreateInstance` style reflection since `BlockData` may be deleted. A simpler test approach: build the test by directly invoking `MapAutoMigrator.ReadFromPrefab` with a hand-built hierarchy that has `BlockData` MBs (which still exist at the point this task runs, **before** Task 2.6's deletion). Since this is Task 4.1 and Task 2.6 is earlier in the plan, the test as written works.

- [ ] **Step 3: Write the migrator**

`Assets/Editor/LevelEditor/PathEditing/MapAutoMigrator.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot migration from the legacy BlockData-on-prefab format to
/// LevelData.MapData. See spec §6.
/// </summary>
public static class MapAutoMigrator
{
    /// <summary>
    /// Read all BlockData MBs from a prefab root's children and produce
    /// a list of <see cref="BlockDataEntry"/>. Coordinates are taken from
    /// each child's transform.position (integer part).
    /// </summary>
    public static List<BlockDataEntry> ReadFromPrefab(GameObject prefabRoot)
    {
        var result = new List<BlockDataEntry>();
        if (prefabRoot == null) return result;
        for (int k = 0; k < prefabRoot.transform.childCount; k++)
        {
            var child = prefabRoot.transform.GetChild(k);
            var bd = child.GetComponent<BlockData>();
            if (bd == null) continue;
            int i = (int)child.position.y;
            int j = (int)child.position.x;
            result.Add(new BlockDataEntry
            {
                i = i, j = j,
                highland = bd.Highland,
                canSet = bd.CanSet,
                passableType = bd.PassableType,
                deadly = bd.Deadly,
                portalOutI = bd.ProtalOutBlock != null ? (int)bd.ProtalOutBlock.transform.position.y : -1,
                portalOutJ = bd.ProtalOutBlock != null ? (int)bd.ProtalOutBlock.transform.position.x : -1,
                portalColor = bd.ProtalColor,
            });
        }
        return result;
    }

    /// <summary>
    /// Strip <see cref="BlockData"/> MBs from a prefab root's children in-place
    /// and save the prefab asset. Returns the number of components removed.
    /// </summary>
    public static int StripBlockDataFromPrefab(string assetPath)
    {
        var contents = PrefabUtility.LoadPrefabContents(assetPath);
        int removed = 0;
        for (int k = 0; k < contents.transform.childCount; k++)
        {
            var child = contents.transform.GetChild(k);
            var comps = child.GetComponents<BlockData>();
            foreach (var c in comps)
            {
                Object.DestroyImmediate(c, true);
                removed++;
            }
        }
        PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
        PrefabUtility.UnloadPrefabContents(contents);
        return removed;
    }

    /// <summary>
    /// Run the full migration on a LevelData: read legacy MBs into MapData,
    /// strip MBs from prefab, save both. Idempotent: if MapData is non-empty,
    /// does nothing.
    /// </summary>
    public static bool MigrateLevelData(LevelData levelData)
    {
        if (levelData == null) return false;
        if (levelData.MapData == null) levelData.MapData = new List<BlockDataEntry>();
        if (levelData.MapData.Count > 0) return false; // already migrated

        if (levelData.MapPrefab == null) return false;

        var prefabPath = AssetDatabase.GetAssetPath(levelData.MapPrefab);
        if (string.IsNullOrEmpty(prefabPath)) return false;

        var temp = Object.Instantiate(levelData.MapPrefab);
        try
        {
            var entries = ReadFromPrefab(temp);
            foreach (var e in entries) levelData.MapData.Add(e);
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }

        EditorUtility.SetDirty(levelData);
        AssetDatabase.SaveAssets();
        StripBlockDataFromPrefab(prefabPath);
        AssetDatabase.SaveAssets();
        return true;
    }

    [MenuItem("Tools/Level Editor/Migrate Old Map (active LevelData)")]
    public static void MigrateMenuItem()
    {
        var sel = Selection.activeObject as LevelData;
        if (sel == null)
        {
            EditorUtility.DisplayDialog("Migrate", "请先在 Project 窗口选中一个 LevelData 资产。", "OK");
            return;
        }
        if (MigrateLevelData(sel))
        {
            EditorUtility.DisplayDialog("Migrate", $"已迁移 {sel.MapData.Count} 个地块。", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Migrate", "无需迁移 (MapData 已存在,或 prefab 为空)。", "OK");
        }
    }
}
```

- [ ] **Step 4: Run test to verify they pass**

Run. Expected: **2 tests pass** (9 total in the suite).

- [ ] **Step 5: Commit**

```bash
git add Assets/Editor/LevelEditor/PathEditing/MapAutoMigrator.cs \
        Assets/Tests/Editor/MapData/MapAutoMigratorTests.cs
git commit -m "feat(level-editor): MapAutoMigrator + escape-hatch menu item"
```

---

### Task 4.2: Auto-migrate on `LevelDataEditor.OnEnable`

**Files:**
- Modify: `Assets/Editor/LevelEditor/LevelDataEditor.cs`

Spec reference: §6.1.

- [ ] **Step 1: Add migration call to `OnEnable`**

In `LevelDataEditor.cs`, add to the `OnEnable` method (currently line 131):

```csharp
public override void OnEnable()
{
    Undo.undoRedoPerformed += OnUndoRedo;
    var ld = (LevelData)target;
    if (ld.MapData != null && ld.MapData.Count == 0)
    {
        try
        {
            if (MapAutoMigrator.MigrateLevelData(ld))
            {
                Debug.Log($"[LevelDataEditor] Auto-migrated {ld.MapData.Count} blocks from {ld.name}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LevelDataEditor] Auto-migration failed: {e}");
        }
    }
}
```

- [ ] **Step 2: Manual smoke test on MC-1**

Make a backup copy of `Assets/Resources/Prefabs/Levels/Main/AdventureOfMinecraft/MC-1/Map.prefab` (just in case):

```bash
cp Assets/Resources/Prefabs/Levels/Main/AdventureOfMinecraft/MC-1/Map.prefab /tmp/Map.prefab.bak
cp Assets/Resources/Prefabs/Levels/Main/AdventureOfMinecraft/MC-1/MC-1.asset /tmp/MC-1.asset.bak
```

Then in Unity: select `MC-1.asset` in the Project window. The inspector opens; `OnEnable` runs; the migrator reads 49 `BlockData` MBs from `Map.prefab`, writes 49 entries to `MC-1.asset::MapData`, strips `BlockData` from `Map.prefab`, and saves both. The Unity console shows the log line.

Verify:
- `MC-1.asset` now has a `MapData` array of 49 entries.
- `Map.prefab` no longer has any `BlockData` MBs (open the prefab in Prefab Mode, the inspector for a child shows no `BlockData` component).
- The map editor tab renders the 49 tiles.

- [ ] **Step 3: Restore backup if anything looks off**

```bash
cp /tmp/Map.prefab.bak Assets/Resources/Prefabs/Levels/Main/AdventureOfMinecraft/MC-1/Map.prefab
cp /tmp/MC-1.asset.bak Assets/Resources/Prefabs/Levels/Main/AdventureOfMinecraft/MC-1/MC-1.asset
```

(If everything looks correct, you can skip this step.)

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/LevelEditor/LevelDataEditor.cs
git commit -m "feat(level-editor): auto-migrate legacy BlockData on LevelData open"
```

---

## Phase 5: Verification

### Task 5.1: End-to-end manual verification

**Files:** none (read-only verification)

- [ ] **Step 1: Open `MC-1.asset` in the inspector**

Expected: no compile errors; map editor shows 49 tiles in the canvas (post-migration); path tab shows existing paths.

- [ ] **Step 2: Paint a new tile**

In the map tab, set brush to `highland=true, canSet=false, passable=0, deadly=false`. Click any empty cell. Verify the canvas updates immediately and a new entry appears in `MapData`.

- [ ] **Step 3: Undo**

`Ctrl+Z`. Verify the new entry is removed and the canvas updates.

- [ ] **Step 4: Set a portal**

Switch portal mode to `SetPortalOut`. Click cell A, then click cell B. Verify cell A's `portalOutI/portalOutJ` are set to B's coordinates; the canvas shows the portal source highlighted in `portalColor`.

- [ ] **Step 5: Right-click to clear**

Right-click on a non-empty cell. Verify it's removed from `MapData` and disappears from the canvas.

- [ ] **Step 6: Change `iSize` / `jSize`**

Set `iSize=8, jSize=8` (smaller than the map). Verify entries outside the new range are flagged with a warning chip (out of range) and excluded from render.

- [ ] **Step 7: Save and reload**

Save the `MC-1.asset` (Unity auto-saves on focus loss / Ctrl+S). Reopen the inspector. The new state persists.

- [ ] **Step 8: Run the game (Playtest)**

Click `▶ Playtest` in the inspector footer. The game starts. Spawn an enemy; verify A\* still works (path visible, enemy follows the path). Place a tower; verify placement eligibility check works (uses the new `BlockState` data).

- [ ] **Step 9: Run all EditMode tests one more time**

Run via Test Runner. Expected: **9 tests pass** (BlockDataEntry 3 + BlockState 1 + MapDataManagerInit 3 + EditorPathFinder 3 + MapAutoMigrator 2).

- [ ] **Step 10: Document any issues in a follow-up**

If any manual verification step fails, do not proceed. File a follow-up task in the team's tracker. The plan ends here only on green.

---

## Acceptance Checklist

- [ ] `BlockData.cs` deleted, no remaining references in code or YAML.
- [ ] `PathEditingSection.cs` deleted, no remaining references.
- [ ] `LevelData.MapData` is the single source of truth for per-tile data.
- [ ] `MapPrefab == null` does not prevent the editor from working or the game from starting.
- [ ] Auto-migration runs once per LevelData, strips `BlockData` from prefab, populates `MapData`.
- [ ] `▶ Playtest` runs the game with the new data model (A* + tower placement verified).
- [ ] All 9 EditMode tests pass.
- [ ] No new compile warnings.
