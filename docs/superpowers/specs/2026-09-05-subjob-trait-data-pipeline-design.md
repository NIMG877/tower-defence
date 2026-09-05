# CharacterSubJob 子职业特性·数据读取链路 设计

> **日期：** 2026-09-05
> **状态：** 已确认（用户批准）
> **范围：** 仅数据读取链路。运行时生效（PreWarm 装配）与 UI 展示（SubpCard）为后续阶段，另立设计。

---

## 1. 背景与问题

`EntityData` 想为干员引入子职业（CharacterSubJob）概念：每个子职业有一个固有特性，与具体干员无关（黑键=秘术师：积攒攻击能量；翎羽=冲锋手：击杀敌人获得 1 费用；梓兰=凝滞师：攻击造成停顿）。

现状：

- `EntityData` 只有 `CharacterJob`（0=先锋,1=近卫,...,9=_），**没有** `CharacterSubJob` 字段。
- UI 已有 `DetailsPage.SubProfession` 页签与 `SubpCard`，但 `UpdateSubpCardMessage` 为空方法。
- 黑键特性的"积攒攻击能量"实现为 prefab 挂 `ChargeAttack`（AttackBase 子类）+ `ebnhlz_t1` 资产拼装，无统一模板位；翎羽/梓兰未实现。

**决策（用户定向）：** 子职业特性走 AbilitySystem，与 Skills/Talents 平行；`EntityData` 单字段存储（非列表）；由 Rebuild 工具按 `CharacterSubJob` 装载资产。共享模板表、运行时装配、UI 为后续阶段。

## 2. 数据字段（EntityData.cs）

```csharp
// 角色属性（当 entity 是 "c" 类别时使用）
public int    CharacterJob;          // 0=先锋,1=近卫,...,9=_
public int    CharacterSubJob;       // 0=无,1=秘术师,2=冲锋手,3=凝滞师,...（映射见 XLSX2DataAsset.ParseSubJob）

// 技能 / 天赋 / 子职业特性（静态数据驱动框架）
public List<AbilityConfig> Skills = new List<AbilityConfig>();
public List<AbilityConfig> Talents = new List<AbilityConfig>();
public AbilityConfig SubJobTrait;    // 子职业特性；Rebuild 工具按 CharacterSubJob 装载，运行时装配后续阶段接
```

- **`0=无`，实子职业从 1 开始**——有意不沿用 CharacterJob "末位=_"约定：xlsx 缺列/空格子经 `GetInt` 默认得 0，`0=无` 让"没有子职业"成为零配置默认值，怪物行零填写；若 0=第一个子职业，所有怪物会被静默赋予子职业 0。
- 单字段、类型 `AbilityConfig`（ScriptableObject 引用），位置在 `Talents` 之后。
- 同步更新 `Skills/Talents` 上方注释块，注明 `SubJobTrait` 的装载来源。

## 3. 资产目录与映射（XLSX2DataAsset.cs）

- 新文件夹 `Assets/Resources/Prefabs/Abilities/SubJobs/`，每个**已实现**的特性一个 `.asset`（AbilityConfig）。
- 子职业 id → 资产 Resources 路径的映射为工具内**显式字典**——字典即"已实现特性注册表"：

```csharp
static readonly Dictionary<int, string> SubJobAbilityPaths = new()
{
    [1] = "Prefabs/Abilities/SubJobs/mystic_t0",     // 秘术师：积攒攻击能量
    [2] = "Prefabs/Abilities/SubJobs/charge_t0",     // 冲锋手：击杀获得 1 费用
    [3] = "Prefabs/Abilities/SubJobs/binder_t0",     // 凝滞师：攻击造成停顿
};
```

- 选显式字典而非"按 id 命名资产"的约定：路径即注册表，一眼可查哪些特性已实现；资产名不受 id 约束。
- **不建** SO 定义表（YAGNI）：UI 阶段需要名称/图标元数据时再升级为定义表。

## 4. Rebuild 装载逻辑（XLSX2DataAsset.cs）

> **修订（2026-09-05，用户裁定）：** 任何子职业数据问题都不得打断 Rebuild——原"未知名
> throw 中断"与"ValidateSubJobs 校验 throw"取消，统一降为 LogWarning 跳过、继续装载。

xlsx 新列 `CharacterSubJob`，格子填中文子职业名（与 CharacterJob 列同风格），空 = 无。

1. `TryParseSubJob(string, out int)`：空 → (true, 0)；已知名 → (true, id)；未知名 → (false, 0)。
2. 装载（`ReadEntityData` 内经 `ResolveSubJobTrait`）：
   - 空 → 不装载（`SubJobTrait = null`，正常态，无日志）。
   - 未知名 → LogWarning 指名，按无子职业处理（`CharacterSubJob = 0`），继续装载。
   - id 已知但 `SubJobAbilityPaths` 无此 id、或 `Resources.Load` 落空 → LogWarning 指名，不装载——"子职业已登记、特性尚未实现/资产被挪走"是合法中间态。
3. "trait 已装载 ⇒ 子职业已登记"由 `ResolveSubJobTrait` 构造期保证，无独立校验步骤（原 ValidateSubJobs 已删）。

## 5. 占位资产与测试

- `SubJobs/` 下建 3 个空规则 AbilityConfig 占位（abilityId：`mystic_t0` / `charge_t0` / `binder_t0`），使链路可端到端验证；实际规则待生效阶段讨论成熟后配置。
- EditMode 测试（`Assets/Tests/Editor/`）：`ParseSubJob` 映射 + 装载三分支（空跳过 / 已知装载 / 已知但缺资产报错不装载）。
- xlsx 加列是 Excel 手工步骤（二进制文件不入代码改动）；代码对"列尚不存在"安全：`GetStr` 警告 → null → 0 → 无子职业。可先落代码后补表。

## 6. 本期不做（后续阶段）

- **运行时装配**：`EntityAbilityRunner.PreWarm` 构建 `SubJobTrait`；届时大概率给 `AbilityKind` 枚举尾部追加 `SubJobTrait` 值（保既有资产序号稳定），并决定 `OnTeardown` 保留语义（同 Talents）。
- **UI**：`SubpCard.UpdateSubpCardMessage` 按 `CharacterSubJob` 显示名称/图标/描述。
- **秘术师职责划分**：`ChargeAttack`（prefab 攻击模块变体）与特性资产的边界、`ebnhlz_t1` 是否并入特性资产。

## 7. 实现面

| 文件 | 改动 |
|---|---|
| `Assets/PublicScripts/GameData/EntityData/EntityData.cs` | +`CharacterSubJob`、+`SubJobTrait`、注释更新 |
| `Assets/DataTools/XLSX2DataAsset.cs` | +`ParseSubJob`、+`SubJobAbilityPaths`、`ReadEntityData` 装载、`ValidateSubJobs` |
| `Assets/Resources/Prefabs/Abilities/SubJobs/` | 新目录 + 3 个占位 AbilityConfig .asset |
| `Assets/Tests/Editor/...` | ParseSubJob / 装载三分支测试 |
