# AttackEventValueModifier 设计

**日期:** 2026-06-11
**状态:** Approved
**作者:** Claude (brainstorming session)

## 目的

`AttackMultiplierBoost` 和 `SetAttackCombo` 两个组件底层操作相同——都在 `BeforeAttackEvent` 触发时改写事件字段。差异只在字段名和方法算子。本设计引入一个通用组件 `AttackEventValueModifier`,把"字段名 + 改写值 + 改写方法"三个维度参数化,统一覆盖这两种(以及未来更多)改写需求。

## 范围

- **新增:** 1 个新组件 `AttackEventValueModifier`
- **修改:** 给 `AttackMultiplierBoost` 和 `SetAttackCombo` 加 `[Obsolete]` 标签(无功能改动)
- **不改:** 其他 20 个组件、ParamList 系统、Inspector 渲染、任何 `.asset`

## 命名论证

按 [skill-component-naming-convention] 约束:
- 无 `Component` 后缀(已在 `Components/` 命名空间下,冗余)
- 前缀 `BeforeAttack` = 操作对象(改写 BeforeAttackEvent 字段);对照 `SetAbnormalState`/`SetAttackCombo` 的命名风格
- 后缀 `ValueModifier` = 操作语义(对值进行改写)。不能用 `Set*`(仅覆写)、不能用 `*Boost`(仅乘性)→ 中性的 `Modifier`

最终: `AttackEventValueModifier`

## Inputs(三个平行 CSV,ParamList 字符串键值对)

| Key | 例值 | 解析后类型 | 默认 |
|---|---|---|---|
| `fields` | `"multiplyer,cumbo"` | `string[]` | `Array.Empty<string>()`(合法 no-op) |
| `values` | `"1.5,2"` | 按字段类型分别入 `_floatValues` / `_intValues` | 空 |
| `methods` | `"mult,set"` | `string[]` | 空 |

`OnInit` 时一次性解析;`OnTrigger` 时不再解析(性能 + 失败位置集中)。

## 字段白名单(硬编码 switch)

8 个字段,类型在组件内部静态决定:

| Name | Type | 来源事件 |
|---|---|---|
| `multiplyer` | float | `DamageEventBase`(所有 4 个事件) |
| `defPenetrate` | float | `DamageEventBase` |
| `mgrPenetrate` | float | `DamageEventBase` |
| `defPenetrate_value` | float | `DamageEventBase` |
| `mgrPenetrate_value` | float | `DamageEventBase` |
| `damageType` | int | `DamageEventBase`(3 = true damage,见 [skill-component-concerns-2026-06-10]) |
| `applyType` | int | `DamageEventBase` |
| `cumbo` | int | `BeforeAttackEvent` only |

设计师"宽松"匹配三法——三种方法对所有字段都生效(按用户确认)。类型不对的组合(如 `mult` 作用于 `damageType` = enum 字段)由设计师负责合理性,组件不校验。

## 方法算子

- `mult`: `field = field * value`
- `add`: `field = field + value`
- `set`: `field = value`

int 字段的 `mult`: `field = field * value` 走纯 int 运算;设计师应避免浮点值(解析阶段会 `int.TryParse` 失败 → LogWarning 跳过)。

## 类型解析规则

- float 字段:`float.TryParse(valueCsv[i], out v)` → 入 `_floatValues`
- int 字段:`int.TryParse(valueCsv[i], out v)` → 入 `_intValues`
- 解析失败:LogWarning("AttackEventValueModifier: cannot parse '{value}' for field '{field}'"),跳过该条
- 复用 `BuffParamParser.ParseFloats` 解析 float CSV(已存在,见 `Util/BuffParamParser.cs`)
- int 解析就地 `int.TryParse`(当前无 `ParseInts` helper;若后续要扩,可加入 `BuffParamParser`,但本设计不动)

## OnTrigger 行为

```
1. Cast ctx.currentEvent → DamageEventBase dab
   失败 → LogError + return
2. 提前 cast → BeforeAttackEvent bae(若 dab 不是 BeforeAttackEvent,bae = null)
3. 对每条 (field, value, method):
   switch (field):
     case "multiplyer":        dab.multiplyer        = ApplyFloat(dab.multiplyer,        v, m)
     case "defPenetrate":      dab.defPenetrate      = ApplyFloat(dab.defPenetrate,      v, m)
     case "mgrPenetrate":      dab.mgrPenetrate      = ApplyFloat(dab.mgrPenetrate,      v, m)
     case "defPenetrate_value":dab.defPenetrate_value= ApplyFloat(dab.defPenetrate_value,v, m)
     case "mgrPenetrate_value":dab.mgrPenetrate_value= ApplyFloat(dab.mgrPenetrate_value,v, m)
     case "damageType":        dab.damageType        = ApplyInt  (dab.damageType,        v, m)
     case "applyType":         dab.applyType         = ApplyInt  (dab.applyType,         v, m)
     case "cumbo":
       if (bae == null) → LogInfo("cumbo skipped: not BeforeAttackEvent")
       else              bae.cumbo = ApplyInt(bae.cumbo, v, m)
     default: LogWarning("unknown field: {field}")
```

## CSV 错误处理

| 情况 | 行为 |
|---|---|
| `fields` 为空 | 合法 no-op,直接 return |
| `fields` / `values` / `methods` 长度不等 | LogWarning("length mismatch: fields={n1}, values={n2}, methods={n3}"),按 `Min(n1,n2,n3)` 处理 |
| 字段名未知 | LogWarning,跳过该条 |
| 方法名未知 | LogWarning,跳过该条 |
| 数值解析失败 | LogWarning,跳过该条 |

## 旧组件 Obsolete 化

```csharp
[Obsolete("Use AttackEventValueModifier")]
[RegisterComponent("AttackMultiplierBoost")]
public class AttackMultiplierBoost : ISkillComponent { /* 不变 */ }

[Obsolete("Use AttackEventValueModifier")]
[RegisterComponent("SetAttackCombo")]
public class SetAttackCombo : ISkillComponent { /* 不变 */ }
```

零 `.asset` 引用 → 影响面为空,仅产生 IDE 编译警告提示未来迁移。

## 文件清单

- **新增:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/AttackEventValueModifier.cs`
- **修改:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/AttackMultiplierBoost.cs`(加 `[Obsolete]`)
- **修改:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/SetAttackCombo.cs`(加 `[Obsolete]`)

## 验证(Unity 无单测,PlayMode 手工)

1. 建 SkillConfig,挂 `AttackEventValueModifier`,`OnBeforeAttack` 触发,参数:
   - `fields=multiplyer,cumbo,damageType`
   - `values=1.5,3,2`
   - `methods=mult,set,set`
2. 跑游戏触发技能,断点确认:
   - `bae.multiplyer *= 1.5` 生效
   - `bae.cumbo = 3` 生效
   - `bae.damageType = 2` 生效
3. 边界用例:
   - CSV 错位(3 fields, 2 values)→ LogWarning 后应用前 2 条,不崩
   - 字段名拼错 `multipler` → LogWarning,跳过该条
   - `cumbo` 挂到 `OnAfterAttack` → LogInfo 跳过
   - `methods=multiply` 拼错 → LogWarning,跳过该条
4. 旧组件标 `[Obsolete]` 后编译,确认 IDE 出现黄色提示(若有任何代码引用 → 警告;零引用 → 仅元数据警告)

## 不在本设计范围

- Inspector schema(`ComponentParamSpec.Params[]` 静态数组)——已在 [skill-component-inspector-schema] 规划但尚未落地;本设计沿用当前 `ParamList` 字符串键值对
- 其他 DamageEventBase 事件的字段改写(暂不需要)
- `BeforeHurtEvent` / `HurtEventBase` 字段(完全不同的字段集,场景不同)

## 关联 memory

- [skill-component-naming-convention] — 命名规范
- [skill-component-concerns-2026-06-10] — `damageType = 3` = true damage 项目约定
- [skill-component-inspector-schema] — Params[] 静态数组(尚未落地,本设计不引入)
- [skill-events-base-classes] — `BeforeAttackEvent` 继承自 `DamageEventBase`
