# Level Editor 使用说明

## 入口

在 Project 窗口选中任意 `LevelData` 资产，Inspector 自动呈现本编辑器（UI Toolkit 自定义 Inspector，`Assets/Editor/LevelEditor/LevelDataEditor.cs`）。

## Section 总览（自上而下）

1. **元数据**（MetadataSection）：关卡名/代码/描述/相机/转场贴图
2. **引用**（ReferencesSection）：MapPrefab（可拖拽引用或一键生成）/ EnvironmentalControlDevice
3. **经济**（EconomySection）：HP/成本/上限/部署上限/恢复速度
4. **波次时间线**（WaveTimelineSection）：多 Wave × 多 Track
5. **地图编辑器**（MapEditorSection）：Tab 切换"地图"（方块/传送门）与"路径"
6. **底部状态条**：左侧校验条，右侧 ▶ Playtest 按钮

## 波次时间线

结构为 `Wave → Track[] → Action[]`（`LevelActions.cs`）。每个 Wave 渲染为一个圆角块，头部标签 `Wave N · M tracks`；每条 Track 是一行：左侧 × 删除按钮 + 轨道头（Name / 激活开关 / + / ×）+ 右侧横向时间轴。Wave 块顶部有一条与所有 Track 横向滚动同步的共享刻度尺。

### 顶层与 Wave 级操作

- **+ 新增 Wave**：区块底部全宽按钮。新 Wave 的 Tracks 数组为空（0 条轨道）。
- **× 删除 Wave**：每个 Wave 块刻度尺行最左侧，点击弹确认框。
- **缩放 slider**（`缩放 N.Nx`）：与 + 新增 Track 同一行，范围 0.1–5.0，**每个 Wave 独立**，只影响本 Wave 的像素密度（24 px/s × 缩放）。

### 每条轨道的操作

- **×**（最左）：删除本 Track，弹确认框。
- **Name**：行内文本框，可编辑。新建 Track 默认名 `Track N`（N 为数组下标），`Locked=false`，Actions 为空。
- **激活开关**：文本按钮，文案为点击后将执行的动作（`Locked=true` 显示"激活"，`Locked=false` 显示"不激活"）。`Locked=true` 表示**未激活**——运行时不加载该轨道的任何 Action；时间线上该 Track 的卡片描边为琥珀色。
- **+**：在本 Track 的 Actions 末尾追加一个新 Action：`CommandType=0`，`TriggerTime=现有最大 TriggerTime+1`，`GapsFromLastRepeat` 为空数组。
- **×**（按钮行内）：删除**当前选中**的 Action（未选中或选中不属于本 Track 时按钮禁用），弹确认框。

### Action 卡片

卡片是绝对定位的 `A{i}` 按钮，按 `TriggerTime × 像素密度` 排布，底色按 CommandType（0 绿 / 1 蓝 / 2-4 黄 / 5 紫 / 6 红），点击选中并在 Track 行下方展开详情面板。宽度：CommandType 2/3/4 固定 20px，其余为占用时长 × 像素密度（最小 20px）。边框三态：选中（亮琥珀）> 未激活 Track（琥珀）> 普通（灰）。

CommandType 0 的卡片上按 `GapsFromLastRepeat` 画深绿正方形标记，第 g 个标记位于 `TriggerTime + sum(gaps[0..g])` 处。

**卡片不可拖拽**——所有时间编辑都在详情面板完成。

## 选中 Action 编辑

详情面板标题 `▸ 已选 Wave[w].Track[t].Action[a]`。基础字段（恒显）：`CommandType`、`TriggerTime`、`OnBeforeAction`。条件字段按 CommandType 实时显隐：

| CommandType | 含义 | 显示的字段 |
|---|---|---|
| 0 | 召唤可移动实体 | EntityPrefabID、Camp、OnActionRepeat、PathSerial、GapsFromLastRepeat、ModifyAttributes（勾选后展开 ModifyLevelHpConsume / ModifyPrimary / ModifyCountOperate） |
| 1 | 生成静止实体 | EntityPrefabID、Camp、OnActionRepeat、Destination、Orientation |
| 2/3/4 | 显示地面/近地悬浮/飞行路径预览 | PathSerial |
| 5 | 显示右侧提示卡 | HeadImage、Content、DurationTime |
| 6 | 显示剧情 | Contents[] |

- `TriggerTime` — 绝对触发时间（Wave 开始后 N 秒），在详情面板数值编辑
- `EntityPrefabID` — EntityID 二元组 `ID_C` + `ID_N`（专用选择器）
- `PathSerial` — 路径预制体序号（专用选择器）
- `GapsFromLastRepeat[]` — spawner 重复间隔，数组长度 = 重复次数
- `OnBeforeAction` / `OnActionRepeat` — UnityEvent，经 IMGUI 容器渲染

## 地图编辑器（地图 / 路径 Tab）

- **地图 Tab**（MapEditTab）：单字段画刷 + 橡皮 + 传送门两点模式，直接编辑 `LevelData.MapData`（经 BlockMapCache 读写）。地图方块与传送门都在这里编辑。
- **路径 Tab**（PathEditTab）：编辑路径检查点。右侧栏在检查点列表与详情之间有**长度查询**模块：选起点/终点检查点，按当前 moveMethod 沿 A* 折线逐段累加路径长度（与画布绿线同源，含绕障；某段不可达标红）；可选填移动速度（格/秒）折算耗时 = 长度/速度 + 途经点 WaitTime（起点等计入、终点等不计，与运行时"到点先等再走"口径一致；负等待按 0）。

MapPrefab 无需手工搭建：引用节的 **生成 MapPrefab** 按钮按当前 `MapData` 生成/重建预制体的地块方块（每个画过的格子一个 Quad 子物体，位置 = 格点 (j, i)，z=0.01；材质按 地穴/高台/地面 三槽位选择，槽位留空则该类格子呈紫红占位）。生成规则：

- 首次生成落在 LevelData 同目录 `Map.prefab`，并自动填入引用；已引用的 prefab 则原地重建（保 GUID，其他资产对它的引用不断）。
- 覆盖已有 prefab 时只删除"会被运行时当成地块"的子物体（整数格点 + MeshRenderer，与 `MapDataManager.MapInitialize` 的消费判据同源），SpriteRenderer 出生点标记等手工装饰原样保留，生成可反复迭代。
- 生成后点 **编辑** 进入 Prefab 模式换材质、摆装饰；之后在地图 Tab 重画格子再点生成，装饰仍在。

## 校验条

底部状态条文案三态：`✓ 校验通过` / `⚠ N 个 Warning` / `✗ N 个 Error, M 个 Warning`。规则清单见 [LevelDataValidator](../Assets/Editor/LevelEditor/Validation/LevelDataValidator.cs)。

## 范围

- 修改 LevelData 不需要重启编辑器
- 所有 Undo/Redo 走 Unity 内置 Ctrl+Z / Ctrl+Y（编辑器各操作均已接入 Undo.RecordObject）
