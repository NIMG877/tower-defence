# 数值系统重构：BuffType 账本 → Modifier + AttributeStore

**日期**: 2026-07-08
**状态**: 已实施

## 背景

当前 `BuffController` 用 `Dictionary<BuffType, float> buffValue` 以"账本式增量累计"管理所有属性变动：
- `BuffType` 枚举把"属性"和"操作类型"硬编码在一起（21 项，新增属性要加 N 个枚举项）
- `BuffResultStatistic` 用 `buffValue[t] += delta` 累计，靠销毁时传全 0 差值扣回
- 四个 `*_delta_rate` 类型的叠加公式 `value * (1 + buffValue[t])` 顺序敏感、与规格不符
- `EntityStats` 是 `buffValue` 的唯一下游消费者，散装公式分散在各 `XxxS` 属性里

存在的问题：
1. 最终乘算（rate 类）的运算公式错误，应是连乘却被实现成自参考加法增量
2. 直接乘算缺 `<0 → 0` 钳制
3. 缺"最终加算"这一整类
4. 账本式 push 累计与连乘/钳制语义本质冲突，`SetBuffValues` 的差值语义无法表达乘法撤销
5. `Nsabr/ts1.cs` 直接改 `buff.buff_values[0]` 绕过 setter，`buffValue` 不更新（潜伏 bug）

## 目标

将整套数值系统换成业界标准的 **Modifier + AttributeStore** 形式：
- modifier 与 buff 解耦：modifier 是纯数值修改器，buff 是载体 + 生命周期容器
- 属性聚合用 lazy + 脏标记，忠实表达四类运算的快照语义
- `EntityStats` 散装公式收敛到 `AttributeStore` 一处
- 下游冲突脚本以重构后结构为准修改

## 运算规格（用户定义，权威）

四类运算方式：

- **直接加算** (AddFlat): 同类加和 `F = Σ fᵢ`
- **直接乘算** (AddPercent): 同类加和 `M = Σ mᵢ`，结果 `<0` 时钳为 `0`
- **最终加算** (AddFlatFinal): 同类加和 `G = Σ gᵢ`
- **最终乘算** (MulFinal): 同类连乘 `P = Π pᵢ`，任意单项 `<0` 时该项视为 `1`

最终属性值：
```
Final = ((base + F) * (1 + M) + G) * P
```

阶段顺序固定（直接类先于最终类），同类内部顺序无关。

## 设计决策（已与用户确认）

1. **modifier 用字符串 key 指定属性**（非枚举）—— 与现代能力系统 string-keyed 黑板风格一致，数据驱动零摩擦，新增属性零代码改动。代价：拼错运行时才发现，用集中的 `Attributes` 常量类 + 运行时 warn 缓解。
2. **聚合时机：lazy + 脏标记** —— modifier 增删置脏，读取 `GetFinal` 时才重算。读多写少高效，与 `EntityStats` 现有"每次访问现算"语义兼容。
3. **rate 类属性归类：属性值透明，下游自解释** —— store 只按四类 op 算最终值，不关心属性用途。`PhysicalDamageRate` 的 Final 就是伤害乘率，`PhysicalDodge` 的 Final 就是闪避增量，下游按自己语义用。
4. **modifier 是纯数值修改器，与语义解耦** —— `Modifier { attribute, op, magnitude }`，op 只管叠加方式。旧 `phd_delta_rate=-0.995`（表示"伤害×0.005"）的语义效果保持，但 magnitude 换算成最终乘数本身 `0.005`（即 `1+旧值`），配置数值换算。
5. **Buff 保留为载体 + 生命周期容器** —— 持 modifiers + duration + effect + whitelist + origin + stack。与旧结构最接近，迁移代价最小，Cards UI 能直接读 `Buff.modifiers`。
6. **动态更新统一走 `SetBuffValues`** —— `Buff.modifiers` 是不可变快照，所有改动经 `SetBuffValues` 重建数组 + 通知 store 置脏。消除绕 setter 的 bug，UI 读一致快照。
7. **架构方案 A**：`AttributeStore` 独立成纯 C# 类（非 MonoBehaviour），`BuffController` 退化为生命周期容器，`EntityStats` 读 store。职责切分最干净，与现代能力架构同构。
8. **`AttackSpeed` base=100 走通用公式** —— 不做特殊 store 处理，作为标准属性进 store，base 是设计常量 100（非来自 EntityData）。下游 `BaseAttackTimeS` 分母直接是 `GetFinal("AttackSpeed")`，不用再加 100。代价：base=100 是魔法值，且给 AttackSpeed 开放了 AddPercent/MulFinal 语义维度，但当前设计只用 AddFlat。
9. **DOT / AbnormalState 零改动** —— 这两个子系统与属性聚合正交（操作"能不能动/能不能打"，不是"数值多少"），留在 `BuffController` 原样不动。
10. **`FetchBuff` 删除** —— 死代码，零调用，不留壳。
11. **不写测试** —— 靠重构后跑游戏验证数值。
12. **`.asset` 迁移手工修改** —— 13 处配置 + 12 处代码 CreateBuff 逐个改，不写迁移脚本。

## 数据模型

### `Modifier` —— 纯数值修改器

```csharp
public enum ModifierOp { AddFlat, AddPercent, AddFlatFinal, MulFinal }

[Serializable]
public readonly struct Modifier
{
    public readonly string attribute;   // "Attack", "PhysicalDamageRate", ...
    public readonly ModifierOp op;
    public readonly float magnitude;    // 纯数值，不含语义
    public Modifier(string attribute, ModifierOp op, float magnitude) { ... }
}
```

`readonly struct`（值类型）—— 零 GC、缓存友好（`List<Modifier>` 是紧凑连续数组）、契合高频重算。不可变，所有改动重建整个数组而非改单个元素。

### `Buff` —— modifier 载体 + 生命周期

```csharp
[Serializable]
public class Buff
{
    public string id;                  // "spot_s1", "lavaBuff", ...
    public Modifier[] modifiers;       // 不可变快照，所有改动经 SetBuffValues 重建
    public float duration;             // >0 限时; <=-5 永久; (-5,0] 待销毁
    public bool isWhitelist;
    public GameObject effect;
    public Entity origin;
}
```

### `AttributeStore` —— 聚合引擎（纯 C# 类，非 MonoBehaviour）

```csharp
public class AttributeStore
{
    private struct AttrState
    {
        public float baseValue;
        public List<Modifier> modifiers;   // 该属性的所有活跃 modifier
        public bool dirty;
        public float cached;
    }
    private Dictionary<string, AttrState> _states;

    public void SetBase(string attribute, float value);
    public void AddModifiers(Modifier[] modifiers);    // buff 创建/更新时
    public void RemoveModifiers(Modifier[] modifiers); // buff 销毁时
    public float GetFinal(string attribute);           // dirty 则重算并缓存
    public void Clear();                               // 池回收，清 modifiers 不清 base
}
```

store 按 attribute 名分桶存 `List<Modifier>`，重算时只扫该属性的桶。store 不持有 Buff 引用——buff 增删通过 `BuffController` 转译成 `AddModifiers`/`RemoveModifiers`。

### 聚合公式实现（`GetFinal` 内）

```
F  = Σ AddFlat              // 直接加算
M  = Σ AddPercent, clamp≥0  // 直接乘算累加和 <0 补 0
G  = Σ AddFlatFinal         // 最终加算
P  = Π MulFinal_i, 单项<0→1 // 最终乘算连乘，空桶→1（乘法单位元）

Final = ((base + F) * (1 + M) + G) * P
```

关键实现点：
- 空 `MulFinal` 桶 → `P = 1`（不能默认 0，否则移除所有减免 buff 后伤害归零）
- `AddPercent` 累加和 `M < 0` → clamp 0
- 单个 `MulFinal` 项 `< 0` → 该项视为 1

## 装配关系与注入链路

```
Entity (MonoBehaviour, IPoolOperation)
  ├── AttributeStore        (新，纯 C# 类)
  ├── BuffController        (MonoBehaviour，瘦身后的生命周期容器)
  └── EntityStats           (POCO，持基础值 + 读 store)
```

### 注入（在 `Entity.PreWarm`）

旧：
```csharp
_stats = new EntityStats(this);
if (buffController != null) _stats.BindBuffController(buffController);
```

新：
```csharp
_attributeStore = new AttributeStore();
_stats = new EntityStats(this, _attributeStore);
if (buffController != null) buffController.Bind(_attributeStore);
_stats.AttributesCaculateFirst(entityData);  // 注入基础值到 store
```

三向解耦：
- `EntityStats` → 持 store（读 Final）+ 注入基础值（`SetBase`）
- `BuffController` → 持 store（buff 增删时调 `Add/RemoveModifiers`）
- `AttributeStore` → 不回指任何人，纯被动聚合

### `BuffController.SetBuffValues` 新语义

```csharp
public void SetBuffValues(Modifier[] newModifiers, Buff target)
{
    var old = target.modifiers;
    if (old != null) _store.RemoveModifiers(old);
    target.modifiers = newModifiers;
    _store.AddModifiers(newModifiers);  // 置脏
}
```

`CreateBuff` / `DestroyBuff` 都经此路径。

### `EntityStats` 改动

`XxxS` 属性从"读字典 + 自组合"变成"读 store + 下游 clamp"：
```csharp
public float AttackS => Mathf.Max(0, _store.GetFinal("Attack"));
public float MoveSpeedS => Mathf.Max(0.01f, _store.GetFinal("MoveSpeed"));
public float MaxHpS => _store.GetFinal("MaxHp");
```

下游 clamp（`Max(0,...)`、`Max(0.01f,...)`）留在 `EntityStats`——store 只算纯数值，clamp 是消费语义。

`ApplyDamage` 的 `phd`/`mgd`（`EntityStats.cs:215-216`）：
```csharp
float physRate = _store.GetFinal("PhysicalDamageRate");
damage *= Mathf.Max(0, physRate);
```

`PhysicalDodgeS` / `MagicDodgeS`：
```csharp
public float PhysicalDodgeS
{
    float rate = _store.GetFinal("PhysicalDodge");
    return 1 - (1 - _physicalDodgeBase) * (1 - rate);
}
```

## 属性名注册表

```csharp
public static class Attributes
{
    public const string Attack = "Attack";
    public const string Defense = "Defense";
    public const string MagicResistance = "MagicResistance";
    public const string MaxHp = "MaxHp";
    public const string MoveSpeed = "MoveSpeed";
    public const string BaseAttackTime = "BaseAttackTime";
    public const string AttackSpeed = "AttackSpeed";          // 旧 atkspd_delta_value, base=100
    public const string AttackNum = "AttackNum";
    public const string AttackMinNum = "AttackMinNum";
    public const string BlockOccupation = "BlockOccupation";
    public const string HpRecover = "HpRecover";              // base=0, 纯增量
    public const string PhysicalDodge = "PhysicalDodge";
    public const string MagicDodge = "MagicDodge";
    public const string PhysicalDamageRate = "PhysicalDamageRate";  // 旧 phd
    public const string MagicDamageRate = "MagicDamageRate";        // 旧 mgd
}
```

代码侧用 `Attributes.Attack`，`.asset` 侧用字符串 `"Attack"`。`GetFinal` 遇未注册 key → lazy 创建空 `AttrState`（base=0）+ 一次性 warn。

## 旧 `BuffType` → 新 `(attribute, op)` 映射表

| 旧 BuffType | 新 attribute | 新 op | magnitude 换算 |
|---|---|---|---|
| `atk_delta_value` | `Attack` | `AddFlat` | 原值 |
| `atk_delta_percent` | `Attack` | `AddPercent` | 原值 |
| `def_delta_value` | `Defense` | `AddFlat` | 原值 |
| `def_delta_percent` | `Defense` | `AddPercent` | 原值 |
| `mgr_delta_value` | `MagicResistance` | `AddFlat` | 原值 |
| `mgr_delta_percent` | `MagicResistance` | `AddPercent` | 原值 |
| `mhp_delta_value` | `MaxHp` | `AddFlat` | 原值 |
| `mhp_delta_percent` | `MaxHp` | `AddPercent` | 原值 |
| `phd_delta_rate` | `PhysicalDamageRate` | `MulFinal` | **`1+旧值`**（-0.995 → 0.005） |
| `mgd_delta_rate` | `MagicDamageRate` | `MulFinal` | **`1+旧值`** |
| `phdoge_delta_rate` | `PhysicalDodge` | `MulFinal` | **`1-旧值`**（0.25 → 0.75，存未命中概率） |
| `mgdoge_delta_rate` | `MagicDodge` | `MulFinal` | **`1-旧值`**（0.25 → 0.75，存未命中概率） |
| `batkt_delta_value` | `BaseAttackTime` | `AddFlat` | 原值 |
| `batkt_delta_percent` | `BaseAttackTime` | `AddPercent` | 原值 |
| `atkspd_delta_value` | `AttackSpeed` | `AddFlat` | 原值（base=100） |
| `blo_delta_value` | `BlockOccupation` | `AddFlat` | 原值 |
| `atkn_delta_value` | `AttackNum` | `AddFlat` | 原值 |
| `atkminn_delta_value` | `AttackMinNum` | `AddFlat` | 原值 |
| `hprecover_delta_value` | `HpRecover` | `AddFlat` | 原值（base=0） |
| `mspeed_delta_value` | `MoveSpeed` | `AddFlat` | 原值 |
| `mspeed_delta_percent` | `MoveSpeed` | `AddPercent` | 原值 |

**关键换算**：共 4 项 magnitude 换算——`phd`/`mgd` 换成 `1+旧值`（最终乘数本身），`phdoge`/`mgdoge` 换成 `1-旧值`（未命中概率）。其余 17 项原值不变。

## dodge（phdoge/mgdoge）语义说明（存法 B，已确认）

旧 dodge 公式：`DodgeS = 1 - (1 - base) * (1 - rate)`，`rate` 来自 `buffValue[phdoge_delta_rate]`。旧 `BuffResultStatistic` 对 dodge 的累加是 `value * (1 + buffValue[t])`（自参考乘法，错误）。

新模型用存法 B：magnitude 存**未命中概率**（`1-旧值`，如 `0.25 → 0.75`）。`MulFinal` 连乘 `P = Π(1-rᵢ)` 即未命中概率连乘。下游：
```
DodgeS = 1 - (1 - base) * P = 1 - (1 - base)(1-r1)(1-r2)...
```
完美匹配业界独立概率标准式，治本。换算性质与 `phd`/`mgd` 同（都是 `1±旧值`）。当前配置仅 `spot_t1.asset` 的 `phdoge_delta_rate=0.25` → `0.75`。

## 特殊语义属性处理（在 `EntityStats`，非改 store 公式）

1. **`AttackSpeed`**: base=100（`AttributesCaculateFirst` 里 `SetBase("AttackSpeed", 100)`，不从 EntityData）。下游：
   ```
   BaseAttackTimeS = GetFinal("BaseAttackTime") * 100 / Mathf.Max(1, GetFinal("AttackSpeed"))
   ```
2. **`PhysicalDodge` / `MagicDodge`**: store Final = 未命中概率连乘积（存法 B，magnitude 存 `1-旧值`）。下游 `DodgeS = 1 - (1 - base) * Final`。
3. **`HpRecover`**: base=0，Final = Σ AddFlat。下游 `Mathf.Max(0, Final)`。

## `BuffController` 瘦身后形态

**删除**：
- `public Dictionary<BuffType, float> buffValue` 字段
- `BuffResultStatistic` 方法（聚合下沉到 store）
- `BuffType` 枚举（迁移到 `Attributes` + `ModifierOp`）
- `FetchBuff`（死代码）
- `Buff` 类的 `buff_types` / `buff_values` 字段（换 `modifiers`）

**保留**：
- `white_list_buffs` / `normal_buffs` + `Buffs` 聚合属性
- `CreateBuff` / `DestroyBuff` / `SetBuffValues`（签名改 `Modifier[]`）
- `BuffUpdate`（duration 计时 + 过期销毁）
- 整个 DOT 区块、整个 AbnormalState 区块（零改动）
- `PreWarm` / `Initialize` / `Dormancy`（池生命周期）

**新增**：
- `Bind(AttributeStore store)`
- `SetBuffValues` 内部调 `_store.AddModifiers` / `RemoveModifiers`
- `Dormancy` 调 `_store.Clear()`（清 modifiers + 置脏 + 缓存重置，不清 base）

## `ApplyBuff` 组件与 `.asset` 数据格式

### 新 `.asset` 格式（三 CSV）

```yaml
attributes: "Attack,Attack"
ops:        "AddFlat,AddPercent"
magnitudes: "50,0.5"
buffId:     "spot_s1"
buffTime:   -5
isWhiteList: false
```

三数组按下标对齐 → `Modifier[]`。长度不一致取最短 + 一次性 warn（沿用 `AttackEventValueModifier` 既有容错）。`ModifierOp` 用 `Enum.Parse<ModifierOp>`，拼错加载时抛异常（设计期资产，错了一进游戏暴露）。

### `ApplyBuff` 组件字段迁移

旧：`buffTypes` / `buffValues` / `buffId` / `buffTime` / `isWhiteList`
新：`attributes` / `ops` / `magnitudes` / `buffId` / `buffTime` / `isWhiteList`

`OnInit` 解析三 CSV 成 `Modifier[]`，`OnTrigger` 调 `buffController.CreateBuff(modifiers, effect, buffId, buffTime, isWhiteList)`。aura 模式（`ApplyBuff.cs:141-196`）同步改 `SetBuffValues(Modifier[], Buff)`，tracked buff 存储不变。

### `DestroyBuff` / `ApplyAbnormalState` / `DestroyAbnormalState` 组件

零改动——`DestroyBuff` 只持 `Buff` 引用按 `buffId` 销毁，不碰 modifier 结构；后两者操作 AbnormalState 不碰属性。

### `Cards` UI（`Cards.cs:141-168`）

`BuffCard` 改读 `buff.modifiers[]`，每个 modifier 展示 `{attribute, op, magnitude}`。显示名表 `Dictionary<BuffType, string>` → `Dictionary<string, string>`，`atkminn_delta_value → "最小攻击数"` 迁移成 `AttackMinNum → "最小攻击数"`，其余属性名直接 `ToString`。

## 边界情况

1. **空 modifier 数组 buff**：合法，`AddModifiers(空)` 是 no-op。
2. **同属性多 op 混合**：一个 buff 含 `{Attack,AddFlat,50}` + `{Attack,AddPercent,0.3}`，store 分桶后 F/M 各取各的，互不干扰。
3. **`MulFinal` 空桶 → 1**：必须显式处理，不能默认 0。
4. **`duration` 哨兵值**：`>0` 限时、`<=-5` 永久、`(-5,0]` 待销毁，`BuffUpdate` 不变。
5. **池复用残留**：`Dormancy` 调 `_store.Clear()` 兜底。`Clear` 只清 modifiers + dirty + cached，不清 baseValue。
6. **`HpRecover` 无 base**：base=0，空桶 Final=0。
7. **`AttackSpeed` base=100 持久化**：每次 `PreWarm` 都 `SetBase("AttackSpeed", 100)`，`Clear` 不动 base。

## 错误处理

- **未知属性名**：`GetFinal` lazy 创建空 `AttrState`（base=0, Final=0）+ 一次性 warn，不崩。
- **`ModifierOp` 解析失败**：`Enum.Parse` 抛异常，不静默吞。
- **三 CSV 长度不一致**：取最短 + 一次性 warn。
- **`SetBuffValues` 的 target 不在列表**：不防御，由调用方保证（内部代码）。

## 实现顺序（自底向上）

1. 新增 `Modifier` + `ModifierOp` + `Attributes` + `AttributeStore`（纯新增，不删旧）
2. 改造 `EntityStats`：注入 store，`SetBase` 所有属性（含 AttackSpeed=100），`XxxS` 改读 `GetFinal`，`ApplyDamage` 的 phd/mgd 改读 store
3. 改造 `BuffController`：`Bind(store)`，`CreateBuff`/`SetBuffValues`/`DestroyBuff` 改 `Modifier[]` 签名 + 调 store，删 `BuffType`/`buffValue`/`BuffResultStatistic`/`FetchBuff`，`Buff` 字段换 `modifiers`，`Dormancy` 加 `store.Clear()`
4. 迁移 12 处代码 CreateBuff 调用方（含 `Nsabr` 绕 setter bug 修复 + `phd`/`mgd` 数值换算）
5. 迁移 13 处 `.asset` 文件（三 CSV 格式 + `phd`/`mgd` 换算）
6. 迁移 `ApplyBuff` 组件（OnInit 解析三 CSV，aura 同步）
7. 改造 `Cards` UI
8. 删旧 `BuffType` 残留 + 全项目编译 + 跑游戏验证数值

**中间态风险**：第 2-3 步之间游戏处于"buff 不生效"状态（stats 读 store 但 buffController 还没写 modifier）。建议第 2-4 步在一个连续工作单元内完成，不停在中间态。

## 风险点

1. **中间态不可跑**：第 2-4 步之间 buff 不生效，需连续完成。
2. **`phd`/`mgd`/`phdoge`/`mgdoge` 数值换算易错**：`phd`/`mgd` 用 `1+旧值`（-0.995→0.005），`phdoge`/`mgdoge` 用 `1-旧值`（0.25→0.75）。percent 类（如 `-0.45`）不动。手工逐个核对映射表。
3. **`AttackSpeed` base=100 注入点**：漏了会导致 `BaseAttackTimeS` 分母错乱。
4. **`MulFinal` 空桶 → 1**：漏了会导致移除减免 buff 后伤害归零。
5. **`MCEnvironmentalDevice` 读 `buff.buff_values[0]`**：迁移成 `buff.modifiers[i].magnitude`，核对索引对应。
6. **`.asset` 手工改格式错误**：YAML 缩进/逗号错导致解析失败，改完逐个加载验证。

## 不在本次范围

- DOT 内部逻辑、AbnormalState 内部逻辑：零改动
- `AttackEventValueModifier` / `WriteBlackboard` 现代架构：不碰，两套架构继续保持各自独立
- 伤害公式本身（`EntityStats.ApplyDamage` 的穿透/护甲计算）：只改 phd/mgd 读取来源，公式结构不动

## 受影响文件清单

**新增**：
- `AttributeStore.cs`
- `Modifier.cs`（含 `ModifierOp` + `Modifier` + `Attributes`，或拆分）

**重写**：
- `BuffController.cs`（删 BuffType/buffValue/BuffResultStatistic/FetchBuff，Buff 字段换 modifiers，签名改 Modifier[]）
- `EntityStats.cs`（XxxS 改读 store，SetBase 注入，ApplyDamage phd/mgd 改读 store）

**迁移调用方**（12 处代码 CreateBuff + 4 处 SetBuffValues）：
- `ApplyBuff.cs` / `DestroyBuff.cs`（组件）
- `Nsabr/ts1.cs`（含 bug 修复）
- `MachineTalent1.cs` / `MCEnvironmentalDevice.cs`
- `Eyjafjalla/Talent1.cs` / `Eyjafjalla/Skill1.cs`
- `WdslmSkill2.cs` / `WitherAttack.cs` / `WitherTalent3.cs` / `HeadSeterTalent1.cs`

**迁移数据**（13 处 `.asset`）：
- `Characters/3/Spot/skills/spot_s1.asset` + `talents/spot_t1.asset`
- `Characters/3/Stward/talents/stward_t1.asset`
- `Characters/3/Hibisc/talents/hibisc_t1.asset` + `skills/hibisc_s1.asset`
- `Characters/3/Melan/talents/melan_t1.asset` + `skills/melan_s1.asset`
- `Characters/6/Ebnhlz/skills/ebnhlz_s3.asset`
- `Monsters/MC/Zombie/talents/zombie_t1.asset`
- `Monsters/MC/Creeper/talents/creeper_t1.asset`（AbnormalState，实际不换算）

**UI**：
- `Cards.cs`（BuffCard 读 modifiers，显示名表 key 换属性名）

**注入点**：
- `Entity.cs`（PreWarm 注入 AttributeStore）
