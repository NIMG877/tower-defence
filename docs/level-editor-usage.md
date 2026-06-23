# Level Editor 使用说明

## 入口

在 Project 窗口选中任意 `LevelData` 资产, Inspector 自动呈现本编辑器。

## 4 个 Section

- **元数据**: 关卡名/代码/描述/相机/转场贴图
- **引用**: 拖拽 MapPrefab / EnvironmentalControlDevice
- **波次时间线**: 每条 Wave 一行, Action 渲染为卡片, 卡片宽度按 GapFromLastAction 比例计算
- **经济**: HP/成本/上限/部署上限/恢复速度

## 选中 Action 编辑

点时间线上的卡片, 下方内联面板出现该 Action 的字段。CommandType 切换会实时显隐条件字段。

> 2026-06-23 变更:Action 的 `EntityPrefabSerial`(整数索引)已改为 `EntityPrefabID`(直接 EntityID 二元组 `ID_C` + `ID_N`)。`WaveEntityPrefabIDs` 主清单已删除,runtime 直接从 actions 扫描派生池大小。

## 校验条

底部状态条颜色: 绿 ✓ / 黄 ⚠ / 红 ✗。规则清单见 [validator 实现](../Assets/Editor/LevelEditor/Validation/LevelDataValidator.cs)。

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
