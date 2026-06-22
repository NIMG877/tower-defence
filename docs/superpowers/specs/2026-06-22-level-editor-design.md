# 关卡编辑器 (Level Editor) — 设计文档

**日期**: 2026-06-22
**状态**: Draft (待用户审阅)
**作者**: brainstorming session

---

## 1. 目标与非目标

### 1.1 目标

为 `LevelData` ScriptableObject 提供一个完整的可视化编辑器,使得关卡制作不再依赖手填 Inspector。编辑器须能:

- 填写 `LevelData` 中**所有字段**(元数据、引用、波次、经济)。
- 可视化编辑 `Waves[]` 嵌套 `Actions[]`,支持时间线浏览、条件字段显隐、增删重排。
- 编辑期间提供静态校验(索引越界、ID 缺失、空 Wave、EntityPrefabSerial 越界等)。
- 一键 Playtest:复用现有测试场景,自动构建(若缺失)并进入 Play 模式跑当前 LevelData。

### 1.2 非目标 (本期不做)

- 地图方块绘制、地形编辑、BlockData 内部编辑。
- 传送门/路径连接的可视化编辑(Portal 编辑属于 MapPrefab 内部编辑,本期排除)。
- 选中 `MapPrefab` 资产后进入 Prefab Mode 的扩展(后续版本)。
- `EnvironmentalControlDevice` 内部组件编辑。
- `LevelCollectionData` 的批量管理(可作为后续增强)。
- 多语言/i18n。

### 1.3 范围外影响

- 运行时代码(`LevelResourceSharing`、`MapDataManager`、`LevelActionManager` 等)**零修改**。
- 现有 LevelData assets 100% 兼容(新编辑器只是更友好的输入界面,序列化格式不变)。

---

## 2. 入口与触发

- 在 `Assets/Editor/LevelEditor/` 下实现 `[CustomEditor(typeof(LevelData))]` 的 `LevelDataEditor`。
- Project 窗口里点选任意 `LevelData` 资产,Inspector 自动渲染本编辑器(默认 Inspector 被替换)。
- `LevelCollectionData` **不**触发本编辑器(它有自己的字段,不在本期范围)。

---

## 3. 文件结构

```
Assets/Editor/LevelEditor/
├── LevelDataEditor.cs              # [CustomEditor(typeof(LevelData))] 入口;CreateInspectorGUI() 入口
├── Sections/                       # 4 个分节,各负责一段 VisualElement 子树
│   ├── MetadataSection.cs          # 元数据 (Name/Code/Description/Camera/Cutscene)
│   ├── ReferencesSection.cs        # 引用 (MapPrefab/EnvControl/CheckPoints/WaveEntityPrefabIDs)
│   ├── EconomySection.cs           # 经济 (LevelHp/Cost0/MaxCost/CanSetNum/CostRecoverSpeed)
│   ├── WaveTimelineSection.cs      # 横向时间线 + 卡片选中事件
│   └── ActionDetailSection.cs      # 选中 Action 的内联条件字段 (CommandType 0-6 显隐)
│
├── Validation/
│   ├── LevelDataValidator.cs       # 纯函数校验(无 UI 依赖,可单测)
│   └── ValidationBar.cs            # 底部状态条
│
├── Playtest/
│   ├── PlaytestLauncher.cs         # 切场景 + 赋 LD + EnterPlaymode
│   ├── LevelTestSceneBuilder.cs    # 首次使用自动创建 LevelTest.unity
│   └── LevelTestStarter.cs         # 测试场景里挂的 MonoBehaviour
│
├── LevelEditorStyles.uss           # 共享样式
└── Tests/
    └── LevelDataValidatorTests.cs  # 纯函数校验的单元测试 (Unity Test Framework)
```

> 沿用 Unity 的 `Assets/Editor/` 特殊目录,自动归到 `Assembly-CSharp-Editor`,**不**新建 .asmdef。运行时调用编辑器代码会被编译器拒绝(单向可见性)。

---

## 4. 组件职责

### 4.1 `LevelDataEditor : Editor`

- 在 `OnEnable` 里 `serializedObject` 已经可用(基类提供)。
- Override `CreateInspectorGUI()` 返回一个 `VisualElement`,内含:
  - Header(关卡名 + 校验徽章)
  - 4 个 Tabs / 4 个 Sections(由 4 个 `Section.BuildVisualElement(SerializedObject, Action<waveIdx, actionIdx> onActionSelected)` 拼接)
  - `ValidationBar`(底部)
- Override `RequiresConstantRepaint()` 在选中 Action 时返回 true(选中状态高亮需持续刷新)。
- 监听 `Undo.undoRedoPerformed` 事件,重建 VisualElement(回滚后数据变了)。

### 4.2 Sections

每个 Section 都有:
```csharp
public static VisualElement BuildVisualElement(SerializedObject so, Action<...> ...);
```

- **MetadataSection**:5 个 PropertyField(Name/Code/Description/CameraSize/CameraPos/CutToLevelTexture),全部用 `BindProperty`。
- **ReferencesSection**:4 个 ObjectField/Array 控件。
  - `MapPrefab`、`EnvironmentalControlDevice`:`ObjectField` (type=GameObject)
  - `CheckPoints`:List with drag-drop GameObject,带 `+ 添加` 按钮
  - `WaveEntityPrefabIDs`:List with drag-drop `EntityID` ScriptableObject,带 `+ 添加` 按钮;支持右键删除
  - 数组可视化用 UI Toolkit 的 `ListView`(Unity 2022 已稳定)
- **EconomySection**:5 个 PropertyField。
- **WaveTimelineSection**:
  - 遍历 `Waves.Array`,每条 Wave 一行时间线
  - 每条 Action 渲染为卡片,宽度按 `GapFromLastAction` 比例计算
  - 卡片点击 → 派发 `ActionSelected` 事件
  - Wave 右侧有 [+ 新增 Action] / [+ 新增 Wave] 按钮
  - Action 卡片右键菜单:复制 / 删除
- **ActionDetailSection**:
  - 接收 `(waveIdx, actionIdx)`,绑定到对应路径的 `SerializedProperty`
  - 顶部固定:CommandType、GapFromLastAction、OnBeforeAction(用 IMGUIContainer 包 IMGUI)
  - 下方按 CommandType 显隐:
    - 0/1:EntityPrefabSerial、Camp、OnActionRepeat(IMGUIContainer)
    - 0/2/3/4:PathSerial
    - 0:GapsFromLastRepeat、ModifyAttributes(勾选后展开 ModifyLevelHpConsume/ModifyPrimary/ModifyCountOperate)
    - 1:Destination、Orientation
    - 5:HeadImage、Content、DurationTime
    - 6:Contents(可编辑 ListView)
  - 底部操作:复制 / 删除

### 4.3 `LevelDataValidator`

纯静态类,签名:
```csharp
public static class LevelDataValidator {
    public static List<ValidationIssue> Validate(LevelData data);
}
```

校验规则(本期):

| # | 规则 | 严重度 |
|---|------|--------|
| 1 | `Waves` 为 null 或空 | Error |
| 2 | 任一 `Wave.Actions` 为 null 或空 | Warning |
| 3 | `WaveEntityPrefabIDs` 为 null 或空 | Error |
| 4 | `EntityPrefabSerial` 越界 `WaveEntityPrefabIDs.Length` | Error |
| 5 | `PathSerial` 越界 `LD.Paths` (待定:Paths 是否在 LevelData 内?需查) | Warning |
| 6 | `LevelHp <= 0` | Error |
| 7 | `MaxCost < Cost0` | Error |
| 8 | `MapPrefab == null` | Error |
| 9 | `CutToLevelTexture == null` | Warning |
| 10 | `CameraSize <= 0` | Error |

返回 `ValidationIssue` (severity + path + message),`ValidationBar` 按级别显示颜色和条数。

> 注:规则 5 的 `Paths` 字段在当前 `LevelData` 里**没有**,Action 里只有 `PathSerial` 这个 int 索引。本期不做 Path 编辑,所以规则 5 暂不实现(占位,后续版本补)。

### 4.4 `ValidationBar`

- 固定在 Inspector 底部。
- 显示 ✓ 绿/⚠ 黄/✗ 红 + 数量 + 上一条错误简述。
- 点击展开 Popup,列出全部 issue(可点击跳到对应字段)。
- 订阅 SerializedObject 的 `ApplyModifiedProperties` 后回调,实时刷新。

### 4.5 `PlaytestLauncher`

流程:

1. 校验当前 `LevelData`。若 Error 数量 > 0,弹 Dialog "N 个错误,是否仍要 Playtest?" → 用户选择继续或取消。
2. 调 `LevelTestSceneBuilder.EnsureScene()` 确认 `Assets/Scenes/LevelTest.unity` 存在,缺失则创建。
3. `EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()`(保存当前打开场景)
4. `EditorSceneManager.OpenScene("Assets/Scenes/LevelTest.unity", OpenSceneMode.Single)`
5. 找到 `LevelTestStarter` 实例,设 `LevelData` 字段
6. `EditorApplication.EnterPlaymode()`

### 4.6 `LevelTestSceneBuilder`

- 路径:`Assets/Scenes/LevelTest.unity`(常量)
- 检查存在性:不存在则:
  1. `EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)`
  2. 创建 GameObject: `"LM"`(空 Transform)、`"MCam"`(Camera,orthographic)、`"UICam"`(Camera,orthographic,depth=1)
  3. 创建 GameObject: `"LevelTestStarter"` 挂 `LevelTestStarter` 组件
  4. `EditorSceneManager.SaveScene(scene, "Assets/Scenes/LevelTest.unity")`

### 4.7 `LevelTestStarter : MonoBehaviour`

- 公开字段 `public LevelData LevelDataToPlay;`
- `Awake()` 里:
  ```csharp
  if (LevelDataToPlay != null) {
      LevelResourceSharing.LD = LevelDataToPlay;
      LevelResourceSharing.LevelInitialize();
      LevelResourceSharing.LevelStart();
  }
  ```
- 退出 Play 模式时(`OnDisable` 调 `LevelEnd()`),清理环境。

---

## 5. 数据流

```
┌────────────────────────────────────────┐
│ LevelData (ScriptableObject)            │
│ - serializedObject (Editor 持有)        │
│ - SerializedProperty (字段路径)         │
└────────────────────────────────────────┘
       ↑              ↓ Bind / PropertyField
       │              │
       │   ┌──────────┴──────────┐
       │   │   VisualElement     │
       │   │  (Inspector 根)     │
       │   └──────────┬──────────┘
       │              │ Bind
       │   ┌──────────┴──────────┐
       │   │ 4 Sections          │
       │   │ - Metadata          │
       │   │ - References        │
       │   │ - WaveTimeline      │──→ ActionSelected 事件 ──→ ActionDetail
       │   │ - Economy           │
       │   └──────────┬──────────┘
       │              │ ApplyModifiedProperties 之后
       │   ┌──────────┴──────────┐
       │   │ LevelDataValidator  │
       │   │ (纯函数)            │
       │   └──────────┬──────────┘
       │              │
       │   ┌──────────┴──────────┐
       └───│ ValidationBar       │
           └─────────────────────┘
```

**关键点**:

1. **所有原生字段走 PropertyField.BindProperty** — Unity 自动处理 Undo/Redo/脏标记/Save。
2. **UnityEvent 字段**(`OnBeforeAction`、`OnActionRepeat`)用 `IMGUIContainer` 包一层 IMGUI `EditorGUILayout.PropertyField`。这是编辑器里**唯一一个不是纯 UI Toolkit** 的地方。
3. **结构变更**(增/删/重排 Wave、Action):`Undo.RecordObject(target, "Add Action")` → 改 SerializedProperty → `serializedObject.ApplyModifiedProperties()` → 调时间线 `MarkDirtyRepaint()` 重建。
4. **选中 Action** 是纯 UI 状态,不进 SerializedObject。每次时间线 `ActionSelected` 事件,ActionDetail 重新绑对应路径的子 SerializedProperty。
5. **Undo.undoRedoPerformed** 触发后,Editor 基类会调 `CreateInspectorGUI()` 重建(或我们手动 `serializedObject.Update()`,然后各 Section 监听自己的 Property 变化)。
6. **Playtest 流程**只读不写:不修改 LevelData,只复制引用。

---

## 6. 错误处理

### 6.1 编辑期间

- 校验在每次 `ApplyModifiedProperties` 后跑,延迟 1 帧(防抖)避免高频。
- 校验失败不阻断编辑(允许存"半成品"),只标红 + 状态条计数。
- 字段级错误:对应 PropertyField 边框变红 + tooltip 显示问题描述。
- 顶部校验徽章 = 错误总数。点开看详细列表。

### 6.2 Playtest 期间

- Error > 0 时弹 Dialog 让用户选择"继续"或"取消"。
- 测试场景创建失败(权限/路径冲突)→ 弹错误 Dialog,LogError。
- `EnterPlaymode` 失败 → 还原上一场景。

### 6.3 加载异常

- 选中 `LevelData` 但内部 `Waves` 数组被外部破坏(例如旧版本升级)→ 校验规则 1 触发,编辑器仍然可打开,UI 优雅降级(空数组可继续编辑)。

---

## 7. 测试策略

### 7.1 单元测试 (Unity Test Framework)

- `Tests/LevelDataValidatorTests.cs`:每条规则至少 1 个通过用例 + 1 个失败用例。
- 不用 Edit Mode 测试框架时,这些测试都是 plain C#。

### 7.2 手工 smoke test

按这个 checklist 跑一次:
1. 创建新 LevelData → 各 Tab 都能打开。
2. 添加 3 条 Wave,每条 4 个 Action,CommandType 各不同。
3. 故意把 EntityPrefabSerial 设到越界值,确认状态条变红。
4. 点 Playtest → 测试场景自动建好(或已存在) → 进入 Play 跑完第一波。
5. 退出 Play → 回到 LevelData 编辑器,所有更改保留。

### 7.3 不在范围

- 性能测试(本期数据量小,几百条 Action 不会卡)。
- E2E 自动化(用 Playwright 之类,Unity Editor 自动化成本过高,不值得)。

---

## 8. 已知限制 / 后续工作

1. **路径编辑缺失** — Action 里有 `PathSerial` 索引,但 LevelData 没有 `Paths` 字段。后续要么新增 Paths 字段(数据结构调整),要么在 `MapPrefab` 里编辑。
2. **多选 / 批量操作** — ActionDetail 当前只支持编辑单条 Action。
3. **撤销栈可视化** — Unity 自带 Undo 栈,本编辑器不额外提供 UI。
4. **Asset 重复检查** — 两个 LevelData 引用同一个 MapPrefab 时不警告(可能合法,也可能误用,后续按需加)。
5. **i18n** — 所有 UI 文案中文硬编码。
6. **配色 / 主题** — 跟 Unity 默认深色主题对齐,不提供 light theme 适配。
7. **Inspector 在 Project 窗口外不可用** — 选了 LevelData 才能编辑。如果要做"独立窗口"入口,留待后续。

---

## 9. 实施顺序(概览)

具体实施计划会在 spec 通过后用 `writing-plans` 技能生成。本节是粗略顺序:

1. **骨架**:`LevelDataEditor` + 4 个空 Sections → 验证 CustomEditor 触发
2. **MetadataSection** + **ReferencesSection** + **EconomySection** — 简单 PropertyField
3. **LevelDataValidator** + 单元测试
4. **ValidationBar** 接线
5. **ActionDetailSection** — 条件显隐 + IMGUIContainer for UnityEvent
6. **WaveTimelineSection** — 卡片 + 选中事件
7. **LevelTestSceneBuilder** + **LevelTestStarter**
8. **PlaytestLauncher** 接线
9. **USS 样式调优** + 联调
10. **smoke test checklist**

---

## 10. 待用户确认事项

- [ ] 规则 5(PathSerial 校验)暂不实现 — 因为 LevelData 里没有 Paths 字段,确认？
- [ ] 测试场景路径固定为 `Assets/Scenes/LevelTest.unity` — 接受?
- [ ] UnityEvent 字段用 IMGUIContainer 包一层(非纯 UI Toolkit)— 接受?
- [ ] Playtest 前 Error 弹 Dialog 但不强制修复 — 接受?
- [ ] 文件结构(8 个 .cs + 1 个 .uss + 1 个测试)— 接受?

---

**审阅 checklist**:
- [ ] 第 1 节目标/非目标清晰
- [ ] 第 3 节文件结构合理
- [ ] 第 4 节组件职责明确
- [ ] 第 4.3 校验规则完整(规则 5 的占位可接受)
- [ ] 第 5 节数据流正确
- [ ] 第 6 节错误处理覆盖关键场景
- [ ] 第 7 节测试策略合理
- [ ] 第 8 节已知限制可接受
- [ ] 第 10 节所有问题已答
