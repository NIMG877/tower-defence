# 统一"被查看实体"实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** LevelMessage 选中逻辑统一为唯一"被查看实体"（`Inspected` + `IsDeployed`），消除双源 fallback，并由此治本解决待部署干员 buff 不显示。

**Architecture:** `LevelMessageSelectionContext`（POCO，定义在 LevelMessagePanel.cs 内）持有唯一 `Entity Inspected`；`LevelMessagePlaceData` 持有池样本 `Entity Sample` 取代 `EntityStats`/`EntityVision` 碎片；EntityModule 三个 GetCurrentX 收敛为直读 `Inspected`；"仅场上"特性用 `IsDeployed` 门控。

**Tech Stack:** Unity 2022.3 / C#，EditMode 测试跑在 `AbilitySystem.Tests.Editor` asmdef（已引用 BasicScripts）。

**约束事实：**
- 项目无 InternalsVisibleTo，惯例是被测类型直接 public → `LevelMessageSelectionContext` 与 `LevelMessagePlaceData` 转 public（public 属性暴露 internal 类型是编译错，可见性必须成链放开）。
- `EntityID` 构造：`new EntityID(string idC, int idN)`；`Entity` 可 `AddComponent` 于 EditMode，`EntityData` 为可 set 的普通属性。
- Unity 编辑器可能开着（Temp/UnityLockfile），批处理测试被锁；最终验证步骤若被锁则交给用户在编辑器内执行。
- C# 程序集整体编译，Task 1-4 完成前不可编译——中间不提交，最后统一验证后提交。

**设计文档：** `docs/superpowers/specs/2026-09-05-unify-inspected-entity-design.md`

---

### Task 1: SelectionContext 重构 + EditMode 测试

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs:8-93`（`LevelMessageSelectionContext` 与 `LevelMessageViewLookup` 所在）
- Create: `Assets/Tests/Editor/AbilitySystem/LevelMessageSelectionContextTests.cs`

- [ ] **Step 1: 写失败测试**（新文件）

```csharp
using NUnit.Framework;
using UnityEngine;
using MyUI;

namespace AbilitySystem.Tests
{
    /// <summary>
    /// 统一"被查看实体"上下文契约：Inspected 唯一来源、IsDeployed 区分场上场下、
    /// SelectedStaticEntityID 由 Inspected 派生。SelectPlace 依赖 PlaceData 的
    /// 选择器视图层级，EditMode 无法构造，归 PlayMode 手动清单。
    /// </summary>
    public class LevelMessageSelectionContextTests
    {
        private LevelMessageSelectionContext _context;
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _context = new LevelMessageSelectionContext();
            _go = new GameObject("inspected-subject");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void FreshContext_HasNoSelection()
        {
            Assert.That(_context.HasSelection, Is.False);
            Assert.That(_context.SelectedStaticEntityID, Is.Null);
            Assert.That(_context.IsDeployed, Is.False);
        }

        [Test]
        public void SelectEntity_MarksInspectedDeployedAndDerivesID()
        {
            Entity entity = _go.AddComponent<Entity>();
            EntityID id = new EntityID("c3", 3);
            entity.EntityData = new EntityData { ID = id };

            _context.SelectEntity(entity);

            Assert.That(_context.Inspected, Is.EqualTo(entity));
            Assert.That(_context.IsDeployed, Is.True);
            Assert.That(_context.HasSelection, Is.True);
            Assert.That(_context.SelectedStaticEntityID, Is.EqualTo(id));
            Assert.That(_context.SelectedPlaceData, Is.Null);
        }

        [Test]
        public void Clear_ResetsInspection()
        {
            Entity entity = _go.AddComponent<Entity>();
            entity.EntityData = new EntityData { ID = new EntityID("c3", 3) };
            _context.SelectEntity(entity);

            _context.Clear();

            Assert.That(_context.HasSelection, Is.False);
            Assert.That(_context.SelectedStaticEntityID, Is.Null);
            Assert.That(_context.IsDeployed, Is.False);
        }
    }
}
```

- [ ] **Step 2: 重写 `LevelMessageSelectionContext`**（LevelMessagePanel.cs 内，替换 L21-76 的整个类）

```csharp
    /// <summary>
    /// The single "inspected entity" plus placement-preview state, shared by
    /// deployment and selected-entity presentation. State transitions are owned
    /// by <see cref="LevelMessagePanel"/>.
    /// Inspected：部署前 = 待部署槽位的池样本实体（与 CallOut 派出的是同一
    /// GameObject，Stats/Vision/level buff 可读），部署后 = 场上实体；
    /// IsDeployed 区分二者。SelectedPlaceData 仍归属"卡片"（数量/费用/技能索引），
    /// 与被查看实体正交。public 供 EditMode 测试直接断言（测试 asmdef 无
    /// InternalsVisibleTo）。
    /// </summary>
    public sealed class LevelMessageSelectionContext
    {
        public LevelMessagePlaceData SelectedPlaceData { get; private set; }
        public Entity Inspected { get; private set; }
        public bool IsDeployed { get; private set; }
        public bool HasSelection => Inspected != null;
        public EntityID? SelectedStaticEntityID => Inspected?.EntityData.ID;
        public int Orientation { get; private set; } = -1;
        public Vector2 PreviewPosition { get; private set; }
        public bool HasPreviewPosition { get; private set; }

        public void SelectPlace(LevelMessagePlaceData placeData)
        {
            SelectedPlaceData = placeData;
            Inspected = placeData.Sample;
            IsDeployed = false;
            Orientation = -1;
            PreviewPosition = default;
            HasPreviewPosition = false;
        }

        public void SelectEntity(Entity entity)
        {
            SelectedPlaceData = null;
            Inspected = entity;
            IsDeployed = true;
            Orientation = -1;
            PreviewPosition = default;
            HasPreviewPosition = false;
        }

        public void Clear()
        {
            SelectedPlaceData = null;
            Inspected = null;
            IsDeployed = false;
            Orientation = -1;
            PreviewPosition = default;
            HasPreviewPosition = false;
        }

        public void SetOrientation(int orientation)
        {
            Orientation = orientation;
        }

        public void SetPreview(Vector2 position)
        {
            PreviewPosition = position;
            HasPreviewPosition = true;
        }

        public void ClearPreview()
        {
            PreviewPosition = default;
            HasPreviewPosition = false;
        }
    }
```

注意：`LevelMessagePlaceData` 此时还是 internal → 编译错（public 属性暴露 internal 类型）。Task 2 Step 1 放开。

### Task 2: LevelMessagePlaceData（Sample 取代碎片属性）

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessageDeploymentModule.cs:13-194`

- [ ] **Step 1: 类转 public**——L13 `internal sealed class LevelMessagePlaceData` → `public sealed class LevelMessagePlaceData`（构造函数保持 internal：仅 DeploymentModule 构造）。

- [ ] **Step 2: 属性区改造**——删除 L36-37 的 `EntityStats`/`EntityVision`，加 `Sample`：

```csharp
        public EntityID EntityId { get; private set; }
        /// <summary>该干员本次战斗携带的技能在 EntityData.Skills 中的索引（来自编队存档）。</summary>
        public int SkillIndex { get; private set; }
        public EntityData EntityData { get; private set; }
        /// <summary>
        /// 池样本实体：与 CallOut 派出的是同一 GameObject。部署前详情的
        /// Stats/Vision/level buff 数据源（数值为 store 终值，含 OnPreWarm 落的局内 buff）。
        /// </summary>
        public Entity Sample { get; private set; }
        public bool IsAffordable { get; private set; }
```

- [ ] **Step 3: Initialize 取样本处**（L61-70）改为：

```csharp
            EntityPool pool = EntityPoolManager.Manager.FetchEntityPool(staticId);
            if (pool != null)
            {
                Sample = pool.GetEntity();
            }
```

- [ ] **Step 4: CalculateCost 改读 Sample**（L84-96，三处 `EntityStats.CostS` → `Sample.Stats.CostS`）：

```csharp
        public int CalculateCost()
        {
            // 费用读 AttributeStore 终值（CostS），modifier 才能作用于部署费
            if (EntityData.RespawnCostUp <= 0)
                return Sample.Stats.CostS;

            float multiplier = 1 + EntityData.RespawnCostUp / 100;
            if (_deployCount == 0)
                return Sample.Stats.CostS;
            if (_deployCount == 1)
                return (int)(Sample.Stats.CostS * multiplier);
            return (int)(Sample.Stats.CostS * multiplier * multiplier);
        }
```

### Task 3: LevelMessageEntityModule 收敛

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessageEntityModule.cs`

- [ ] **Step 1: ShowLeftMessage（L201-209）**——门控与取数直读：

```csharp
            if (!_context.HasSelection)
                return;

            EntityData entityData = _context.Inspected.EntityData;
            SwitchDetailsPage(_currentDetailsPage, entityData, _context.Inspected);
```

- [ ] **Step 2: UpdateLeftMessage（L212-236）**——删 `GetCurrentEntityStats` 回退三元：

```csharp
        public void UpdateLeftMessage()
        {
            if (!_context.HasSelection)
                return;

            EntityStats stats = _context.Inspected.Stats;
            float attack = stats.AttackS;
            float defense = stats.DefS;
            float magicResistance = stats.MagicResistanceS;
            int block = stats.BlockOccupationS;
            _statsText.text =
                $"攻击  {(int)attack}\n防御  {(int)defense}\n法抗  {(int)magicResistance}\n阻挡  {block}";

            float maxHp = stats.MaxHpS;
            float currentHp = stats.IsActive ? stats.CurrentHp : maxHp;
```

（后续 hpSlider/hpText 行不变；方法尾部 `entityData` 若无他用一并清理。）

- [ ] **Step 3: ShowOperator（L254-256）**——门控改语义：

```csharp
            if (!_context.IsDeployed)
                return;
            Entity entity = _context.Inspected;
```

- [ ] **Step 4: 详情页签点击（L431-433）**：

```csharp
                    if (_currentDetailsPage == page || !_context.HasSelection)
                        return;
                    SwitchDetailsPage(page, _context.Inspected.EntityData, _context.Inspected);
```

- [ ] **Step 5: 删除 `GetCurrentEntityStats`（L478-483）与 `GetCurrentEntityData`（L485-490）两个方法**。

- [ ] **Step 6: GetCurrentWorldRange（L492-523）整体替换**：

```csharp
        private (int x, int y)[] GetCurrentWorldRange()
        {
            if (!_context.HasSelection)
                return null;

            if (_context.IsDeployed)
                return _context.Inspected.Vision?.Range;

            if (_context.Orientation != -1 && _context.HasPreviewPosition)
            {
                var baseRange = _context.Inspected.Vision?.BaseRange;
                if (baseRange != null)
                {
                    Vector2 position = _context.PreviewPosition;
                    (int x, int y) tilePosition =
                        ((int)(position.x + 0.5), (int)(position.y + 0.5));
                    return MapDataManager.Manager.RangeCaculator(
                        baseRange,
                        tilePosition,
                        _context.Orientation);
                }
            }

            List<Vector2Int> baseVisionRange = _context.Inspected.EntityData.VisionRange;
            if (baseVisionRange == null)
                return null;
            var result = new (int x, int y)[baseVisionRange.Count];
            for (int i = 0; i < baseVisionRange.Count; i++)
                result[i] = (baseVisionRange[i].x, baseVisionRange[i].y);
            return result;
        }
```

- [ ] **Step 7: ShowSkillRange（L529）/ GetCurrentDisplayRange Skill 分支（L550-557）**——`_context.SelectedEntity == null` → `!_context.IsDeployed`，`Entity entity = _context.Inspected;`。

- [ ] **Step 8: ShowBuffDetails（L768-772）**——部署前 buff 显示的落点：

```csharp
        private void ShowBuffDetails(Entity entity)
        {
            List<Buff> buffs = entity.buffController.Buffs;
```

（池化实体契约：`CreateNewEntity` 必 AddComponent `BuffController` 且 `Entity.PreWarm` 已缓存——不加判空，契约破坏就崩。）

- [ ] **Step 9: ShowAbilityDetails（L706-724）**：

```csharp
        private void ShowAbilityDetails(EntityData entityData, Entity entity)
        {
            AbilitySystem.AbilityRuntime runtime = null;
            AbilitySystem.AbilityConfig config = null;
            if (_context.IsDeployed)
            {
                runtime = GetPrimarySkill(entity);
                if (runtime != null)
                    config = runtime.config;
            }
            else if (entityData.Skills != null && entityData.Skills.Count > 0)
            {
                // 部署前预览:显示该干员本次携带(编队选择)的技能。样本实体的
                // SelectedSkillIndex 恒为默认 0,不代表编队选择,故走卡片配置。
                config = entityData.Skills[_context.SelectedPlaceData.SkillIndex];
            }

            _abilityCard.AbilityRT.gameObject.SetActive(config != null);
            if (config != null)
                _abilityCard.UpdateAbilityCardMessage(config, runtime);
        }
```

- [ ] **Step 10: ShowTalentDetails（L734-751）**：

```csharp
        private void ShowTalentDetails(EntityData entityData, Entity entity)
        {
            AbilitySystem.AbilityConfig[] talents;
            if (_context.IsDeployed)
            {
                var runner = entity.AbilityRunner;
                talents = new AbilitySystem.AbilityConfig[runner.Talents.Count];
                for (int i = 0; i < runner.Talents.Count; i++)
                    talents[i] = runner.Talents[i].config;
            }
            else
            {
                // 部署前预览:走卡片配置(理由同 ShowAbilityDetails)
                talents = entityData.Talents != null
                    ? entityData.Talents.ToArray()
                    : System.Array.Empty<AbilitySystem.AbilityConfig>();
            }
            UpdateTalentCards(talents);
        }
```

### Task 4: LevelMessagePanel 简化

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs`

- [ ] **Step 1: SwitchToViewAfterSet（L301-307）**：

```csharp
        private void SwitchToViewAfterSet(Entity entity)
        {
            _hud.SetSlow(true);
            _selection.SelectEntity(entity);
            ApplyStateView(LevelMessageUIState.ViewAfterSet);
            EnterStateAndStartUpdates(LevelMessageUIState.ViewAfterSet);
        }
```

- [ ] **Step 2: 空白点击调用点（L417）**：`SwitchToViewAfterSet(entity.EntityData.ID, entity);` → `SwitchToViewAfterSet(entity);`

- [ ] **Step 3: OnResume（L261-269）**：

```csharp
            if (_currentState == LevelMessageUIState.ViewAfterSet &&
                !_selection.Inspected.Stats.IsActive)
            {
                SwitchToNormal();
            }
            else
            {
                ApplyStateView(_currentState);
            }
```

- [ ] **Step 4: UpdateCurrentState ViewAfterSet 分支（L364-370）**：

```csharp
                case LevelMessageUIState.ViewAfterSet:
                    if (!_selection.Inspected.Stats.IsActive)
                    {
                        SwitchToNormal();
                        return;
                    }
```

### Task 5: 验证 + 提交

- [ ] **Step 1: 批处理测试**（若编辑器未开）：

```bash
"E:/UnityHub/Editor/2022.3.62f3/Editor/Unity.exe" -batchmode -projectPath "E:/Unity/projects/TD" -runTests -testPlatform EditMode -testResults E:/Unity/projects/TD/Temp/test-results.xml -logFile E:/Unity/projects/TD/Temp/test-run.log; echo "exit=$?"
```

期望：新增 3 个 SelectionContext 测试 PASS；基线失败仍为 22 个（MapData×15 / AbilitySystem×7，与 stash 基线对照确认无新增）。若 `Temp/UnityLockfile` 存在（编辑器开着）→ 跳到 Step 2。

- [ ] **Step 2: 用户编辑器内验证**（编辑器开着时的路径）：Test Runner 跑 `LevelMessageSelectionContextTests`（3 用例 PASS）+ PlayMode 清单：
  1. 进关 → 点待部署干员卡片 → Buff 页出现 level buff（`catap_t1_cost / Cost: -1.000`）
  2. 数值页/技能页/天赋页内容与重构前一致（技能为编队选中项）
  3. 拖放部署（含方向选择）→ 费用 -1 生效 → 场上点选：操作区/技能范围/HP/数值正常
  4. 拖拽朝向时的范围预览（旋转随朝向变化）
  5. 回收（callback）→ 卡片数量回加 → 再部署正常
  6. 干员死亡 → ViewAfterSet 自动回 Normal

- [ ] **Step 3: 提交**（验证通过后）：

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs \
        Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessageEntityModule.cs \
        Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessageDeploymentModule.cs \
        Assets/Tests/Editor/AbilitySystem/LevelMessageSelectionContextTests.cs
git commit -m "重构：LevelMessage 统一被查看实体（Inspected + IsDeployed）" -- <同上路径>
```
