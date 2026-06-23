# Map Data Storage Refactor — Design Spec

**Date**: 2026-06-23
**Status**: Approved (pending user review of written spec)
**Scope**: Replace per-tile `BlockData` MonoBehaviour with a data-on-`LevelData` storage model; add a map-editing tab to the existing path editor; refactor `MapDataManager` to read from the new data store; keep `MapPrefab` purely as visual model asset (optional).

## 1. Goal & Motivation

Today every tile in a map prefab carries a `BlockData : MonoBehaviour` whose only purpose is to be a per-tile data bag. `MapDataManager.MapInitialize` walks the prefab hierarchy at runtime, fills `BlockData[,] BlockDataMatrix`, and the rest of the game reads `.PassableType / .Highland / .CanSet / .Deadly / .ProtalOutBlock / .ProtalColor / .Material` off those components.

This design has three concrete problems:

1. **Editing requires editing prefab assets.** Designers must open the prefab, click each tile, and tweak fields one at a time. There is no `BlockType` palette, no brush, no mass-edit.
2. **Data is split between editor (prefab MB) and runtime (`BlockStateMatrix` mirror).** The MB is the source of truth in the asset but immediately copied into a runtime array; nothing about the MB is Unity-specific except `transform` and `GetComponent<MeshRenderer>().material`.
3. **`MapPrefab` is required for the game to start**, even though the runtime only needs it for visual meshes. Tiles with no special properties still need an empty prefab child for `BlockDataMatrix[i,j]` to point at.

This refactor moves the data onto `LevelData` as a sparse list of `BlockDataEntry`, demotes `BlockData` to a pure data shape (`BlockState`) used only at runtime, and decouples the editor from the prefab. The prefab becomes an optional visual model — the game runs (with data-driven A\* / tower placement) even if `MapPrefab` is null.

## 2. Architecture Overview

```
┌────────────────────────────────────────────────────────────────────┐
│  LevelData  (ScriptableObject, serialized .asset)                  │
│   ├── MapPrefab : GameObject?       (optional, visuals only)       │
│   ├── iSize : int                    (grid height, user-editable)  │
│   ├── jSize : int                    (grid width, user-editable)   │
│   ├── MapData : List<BlockDataEntry> (sparse, only non-empty tiles)│
│   ├── Paths : PathData[]                                              │
│   ├── Waves : Wave[]                                                  │
│   └── ...other existing fields...                                     │
└────────────────────────────────────────────────────────────────────┘
                ▲                                       ▲
                │ read at runtime                       │ edit at edit-time
                │                                       │
┌─────────────────────────────────┐    ┌─────────────────────────────────┐
│  MapDataManager  (runtime)      │    │  MapEditorSection  (UI Toolkit) │
│   - CreateMap(levelData.MapPrefab?)│    │   shared:                       │
│   - Initialize()                  │    │     - MapCache (LevelData only)│
│   - Build BlockState[,]          │    │     - ViewTransform (zoom/pan)  │
│   - AStarWayFinding / RangeCalc  │    │   tabs:                          │
└─────────────────────────────────┘    │     - MapEditTab (paint tiles)   │
                                       │     - PathEditTab (paint paths)  │
                                       └─────────────────────────────────┘
```

Two parallel paths:

- **Edit-time**: editor paints into `LevelData.MapData` directly via `SerializedProperty`. Prefab is **not read** by the editor (except during the one-time migration described in §6).
- **Run-time**: `MapDataManager` instantiates `MapPrefab` if non-null (visual mesh + cached `Material` per child), then builds `BlockState[,]` from `LevelData.MapData`. If `MapPrefab` is null, no visuals — but data is still there and A\* still works.

## 3. Data Structures

### 3.1 `BlockDataEntry` (serialized on `LevelData`)

```csharp
[System.Serializable]
public struct BlockDataEntry
{
    public int   i;             // grid row, 0..iSize-1
    public int   j;             // grid col, 0..jSize-1
    public bool  highland;
    public bool  canSet;
    public int   passableType;  // 0 = ground walk, 1 = ground+low-air, 2 = ground+low+high-air, 3 = impassable
    public bool  deadly;
    public int   portalOutI;    // -1 = no portal
    public int   portalOutJ;    // -1 = no portal
    public Color portalColor;
}
```

Stored as `public List<BlockDataEntry> MapData = new();` on `LevelData`. Sparse — empty cells are implicit (no entry). The list is naturally de-duplicated by `(i, j)`: when the editor paints an existing cell, the entry's fields are updated in place; when it paints an empty cell, a new entry is appended.

### 3.2 `BlockState` (runtime, struct)

```csharp
public struct BlockState
{
    public bool     highland;
    public bool     canSet;
    public int      passableType;
    public bool     deadly;
    public int      portalOutI;
    public int      portalOutJ;
    public Color    portalColor;
    public Material material;       // null if no prefab; runtime reads `.color` for placement feedback
    public bool     tempOccupy;     // kept for future use; no current writers (dead code today)
}
```

Stored as `BlockState[,] BlockStateMatrix` inside `MapDataManager` (replaces today's `BlockData[,] BlockDataMatrix`). Indexed `[i, j]`. Defaults to `default(BlockState)` (i.e. `passableType = 0`, all bools false, `material = null`) for cells without an entry.

### 3.3 Prefab binding (runtime only)

The runtime uses the prefab child's `transform.position` to assign visual data to a grid cell:

```
i = (int)child.position.y      // grid row
j = (int)child.position.x      // grid col
BlockStateMatrix[i, j].material = child.GetComponent<MeshRenderer>().material
```

The integer part of `position.x/.y` is the grid coordinate. Half-pixel offsets (`.5`) are the cell center; only the integer part is used for the index. **Reordering prefab children does not change the binding** (matches today's `BlockMapCache` behavior).

If `MapPrefab == null`, `BlockStateMatrix[i, j].material` is `null` for all cells. Callers that touch `material.color` (e.g. `LevelMessagePanel`) must null-check (the call sites today already dereference `.color` directly — these are updated to null-check, see §7).

## 4. Editor — Combined `MapEditorSection`

### 4.1 Layout

The current `PathEditingSection` is replaced by a merged section:

```
MapEditorSection.cs       ← shared scaffolding (camera, cache, canvas bg, base manipulator)
├── tabs container        ← two tabs: [地图] / [路径]
├── MapEditTab.cs         ← map-paint workflow (§4.3)
└── PathEditTab.cs        ← path-paint workflow (today's logic, moved here)
```

`PathEditingSection.cs` is deleted. `LevelDataEditor.cs:36-44` (the body stack) gains one line to insert `MapEditorSection` in place of the old `PathEditingSection`.

### 4.2 Shared scaffolding (`MapEditorSection.cs`)

Responsibilities:

- Build `MapCache` from `LevelData.MapData` + `LevelData.iSize/jSize` (no prefab dependency).
- Own `ViewTransform` (zoom/pan state) — the existing `ViewTransform` class is reused.
- Render canvas background: walk `LevelData.MapData`, draw one procedural rectangle per entry using `Painter2D` (the same color logic as today's `MapCanvasView.DrawBlocks`, but driven by `BlockDataEntry` instead of `BlockData`).
- Provide a base mouse manipulator that converts `MouseDownEvent`/`MoveEvent`/`UpEvent` to grid `(i, j)` coords (lifted from today's `EditorPathManipulator`).
- Emit `Changed` event when `LevelData.MapData` mutates (so the inspector repaints and so other tabs can re-render).
- Dispose `MapCache` in `OnDisable`.

### 4.3 `MapEditTab.cs`

UI:

- **Brush panel** (left side or top): five controls
  - `highland` toggle
  - `canSet` toggle
  - `passableType` enum (0/1/2/3)
  - `deadly` toggle
  - `portal mode` enum: `Off` / `Set as portal out` / `Pick portal target`
- **Canvas** (right side): shared canvas from §4.2.
- **Cursor crosshair**: a square outline at the hovered cell, color depends on current brush (red = deadly, blue = portal-out, etc.).
- **Status bar**: show `(i, j)` and current entry's fields at the cursor (read-only).

Interaction:

- **Click / drag with `portal mode = Off`**: write `highland / canSet / passableType / deadly` from brush panel into `BlockStateMatrix[i, j]`. If `(i, j)` is out of range → ignore. If entry exists → update fields in place; else → append new entry.
- **Click with `portal mode = Set as portal out`** (two-click flow): first click marks the source cell (highlighted in `portalColor`); second click sets the target. After the second click, the source cell's `portalOutI / portalOutJ` are written to the target's `(i, j)`. While waiting for the second click, draw a dashed line from source to cursor (preview). `Esc` cancels the pending source.
- **Right-click on a cell**: clear that cell's entry — see §10.3.
- **`iSize` / `jSize`**: two `IntegerField`s at the top of the brush panel, bound to `LevelData.iSize / jSize`. Changing them auto-clips `MapData`: any entry with `i >= iSize` or `j >= jSize` is flagged as out-of-range (warning chip) and excluded from render/A\* until the user fixes it.

Persistence:

- All field writes go through `SerializedObject` on `LevelData`, wrapped with `Undo.RecordObject(levelData, "Paint Block")`.
- `so.ApplyModifiedProperties()` triggers `MapCache.Rebuild()` and the section's `Changed` event → repaint.

### 4.4 `PathEditTab.cs`

Today's `PathEditingSection.cs` logic moved verbatim, with two adjustments:

- It subscribes to the shared `Changed` event so path preview re-renders after a map edit (paths may now cross newly-edited tiles).
- `EditorPathFinder` is updated to read `BlockState[,]` (see §5).

The path tab's data source is `LevelData.Paths`, unchanged.

## 5. Runtime — `MapDataManager` Refactor

### 5.1 Field changes

```csharp
// before
public BlockData[,] BlockDataMatrix;

// after
public BlockState[,] BlockStateMatrix;
public int ISize => iSize;
public int JSize => jSize;
```

### 5.2 Lifecycle

```csharp
public void CreateMap(GameObject map)
{
    _map = map != null ? UnityEngine.Object.Instantiate(map, LevelResourceSharing.LM) : null;
}

public void Initialize()
{
    // 1. grid size comes from LevelData (not from prefab)
    iSize = _levelData.iSize;
    jSize = _levelData.jSize;
    BlockStateMatrix = new BlockState[iSize, jSize];

    // 2. seed entries from LevelData.MapData
    foreach (var entry in _levelData.MapData)
    {
        if (entry.i < 0 || entry.i >= iSize || entry.j < 0 || entry.j >= jSize) continue;
        BlockStateMatrix[entry.i, entry.j] = entry.ToBlockState();
    }

    // 3. if prefab exists, assign material per child by transform.position
    if (_map != null)
    {
        for (int k = 0; k < _map.transform.childCount; k++)
        {
            var child = _map.transform.GetChild(k);
            int i = (int)child.position.y;
            int j = (int)child.position.x;
            if (i < 0 || i >= iSize || j < 0 || j >= jSize) continue;
            var mr = child.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var s = BlockStateMatrix[i, j];
                s.material = mr.material;
                BlockStateMatrix[i, j] = s;
            }
        }
    }

    // 4. build A* graph as today
    graph = new AStarProperty[iSize, jSize];
    heap = new HeapEntry[iSize * jSize + 16];
    for (int i = 0; i < iSize; i++)
    for (int j = 0; j < jSize; j++)
    {
        graph[i, j].plotPos = new Vector2(j, i);
        graph[i, j].portalEnter = BlockStateMatrix[i, j].portalOutI != -1;
    }
    EntityManager.Manager.BlockEntitysInitialize(iSize, jSize);
}
```

`MapDataManager` gains a constructor / factory that takes `LevelData`:

```csharp
public void AttachLevelData(LevelData levelData) { _levelData = levelData; }
```

(or the existing `CreateMap` signature is widened to `CreateMap(LevelData)` — chosen at impl time; see §10).

### 5.3 A\* and other call sites

| Today                                                                       | After                                                                  |
|-----------------------------------------------------------------------------|------------------------------------------------------------------------|
| `BlockDataMatrix[i, j].PassableType`                                       | `BlockStateMatrix[i, j].passableType`                                  |
| `BlockDataMatrix[i, j].Highland`                                            | `BlockStateMatrix[i, j].highland`                                      |
| `BlockDataMatrix[i, j].CanSet`                                              | `BlockStateMatrix[i, j].canSet`                                        |
| `BlockDataMatrix[i, j].Deadly`                                              | `BlockStateMatrix[i, j].deadly`                                        |
| `BlockDataMatrix[i, j].TempOccupy`                                          | `BlockStateMatrix[i, j].tempOccupy` (kept, still no writers — see §10) |
| `BlockDataMatrix[i, j].ProtalOutBlock` (cross-ref)                          | `BlockStateMatrix[i, j].portalOutI / portalOutJ`                       |
| `BlockDataMatrix[bd.ProtalOutBlock.transform.position.y, ...x]`             | `BlockStateMatrix[BlockStateMatrix[i, j].portalOutI, portalOutJ]`      |
| `blockData.Material = blockData.GetComponent<MeshRenderer>().material`      | `BlockStateMatrix[i, j].material = child.GetComponent<MeshRenderer>().material` (during init) |
| `BlockData.PassableType > 0` (`InteractableStatic.cs:45/53/61/69`)          | `BlockState.passableType > 0`                                          |
| `BlockData.Highland` (`MoveBase.cs:131`)                                    | `BlockState.highland`                                                  |
| `MachineTalent1.cs:144` `map[i, j].PassableType <= _thisMove.MoveMethod`    | unchanged signature, but the `map` field is now `BlockState[,]`        |

`MapDataManager.GetPosBlock` returns `BlockState` by value, not `BlockData` — call sites that need to mutate `material.color` (`LevelMessagePanel.ResetCanSetBlockColors`) must use the existing reference semantics. To avoid a struct-copy surprise, `MapDataManager` exposes `ref BlockState GetPosBlockRef(int i, int j)` returning `ref BlockStateMatrix[i, j]`. (Or the call sites are updated to use indices directly; chosen at impl time — see §10.)

### 5.4 `EditorPathFinder.cs`

Pure-function A\* now takes `BlockState[,]` instead of `BlockData[,]`. Portal lookup changes from `block.ProtalOutBlock.transform.position` to `blocks[i, j].portalOutI / portalOutJ`. All other logic is byte-identical (the existing code is already a 1:1 port of `MapDataManager.AStarWayFinding`).

### 5.5 `BlockData.cs`

Deleted. All references in the codebase (call sites listed in §5.3) are updated.

## 6. Migration

### 6.1 One-time auto-migration on editor open

When `LevelDataEditor.OnEnable` runs, after `MapCache.Load(levelData)`:

```
if (levelData.MapData.Count == 0)
{
    if (levelData.MapPrefab != null && HasBlockDataMB(levelData.MapPrefab))
    {
        // Auto-migrate: copy MB fields into MapData
        var contentsRoot = PrefabUtility.LoadPrefabContents(assetPath);
        for each child of contentsRoot:
            if child has BlockData bd:
                var entry = new BlockDataEntry {
                    i = (int)child.position.y,
                    j = (int)child.position.x,
                    highland = bd.Highland,
                    canSet = bd.CanSet,
                    passableType = bd.PassableType,
                    deadly = bd.Deadly,
                    portalOutI = bd.ProtalOutBlock != null ? (int)bd.ProtalOutBlock.transform.position.y : -1,
                    portalOutJ = bd.ProtalOutBlock != null ? (int)bd.ProtalOutBlock.transform.position.x : -1,
                    portalColor = bd.ProtalColor
                };
                levelData.MapData.Add(entry);
        // Strip BlockData from prefab
        for each child of contentsRoot:
            for each BlockData comp in child.GetComponents<BlockData>():
                Object.DestroyImmediate(comp, true);
        PrefabUtility.SaveAsPrefabAsset(contentsRoot, assetPath);
        PrefabUtility.UnloadPrefabContents(contentsRoot);
        EditorUtility.SetDirty(levelData);
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("已迁移", "该关卡已从旧 BlockData 格式迁移到 LevelData.MapData;prefab 上的 BlockData 已清理。", "OK");
    }
    else if (levelData.MapData.Count == 0 && levelData.iSize == 0 && levelData.jSize == 0)
    {
        // Fresh level — set defaults: iSize = jSize = 16
        levelData.iSize = 16;
        levelData.jSize = 16;
    }
}
```

### 6.2 Behavior after migration

- The prefab no longer carries `BlockData` MBs. Only `MeshFilter + MeshRenderer (+ Collider)` remain.
- `LevelData.MapData` is populated. Subsequent opens don't re-migrate (the `Count == 0` guard).
- If the user opens a LevelData whose prefab still has `BlockData` MBs (e.g. a level that was saved by an old version of the editor and never re-opened), the warning banner from §7 fires.

## 7. Error Handling & Validation

| Scenario                                                       | Handling                                                                |
|----------------------------------------------------------------|-------------------------------------------------------------------------|
| `iSize <= 0` or `jSize <= 0` on open                          | Treat as "fresh level" → defaults to 16×16 + warning chip               |
| `MapData` entry has `i/j` out of range (after `iSize` shrink) | Flag with a chip in the inspector; exclude from render/A\*              |
| Prefab child position out of range                            | Silently skipped (prefab may have stray objects; this is non-fatal)     |
| `MapData` has duplicate `(i, j)` after manual YAML edit       | First one wins; warning chip "duplicate entry at (i, j)"                 |
| Prefab still has `BlockData` MB after migration               | Warning chip "prefab has legacy BlockData; auto-clean on next save"      |
| `MapPrefab == null`                                            | Editor still works (no visual canvas, just procedural bg); runtime OK   |
| Runtime: `BlockState.material == null` on placement feedback   | `LevelMessagePanel` null-checks before reading `.color`                 |
| Runtime: `portalOutI/J` target cell has no entry               | A\* treats portal as non-traversable (same as today's "no ProtalOutBlock") |

A single warning chip strip lives at the top of `MapEditorSection`, populated by `MapCache.GetWarnings()`.

## 8. Save / Load

- **Editor edits to `MapData`**: standard `EditorUtility.SetDirty(levelData) + AssetDatabase.SaveAssets()` flow, wrapped in `Undo.RecordObject` so `Ctrl+Z` works.
- **Editor edits to `MapPrefab`** (e.g. designer tweaks a tile mesh): unchanged — `PrefabUtility.SavePrefabAsset`. Auto-migration's cleanup pass also goes through this.
- **Runtime**: `LevelData` is loaded by `Resources.Load` as today; `MapData` deserializes as a normal `List<BlockDataEntry>`. No custom `OnAfterDeserialize` / `OnBeforeSerialize` needed.
- **YAML format**: each entry becomes a YAML block in the `.asset` file:
  ```yaml
  MapData:
  - i: 5
    j: 7
    highland: 1
    canSet: 0
    passableType: 0
    deadly: 0
    portalOutI: -1
    portalOutJ: -1
    portalColor: {r: 0, g: 0, b: 0, a: 0}
  - i: 8
    j: 1
    ...
  ```
  Sparse list — most `.asset` files will be ~1/3 the size of today's prefab-MB-heavy maps.

## 9. Files Affected

### 9.1 Modified

| Path                                                                                                  | Change                                                                |
|-------------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------|
| `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelData.cs`                           | Add `iSize`, `jSize`, `MapData` fields                                |
| `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/MapDataManager.cs`                      | Replace `BlockData[,]` with `BlockState[,]`; read from `LevelData`   |
| `Assets/Editor/LevelEditor/LevelDataEditor.cs`                                                       | Replace `PathEditingSection` slot with `MapEditorSection`             |
| `Assets/Editor/LevelEditor/PathEditing/MapCanvasView.cs`                                             | Drive from `LevelData.MapData` instead of `BlockData[,]`              |
| `Assets/Editor/LevelEditor/PathEditing/EditorPathFinder.cs`                                          | Take `BlockState[,]` instead of `BlockData[,]`                        |
| `Assets/Editor/LevelEditor/PathEditing/BlockMapCache.cs`                                             | Load from `LevelData.MapData`; drop prefab dependency                 |
| `Assets/Editor/LevelEditor/PathEditing/EditorPathManipulator.cs`                                      | No change (operates on `(i, j)`)                                      |
| `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/StaticScript/InteractableStatic.cs`   | `BlockData` → `BlockState`                                            |
| `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/MoveScripts/MoveBase.cs`              | `BlockData` → `BlockState`                                            |
| `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs`                    | `BlockData` → `BlockState`, null-check `.material.color`              |
| `Assets/Resources/Prefabs/Monsters/Origin/Wdslm/Scripts/Machine/MachineTalent1.cs`                   | `BlockData` → `BlockState`                                            |
| (existing 49-tile `Map.prefab` for MC-1 and any other map prefab with `BlockData`)                   | Auto-migrated on first save                                           |

### 9.2 Added

| Path                                                                                                  | Purpose                                                                |
|-------------------------------------------------------------------------------------------------------|------------------------------------------------------------------------|
| `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/BlockDataEntry.cs`                     | Serializable struct (§3.1)                                             |
| `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/BlockState.cs`                         | Runtime struct (§3.2)                                                  |
| `Assets/Editor/LevelEditor/Sections/MapEditorSection.cs`                                             | Shared scaffolding (§4.2)                                              |
| `Assets/Editor/LevelEditor/Sections/MapEditTab.cs`                                                   | Map-paint tab (§4.3)                                                   |
| `Assets/Editor/LevelEditor/Sections/PathEditTab.cs`                                                  | Path-paint tab (today's logic, relocated)                              |
| `Assets/Editor/LevelEditor/PathEditing/MapAutoMigrator.cs` (optional, may live in `LevelDataEditor`)  | One-shot migration helper (§6.1)                                       |

### 9.3 Deleted

| Path                                                                                                  | Reason                                                                  |
|-------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------|
| `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/BlockData.cs`                          | No longer used                                                          |
| `Assets/Editor/LevelEditor/Sections/PathEditingSection.cs`                                           | Replaced by `MapEditorSection` + `PathEditTab`                          |

## 10. Implementation Decisions

These choices are decided; they are listed here so the plan/implementation has a single reference and doesn't re-litigate them.

1. **`BlockState` mutability**: store `BlockState[,]` (struct, value-type) and expose `ref BlockState GetPosBlockRef(int, int)`. The `ref` accessor lets mutating call sites (`LevelMessagePanel.ResetCanSetBlockColors`) keep their existing "get block → mutate material.color" pattern with minimal churn.
2. **`MapDataManager` LevelData injection**: add `void AttachLevelData(LevelData levelData)`. The existing `CreateMap(GameObject map)` signature is preserved (so other callers don't break); the manager reads the LevelData it was attached to during `Initialize`.
3. **Right-click on painted cell**: clear all fields of that cell's entry (sets highland/canSet/deadly = false, passableType = 0, portalOutI/J = -1, portalColor = default). Matches an "erase" mental model and is the simpler implementation.
4. **`TempOccupy` field**: dropped from `BlockState`. There are no writers anywhere in the codebase today (`Grep "_tempOccupy\s*="` returns zero hits), and keeping it would just be dead-code dead-weight on the new struct.
5. **Portal mode UX**: ship the **two-click "Set"** variant only (the simpler flow described in §4.3). The hover-then-click "Pick" variant is YAGNI; if users want it later, it's a small extension on top of the two-click flow.
6. **Auto-migration trigger location**: at `OnEnable`, zero-friction. A escape-hatch menu item `Tools/Level Editor/Migrate Old Map` is added in case a level fails to migrate cleanly.