# 关卡路径编辑器 (Path Editor) — 设计文档

**日期**: 2026-06-22
**状态**: 已实施
**作者**: brainstorming session
**上游依赖**: `docs/superpowers/specs/2026-06-22-level-editor-design.md` (Level Editor 主框架)

---

## 1. 目标与非目标

### 1.1 目标

替换 `LevelData.CheckPoints : GameObject[]` 这套"用 prefab 编码路径点"的旧机制,改为**纯数据 + 可视化编辑**的方案:

- 在 LevelEditor 内新增独立的 **PathEditingSection**,可视化编辑所有 `Paths[i].CheckPoints` 和 `WaitTimes`。
- 渲染当前 `LevelData.MapPrefab` 的 BlockData 网格(区分 PassableType 0/1/2/3、Highland、Deadly、Portal 等),让设计师直接看到地图地形。
- checkpoint 在画布上以**半径 = `EntityManager.EntityR`(= 0.25 逻辑单位,直径 0.5 格)** 的圆形绘制,叠加在网格之上。
- 每对相邻 checkpoint 之间画一条 **A\* 实际寻路结果**(复用 `MapDataManager.AStarWayFinding` 的算法,在编辑器侧独立实现为 `EditorPathFinder`)。
- 提供 `moveMethod` 切换(地面/近地/飞行),实时重算 A\* + 重着色 BlockData,直观对比不同移动方式下的路径差异。
- 支持 checkpoint 的新增、删除、点击选中、拖拽改位置(吸附到格点中心)、精确字段编辑(右侧详情面板)。
- 一次性迁移工具:把现有 prefab 数据(`child.position` → `Vector2`、`float.Parse(child.name)` → `WaitTime`)批量转换为 `Paths : PathData[]`。

### 1.2 非目标 (本期不做)

- **BlockData 内部编辑**(修改 `_passableType` / `_highland` / `Deadly` 等字段) — 仍然需要打开 MapPrefab 的 Prefab Mode 编辑。
- **传送门(Portal)配置** — `BlockData.ProtalOutBlock` 仍在 MapPrefab 中编辑;编辑器只展示。
- **`PathData` 字段扩展**(formationWidth / facing / label / portal trigger 等) — 保持最小集(`CheckPoints` + `WaitTimes`),YAGNI。
- **多选 checkpoint / 框选 / 复制粘贴 / 撤销栈定制** — 复用 Unity 默认 Undo,不做自定义批量操作。
- **关卡间路径复制** — 单个 LevelData 内编辑即可。
- **运行时路径烘焙** — `PathDataManager` 在运行时已有的 A\* 预计算逻辑保持不变;新数据格式直接被它消费。
- **可视化编辑 `EnvironmentalControlDevice`** — 仍然在场景里编辑。

### 1.3 范围外影响

- **运行时代码零修改**:`MapDataManager.cs` / `PathDataManager.cs` / `EntityManager.cs` / `BlockData.cs` / `LevelActionManager.cs` 等 runtime 文件**完全不修改**。`LevelData.CheckPoints : GameObject[]` 字段标记 `[Obsolete]` 但保留,`Paths : PathData[]` 是新主字段;`PathDataManager.CreatePaths(GameObject[])` 这个旧 API 保留兼容(走 `[Obsolete]` 路径,只是没数据可读)。
- **现有 LevelData assets 兼容**:迁移前自动检测 `Paths.Length == 0 && CheckPoints.Length > 0`,banner 提示一键迁移。

---

## 2. 数据模型

### 2.1 新增 `PathData` struct

```csharp
[System.Serializable]
public struct PathData
{
    public Vector2[] CheckPoints;   // 长度 N
    public float[]   WaitTimes;     // 长度 N,与 CheckPoints 等长
}
```

**不变量**:
- `CheckPoints.Length == WaitTimes.Length`(允许为 0,validator 在保存/Playtest 时检查 ≥ 2)
- 每个 `CheckPoints[i]` 坐标吸附到格点中心,即 `(i+0.5, j+0.5)` 形态
- `WaitTimes[i] >= 0`

### 2.2 `LevelData` 字段变更

```csharp
// 新增(主字段)
[SerializeField] public PathData[] Paths = new PathData[0];

// 保留(废弃)
[System.Obsolete("Use Paths[] instead. Kept temporarily for legacy asset migration.")]
[SerializeField] public GameObject[] CheckPoints = new GameObject[0];
```

迁移完成后 `CheckPoints` 字段保留在类中但值清空(`Array.Resize(ref CheckPoints, 0)`),`Paths` 写入数据。序列化 YAML 仍包含两个字段(Unity 不会自动清理 `[Obsolete]` 字段)。

---

## 3. 文件结构

```
Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/
└── LevelData.cs                                       # 修改:新增 Paths / PathData / [Obsolete] CheckPoints

Assets/Editor/LevelEditor/
├── Sections/
│   └── PathEditingSection.cs                          # 新增:UI Toolkit section 顶层容器
├── PathEditing/
│   ├── BlockMapCache.cs                               # 新增:PrefabUtility Load + 解析 BlockData[,]
│   ├── EditorPathFinder.cs                            # 新增:静态 A*(与 MapDataManager.AStarWayFinding 行为一致)
│   ├── MapCanvasView.cs                               # 新增:固定 600×400 画布(网格 + painter2D 路径 + checkpoint 圆)
│   ├── CheckpointListView.cs                          # 新增:右侧 checkpoint 列表
│   ├── CheckpointDetailView.cs                        # 新增:右侧详情面板 (Position X/Y / WaitTime)
│   ├── PathEditingState.cs                            # 新增:编辑会话状态
│   ├── EditorPathManipulator.cs                       # 新增:MouseManipulator(点击/拖拽/缩放/平移)
│   └── ViewTransform.cs                               # 新增:world ↔ screen 坐标转换 + 吸附格点
├── Migration/
│   └── EditorPathMigrationTool.cs                     # 新增:[MenuItem] 一次性迁移
└── LevelDataEditor.cs                                 # 修改:追加 PathEditingSection 到 sections 列表

Assets/Editor/LevelEditor/Validation/
└── LevelDataValidator.cs                              # 修改:新增 path 校验规则
```

---

## 4. 入口与触发

### 4.1 编辑器 UI 入口

- **常规入口**:在 Project 窗口选中 `LevelData` 资产 → Inspector 自动渲染 `LevelDataEditor.CreateInspectorGUI()`,其中按顺序追加 6 个 section:`Metadata / References / Economy / WaveTimeline / ActionDetail / PathEditing`。
- **PathEditingSection 默认展开**(因为它的体积最大,且通常需要先看路径再写 Action);其余 5 个默认按现有惯例折叠。

### 4.2 一次性迁移入口

- **菜单**:`Tools / Level Editor / Migrate CheckPoint Prefabs to PathData`(走 `EditorPathMigrationTool` 类)。
- **首次打开 banner**:在 `LevelDataEditor.OnEnable` 检测 `Paths.Length == 0 && CheckPoints.Length > 0`,在 ValidationBar 上方插入一条**黄色 banner** "此 LevelData 包含旧 prefab 路径 (N 条),点击迁移" + `[迁移]` 按钮 + `[忽略]` 按钮。
- 迁移成功后 `Paths` 写入数据、`CheckPoints` 清空、banner 消失。

---

## 5. 关键模块设计

### 5.1 `BlockMapCache`

```csharp
public sealed class BlockMapCache : IDisposable
{
    public BlockData[,] Blocks;        // [iSize, jSize],i = row, j = col
    public int ISize, JSize;
    public float EntityR;

    public static BlockMapCache Load(GameObject mapPrefab)
    {
        var contents = PrefabUtility.LoadPrefabContents(mapPrefab.name);
        var cache = new BlockMapCache();
        try
        {
            cache.ParseBlocks(contents);
            cache.EntityR = EntityManager.EntityR; // 缓存,避免反复访问静态字段
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
        return cache;
    }

    void ParseBlocks(GameObject mapRoot);    // 遍历 children,blocks[i,j] = BlockData 组件
    public void Dispose();                  // 清空 Blocks 引用(让 GC 回收)
}
```

**生命周期**:`LevelDataEditor.OnEnable` 创建,`OnDisable` 调 `Dispose`。`Undo.undoRedoPerformed` 不重新 Load(只刷新 `Paths`)。

### 5.2 `EditorPathFinder`

```csharp
public static class EditorPathFinder
{
    public static MoveParameters[] AStar(
        BlockData[,] blocks, int iSize, int jSize,
        Vector2 start, Vector2 end,
        float entityR, int moveMethod);
}
```

**实现策略**:把 `MapDataManager.AStarWayFinding` 的算法体 1:1 复制为静态方法,操作 `BlockData[,]` 参数而非私有 `graph` 字段。

- `AStarProperty` / `HeapEntry` 私有 struct 复制为 `EditorPathFinder` 内私有 struct
- `HeapPush` / `HeapPop` / `HeapClear` 直接复用
- `FindNewFrontier` 内访问 `graph[i, j].Passable` 等改为 `blocks[i, j].PassableType <= moveMethod && !blocks[i, j].TempOccupy`
- 访问 `graph[i, j].portalEnter` 改为 `blocks[i, j].ProtalOutBlock != null`
- `graph[ti, tj].plotPos = BlockDataMatrix[ti, tj].transform.position` 改为读取 `blocks[ti, tj].transform.position`(但 blocks 是 detached,无 transform;此处缓存 `plotPos : Vector2` 在 `AStarProperty` 内)
- `IsBlocked` / `FirstBlockLine` / `BaseOnBlockNewPoint` / `CorrectTmpPositions` 全部复制
- 返回 `MoveParameters[]`,与 runtime 同形

**行为对等测试**(NUnit,Editor 程序集):在多个 (start, end, moveMethod, entityR, blocks 布局) 组合上,断言 `EditorPathFinder.AStar(...)` 与 runtime `MapDataManager.AStarWayFinding(...)` 返回的 `targetPosition[]` 完全一致(顺序、数量、坐标)。

### 5.3 `ViewTransform`

```csharp
public struct ViewTransform
{
    public Vector2 Offset;     // 视口左上角对应的 world 坐标
    public float Zoom;         // 1.0 = fit 整图

    public const float CanvasSize = 600f;   // 画布固定 600×400,后续可改
    public const float CanvasHeight = 400f;

    public static ViewTransform Fit(BlockMapCache cache)
    {
        float fitZoom = Mathf.Min(CanvasSize / cache.JSize, CanvasHeight / cache.ISize);
        return new ViewTransform
        {
            Offset = Vector2.zero,
            Zoom = fitZoom
        };
    }

    public Vector2 WorldToScreen(Vector2 world)
    {
        float unitX = CanvasSize / JSize;
        float unitY = CanvasHeight / ISize;
        return new Vector2(
            (world.x - Offset.x) * Zoom * unitX,
            (world.y - Offset.y) * Zoom * unitY);
    }

    public Vector2 ScreenToWorld(Vector2 screen)
    {
        float unitX = CanvasSize / JSize;
        float unitY = CanvasHeight / ISize;
        return new Vector2(
            screen.x / (Zoom * unitX) + Offset.x,
            screen.y / (Zoom * unitY) + Offset.y);
    }

    public Vector2 SnapToGrid(Vector2 world)
        => new Vector2(Mathf.Round(world.x) + 0.5f, Mathf.Round(world.y) + 0.5f);
}
```

### 5.4 `PathEditingState`

```csharp
public sealed class PathEditingState
{
    public int SelectedPathIdx;             // 0..Paths.Length-1
    public int SelectedCheckpointIdx = -1;  // -1 = 未选
    public int MoveMethod = 1;              // 0=地面 / 1=近地 / 2=飞行
    public ViewTransform View;

    public event Action StateChanged;       // UI 订阅以重绘
}
```

`PathEditingSection` 持有单例,UI 元素订阅 `StateChanged` 触发 `MarkDirtyRepaint()`。

### 5.5 `MapCanvasView` (核心 UI 容器)

继承 `VisualElement`,固定 600×400 像素布局:

```
┌────────────────────────────────────────────┐
│ <generateVisualContent>                    │
│   - BlockData 网格 (rect 阵列)             │
│   - A* 路径 (polyline via painter2D)       │
│ </generateVisualContent>                   │
│                                            │
│  [checkpoint 圆 #0] (绝对定位 VisualElement)│
│  [checkpoint 圆 #1]                        │
│  ...                                       │
│                                            │
│  bottom-right:操作提示文字                 │
│  bottom-left: cursor world 坐标           │
└────────────────────────────────────────────┘
```

**绘制顺序**:
1. `generateVisualContent`:画 BlockData 网格(每个 block 一个 `<rect>`)+ Highland/CanSet 描边 + Deadly 红底 + Portal 描边
2. `generateVisualContent`:在每对相邻 checkpoint 上调 `EditorPathFinder.AStar(...)`,把所有 `targetPosition` 用 `painter2D.LineTo` 连起来,绿色 stroke
3. 每个 checkpoint 是一个绝对定位的子 `VisualElement`(圆形,`border-radius: 50%`,背景半透明绿,描边实绿;选中时改黄色 + box-shadow 发光)

**绘制层级**:UI Toolkit 中子 VisualElement 自动绘制于父 `generateVisualContent` 之上,因此 checkpoint 圆形天然位于 BlockData 网格和 A* 折线之上,无需手动控制 z-index / `BringToFront()`。

### 5.6 `EditorPathManipulator` (MouseManipulator)

订阅以下事件:

| 事件 | 行为 |
|------|------|
| `MouseDownEvent` (左键,空白处) | 计算吸附后坐标 → `serializedObject` 追加新 Vector2 + 等长 WaitTimes 数组扩展 → `RecordUndo` → `ApplyModifiedProperties` → 设为选中 |
| `MouseDownEvent` (左键,命中 checkpoint) | `state.SelectedCheckpointIdx = i` → 进入"拖动模式"(manipulator 临时缓存起始偏移) |
| `MouseMove` (拖动模式) | 计算吸附后坐标 → **只更新圆形 VisualElement 的 `style.left/top`,不写 SerializedProperty** |
| `MouseUpEvent` (拖动模式) | 一次性 `prop.vector2Value = snappedPos; ApplyModifiedProperties()`(单次 Undo) |
| `WheelEvent` | 以鼠标位置为中心缩放,clamp `[0.25, 4.0]`;更新 `state.View.Zoom` + `state.View.Offset` |
| `MouseDownEvent` (中键) + `MouseDrag` | 平移视口,更新 `state.View.Offset` |
| `MouseMoveEvent`(无按键) | 更新 bottom-left 的 cursor world 坐标文字 |

### 5.7 `PathEditingSection` (顶层容器)

```csharp
public static class PathEditingSection
{
    public static VisualElement Build(SerializedObject so, PathEditingState state, BlockMapCache cache)
    {
        var root = new VisualElement();
        root.style.backgroundColor = new Color(0.176f, 0.176f, 0.188f);  // section bg
        root.style.paddingTop = 8; root.style.paddingBottom = 8;
        // ...

        // Row 1: Path picker (PopupField<string> + 新建/删除)
        // Row 2: Toolbar (新建点 / 删除点 / moveMethod 切换 / 重置视图)
        // Row 3: Split: 左 MapCanvasView(600×400),右 CheckpointListView + CheckpointDetailView

        root.Add(PathPicker.Build(so, state));
        root.Add(Toolbar.Build(state, cache, root));
        var split = new VisualElement();
        split.style.flexDirection = FlexDirection.Row;
        split.Add(MapCanvasView.Build(so, state, cache));
        var rightCol = new VisualElement();
        rightCol.Add(CheckpointListView.Build(so, state));
        rightCol.Add(CheckpointDetailView.Build(so, state));
        split.Add(rightCol);
        root.Add(split);
        return root;
    }
}
```

### 5.8 路径列表 / Checkpoint 列表 / 详情面板

**Path picker**(`PopupField<string>`):选项 = `Paths` 数组的索引 + 简短摘要,例如 `"Path 2: 5 checkpoints, 21.4 units long"`。变更触发 `state.SelectedPathIdx = newIdx`,UI 重绘。

**Checkpoint 列表**:简单 `VisualElement` 垂直列表,每项显示 `#i (x.x, y.y) wait Xs`。点击同步选中。

**Checkpoint 详情**:`Vector2Field`(Position)+ `FloatField`(WaitTime),走标准 `SerializedObject` + `TrackPropertyValue` 双向同步。

---

## 6. A* 路径渲染策略

对 `Paths[selectedPathIdx].CheckPoints` 的每对相邻点 `(P_i, P_{i+1})`:

1. 调 `EditorPathFinder.AStar(blocks, P_i, P_{i+1}, entityR, moveMethod)`
2. 拿到 `MoveParameters[] result`
3. 若返回 null:在两点之间画**红色虚线**(无法到达)
4. 否则把所有 `result[j].targetPosition` 用 `painter2D.LineTo` 串联,最后 `Stroke()`

checkpoint 之间的多条折线在画布上**串联绘制**,视觉上是一条连续的 A* 路径(中间有可能在 cp 上断开,因为 cp 之间各自独立寻路)。

---

## 7. BlockData 视觉编码

颜色映射(与现有 `LevelEditorStyles.uss` 风格一致):

| 条件 | 颜色 / 样式 |
|------|-------------|
| `PassableType > moveMethod`(不可走) | `rgb(40, 40, 40)` 实底 |
| `PassableType == 0`(地面可走) | `rgb(60, 65, 55)` |
| `PassableType == 1`(近地可走) | `rgb(45, 60, 90)` |
| `PassableType == 2`(飞行可走) | `rgb(50, 80, 90)` |
| `Deadly == true` | `rgb(120, 60, 60)` 覆盖 |
| `Highland == true` | `inset 0 0 0 2px rgb(180, 140, 60)` 黄色内框 |
| `CanSet == true` | `inset 0 0 0 2px rgb(140, 200, 180)` 青色内框 |
| `ProtalOutBlock != null` | `inset 0 0 0 2px` 用 `ProtalColor` 描边 |

`moveMethod` 切换时整张网格重画一次。

---

## 8. 校验 (`LevelDataValidator` 新增规则)

```csharp
// 新增规则:
ValidatePathCount(LD);            // Paths.Length > 0 → warning "无路径"
ValidatePathHasTwoCheckpoints(LD); // 每个 Paths[i].CheckPoints.Length < 2 → error "Path N 至少需要 2 个 checkpoint"
ValidateCheckPointInBounds(LD);   // 每个 CheckPoints[k] 必须落在 [0, ISize) × [0, JSize) → error
ValidateCheckPointUniqueness(LD); // 同 path 内 CheckPoints 不能重复 → warning
```

校验在 `LevelDataValidator.Validate(LD)` 末尾追加,跟现有规则一起输出。

---

## 9. 一次性迁移工具

```csharp
public static class EditorPathMigrationTool
{
    [MenuItem("Tools/Level Editor/Migrate CheckPoint Prefabs to PathData")]
    public static void MigrateAll();

    public static void MigrateAsset(LevelData ld);  // 公开,供 banner 按钮调用

    // 实现:
    // 1. 检查 Paths.Length == 0 && CheckPoints.Length > 0,否则跳过
    // 2. 对每个 prefab in CheckPoints:
    //    解析 child.position → CheckPoints[],float.Parse(child.name) → WaitTimes[]
    //    注意:float.Parse 失败时 fallback 到 0f,记录 warning
    // 3. 写入 ld.Paths,清空 ld.CheckPoints(Resize 0)
    // 4. EditorUtility.SetDirty + AssetDatabase.SaveAssetIfDirty
}
```

Banner 检测逻辑:
```csharp
// LevelDataEditor.OnEnable 末尾:
if (target is LevelData ld && ld.Paths.Length == 0 && ld.CheckPoints.Length > 0)
{
    root.Add(MigrationBanner.Build(so, ld));
}
```

---

## 10. 错误处理 / 边界

- **MapPrefab 为空**:`BlockMapCache.Load(null)` 抛 ArgumentException;PathEditingSection 显示红色提示 "MapPrefab 未指定,无法可视化路径",其他子组件(列表、详情)仍可用,只是画布空。
- **BlockData 缺失**:`BlockMapCache.ParseBlocks` 跳过没有 `BlockData` 组件的 children;validator 记录 warning "MapPrefab 子物体缺少 BlockData"。
- **A\* 找不到路径**:两 cp 之间画红色虚线,validator 在保存时给出 error "Path[i] 段 j→j+1 不可达"。
- **拖出地图外**:`SnapToGrid` 后如果落在 `[-0.5, ISize+0.5)` 外,validator 报错;编辑器不阻止(允许越界编辑,validator 兜底)。
- **Undo/Redo**:`SerializedObject` 自动处理;checkpoint 列表 / 详情 / 画布都通过 `TrackPropertyValue` 订阅同步。
- **资产切换**:`OnDisable` Dispose cache,`CreateInspectorGUI` 检测 `target` 变化,如有变化 Dispose + 重新 Load。

---

## 11. 测试策略

### 11.1 单元测试 (NUnit,Editor 程序集)

**`EditorPathFinderTests`**:
- `AStar_OnStraightOpenPath_ReturnsTwoPoints`:start = end 同格,直接返回两点
- `AStar_AroundObstacle_MatchesRuntime`:在固定 5×5 块布局上,对比 `EditorPathFinder.AStar` 与 `MapDataManager.AStarWayFinding` 的输出
- `AStar_MoveMethodFiltersBlocks`:moveMethod=0 时 PassableType=1 的块视为不可走
- `AStar_Unreachable_ReturnsNull`:目标被封死,返回 null
- `AStar_PortalTeleport_JumpsCorrectly`:portal 块之间正确跳转

**`ViewTransformTests`**:
- `WorldToScreen_RoundTripsWithScreenToWorld`
- `SnapToGrid_CenterOnHalfInteger`
- `Fit_MakesLargestDimensionTouchCanvas`

### 11.2 手动测试清单

- [ ] 新建 LevelData + 新建 Path + 新建 3 个 checkpoint,验证 A* 路径正确
- [ ] 拖动 checkpoint 跨过 PassableType=3 块,验证 A* 实时重算 + 红色虚线
- [ ] 切换 moveMethod,验证 BlockData 重新着色 + A\* 路径改变
- [ ] 滚轮缩放以鼠标位置为中心
- [ ] 中键平移视口
- [ ] 选中 checkpoint,详情面板字段编辑,验证圆点和列表同步
- [ ] 删到 1 个 checkpoint,validator 出 error
- [ ] 拖出地图外,validator 出 error
- [ ] Undo / Redo checkpoint 新增 / 移动 / 删除
- [ ] 旧 LevelData(只有 CheckPoints prefab),验证 banner 出现 + 一键迁移成功
- [ ] 切换到别的 LevelData,验证 cache 重新 Load(不会内存泄漏)
- [ ] PathDataManager.CreatePaths(ld.CheckPoints) 在迁移后传入空数组,运行时不崩

---

## 12. 实施任务(概要)

完整的逐步 plan 将在 `writing-plans` skill 中产出。概要如下:

1. **数据模型**:`LevelData.cs` 新增 `Paths / PathData`,`CheckPoints` 标 `[Obsolete]`。
2. **`BlockMapCache`**:`PrefabUtility.LoadPrefabContents` + 解析 BlockData + 缓存。
3. **`EditorPathFinder`**:`MapDataManager.AStarWayFinding` 复制为静态版本 + 行为对等测试。
4. **`ViewTransform` + 坐标工具**:`WorldToScreen` / `ScreenToWorld` / `SnapToGrid`。
5. **`MapCanvasView`**:`generateVisualContent` 绘制 BlockData 网格 + A* 路径 + checkpoint 圆形。
6. **`EditorPathManipulator`**:点击新增 / 拖动 / 滚轮缩放 / 中键平移。
7. **`CheckpointListView` + `CheckpointDetailView`**:列表 + 详情,`SerializedObject` 双向同步。
8. **`PathEditingSection`**:顶层装配,接入 `LevelDataEditor`。
9. **`EditorPathMigrationTool`**:菜单迁移 + banner 检测。
10. **`LevelDataValidator`** 新增路径规则。
11. **测试 + 手动验证清单**。
12. **跑通 Playtest**:用新 `Paths` 数据完整跑一个 LevelData。

---

## 13. 风险与缓解

| 风险 | 缓解 |
|------|------|
| `EditorPathFinder` 与 runtime 行为偏离 | 单元测试覆盖 N 个典型场景 + 一个 portal 场景;偏离则替换 A* 直到一致 |
| `PrefabUtility.LoadPrefabContents` 在某些 prefab 上失败(嵌套 prefab、missing script) | `BlockMapCache.Load` 抛清晰异常,UI 提示;validator 不阻断但记录 warning |
| 旧 prefab 的 `child.name` 不是合法 float(老数据可能含非数字) | `float.TryParse` fallback 0f + warning 列表 |
| `EntityManager.EntityR` 是 static,编辑器访问它要求 GameData assembly 已加载 | 编辑器程序集已有 GameData 引用;无需特殊处理 |
| `Paths` 序列化格式变更(YAML 结构) | Unity 自动迁移;无破坏 |
| A\* 在大地图(50×50)上每次拖动都重算会卡 | 只在 cp 增删改 / moveMethod 切换时重算;拖动过程中不重算(松手才算) |
| 旧 prefab 文件仍在 Resources 下但不再引用 | 不主动删除,等关卡设计师确认无引用后手动清理(避免破坏) |
| `LevelData` 是 ScriptableObject,Undo 栈与 prefab asset 联动 | 标准 Undo 行为,无需特殊处理 |

---

## 14. 不在本期

明确**不做**的相邻功能,留作后续 spec:

- 选中 `MapPrefab` 后进入 Prefab Mode 编辑 BlockData 字段
- PathData 字段扩展(formationWidth / facing / portal trigger 等)
- `EnvironmentalControlDevice` 可视化编辑
- 关卡间路径模板复用(把一条 Path 保存为 asset 供多个 LevelData 引用)
- 路径批量操作(框选 / 多选 / 对齐 / 分布)
- 3D 预览(把 MapPrefab 实例化到临时场景,从 Scene 视图查看)