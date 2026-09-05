# SPSlider 适配新 Skill 系统 Implementation Plan

> 文档状态：历史实施计划存档，非当前有效文档。当前实现以代码与 docs/ 现行文档为准。


> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 `SpSliderController` 读取新 `SkillRuntime.spEngine` 的 SP / 激活窗口数据，使 SPSlider 正常显示，旧 `Skill.cs` 与 `Entity.skill[]` 字段保留不动。

**Architecture:** 在 `SPEngine` 上加一个只读 `CurrentDuration` getter；基类 `SliderControllerBasic.SetHostEntity` 改 `virtual`；`SpSliderController` 覆写 `SetHostEntity` 缓存 `SkillRunner` 引用并重写 `SetRateOperations` 改读 `spEngine`；`Entity.Initialize` 把创建条件从 `skill.Length == 1` 切到 `EntityData.Skills.Count == 1`。

**Tech Stack:** Unity 2022+ / C# 7.3+ / 新 `SkillSystem` 命名空间（已存在）

---

## File Structure

| 文件 | 角色 | 改动 |
|------|------|------|
| `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SPEngine.cs` | 暴露 `_currentDuration` 给 slider 读 | +1 行 getter |
| `Assets/PublicScripts/Entity-LevelPublicScripts/UI/SliderControllerBasic.cs` | 让子类能覆写 `SetHostEntity` | 方法签名 +`virtual` |
| `Assets/PublicScripts/Entity-LevelPublicScripts/UI/SpSliderController.cs` | 改读新系统的 SP / 激活数据 | +1 字段、override `SetHostEntity`、重写 `SetRateOperations` |
| `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs` | 创建条件切到新数据源 | 改 1 行条件 |

**不动的文件**：`Skill.cs`、`SlidersManager.cs`、`HpSliderController.cs`、`SkillRunner.cs`、`SkillRuntime.cs`、`EntityData.cs`、prefab。

---

## Task 1: 在 SPEngine 上暴露 `CurrentDuration`

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SPEngine.cs:20-23`

- [ ] **Step 1: 在 `CurrentSp` / `CurrentCharge` / `IsActive` / `IsRecoverForbidden` 那个 `=>` 只读块下追加一行**

打开 [SPEngine.cs](Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SPEngine.cs)，定位到这一段（当前行 20-23）：

```csharp
public float CurrentSp => _currentSp;
public int CurrentCharge => _currentCharge;
public bool IsActive => _isActive;
public bool IsRecoverForbidden => _recoverForbid > 0;
```

在 `IsRecoverForbidden` 这一行**下方**新增一行：

```csharp
public float CurrentDuration => _currentDuration;
```

最终结果：

```csharp
public float CurrentSp => _currentSp;
public int CurrentCharge => _currentCharge;
public bool IsActive => _isActive;
public bool IsRecoverForbidden => _recoverForbid > 0;
public float CurrentDuration => _currentDuration;
```

- [ ] **Step 2: 确认未改动任何写入路径**

打开文件，搜索 `_currentDuration` 的所有出现位置：
- `_currentSp = _cfg.initialSp;`（构造，OK）
- `_currentDuration = 0f;`（构造 + EndSkill，OK）
- `_currentDuration -= dt;`（OnTick 自然消耗，OK）
- `_currentDuration <= 0f`（OnTick 判等，OK）
- `_currentDuration = _cfg.skillDuration;`（FireSkill，OK）

应该没有任何被改动。grep 验证：

```bash
grep -n "_currentDuration" Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SPEngine.cs
```

预期输出（行号可能因你刚才插入的 getter 多 1 而偏移）：

```
10:        private float _currentDuration;
30:            _currentDuration = 0f;
44:            _currentDuration -= dt;
45:            if (_currentDuration <= 0f)
47:                _currentDuration = 0f;
100:            if (_cfg.skillDuration > 0f) _currentDuration = _cfg.skillDuration;
111:            _currentDuration = 0f;
```

并且应该在公开属性块（行 20 附近）看到新加的 `public float CurrentDuration => _currentDuration;`。

- [ ] **Step 3: 编译验证（Unity Editor 已打开则等待其自动重编；否则打开工程等待编译）**

打开 Unity Editor，观察 Console 没有 `SPEngine.cs` 相关编译错误。

预期：仅可能因前置项缺失的旧错误（与本任务无关），**不应出现**关于 `CurrentDuration` 名称冲突或类型不匹配的错误。

- [ ] **Step 4: 提交**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SPEngine.cs
git commit -m "feat(skill-system): expose SPEngine.CurrentDuration for UI consumers"
```

---

## Task 2: 基类 `SetHostEntity` 改 `virtual`

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/UI/SliderControllerBasic.cs:28`

- [ ] **Step 1: 修改方法签名**

打开 [SliderControllerBasic.cs](Assets/PublicScripts/Entity-LevelPublicScripts/UI/SliderControllerBasic.cs)，定位到第 28 行（方法声明）：

```csharp
public void SetHostEntity(Entity hostEntity, float smoothSpeed, int type, int positionLayer, bool hideWhenFull, bool moveSlider)
```

将 `public void` 改为 `public virtual void`：

```csharp
public virtual void SetHostEntity(Entity hostEntity, float smoothSpeed, int type, int positionLayer, bool hideWhenFull, bool moveSlider)
```

> ⚠️ 严格只改这一行的 `public void` → `public virtual void`，**不要改方法体**。
> 方法体内 `value = 0; value_s = 0; _type = type; _hostEntity = hostEntity; ...` 这些语句保持原状。

- [ ] **Step 2: 确认无现存 `override SetHostEntity` 冲突**

```bash
grep -rn "override.*SetHostEntity" Assets/PublicScripts --include="*.cs"
```

预期输出：

```
（空 —— 当前没有类 override 它，本次新增的 SpSliderController.override 在 Task 3 才出现）
```

如果输出非空，先停手，回报冲突。

- [ ] **Step 3: 编译验证**

Unity Editor 重新编译。`HpSliderController` 不 override 该方法，行为不变 —— 这是关键不变量。

预期：编译通过。`HpSliderController` 的 SetRateOperations 仍能正常读取 `_hostEntity.Stats.CurrentHpRate`。

- [ ] **Step 4: 提交**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/UI/SliderControllerBasic.cs
git commit -m "refactor(ui): make SliderControllerBasic.SetHostEntity virtual"
```

---

## Task 3: 重写 `SpSliderController` 读新系统

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/UI/SpSliderController.cs`（整文件改写为下面的内容）

- [ ] **Step 1: 用下面的完整内容覆盖整个 SpSliderController.cs**

[SpSliderController.cs](Assets/PublicScripts/Entity-LevelPublicScripts/UI/SpSliderController.cs) 替换为：

```csharp
using UnityEngine;
using UnityEngine.UI;

public class SpSliderController : SliderControllerBasic
{
    private Color _skillFillColor;
    private Color _normalFillColor;
    private SkillSystem.SkillRunner _skillRunner;

    public override void SliderInitialize(GameObject sliderObject)
    {
        base.SliderInitialize(sliderObject);
        _skillFillColor = _slider.transform.Find("SkillFill").GetComponent<Image>().color;
        _normalFillColor = _slider.transform.Find("Fill").GetComponent<Image>().color;
    }

    public override void SetHostEntity(Entity hostEntity, float smoothSpeed, int type,
        int positionLayer, bool hideWhenFull, bool moveSlider)
    {
        base.SetHostEntity(hostEntity, smoothSpeed, type, positionLayer, hideWhenFull, moveSlider);
        hostEntity.TryGetComponent<SkillSystem.SkillRunner>(out _skillRunner);
    }

    protected override void SetRateOperations()
    {
        if (_skillRunner == null || _skillRunner.Skills.Count == 0)
        {
            SetRate(0f);
            return;
        }
        var runtime = _skillRunner.Skills[0];
        var sp = runtime.spEngine;
        if (sp == null)
        {
            SetRate(0f);
            return;
        }

        bool isActive = sp.IsActive;
        float rate;
        if (isActive)
        {
            var cfg = runtime.config != null ? runtime.config.sp : null;
            float skillDuration = cfg != null ? cfg.skillDuration : 0f;
            rate = skillDuration > 0f
                ? Mathf.Clamp01(sp.CurrentDuration / skillDuration)
                : 1f;
        }
        else
        {
            var cfg = runtime.config != null ? runtime.config.sp : null;
            if (cfg == null || cfg.totalSp <= 0)
            {
                SetRate(0f);
                return;
            }
            rate = Mathf.Clamp01(sp.CurrentSp / cfg.totalSp);
        }

        _fill.color = isActive ? _skillFillColor : _normalFillColor;
        SetRate(rate);
    }
}
```

- [ ] **Step 2: 编译验证**

观察 Unity Console。

预期：无编译错误。如果出现 `SkillSystem` 命名空间未引用，查看 `SpSliderController.cs` 顶部 using 块是否完整（保留 `using UnityEngine;` 和 `using UnityEngine.UI;` 即可，因为 `SkillSystem` 类型在 `SkillSystem` 命名空间下，已通过 `SkillSystem.SkillRunner` 全限定引用）。

- [ ] **Step 3: 静态检查旧字段引用已全部移除**

```bash
grep -n "_hostEntity.skill\|_hostEntity\.skill" Assets/PublicScripts/Entity-LevelPublicScripts/UI/SpSliderController.cs
```

预期输出：`（空）` —— 不应再有任何对 `_hostEntity.skill` 的访问。

- [ ] **Step 4: 提交**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/UI/SpSliderController.cs
git commit -m "feat(ui): SpSliderController reads from new SkillRuntime.spEngine"
```

---

## Task 4: Entity.Initialize 创建条件切换

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs:226-229`

- [ ] **Step 1: 定位并替换判断条件**

打开 [Entity.cs](Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs)，定位到 `Initialize()` 方法末尾（行 226-229）：

旧：

```csharp
if (skill != null && skill.Length == 1)
{
    SlidersManager.Manager.SetSlider<SpSliderController>(this, 10, camp2 ? 3 : 4, 1, camp2, canmove);
}
```

新：

```csharp
if (EntityData != null && EntityData.Skills != null && EntityData.Skills.Count == 1)
{
    SlidersManager.Manager.SetSlider<SpSliderController>(this, 10, camp2 ? 3 : 4, 1, camp2, canmove);
}
```

> 严格只改 `if` 条件这一行。**不要删** `public Skill[] skill;` 字段（旧系统保留不修）。
> **`canmove` / `camp2` 局部变量也不动**。

- [ ] **Step 2: 确认旧 `Skill[] skill` 字段仍在**

```bash
grep -n "public Skill\[\] skill" Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs
```

预期输出（行号可能因 Unity 重新生成有微小偏移）：

```
24:    [HideInInspector] public Skill[] skill;
```

字段仍在。修改没动到字段声明。

- [ ] **Step 3: 编译验证**

Unity 重新编译。

预期：无编译错误。`EntityData` 已在 [EntityData.cs:93](Assets/PublicScripts/GameData/EntityData/EntityData.cs#L93) 暴露 `List<SkillConfig> Skills`，`EntityData` 字段在 [Entity.cs:18](Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs#L18) 公开。

- [ ] **Step 4: 提交**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs
git commit -m "feat(entity): SPSlider creation gate uses EntityData.Skills (new system)"
```

---

## Task 5: PlayMode 验证（覆盖 spec §测试策略全部 5 个用例）

**Files:**
- 不修改任何代码；本任务是手动验证 + 必要时的小修。

> 本项目无 Unity Test Framework 自动化覆盖此层，沿用项目惯例：在 PlayMode 下肉眼/调试观察。
> 你需要进入 Unity Editor，Build Settings → Play 一次（如果关卡流程在 LevelXXX 场景下）。

- [ ] **Step 1: 用例 1 —— 充能期显示（我方角色）**

部署/选中一个我方（`Camp != 2`）角色，该预制体挂有 `SkillRunner` 组件且 `EntityData.Skills[0]` 配了带 `SPConfig` 的 `SkillConfig`。

观察 SPSlider 头顶表现：
- 进入关卡初始 `SP=0` → SPSlider fill=0, 颜色=normal (绿黄色)
- 等待自然充能 (`recoverMode=Natural` 时由 `Time.fixedDeltaTime` 推进) → fill 从 0 缓慢升到 1
- 因为我方 `hideWhenFull=false` (来自 `Entity.Initialize` 的 `camp2=false`)，fill=1 时 SPSlider 仍可见

预期：✅

- [ ] **Step 2: 用例 2 —— 激活窗口显示**

用例 1 的角色继续。配置改为小 `totalSp`（例如 5）让 `openMode=Auto` 几秒内就触发 `FireSkill`（`SPEngine.OnTick` 检测到 `CanBegin()` 就开火）。

观察：
- `IsActive=true` 期间 → fill 从 ~1 倒数到 0，颜色切到 skill 色（橙色）
- `currentDuration` 走到 0 → `EndSkill` 触发 → `IsActive=false`
- EndSkill 之后：`CurrentSp=0`（因为 `FireSkill` 在 charge=0 时把 SP 扣到 0）→ fill=0，颜色切回 normal

预期：✅

- [ ] **Step 3: 用例 3 —— 多充能**

配置改为 `chargeNum=2, totalSp=10`。
- 充能至 `CurrentCharge=2, CurrentSp=0`（`AddSp` 把超出 `totalSp` 的部分扣到 charge 档）→ fill=0, 颜色=normal

> 注意：与单充能"满时 fill=1"不同，多充能满档时 fill 仍为 0（rate 只看 `currentSp/totalSp`）。
> 这是与旧 `Skill.SkillMessage` 严格一致的行为，**不是 bug**。

- `openMode=Auto` 触发 `FireSkill`（charge>0 走 `charge--` 路径，`CurrentSp` 保持 0）→ active 期间 fill=0, 颜色=skill
- `EndSkill` 后 → `IsActive=false`, fill=0, 颜色=normal

预期：✅（fill 始终是 0 是预期行为，参见 spec §测试策略 #3）

- [ ] **Step 4: 用例 4 —— 死亡回池**

让用例 1 角色 HP 归零死亡。
- 观察 SPSlider 立即从 `FixedUpdate` 退出（`Stats.IsActive=false`）→ 走 `ReturnSlider()` 进池
- 重新部署一个同类角色（同一 `EntityData`）→ SPSlider 从池里复用，跟随新实体的 transform 移动

预期：✅（这是 `SliderControllerBasic.FixedUpdate` 既有行为，本任务未改动）

- [ ] **Step 5: 用例 5 —— 无 SPConfig 容错**

部署一个 `Skills[0]` 配置 `sp == null`（或 `sp.totalSp == 0`）的角色。
- SPSlider 应在 `SetRateOperations` 进入 `if (cfg == null || cfg.totalSp <= 0)` 分支调 `SetRate(0f)` 后返回
- 不应出现 NRE
- Slider 在画面上但 `Fill` 不可见（fill=0）
- 颜色：`_fill.color` 在早返回路径下保持 prefab 默认色（实际是 `SpSliderController.SliderInitialize` 读出来的 `_normalFillColor`，即 prefab 上 `Fill` 节点的初始颜色）

预期：✅（注意颜色没有"切到 normal"的步骤 —— 这是早返回的设计，`_fill.color` 是 prefab 上的初始色）

- [ ] **Step 6: 收尾 —— 任何用例失败时**

如果某个用例不符合预期：
1. 先看 Unity Console 是否有 NRE（最可能：`_skillRunner` 为 null 时漏判 / `cfg` 为 null 时漏判）
2. 用 `Debug.Log` 在 `SpSliderController.SetRateOperations` 入口打印 `_skillRunner?.Skills.Count` / `sp.IsActive` / `sp.CurrentSp` / `cfg.totalSp`
3. 定位后**回到对应 Task 修复**，单独提交 hotfix：
   ```bash
   git commit -am "fix(ui): SpSliderController early-return null guard for <case>"
   ```

- [ ] **Step 7: 全部通过后收尾**

```bash
git log --oneline -5
```

预期看到 4 个 commit（Task 1/2/3/4）按顺序排列：

```
<hash> feat(entity): SPSlider creation gate uses EntityData.Skills (new system)
<hash> feat(ui): SpSliderController reads from new SkillRuntime.spEngine
<hash> refactor(ui): make SliderControllerBasic.SetHostEntity virtual
<hash> feat(skill-system): expose SPEngine.CurrentDuration for UI consumers
```

无 hotfix commit 即说明一次过。无需再提交。

---

## Self-Review Notes

**Spec coverage**:
- §1 架构：4 个文件改动 —— Task 1/2/3/4 各覆盖一个
- §2 `SPEngine.CurrentDuration` —— Task 1
- §3 `SliderControllerBasic.SetHostEntity` virtual —— Task 2
- §4 `SpSliderController` 改读新系统（含缓存 `_skillRunner`、重写 `SetRateOperations`）—— Task 3
- §5 `Entity.Initialize` 创建条件切换 —— Task 4
- §错误处理 / 边界 —— Task 5 验证 5 个用例覆盖

**Placeholder scan**: 无 TBD/TODO/fill-in。

**Type consistency**:
- `SPEngine.CurrentDuration` 在 Task 1 定义为 `public float CurrentDuration => _currentDuration;`，Task 3 的 `sp.CurrentDuration` 类型一致 (`float`)
- `SkillSystem.SkillRunner` 命名空间引用在 Task 3 全限定使用 (`SkillSystem.SkillRunner`)，未引入额外 using（避免污染全局）
- `_fill.color` 来自基类 `SliderControllerBasic` 字段 `protected Image _fill`，旧 `SpSliderController.SetRateOperations` 已经在用，保持一致
- `_skillRunner` 字段类型 `SkillSystem.SkillRunner` 与 [SkillRunner.cs:6](Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRunner.cs#L6) `public class SkillRunner : MonoBehaviour` 一致
- `runtime.config.sp` 链：`SkillRuntime.config : SkillConfig`（[SkillRuntime.cs:7](Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs#L7)）→ `SkillConfig.sp : SPConfig`（[SkillConfig.cs:13](Assets/PublicScripts/GameData/SkillSystem/SkillConfig.cs#L13)）→ `SPConfig.totalSp` / `skillDuration`（[SPConfig.cs:14,19](Assets/PublicScripts/GameData/SkillSystem/SPConfig.cs#L14)）—— 类型一致
- `sp.IsActive` / `sp.CurrentSp` / `sp.CurrentCharge` 都是 `public` 属性，与 Task 3 引用一致

无冲突。
