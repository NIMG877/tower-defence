# LevelMessagePanel.cs 结构重构 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 1811 行的 `LevelMessagePanel.cs` 重组为 8 个 `#region` 块、构造函数拆为 8 个 `Init*()` 方法、清理命名。仅文件结构与命名变化，行为完全不变。

**Architecture:** 单文件保留 + 内部按 UI 子面板分组（嵌套类、UI 元素引用、运行时状态、构造与初始化、公共 API、时间控制、UI 状态、辅助方法）。所有 `GetComponentInChildrenByPath` 路径字符串与原代码逐字一致。

**Tech Stack:** Unity 2022.3 LTS (推测), C#, UniTask, DOTween, TMPro, EventTrigger, 现有项目无自动化测试。

**Spec:** `docs/superpowers/specs/2026-06-06-level-message-panel-structure-refactor-design.md`

**测试策略:** 本项目无单元测试基础设施。验证手段 = 编译成功 + grep 校验 + Unity 手玩流程。详见每个 task 的"Verify"。

---

## 实施原则

- **每次 commit 都要能编译**（虽然允许 line 1049/1115 的 pre-existing 编译错误保留，但**不能引入新错误**）
- **每完成一个 task 就 grep 一次**，确认无残留旧名
- **公共 API 名字和参数严格不变**（见 spec §2.2）
- **行为完全不变**（不修 bug、不优化逻辑、不重构算法）

---

## File Structure

本计划**只修改一个文件**：
- `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs`

不在范围内（已与用户确认）：
- 不创建新文件
- 不修改 `BasePanel`、其他 UI 面板、`EntityManager` 等依赖类
- 不修改 prefab

---

## Task 1: 添加文件顶部 region 框架（占位 + 注释）

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs:11-14`

- [ ] **Step 1: 在类签名后立刻插入 8 个 #region 标签（嵌套类在最前）**

将文件第 11-14 行：

```csharp
namespace MyUI
{
    public class LevelMessagePanel : BasePanel
    {
```

改为：

```csharp
namespace MyUI
{
    public class LevelMessagePanel : BasePanel
    {
        #region Nested Types
        #endregion

        #region UI Element References
        #endregion

        #region Runtime State
        #endregion

        #region Construction & Initialization
        #endregion

        #region Public API
        #endregion

        #region Time Control
        #endregion

        #region UI States
        #endregion

        #region Helpers
        #endregion
    }
}
```

- [ ] **Step 2: 编译验证（允许已有 error）**

操作：在 Unity Editor 中打开项目，等待编译完成。
期望：**不出现新的编译 error**。原本就有的 2 个 pre-existing bug（`line 1049` `_selectedPlaceData.StaticEntity` 和 `line 1115` `sea.CanCallBack`）的错误状态保持不变。

- [ ] **Step 3: Commit**

```bash
cd e:/Unity/projects/TD
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
git commit -m "refactor(LevelMessagePanel): add #region skeleton for 8 sections

No behavior change. Region bodies are empty; fields and methods will be
moved into them in subsequent tasks."
```

---

## Task 2: 把嵌套类 StaticEntityPlaceData 移到 #region Nested Types

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs:38-263`

- [ ] **Step 1: 剪切整个 StaticEntityPlaceData 嵌套类（line 38-263）**

选中从第 38 行 `private class StaticEntityPlaceData` 到第 263 行 `}`（嵌套类结束），整段剪切。

- [ ] **Step 2: 粘贴到第一个 #region（Nested Types）内**

将剪切的内容粘贴到 `Region Nested Types` 块内。结构如下：

```csharp
        #region Nested Types
        private class StaticEntityPlaceData
        {
            // ... 原 line 38-263 内容不变
        }
        #endregion
```

- [ ] **Step 3: 编译验证**

Unity Editor 重新编译。
期望：无新编译 error。

- [ ] **Step 4: Commit**

```bash
cd e:/Unity/projects/TD
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
git commit -m "refactor(LevelMessagePanel): move StaticEntityPlaceData into Nested Types region"
```

---

## Task 3: 把所有 UI 元素引用字段移到 #region UI Element References

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs`（多个 line 段）

**目标字段清单**（按 spec §3 的子分组顺序）：

| 子分组 | 字段 |
|---|---|
| Selector Area | `_placeDataList`, `_selectorObjects`, `_content`, `_selectorSample` |
| Time Control | `_pauseMask`, `_timeMultiple`, `_pause` |
| Resource Display | `_x1`, `_x2`, `_c`, `_p`, `_currentCost`, `_cost`, `_costSlider` |
| Capacity Display | `_canSetNumText`, `_canSetNum` |
| Level Status | `_currentNumAndTotalNum`, `_levelHpLeft` |
| Left Message Panel | `_leftMessage`, `_chooser`, `_up`, `_down`, `_left`, `_right`, `_class`, `_target`, `_self_a`, `_range_a`, `_rangeArea`, `_name`, `_admb`, `_hpSlider`, `_hpBk`, `_hpText`, `_hpSliderSize`, `_professionsSmall`, `_professionsLighten`, `_skillTalentRect`, `_skillTalentRectParent`, `_skillCard`, `_subpCard`, `_talentCards`, `_buffCards`, `_skillTalentSwitchButtons`, `_rangeImg`, `_rangeImgCollection` |
| Operator Panel | `_operateArea`, `_callBack`, `_skillOpen`, `_skillRange`, `_spBk`, `_spState`, `_spText`, `_spMask`, `_stop`, `_skillChargeNum`, `_skillChargeNumText`, `_spMessageAtlas`, `_skillRangeButton`, `_selectSkill` |
| Floating Text Pool | `_text`, `_textsInPool` |
| Misc UI | `_levelMessageTrigger` |

**本任务不重命名字段**（重命名是 Task 5）。本任务只**重新定位**字段到正确的 #region 内。

- [ ] **Step 1: 准备清单**

打开文件，定位所有上述字段的当前位置（line 264-351 是大部分字段，但有些散布在 line 38 之前、`StaticEntityPlaceData` 之后）。

- [ ] **Step 2: 移动字段**

在 `#region UI Element References` 块内，按"子分组"添加上述字段（保持原 `private` 修饰符和类型），同时从原位置**删除**这些声明。

⚠️ 注意：`_inChooser`、`_orientation`、`_higherCanSetBlock`、`_lowerCanSetBlock`、`_staticEntityExistBlock`、`_canSetType`、`_canSetBlockList`、`_blockDatas`、`_iSize`、`_jSize`、`_camera`、`_cameraOriginalPos`、`_deltaX`、`_leftmessageOpen`、`_operaterOpen`、`_draggerOpen`、`_chooserOpen`、`_rangeOpen`、`_cansetOpen`、`_selectedPlaceData`、`_selectedStaticEntityID`、`_selectedEntity`、`_isShowMessage`、`_isShowCanSetBlock`、``_isShowOperate`、`_isShowAttackRange`、`_isPause`、`_is2X`、`_isSlow`、`_currentUIState`、`_currentShow`、`_colorSelect`、`_colorUnSelect`、`_lightGreen`、`_lightGreen_half`、`_orange`、`_orange_half`、`_gray` 这些**不**是 UI 元素引用，**不**移动到本 region。

- [ ] **Step 3: 编译验证**

Unity Editor 重新编译。
期望：无新编译 error。

- [ ] **Step 4: grep 验证字段未丢失**

```bash
cd e:/Unity/projects/TD
grep -c "private.*_leftMessage\|private.*_chooser\|private.*_operateArea\|private.*_cost\b\|private.*_hpSlider\b\|private.*_text\b" Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
```

期望：每个字段出现 **恰好 1 次**（只有声明，没有遗留的副本）。

- [ ] **Step 5: Commit**

```bash
cd e:/Unity/projects/TD
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
git commit -m "refactor(LevelMessagePanel): group UI element fields into #region"
```

---

## Task 4: 把所有运行时状态字段移到 #region Runtime State

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs`

**目标字段清单**（按 spec §3）：

- 枚举：`UIState`
- 当前状态：`_currentUIState`
- 6 个 _xxxOpen 标志：`_leftmessageOpen`, `_operaterOpen`, `_draggerOpen`, `_chooserOpen`, `_rangeOpen`, `_cansetOpen`
- 三字段选中态：`_selectedPlaceData`, `_selectedStaticEntityID`, `_selectedEntity`
- 资源：`_currentCost`, `_canSetNum`
- 格子数据：`_higherCanSetBlock`, `_lowerCanSetBlock`, `_staticEntityExistBlock`, `_canSetType`, `_canSetBlockList`, `_blockDatas`, `_iSize`, `_jSize`
- 摄像机：`_camera`, `_cameraOriginalPos`, `_deltaX`
- 朝向 / inChooser：`_inChooser`, `_orientation`
- 战斗统计：`_characterChineseName`, `_damageStatisticDatas`
- 时间控制：`_isPause`, `_is2X`, `_isSlow`
- Skill 状态：`_currentShow`, `_selectSkill`, `_colorSelect`, `_colorUnSelect`, `_lightGreen`, `_lightGreen_half`, `_orange`, `_orange_half`, `_gray`
- 未使用的旧 flag：`_isShowMessage`, `_isShowCanSetBlock`, `_isShowOperate`, `_isShowAttackRange`（在文件中声明但 grep 无引用；保留以避免破坏可能的反射/序列化）

- [ ] **Step 1: 移动字段**

在 `#region Runtime State` 块内，按上述清单放置字段（保持原修饰符与类型），同时从原位置**删除**这些声明。

- [ ] **Step 2: 编译验证**

Unity Editor 重新编译。
期望：无新编译 error。

- [ ] **Step 3: grep 验证**

```bash
cd e:/Unity/projects/TD
grep -n "private.*_selectedPlaceData\|private.*_selectedStaticEntityID\|private.*_selectedEntity\|private UIState _currentUIState" Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
```

期望：每个字段恰好 1 个声明（如果存在 line 数 > 1 的输出，说明有遗留）。

- [ ] **Step 4: Commit**

```bash
cd e:/Unity/projects/TD
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
git commit -m "refactor(LevelMessagePanel): group runtime state fields into #region"
```

---

## Task 5: 重命名外层字段（4 个）+ 修正 1 个方法名

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs`

**重命名清单**（spec §5）：

| 当前名 | 新名 | 范围 |
|---|---|---|
| `_admb` | `_statsText` | 类内所有引用 |
| `_self_a` | `_rangeSelfTile` | 类内所有引用 |
| `_range_a` | `_rangeTiles` | 类内所有引用 |
| `CaculateCost` | `CalculateCost` | `StaticEntityPlaceData` 内方法 |

- [ ] **Step 1: grep 定位 `_admb`**

```bash
cd e:/Unity/projects/TD
grep -n "\b_admb\b" Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
```

记录所有出现位置。

- [ ] **Step 2: 用 IDE Rename 重构 `_admb` → `_statsText`**

在 IDE 中选中 `_admb` 字段，使用 Rename 重构（Shift+F6 in Rider, F2 in VSCode）→ `_statsText`。确保所有引用一起改。

- [ ] **Step 3: 重复 Step 1-2 处理 `_self_a` → `_rangeSelfTile` 和 `_range_a` → `_rangeTiles`**

⚠️ 注意：`_range_a` 是 `List<RectTransform>`，调用如 `_range_a.Add(...)`、`_range_a[i]` 等。Rename 工具会同时改这些。

- [ ] **Step 4: 重命名 `CaculateCost` → `CalculateCost`**

在 `StaticEntityPlaceData` 嵌套类内重命名此方法。

- [ ] **Step 5: 编译验证**

Unity Editor 重新编译。
期望：无新编译 error。

- [ ] **Step 6: grep 验证无残留**

```bash
cd e:/Unity/projects/TD
grep -n "\b_admb\b\|\b_self_a\b\|\b_range_a\b\|\bCaculateCost\b" Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
```

期望：**无匹配**。

- [ ] **Step 7: Commit**

```bash
cd e:/Unity/projects/TD
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
git commit -m "refactor(LevelMessagePanel): rename _admb/_self_a/_range_a/CaculateCost

- _admb        -> _statsText          (ATK/DEF/MR/Block stats display)
- _self_a      -> _rangeSelfTile      (attack range visualization self)
- _range_a     -> _rangeTiles         (attack range tile list)
- CaculateCost -> CalculateCost       (typo fix)"
```

---

## Task 6: 重命名 StaticEntityPlaceData 内部字段（9 个）

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs`（仅 StaticEntityPlaceData 内）

**重命名清单**（spec §5）：

| 当前 | 新 |
|---|---|
| `GameObject Selector` | `GameObject _selectorRoot` |
| `RectTransform _selectorRectTransform` | `RectTransform _selectorRect` |
| `EntityID StaticId` | `EntityID EntityId` |
| `EntityData StaticEntityData` | `EntityData EntityData` |
| `EventTrigger _event` | `EventTrigger _trigger` |
| `int _placeTime` | `int _deployCount` |
| `int _leftNum` | `int _remainingCount` |
| `bool _canSet` | `bool _isAffordable` |
| `float _anchory` | `float _selectorYAnchor` |

⚠️ **特别注意**：`StaticId` 和 `StaticEntityData` 是 `public` 字段，被外层代码（`LevelMessagePanel`）访问。必须用 IDE Rename 工具连带外层引用一起重命名。

- [ ] **Step 1: 对每个字段执行 IDE Rename**

逐个处理 9 个字段。每步：
1. `grep -n` 定位所有引用
2. IDE Rename（连带所有引用）
3. 编译验证

- [ ] **Step 2: 全局 grep 验证**

```bash
cd e:/Unity/projects/TD
grep -n "\bSelector\b\|\b_selectorRectTransform\b\|\bStaticId\b\|\bStaticEntityData\b\|\b_event\b\|\b_placeTime\b\|\b_leftNum\b\|\b_canSet\b\|\b_anchory\b" Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
```

⚠️ 注意：`_event` 是 EventTrigger 字段，在 Unity 框架中 `_event` 不是关键字（C# 用 `event` 关键字，但有下划线不冲突），改名 `_trigger` 后无冲突。但 grep 时需要小心，可能匹配到 `_eventTrigger` 之类。改用：

```bash
grep -nE "(\b|_)Selector(\b|_)|\b_selectorRectTransform\b|\bStaticId\b|\bStaticEntityData\b|\b_event\b|\b_placeTime\b|\b_leftNum\b|\b_canSet\b|\b_anchory\b" Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
```

期望：除了 `C# event` 关键字的合法使用（不应出现在本文件），**无其他匹配**。

- [ ] **Step 3: 编译验证**

Unity Editor 重新编译。
期望：无新编译 error。

- [ ] **Step 4: Commit**

```bash
cd e:/Unity/projects/TD
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
git commit -m "refactor(StaticEntityPlaceData): normalize internal field names

- Selector              -> _selectorRoot
- _selectorRectTransform-> _selectorRect
- StaticId              -> EntityId
- StaticEntityData      -> EntityData
- _event                -> _trigger
- _placeTime            -> _deployCount
- _leftNum              -> _remainingCount
- _canSet               -> _isAffordable
- _anchory              -> _selectorYAnchor"
```

---

## Task 7: 拆分构造函数（8 个 Init*() 方法）

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs`（构造函数 line 352-584）

**目标**（spec §4）：把当前 230+ 行的构造函数拆为 8 个 `Init*()` 方法。

| Init 方法 | 包含的代码段（原行号） |
|---|---|
| `InitCoreResources()` | 354-365（camera, sprites, rangeImgCollection），424（rangeImg），510（hpSliderSize） |
| `InitTimeControl()` | 446-480（timeMultiple, pause, exit 按钮 + 回调） |
| `InitTopStatusBar()` | 438-444（cost, costSlider, canSetNum, count, hp, pauseMask） |
| `InitLeftMessagePanel()` | 368-419, 422-423（leftMessage 子树所有元素 + 4 个 switch button） |
| `InitOperatorPanel()` | 426-436, 529-554（operate area + skill/callback/stop 按钮 + skillOpenClick） |
| `InitSelectorArea()` | 421, 443（selectorSample, content） |
| `InitFloatingTextPool()` | 437, 513-522（text + 6 种 text pool） |
| `InitEventTriggers()` | 482-505（levelMessageTrigger.blankClick），555-583（chooser enter/exit/drag/dragEnd） |

⚠️ **注意 `_callBackClick` 和 `_skillRangeClick` 字段**：它们在 `InitOperatorPanel` 中**创建并注册到 EventTrigger**，但回调方法体是在 `UIStates_ShowClose_Operator` 里通过 `_callBackClick.callback.RemoveAllListeners()` 之后 `AddListener()` 注入。**本任务保持这一分离，不修改**。

- [ ] **Step 1: 添加 8 个空 Init*() 方法**

在 `#region Construction & Initialization` 内（构造函数之前），添加 8 个 `private void InitXxx() { }` 桩方法。

- [ ] **Step 2: 移动代码到 InitCoreResources()**

从构造函数中剪切 line 354-365 + 424 + 510，粘贴到 `InitCoreResources()` 内。

- [ ] **Step 3: 移动代码到 InitTimeControl()**

剪切 line 446-480，粘贴到 `InitTimeControl()` 内。

- [ ] **Step 4: 移动代码到 InitTopStatusBar()**

剪切 line 438-444，粘贴到 `InitTopStatusBar()` 内。

- [ ] **Step 5: 移动代码到 InitLeftMessagePanel()**

剪切 line 368-419 + 422-423，粘贴到 `InitLeftMessagePanel()` 内。

- [ ] **Step 6: 移动代码到 InitOperatorPanel()**

剪切 line 426-436 + 529-554，粘贴到 `InitOperatorPanel()` 内。

- [ ] **Step 7: 移动代码到 InitSelectorArea()**

剪切 line 421 + 443，粘贴到 `InitSelectorArea()` 内。

- [ ] **Step 8: 移动代码到 InitFloatingTextPool()**

剪切 line 437 + 513-522，粘贴到 `InitFloatingTextPool()` 内。

- [ ] **Step 9: 移动代码到 InitEventTriggers()**

剪切 line 482-505 + 555-583，粘贴到 `InitEventTriggers()` 内。

- [ ] **Step 10: 简化构造函数**

构造函数体改为：

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

- [ ] **Step 11: 编译验证**

Unity Editor 重新编译。
期望：无新编译 error。pre-existing 2 个 bug 状态不变。

- [ ] **Step 12: 行数验证**

```bash
cd e:/Unity/projects/TD
wc -l Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
```

期望：与重构前（1811）相差 ±5%，应**略少**（因为没有重复的字段声明）。

- [ ] **Step 13: Commit**

```bash
cd e:/Unity/projects/TD
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
git commit -m "refactor(LevelMessagePanel): split ctor into 8 Init*() methods

- InitCoreResources()    : camera, sprites, rangeImg, hpSliderSize
- InitTimeControl()      : timeMultiple, pause, exit buttons
- InitTopStatusBar()     : cost, canSetNum, count, hp, pauseMask
- InitLeftMessagePanel() : name, hp, stats, range, skillTalent
- InitOperatorPanel()    : callback, skill, SP, stop + skill clicks
- InitSelectorArea()     : selectorSample, content
- InitFloatingTextPool() : damage/heal/cost/miss text pools
- InitEventTriggers()    : levelMessageTrigger, chooser drag/hover

Ctor body now ~10 lines, calls all 8 Init*() in order."
```

---

## Task 8: 公共 API 方法移到 #region Public API

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs`

**目标**（spec §2.2 + §3）：把这些 `public` 方法从当前位置移动到 `#region Public API` 内。**仅移动，不重命名，不改参数**。

- `OnEnter()`（line 1755-1793）
- `OnExit()`（line 1794-1805）
- `OnPause()`（line 1806-1814）
- `Panel`（singleton，line 26-37）
- `DamageStatisticDatas`（property，line 305）
- `InitializeStaticEntityPrefabToSelector`（line 586-619）
- `AddStaticEntityPrefabToSelector`（line 620-646）
- `ShowText`（line 653-686）
- `EntityBackToSelector`（line 796-814）
- `AcceptDamageMessage`（line 1724-1754）
- `CostTextUpDate`（line 1696-1705）
- `CanSetNumUpDate`（line 1706-1714）
- `LevelHpLeftTextUpdate`（line 1715-1719）
- `CurrentNumAndTotalNumUpdate`（line 1720-1723）
- `ReSelectOrSetStaticEntity`（line 742-760）

- [ ] **Step 1: 移动方法**

逐个剪切 + 粘贴到 `#region Public API` 内，按上述列表顺序排列。

- [ ] **Step 2: 编译验证**

Unity Editor 重新编译。
期望：无新编译 error。

- [ ] **Step 3: Commit**

```bash
cd e:/Unity/projects/TD
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
git commit -m "refactor(LevelMessagePanel): move public API into #region Public API

All public method signatures and names preserved (spec §2.2)."
```

---

## Task 9: 移到 #region Time Control

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs`

**目标**：移动 2 个方法：
- `SetTimeScale()`（line 688-706）
- `FixedUpdate()`（line 708-717）

- [ ] **Step 1: 移动方法**

剪切并粘贴到 `#region Time Control` 内。

- [ ] **Step 2: 编译验证**

期望：无新编译 error。

- [ ] **Step 3: Commit**

```bash
cd e:/Unity/projects/TD
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
git commit -m "refactor(LevelMessagePanel): move SetTimeScale/FixedUpdate into Time Control region"
```

---

## Task 10: 移到 #region UI States

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs`

**目标**（spec §3）：移动 26 个 UI 状态方法到 `#region UI States` 内，按以下子分组排列：

**子分组 1: SwitchTo（5 个）**
- `UIStates_SwitchTo_Normal`（line 1585-1601）
- `UIStates_SwitchTo_ViewBeforeSet`（line 1602-1618）
- `UIStates_SwitchTo_Setting`（line 1619-1635）
- `UIStates_SwitchTo_Chooseing`（line 1636-1652）
- `UIStates_SwitchTo_ViewAfterSet`（line 1653-1669）

**子分组 2: 状态机 + 分发**
- `UIStateMachine`（line 1670-1694）
- `UIStates_ShowSomethingAndOtherClose`（line 1573-1583）

**子分组 3: 6 个面板的 Show/Close/Update（18 个）**
- LeftMessage: `UIStates_ShowClose_Leftmessage`, `UIStates_Update_Leftmessage`
- Operator: `UIStates_ShowClose_Operator`, `UIStates_Update_Operator`
- Dragger: `UIStates_ShowClose_Dragger`, `UIStates_Update_Dragger`
- Chooser: `UIStates_ShowClose_Chooser`, `UIStates_Update_Chooser`
- Range: `UIStates_ShowClose_Range`, `UIStates_Update_Range`
- CanSet: `UIStates_ShowClose_Canset`, `UIStates_Update_Canset`

具体行号：
- ShowClose_Leftmessage: 1007-1041
- Update_Leftmessage: 1042-1102
- ShowClose_Operator: 1103-1167
- Update_Operator: 1168-1275
- ShowClose_Dragger: 1276-1298
- Update_Dragger: 1299-1331
- ShowClose_Chooser: 1332-1351
- Update_Chooser: 1352-1415
- ShowClose_Range: 1416-1435
- Update_Range: 1436-1481
- ShowClose_Canset: 1482-1503
- Update_Canset: 1504-1572

- [ ] **Step 1: 移动 SwitchTo 5 个方法**

剪切 + 粘贴到 `#region UI States` 顶部。

- [ ] **Step 2: 移动 UIStateMachine 和 ShowSomethingAndOtherClose**

- [ ] **Step 3: 移动 LeftMessage 的 2 个方法**

- [ ] **Step 4: 移动 Operator 的 2 个方法**

- [ ] **Step 5: 移动 Dragger 的 2 个方法**

- [ ] **Step 6: 移动 Chooser 的 2 个方法**

- [ ] **Step 7: 移动 Range 的 2 个方法**

- [ ] **Step 8: 移动 CanSet 的 2 个方法**

- [ ] **Step 9: 编译验证**

Unity Editor 重新编译。
期望：无新编译 error。

- [ ] **Step 10: 完整方法名清单 grep**

```bash
cd e:/Unity/projects/TD
grep -nE "private void UIStates_(SwitchTo_|Update_|ShowClose_)" Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
```

期望：恰好 26 个方法 + 1 个 `UIStates_ShowSomethingAndOtherClose` + 1 个 `UIStateMachine` = 28 个匹配。

- [ ] **Step 11: Commit**

```bash
cd e:/Unity/projects/TD
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
git commit -m "refactor(LevelMessagePanel): consolidate 28 UI state methods into #region UI States

Subgroups: 5 SwitchTo, state machine + dispatch, 6 panels × (ShowClose + Update)."
```

---

## Task 11: 移到 #region Helpers

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs`

**目标**：移动 6 个辅助方法：
- `FetchMapEntityData()`（line 761-785）
- `MoveCamera(Vector2, float)`（line 786-795）
- `HideTargetOrEnterNextStage(StaticEntityPlaceData)`（line 723-741）
- `SwitchShowSkillTalent(int, EntityData)`（line 816-929）
- `ShowAttackRangeAttributes((int, int)[])`（line 947-1006）
- `CostSliderAndCanSetNumUpdate()`（line 930-946）

- [ ] **Step 1: 移动所有 6 个方法**

按上述顺序粘贴到 `#region Helpers` 内。

- [ ] **Step 2: 编译验证**

期望：无新编译 error。

- [ ] **Step 3: Commit**

```bash
cd e:/Unity/projects/TD
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
git commit -m "refactor(LevelMessagePanel): move 6 helper methods into Helpers region"
```

---

## Task 12: 删除 //================================= 装饰行

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs`

**目标**：把文件中残留的 `//=================================` 行替换为正式 `#region` 标签或删除（如果已经被 #region 替代）。

- [ ] **Step 1: grep 装饰行残留**

```bash
cd e:/Unity/projects/TD
grep -n "//===" Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
```

如果上一阶段（Task 2-11）的"按 region 移动"做对了，**大部分装饰行已经自然消失**（它们被随方法一起移到了新位置，但因为新位置已有 #region 标签，重复的装饰行需要删除）。

- [ ] **Step 2: 手动处理残留装饰行**

对于每个 `//=================================` 行：
- 如果它紧邻一个 `#region` 块：删除装饰行
- 如果它是某个方法体内的旧分隔线：删除（这些是原代码用 `//===` 切分方法内不同阶段，spec 已决定统一用 region，无需保留）

⚠️ **特别注意 line 1043-1102** 的 `UIStates_Update_Leftmessage` 方法体内装饰行——这些是原方法**内**的章节分隔，不影响外层 region。逐一删除。

- [ ] **Step 3: 最终 grep 验证**

```bash
cd e:/Unity/projects/TD
grep -n "//===" Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
```

期望：**无匹配**。

- [ ] **Step 4: 编译验证**

Unity Editor 重新编译。
期望：无新编译 error。

- [ ] **Step 5: Commit**

```bash
cd e:/Unity/projects/TD
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
git commit -m "refactor(LevelMessagePanel): remove //===== decoration lines

All sections now marked with proper #region tags."
```

---

## Task 13: 最终验证

**Files:**
- Verify only: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs`

- [ ] **Step 1: 全部 grep 验证通过**

```bash
cd e:/Unity/projects/TD
echo "=== Renamed symbols (should be 0) ==="
grep -nE "\b_admb\b|\b_self_a\b|\b_range_a\b|\bCaculateCost\b|\b_selectorRectTransform\b|\bStaticId\b|\bStaticEntityData\b" Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs | grep -v "//"
echo "=== Decoration lines (should be 0) ==="
grep -n "//===" Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
echo "=== Field declarations count ==="
grep -cE "^\s+private " Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
```

期望：
- 第一个 grep: 无匹配
- 第二个 grep: 无匹配
- 第三个 grep: 与重构前基本一致（约 80-90 个字段声明）

- [ ] **Step 2: 行数检查**

```bash
cd e:/Unity/projects/TD
wc -l Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
```

期望：与重构前（1811）相差 ±5%，应略少。

- [ ] **Step 3: 公共 API 签名未变**

```bash
cd e:/Unity/projects/TD
grep -nE "public (void|[A-Z]\w+) (Panel|OnEnter|OnExit|OnPause|InitializeStaticEntityPrefabToSelector|AddStaticEntityPrefabToSelector|ShowText|EntityBackToSelector|AcceptDamageMessage|CostTextUpDate|CanSetNumUpDate|LevelHpLeftTextUpdate|CurrentNumAndTotalNumUpdate|ReSelectOrSetStaticEntity)" Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
```

期望：13 个 public 方法/属性全部匹配上。

- [ ] **Step 4: 编译验证**

Unity Editor 完整重编一次。
期望：除 line 1049、line 1115 之外的**新错误为 0**。

- [ ] **Step 5: Unity 手玩流程（用户执行）**

启动一个关卡，依次操作：
1. 选中底部某个 placeData → 看到 leftmessage + 可放置范围高亮
2. 拖拽 placeData → 进入 setting 状态
3. 拖到合法格子 → target 变黄 → 松手 → 进入 choosing 状态
4. 在 chooser 上拖拽选择朝向 → 松手
5. 部署完成 → 进入 normal 状态
6. 选中已部署的 entity → leftmessage + operator + range
7. 点 retreat → 资源增加 + entity 消失
8. 选有技能的 entity → 释放技能 → 状态变化
9. 暂停/2X 切换
10. 触发伤害/治疗/费用飘字

每步表现与重构前**完全一致**。

- [ ] **Step 6: 最终 commit（如有未提交改动）**

```bash
cd e:/Unity/projects/TD
git status
# 如果有未提交改动：
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
git commit -m "refactor(LevelMessagePanel): final cleanup pass

- All renames verified via grep
- All decoration lines removed
- Public API signatures preserved
- Behavior verified via manual play test"
```

---

## 完成标准

| 指标 | 期望 |
|---|---|
| 文件行数 | 1811 ± 5% |
| 公共 API 数量 | 13 个（保持不变） |
| `#region` 块数 | 8 个顶层 region |
| `//=====` 装饰行 | 0 |
| 旧名（`_admb`/`_self_a`/`_range_a`/`CaculateCost`） | 0 |
| 新编译 error | 0（line 1049, 1115 的 pre-existing 不算） |
| 行为变化 | 0 |
