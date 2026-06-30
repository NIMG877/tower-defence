# Level Editor 使用说明

## 入口

在 Project 窗口选中任意 `LevelData` 资产, Inspector 自动呈现本编辑器。

## Section 总览

- **元数据**: 关卡名/代码/描述/相机/转场贴图
- **引用**: 拖拽 MapPrefab / EnvironmentalControlDevice
- **波次时间线**: 多 Wave × 多 Track,每条 Track 渲染为一行;Action 卡片按绝对 `TriggerTime` 渲染
- **经济**: HP/成本/上限/部署上限/恢复速度
- **地图编辑器**(Tab): 地图方块编辑 / 路径编辑

## 波次时间线

每个 Wave 由 N 条命名轨道组成。默认新 Wave 有 1 条轨道"默认",可"+ 新增 Track"加更多。

### 每条 Wave 的操作

- **+ 新增 Track**: 在 Wave 末尾追加一条新轨道,默认名 `Track N`,默认色按 TrackIndex 轮转(灰 / 绿 / 蓝 / 黄 / 紫)
- **× 删除 Wave**: 删除整个 Wave 块(弹确认)

### 每条轨道的操作(轨道头)

- **拖拽手柄 `≡`**: 视觉占位,Phase 3 基础版未实现实际拖拽重排
- **Name**: 行内可编辑;默认建议命名 "默认" / "路径预览" / "支援" / "Boss"
- **🎨 按钮**: 打开颜色选择,修改该轨道色(也用于卡片描边色)
- **🔓 / 🔒 按钮**: 锁定/解锁;锁定后该轨道上的 Action 卡片不可拖动
- **+ / × 行内按钮**: 在本轨道末尾追加新 Action / 删除最后一个 Action

### Action 卡片(取决于 CommandType)

| CommandType | 含义 | 卡片形态 | 右沿拖拽 |
|---|---|---|---|
| 0 | 召唤可移动实体 | Range bar,内部小刻度标记重复点 | 可拖(Phase 3 基础版未实现,等比缩放 `GapsFromLastRepeat` 留作 follow-up) |
| 1 | 生成静止实体 | 菱形 ◆ | 无 |
| 2/3/4 | 显示路径预览(地面/近地/飞行) | Range bar | 不可拖(硬编码 printerLifeTime) |
| 5 | 显示对话框 | Range bar | 可拖(改 `DurationTime`,Phase 3 基础版未实现,留作 follow-up) |
| 6 | 显示剧情 | 菱形 ◆ | 无 |

### 时间编辑

- **拖卡片中段**: 改 `TriggerTime`(绝对秒数)。默认吸附到 0.1s,**按住 Shift** 关闭吸附
- **改 TriggerTime 后**: 内部节奏自动跟随,`GapsFromLastRepeat[]` / `DurationTime` 不变
- **顶部缩放 slider**: 调整时间刻度,所有 Wave 共享

## 选中 Action 编辑

点时间线上的卡片, 下方内联面板出现该 Action 的字段。CommandType 切换会实时显隐条件字段。

Action 的关键字段:
- `CommandType` — 选择类型
- `TriggerTime` — 绝对触发时间(Wave 开始后 N 秒),替换了原 `GapFromLastAction`
- `EntityPrefabID` — 直接 EntityID 二元组 `ID_C` + `ID_N`
- `GapsFromLastRepeat[]` — spawner 重复间隔(数组长度 = 召唤次数)
- `Destination` / `Orientation` / `DurationTime` 等 — 按 CommandType 显隐

> 2026-06-23 变更:`EntityPrefabSerial`(整数索引) → `EntityPrefabID`(直接 EntityID 二元组)。
> 2026-06-30 变更:Wave 由 `Action[]` 升级为 `Track[]`(多轨道),Action 由 `GapFromLastAction`(相对)升级为 `TriggerTime`(绝对)。Runtime 调度改为按 TriggerTime 排序后的单调度循环。

## 数据迁移

打开任意旧 LevelData 资产时,Editor 自动通过 `[InitializeOnLoadMethod]` 把 v1 数据迁移到 v2 格式:
- `Wave.Actions[]` → `Wave.Tracks[0].Actions`(单一默认轨道)
- `Action.GapFromLastAction` 累加成绝对时间写入 `Action.TriggerTime`

Console 会打印 `[WaveMigrator] 迁移完成: N 成功, 0 失败`。

## 校验条

底部状态条颜色: 绿 ✓ / 黄 ⚠ / 红 ✗。规则清单见 [validator 实现](../Assets/Editor/LevelEditor/Validation/LevelDataValidator.cs),包含一条新增规则:
- **TriggerTime ≥ 0**:任一 Action 的 TriggerTime 为负时报 Error

## Playtest

点底部 ▶ Playtest 按钮:
1. 若有 Error, 弹 Dialog 询问"继续还是取消"
2. 第一次使用自动创建 `Assets/Scenes/LevelTest.unity` (含 LM/MCam/UICam/LevelTestStarter)
3. 切到测试场景, 设置 LevelDataToPlay, Enter Play
4. 退出 Play 时自动清理

## 范围

- 地图方块/传送门不在本编辑器内编辑, MapPrefab 仍需独立制作
- 修改 LevelData 不需要重启编辑器
- 所有 Undo/Redo 走 Unity 内置 Ctrl+Z / Ctrl+Y