# LevelMessagePanel.cs 结构重构 — 设计文档
> 状态：已归档（历史设计记录）

**日期**：2026-06-06
**作者**：NIMG877（设计对话）
**目标文件**：`Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs`
**当前规模**：1811 行，单文件
**形式**：单文件 + `#region` 分块（用户已选）

---

## 1. 背景与动机

`LevelMessagePanel.cs` 集成了 6 个 UI 子面板的状态机、点击/拖拽/选择事件、放置逻辑、伤害统计、UI 动画驱动等。当前实现的问题：

- **350+ 行字段声明无分组** — 时间控制 / 资源 / 选择态 / 摄像机 / 6 个面板的 UI 引用混在一起
- **230+ 行构造函数** — 一个方法里完成 sprite 加载、6 个面板的 UI 元素查找、EventTrigger 注册、对象池预热
- **方法零散** — `UIStates_ShowClose_Leftmessage` 和 `UIStates_Update_Canset` 隔了 500 行
- **嵌套类 `StaticEntityPlaceData` 226 行** 和外层紧耦合（直接读写 `Panel._selectedStaticEntityID`、`Panel.UIStates_*`）

但用户已经强调：**通过 `GetComponentInChildrenByPath<>` 路径获取 UI 元素的语义不能动**。这是核心约束。

## 2. 范围

### 2.1 改动范围

✅ 全部在范围内：
- 字段按 UI 面板分组 + 命名规范化
- 构造函数拆分为多个 `Init*()` 方法
- 方法按区域重新排序
- `//=================================` 装饰行替换为正式 `#region`
- 嵌套类 `StaticEntityPlaceData` 内部字段/方法重命名（拼写修正如 `CaculateCost` → `CalculateCost`）
- 局部变量名 / 临时变量名调整

❌ 范围外（用户明确指示）：
- 删除 `SwitchShowSkillTalent` 中 line 844-919 的注释 switch 块（用户："不删死代码"）
- 修复 `line 1049` (`_selectedPlaceData.StaticEntity`) 和 `line 1115` (`sea.CanCallBack`) 的 pre-existing bug
- 公共 API 方法的 **名字和参数**（用户："对外暴露的方法先不改名和参数"）

### 2.2 公共 API（保持原样）

通过 `grep LevelMessagePanel\.Panel\.|LevelMessagePanel\.` 确认的外部调用方：

| 方法 / 属性 | 调用方 |
|---|---|
| `LevelMessagePanel.Panel` (singleton) | 多处 |
| `DamageStatisticDatas` (property) | `LevelActionManager.cs:227` |
| `EntityBackToSelector(Entity)` | `InteractableStatic.cs:167` |
| `CanSetNumUpDate()` | `LevelRescurceManager.cs:40` |
| `CurrentNumAndTotalNumUpdate()` | `LevelRescurceManager.cs:52, 64` |
| `LevelHpLeftTextUpdate()` | `LevelRescurceManager.cs:88` |
| `CostTextUpDate()` | `LevelRescurceManager.cs:110` |
| `AcceptDamageMessage(Entity, Entity, float, int)` | `Entity.cs:282` |
| `ShowText(Vector2, int, int)` | `Entity.cs:289, 295, 310` |
| `OnEnter()` / `OnExit()` / `OnPause()` | `BasePanel` virtual 覆写 |

为安全起见，下列 `public` 但 grep 找不到外部调用的方法也**保持名字和参数不变**（仅整理内部实现）：

- `InitializeStaticEntityPrefabToSelector(EntityID[], int[])`
- `AddStaticEntityPrefabToSelector(EntityID[], int[])`
- `ReSelectOrSetStaticEntity()`

### 2.3 不动的核心不变量

- `UIState` 枚举的所有取值（`normal` / `viewBeforeSet` / `setting` / `choosing` / `viewAfterSet`）— 防止反射 / 序列化 / 调试
- 选中状态三字段 `_selectedPlaceData` / `_selectedStaticEntityID` / `_selectedEntity` 的语义（之前已重构）
- 路径字符串本身：`"leftMessageArea/attributes/atkRange/area/self"` 等 50+ 个 `GetComponentInChildrenByPath` 调用参数
- `BasePanel` 的 override 签名
- `using` 列表、namespace、类签名

## 3. 目标文件结构

按下列顺序排列，所有非嵌套的 #region 标签为同层（缩进对齐到类内一级）：

```
namespace MyUI
{
    public class LevelMessagePanel : BasePanel
    {
        // #region Nested Types
            private class StaticEntityPlaceData { ... }   // 226 行，内部字段/方法名规范化
        // #endregion

        // #region UI Element References
            // 子分组（用 // 注释标记，不嵌套 region 以免过度碎片化）：
            //   - Top Status Bar (pause / 2X / exit / 资源条)
            //   - Left Message Panel (name / hp / 攻防魔抗 / 攻击范围 / 技能天赋)
            //   - Operator Panel (撤退 / 技能 / SP)
            //   - Selector Area (placeData / content / sample)
            //   - Floating Text Pool
        // #endregion

        // #region Runtime State
            enum UIState
            UIState _currentUIState
            6 个 _xxxOpen 标志
            三字段选中态 (_selectedPlaceData / _selectedStaticEntityID / _selectedEntity)
            资源/可部署数/格子数据
            朝向 / inChooser / 摄像机
            战斗统计
        // #endregion

        // #region Construction & Initialization
            private LevelMessagePanel()      // 只调用 Init*()
            InitCoreResources()               // 摄像机、sprite atlas
            InitTimeControl()                 // pause/2X/exit 按钮
            InitTopStatusBar()                // cost/canSetNum/count/hp
            InitLeftMessagePanel()            // 左侧消息面板 + skillTalent/cards
            InitOperatorPanel()               // 操作区 + 技能/撤退/停止 按钮
            InitSelectorArea()                // 选择区 sample
            InitFloatingTextPool()            // 飘字对象池
            InitEventTriggers()               // 集中注册 EventTrigger
        // #endregion

        // #region Public API                       // 签名保持原样
            OnEnter / OnExit / OnPause (override)
            Panel (singleton)
            DamageStatisticDatas (property)
            InitializeStaticEntityPrefabToSelector
            AddStaticEntityPrefabToSelector
            ShowText
            EntityBackToSelector
            AcceptDamageMessage
            CostTextUpDate / CanSetNumUpDate / LevelHpLeftTextUpdate / CurrentNumAndTotalNumUpdate
            ReSelectOrSetStaticEntity
        // #endregion

        // #region Time Control
            SetTimeScale
            FixedUpdate (驱动 UIStateMachine)
        // #endregion

        // #region UI States
            UIStateMachine
            UIStates_ShowSomethingAndOtherClose  // 分发
            // 5 个 SwitchTo
            // 6 组 Show/Update/Close：LeftMessage / Operator / Dragger / Chooser / Range / CanSet
        // #endregion

        // #region Helpers
            FetchMapEntityData
            MoveCamera
            HideTargetOrEnterNextStage
            SwitchShowSkillTalent
            ShowAttackRangeAttributes
            CostSliderAndCanSetNumUpdate
        // #endregion
    }
}
```

## 4. 构造函数拆分细则

当前 230+ 行构造函数被拆为下列 8 个 `Init*()` 方法 + 构造本身。每个 `Init*()` 只负责一个 UI 子区域，**所有 `GetComponentInChildrenByPath<>` 调用的路径字符串与原代码逐字一致**。

| 方法 | 负责的 UI 区域 | 对应原代码行 |
|---|---|---|
| `InitCoreResources()` | `_camera`、sprite 资源（`_x1` `_x2` `_c` `_p`、`_professionsSmall` `_professionsLighten`、`_spMessageAtlas`、`_skillRangeButton`）、`_rangeImgCollection`、`_hpSliderSize` | 354-365, 510 |
| `InitTimeControl()` | `_timeMultiple` 按钮 + 回调、`_pause` 按钮 + 回调、`exit` 按钮 | 446-480 |
| `InitTopStatusBar()` | `_cost` `_costSlider` `_canSetNumText` `_currentNumAndTotalNum` `_levelHpLeft` `_pauseMask` | 438-444 |
| `InitLeftMessagePanel()` | `_leftMessage` `_chooser` `_up` `_down` `_left` `_right` `_class` `_target` `_self_a` `_range_a` `_rangeArea` `_name` `_admb` `_hpSlider` `_hpBk` `_hpText` `_skillTalentRect` `_skillTalentRectParent` `_skillCard` `_subpCard` `_talentCards` `_buffCards` `_skillTalentSwitchButtons` + 4 个按钮的 click 回调 | 368-419, 422-423 |
| `InitOperatorPanel()` | `_operateArea` `_callBack` `_skillOpen` `_skillRange` `_spBk` `_spState` `_spText` `_spMask` `_stop` `_skillChargeNum` `_skillChargeNumText` + `_callBackClick` `_skillRangeClick` + `_skillOpen` click + `_stop` click | 426-436, 529-554 |
| `InitSelectorArea()` | `_selectorSample` `_content` | 421, 443 |
| `InitFloatingTextPool()` | `_text` + 6 种 `_textsInPool` 预热（5 实例） | 437, 513-522 |
| `InitEventTriggers()` | `_levelMessageTrigger`（blankClick）、chooser 的 enter/exit/drag/dragEnd | 482-505, 555-583 |

构造函数本体约 10 行：

```csharp
private LevelMessagePanel() : base(new UIType("Prefabs/UI/MyUIs/LevelMessagePanel"))
{
    InitCoreResources();
    InitTimeControl();
    InitTopStatusBar();
    InitLeftMessagePanel();
    InitOperatorPanel();
    InitSelectorArea();
    InitFloatingTextPool();
    InitEventTriggers();
}
```

### 4.1 拆分原则

- `Init*()` 全部为 `private void`（与原构造函数同语义）
- 调用顺序：资源 → 时间控制 → 顶部条 → 左侧 → 操作区 → 选择区 → 飘字池 → 事件注册
  - 顺序依据：上层 UI 先于下层 UI；事件注册必须在所有 UI 引用就绪之后
- 不修改 `OnEnter` 中调用的 `InitializeStaticEntityPrefabToSelector`（它已移到 Public API 区，但**调用方不变**）

## 5. 字段命名清理

仅修改以下 4 个（其余字段名在上下文里已足够清晰）：

| 当前名 | 新名 | 理由 |
|---|---|---|
| `CaculateCost` (方法，在 `StaticEntityPlaceData` 内) | `CalculateCost` | 拼写错误（caculate → calculate） |
| `_admb` | `_statsText` | 上下文显示 "atk / def / mr / block"，新名更可读 |
| `_self_a` | `_rangeSelfTile` | 攻击范围可视化中的"自己所在"格 |
| `_range_a` | `_rangeTiles` | 攻击范围格子列表 |

**StaticEntityPlaceData 内部规范化**（一并处理以提高可读性）：

| 当前 | 新 |
|---|---|
| `GameObject Selector` (无下划线) | `GameObject _selectorRoot` | 统一前缀风格 |
| `RectTransform _selectorRectTransform` | `RectTransform _selectorRect` |
| `EntityID StaticId` | `EntityID EntityId` |
| `EntityData StaticEntityData` | `EntityData EntityData` |
| `EventTrigger _event` | `EventTrigger _trigger` |
| `int _placeTime` | `int _deployCount` |
| `int _leftNum` | `int _remainingCount` |
| `bool _canSet` | `bool _isAffordable` |
| `float _anchory` | `float _selectorYAnchor` |

注意：`StaticEntityPlaceData` 内的 `Static` 前缀在 `LevelMessagePanel` 内部本来就有歧义（既指 prefab 又指静态部署）。新名一律用 `Entity` 前缀。

## 6. 不删的死代码

`SwitchShowSkillTalent` 中 line 844-919 整段 `// switch (_currentShow) { ... }` 注释块保持原状。

## 7. 验证方案

重构完成后应满足：

1. **行为不变**：
   - 启动一个关卡，逐一走完选中 → 拖拽 → 设置 → 选择朝向 → 部署 → 选中已部署 → 撤退 / 技能 / SP 流程
   - 退出关卡（OnExit 清理）
   - 暂停 / 2X 切换
   - 飘字（damage / heal / cost / miss）显示
   - 资源耗尽 / 满资源时可部署判断

2. **代码量不变**：
   - `wc -l` 行数与重构前相差不超过 ±5%（应略少，因为去掉了装饰行）

3. **可编译**：
   - Unity 编辑器无 **新引入的** 编译 error
   - 允许原本就存在的 2 个 pre-existing bug（`line 1049` `_selectedPlaceData.StaticEntity` 字段不存在、`line 1115` `sea` 未定义）**保持原状**——这些不在本次重构范围

4. **grep 校验**：
   - `grep -n '//================================='` 在该文件中无匹配（装饰行已替换）
   - `grep -n '\bCaculateCost\b'` 无匹配
   - `grep -n '\b_admb\b'` 无匹配

## 8. 风险与缓解

| 风险 | 缓解 |
|---|---|
| Init 拆分时漏掉某个 EventTrigger 的注册 | 按 4.1 的表格逐项核对，每完成一个 Init 用 grep 验证对应字段在该 Init 内赋值 |
| 字段重命名漏改引用 | 重命名前先 grep 全文件引用数；改完后再 grep 验证 0 匹配 |
| StaticEntityPlaceData 内部重命名影响外层引用 | 该类为 `private`，所有引用都在本文件内，grep 易查 |
| EventTrigger 回调里的 lambda 闭包捕获字段 | 字段是实例字段，lambda 捕获的是 `this`，重命名不破坏闭包 |

## 9. 实施步骤（概要）

不细化到 step 级别——这留给 `writing-plans` 阶段做。概要顺序：

1. 按 #region 把字段、方法重新排版（不修改任何代码体）
2. 重命名字段（4 个）和方法（`CaculateCost`）
3. 重命名 `StaticEntityPlaceData` 内部字段
4. 拆分构造函数为 8 个 `Init*()` 方法
5. 删除 `//=================================` 装饰行，加 `#region` 标签
6. 编译 + Unity 启动验证
7. grep 校验
