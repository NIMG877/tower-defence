# SPSlider 适配新 Skill 系统

**Date**: 2026-06-08
**Status**: Draft
**Author**: claude (brainstorm with user)
**Supersedes**: 旧 `Skill.SkillMessage` 数据源

## 背景

`SpSliderController` 当前通过 `_hostEntity.skill[0].SkillMessage` 读取 SP 数据。
`Entity.skill[]` 字段为 `[HideInInspector]`，运行时无任何代码路径填充它
(grep `Assets/PublicScripts` 全局未发现对 `entity.skill` 的写入)，所以
`Entity.Initialize` 里的 `if (skill != null && skill.Length == 1)` 永远为 false，
SPSlider 从未被实例化。

与此同时，技能系统已完成 Phase 2 迁移至新架构
(`SkillRuntime` / `SkillRunner` / `SPEngine` / `SkillConfig`)，
新架构在 `SkillRunner.Skills` 暴露了完整的运行时数据。

本次任务：**让 SPSlider 读新系统、旧 Skill 字段/类保留但不修**。

## 范围

- **修改**:
  - `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SPEngine.cs`
  - `Assets/PublicScripts/Entity-LevelPublicScripts/UI/SliderControllerBasic.cs`
  - `Assets/PublicScripts/Entity-LevelPublicScripts/UI/SpSliderController.cs`
  - `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs`
- **不修改**:
  - `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Skill.cs` (旧)
  - `Assets/PublicScripts/Entity-LevelPublicScripts/UI/SlidersManager.cs`
  - `Assets/PublicScripts/Entity-LevelPublicScripts/UI/HpSliderController.cs`
  - `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRunner.cs`
  - `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs`
  - `Assets/PublicScripts/GameData/SkillSystem/*` (新系统数据类)
  - `Assets/Resources/Prefabs/UI/Sliders/SPSlider_normal.prefab`

## 数据流

```
EntityData.Skills[i] : SkillConfig
        │
        │ SkillRunner.PreWarm 里 BuildSkillRuntime
        ▼
SkillRunner._skills[i] : SkillRuntime
        │
        ├─ spEngine : SPEngine
        │     ├─ CurrentSp / cfg.totalSp        → 充能进度
        │     ├─ CurrentCharge / cfg.chargeNum  → 多充能档
        │     ├─ IsActive                       → 旧 isSkill
        │     └─ CurrentDuration / cfg.skillDuration → 激活期倒计时 (本次新增)
        ├─ config.sp : SPConfig
        └─ components / blackboard (本任务不读)
```

## 设计

### 1. SPEngine.cs — 暴露 `CurrentDuration`

在 [SPEngine.cs:20-23](Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SPEngine.cs#L20-L23)
的现有 `=>` 只读块下追加一行:

```csharp
public float CurrentDuration => _currentDuration;
```

零行为变化。`_currentDuration` 在 `FireSkill` 时被设为 `cfg.skillDuration`、在
`OnTick` / `EndSkill` 中递减或清零，读取即可。

### 2. SliderControllerBasic.cs — `SetHostEntity` 改 `virtual`

[SliderControllerBasic.cs:28](Assets/PublicScripts/Entity-LevelPublicScripts/UI/SliderControllerBasic.cs#L28)
的方法签名从 `public void` 改为 `public virtual void`。

子类 (`HpSliderController` / `SpSliderController`) 默认行为不变；只有需要
`override` 的子类才动。`SlidersManager.SetSlider<T>` 通过 `T sc = new T();`
拿到子类，调的是 `SetHostEntity` 的多态分派 —— virtual 化后多态分派正常工作。

### 3. SpSliderController.cs — 改读新系统

#### 3.1 新增字段

```csharp
private SkillSystem.SkillRunner _skillRunner;
```

#### 3.2 覆写 `SetHostEntity` 缓存引用

```csharp
public override void SetHostEntity(Entity hostEntity, float smoothSpeed, int type,
    int positionLayer, bool hideWhenFull, bool moveSlider)
{
    base.SetHostEntity(hostEntity, smoothSpeed, type, positionLayer, hideWhenFull, moveSlider);
    hostEntity.TryGetComponent<SkillSystem.SkillRunner>(out _skillRunner);
}
```

#### 3.3 重写 `SetRateOperations` 读新系统

替换 [SpSliderController.cs:16-28](Assets/PublicScripts/Entity-LevelPublicScripts/UI/SpSliderController.cs#L16-L28)
的方法体。等价于旧 `Skill.SkillMessage` 的三返回值 `(currentSpRate, currentChargeNum, isSkill)`:

```csharp
protected override void SetRateOperations()
{
    if (_skillRunner == null || _skillRunner.Skills.Count == 0) { SetRate(0f); return; }
    var runtime = _skillRunner.Skills[0];
    var sp = runtime.spEngine;
    if (sp == null) { SetRate(0f); return; }

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
        if (cfg == null || cfg.totalSp <= 0) { SetRate(0f); return; }
        rate = Mathf.Clamp01(sp.CurrentSp / cfg.totalSp);
    }

    _fill.color = isActive ? _skillFillColor : _normalFillColor;
    SetRate(rate);
}
```

> **行为对齐旧 `Skill.SkillMessage`**:
> - `isSkill` ↔ `sp.IsActive`：旧版 `_currentSkillAmount > 0` 等价新 `IsActive=true`
>   (都在 `FireSkill` 入口置位、`EndSkill` 出口清零)。
> - 激活期显示 `CurrentDuration/skillDuration`（旧版 `_currentSkillAmount/_skillAmount`），
>   倒计时方向一致 (从 1 倒数到 0)。
> - 非激活期显示 `CurrentSp/totalSp`（旧版 `_currentSp/_totalSp`），
>   多充能下 `SPEngine.AddSp` 已把超出 `totalSp` 的部分扣到下一档并 `CurrentCharge++`，
>   与旧 `Skill.SpRecover` 行为一致。

#### 3.4 `SliderInitialize` 不动

`SkillFill` / `Fill` 子节点查找沿用旧逻辑；prefab 结构未改。

### 4. Entity.cs — 切换创建条件

[Entity.cs:226-229](Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs#L226-L229):

```csharp
// 旧
if (skill != null && skill.Length == 1)
{
    SlidersManager.Manager.SetSlider<SpSliderController>(this, 10, camp2 ? 3 : 4, 1, camp2, canmove);
}
```

```csharp
// 新
if (EntityData != null && EntityData.Skills != null && EntityData.Skills.Count == 1)
{
    SlidersManager.Manager.SetSlider<SpSliderController>(this, 10, camp2 ? 3 : 4, 1, camp2, canmove);
}
```

`Entity.skill` 字段保留不动;只是 Initialize 的判断从"旧字段"切到"新数据"。
如果未来旧字段被填，旧分支也能继续工作（虽然没人填）。

> **多技能行为**: 严格沿用旧 `skill.Length == 1` 语义 —— `Skills.Count == 1` 才创建。
> 多技能实体不显示 SPSlider（与旧行为一致）。后续若要支持多技能 slider，
> 是独立的扩展任务。

## 错误处理 / 边界

| 情况 | 行为 |
|---|---|
| 实体未挂 `SkillRunner` | `_skillRunner == null` → `SetRate(0f)` 返回，颜色保留上一帧（通常 `_normalFillColor`）|
| `SkillRunner.Skills` 为空 | 同上 |
| `Skills[0].spEngine == null` (配置无 `sp`) | 同上 |
| `config.sp.totalSp <= 0` 或 `config.sp == null` | 非激活期 `SetRate(0f)` 返回 |
| 多技能实体 | 仅读 `Skills[0]`，与旧 `skill[0]` 行为一致；多技能扩展以后再做 |
| `currentDuration > skillDuration`（数据错位）| `Mathf.Clamp01` 兜底 |
| 实体被回收 / 失活 | `SliderControllerBasic.FixedUpdate` 已有 `Stats.IsActive` 检查统一处理，slider 回池 |

## 测试策略

本项目无 Unity Test Framework 单元测试覆盖此层 (grep `Tests/` 未发现相关套件)，
沿用项目惯例：手动运行 PlayMode 验证。

**PlayMode 验证清单**:
1. **充能期显示**: 部署一个我方带 `SkillRunner` 的角色（含 SPConfig）。
   - 初始 SP=0 → SPSlider fill=0, 颜色=normal
   - 自然充能到 totalSp → fill 升到 1, 颜色=normal (因 `hideWhenFull=!camp2`=true 不隐藏我方)
2. **激活期显示**: 调小 `totalSp` 让 `Auto` openMode 立刻开火。
   - `IsActive=true` 期间 → 颜色=skill, fill 从 1 倒数到 0
   - `EndSkill` 后 → 颜色切回 normal, fill=0 (因为 SP 在开火时被扣到 0)
3. **多充能**: `chargeNum=2` + `totalSp=10` 的配置。
   - 充能至 charge 满 (`CurrentCharge=2, CurrentSp=0`) → fill=0, 颜色=normal
     （沿用旧 `Skill.SkillMessage` 行为：rate 只看 `currentSp/totalSp`，不看 charge 数）
   - 开火一次（`charge>0` 走 charge-- 路径）→ `CurrentCharge=1, CurrentSp=0`，
     active 期间 fill=0→... ,颜色=skill
   - EndSkill 后 → `IsActive=false`,fill=0,颜色切回 normal
4. **死亡回池**: 实体死亡 → SPSlider 走 `ReturnSlider` 回池复用。
5. **无 SPConfig**: 部署一个 `Skills[0]` 配置无 sp 的角色。
   - 不应出现 NRE；fill=0 保持，slider 仍在画面上但不可见（无 fill）。

## 风险

- **HP slider 误改**: `SliderControllerBasic.SetHostEntity` 改 `virtual` 涉及所有子类。
  风险面只有 `HpSliderController` 和 `SpSliderController` 两个子类。两者均不覆写
  该方法 → 行为不变。
- **多技能扩展**: 本次仅 `Skills[0]`，与旧 `skill[0]` 一致；多技能 slider 扩展是
  后续独立任务。
- **新系统未触发 `EndSkill` 的边界**: `SPEngine.EndSkill` 在 `_currentDuration <= 0`
  时调用，`IsActive` 在下一帧前已是 false。slider 下一帧自然切回充能分支，无残留帧。

## 不在范围

- 旧 `Skill.cs` 中任何修复（用户要求保留不修）
- 把 `Entity.skill[]` 桥接到 `SkillRunner.Skills`（需要修旧系统）
- 多技能场景下 SPSlider 的多档显示
- SPSlider 视觉（颜色 / 大小 / 位置算法）的调整 —— 完全沿用旧逻辑
- HPSlider 与新系统的任何集成
