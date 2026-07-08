# 数值系统重构：Modifier + AttributeStore 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 `BuffController` 的账本式 `buffValue` 累计换成业界标准 `Modifier` + `AttributeStore`（lazy + 脏标记），modifier 与 buff 解耦，四类运算忠实表达。

**Architecture:** 新增 `AttributeStore`（纯 C# 类，按属性分桶存 `List<Modifier>`，lazy 重算 + 脏标记）和 `Modifier`（readonly struct，`{attribute, op, magnitude}`，纯数值）。`BuffController` 退化为生命周期容器（增删/计时/特效/whitelist/DOT/AbnormalState），不再算数值。`EntityStats` 的 `XxxS` 属性改读 `store.GetFinal`，下游 clamp 留在 stats。所有 buff 改动经 `SetBuffValues(Modifier[], Buff)` 重建不可变快照。

**Tech Stack:** Unity C#（项目无测试工程，验证靠编译 + 跑游戏；每个任务以"编译通过"为最小验证单位）。

**Spec:** `docs/superpowers/specs/2026-07-08-attribute-modifier-system-refactor-design.md`

---

## 文件结构

**新增**（`Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/`）：
- `Modifier.cs` — `ModifierOp` 枚举 + `Modifier` readonly struct + `Attributes` 常量类。一个文件，三类同级，都是数值系统的公共类型。
- `AttributeStore.cs` — 聚合引擎。纯 C# 类，按属性分桶 + lazy + 脏标记 + 四段公式。

**重写**：
- `BuffController.cs` — 删 `BuffType` 枚举 / `buffValue` 字典 / `BuffResultStatistic` / `FetchBuff`；`Buff` 字段 `buff_types`+`buff_values` → `modifiers`；`CreateBuff`/`DestroyBuff`/`SetBuffValues` 签名改 `Modifier[]` + 调 store；`Dormancy` 加 `store.Clear()`。DOT/AbnormalState 区块零改动。
- `EntityStats.cs` — `BindBuffController` → `Bind(AttributeStore)`；`XxxS` 改读 `GetFinal` + 下游 clamp；`AttributesCaculateFirst` 加 `SetBase`（含 AttackSpeed=100）；`ApplyDamage` 的 phd/mgd 改读 store。

**迁移调用方**（12 处 CreateBuff + 4 处 SetBuffValues，签名 `BuffType[]+float[]` → `Modifier[]`）：
- `AbilitySystem/Components/ApplyBuff.cs`（数据驱动入口，三 CSV 解析）
- `Nsabr/ts1.cs`（含绕 setter bug 修复）
- `MachineTalent1.cs` / `MCEnvironmentalDevice.cs`
- `Eyjafjalla/Talent1.cs` / `Eyjafjalla/Skill1.cs`
- `WdslmSkill2.cs` / `WitherAttack.cs` / `WitherTalent3.cs` / `HeadSeterTalent1.cs`
- `Cards.cs`（UI 读 `buff.modifiers`）

**迁移数据**（13 处 `.asset`）：`buffTypes`/`buffValues` → `attributes`/`ops`/`magnitudes`，4 项数值换算（phd/mgd 用 `1+旧值`，phdoge/mgdoge 用 `1-旧值`）。

**注入点**：`Entity.cs` PreWarm（注入 AttributeStore）。

---

## 数值换算速查（迁移时逐项核对）

| 旧 BuffType | 新 (attribute, op) | magnitude 换算 |
|---|---|---|
| `atk_delta_value` | `(Attack, AddFlat)` | 原值 |
| `atk_delta_percent` | `(Attack, AddPercent)` | 原值 |
| `def_delta_value` | `(Defense, AddFlat)` | 原值 |
| `def_delta_percent` | `(Defense, AddPercent)` | 原值 |
| `mgr_delta_value` | `(MagicResistance, AddFlat)` | 原值 |
| `mgr_delta_percent` | `(MagicResistance, AddPercent)` | 原值 |
| `mhp_delta_value` | `(MaxHp, AddFlat)` | 原值 |
| `mhp_delta_percent` | `(MaxHp, AddPercent)` | 原值 |
| `phd_delta_rate` | `(PhysicalDamageRate, MulFinal)` | **`1+旧值`** |
| `mgd_delta_rate` | `(MagicDamageRate, MulFinal)` | **`1+旧值`** |
| `phdoge_delta_rate` | `(PhysicalDodge, MulFinal)` | **`1-旧值`** |
| `mgdoge_delta_rate` | `(MagicDodge, MulFinal)` | **`1-旧值`** |
| `batkt_delta_value` | `(BaseAttackTime, AddFlat)` | 原值 |
| `batkt_delta_percent` | `(BaseAttackTime, AddPercent)` | 原值 |
| `atkspd_delta_value` | `(AttackSpeed, AddFlat)` | 原值（base=100） |
| `blo_delta_value` | `(BlockOccupation, AddFlat)` | 原值 |
| `atkn_delta_value` | `(AttackNum, AddFlat)` | 原值 |
| `atkminn_delta_value` | `(AttackMinNum, AddFlat)` | 原值 |
| `hprecover_delta_value` | `(HpRecover, AddFlat)` | 原值（base=0） |
| `mspeed_delta_value` | `(MoveSpeed, AddFlat)` | 原值 |
| `mspeed_delta_percent` | `(MoveSpeed, AddPercent)` | 原值 |

---

## Task 1: 新增 `Modifier.cs`（ModifierOp + Modifier + Attributes）

**Files:**
- Create: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Modifier.cs`

- [ ] **Step 1: 写 `Modifier.cs` 完整内容**

```csharp
using System;

/// <summary>
/// 数值修改器——纯数值，与语义解耦。
/// op 只管同类内部怎么叠加，不掺"伤害/闪避"等用途语义。
/// </summary>
public enum ModifierOp
{
    /// <summary>直接加算：F = Σ fᵢ</summary>
    AddFlat,
    /// <summary>直接乘算：M = Σ mᵢ，结果 <0 钳为 0</summary>
    AddPercent,
    /// <summary>最终加算：G = Σ gᵢ</summary>
    AddFlatFinal,
    /// <summary>最终乘算：P = Π pᵢ，单项 <0 视为 1</summary>
    MulFinal,
}

/// <summary>
/// 纯数值修改器。readonly struct：零 GC、缓存友好（List&lt;Modifier&gt; 连续内存）。
/// 不可变；所有改动重建整个数组，而非改单个元素。
/// </summary>
[Serializable]
public readonly struct Modifier
{
    public readonly string attribute;   // "Attack", "PhysicalDamageRate", ...
    public readonly ModifierOp op;
    public readonly float magnitude;    // 纯数值，不含语义

    public Modifier(string attribute, ModifierOp op, float magnitude)
    {
        this.attribute = attribute;
        this.op = op;
        this.magnitude = magnitude;
    }
}

/// <summary>
/// 属性名常量注册表。代码侧用 Attributes.Xxx，.asset 侧用字符串 "Xxx"。
/// 集中定义避免拼写漂移。
/// </summary>
public static class Attributes
{
    public const string Attack = "Attack";
    public const string Defense = "Defense";
    public const string MagicResistance = "MagicResistance";
    public const string MaxHp = "MaxHp";
    public const string MoveSpeed = "MoveSpeed";
    public const string BaseAttackTime = "BaseAttackTime";
    public const string AttackSpeed = "AttackSpeed";            // base=100（设计常量，非来自 EntityData）
    public const string AttackNum = "AttackNum";
    public const string AttackMinNum = "AttackMinNum";
    public const string BlockOccupation = "BlockOccupation";
    public const string HpRecover = "HpRecover";                // base=0，纯增量
    public const string PhysicalDodge = "PhysicalDodge";        // 存未命中概率（1-旧值）
    public const string MagicDodge = "MagicDodge";              // 存未命中概率（1-旧值）
    public const string PhysicalDamageRate = "PhysicalDamageRate";  // 存最终乘数（1+旧值）
    public const string MagicDamageRate = "MagicDamageRate";        // 存最终乘数（1+旧值）
}
```

- [ ] **Step 2: 等待 Unity 编译**

在 Unity 编辑器里切回主窗口让其编译（或确认 VSCode 无报错）。预期：编译通过，无错误。`BuffType` 仍在 `BuffController.cs`，此时与新类型并存，不冲突。

- [ ] **Step 3: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Modifier.cs
git commit -m "数值系统：新增 Modifier/ModifierOp/Attributes 公共类型"
```

---

## Task 2: 新增 `AttributeStore.cs`（聚合引擎）

**Files:**
- Create: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/AttributeStore.cs`

- [ ] **Step 1: 写 `AttributeStore.cs` 完整内容**

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 属性聚合引擎。纯 C# 类（非 MonoBehaviour），由 Entity 持有。
/// 按属性分桶存 List&lt;Modifier&gt;，lazy + 脏标记：modifier 增删置脏，
/// GetFinal 读取时才重算并缓存。读多写少高效，天然无漂移。
///
/// 聚合公式（阶段顺序固定，同类内部顺序无关）：
///   F = Σ AddFlat              直接加算
///   M = Σ AddPercent, clamp≥0  直接乘算累加和 <0 补 0
///   G = Σ AddFlatFinal         最终加算
///   P = Π MulFinal_i, 单项<0→1 最终乘算连乘，空桶→1（乘法单位元）
///   Final = ((base + F) * (1 + M) + G) * P
///
/// store 不持有 Buff 引用——buff 增删通过 BuffController 转译成 Add/RemoveModifiers。
/// </summary>
public class AttributeStore
{
    private struct AttrState
    {
        public float baseValue;
        public List<Modifier> modifiers;
        public bool dirty;
        public float cached;
    }

    private readonly Dictionary<string, AttrState> _states = new Dictionary<string, AttrState>();

    /// <summary>设置属性基础值。EntityStats.AttributesCaculateFirst 调用。</summary>
    public void SetBase(string attribute, float value)
    {
        EnsureState(attribute).baseValue = value;
        EnsureState(attribute).dirty = true;
    }

    /// <summary>加入一组 modifier（buff 创建/更新时）。分桶到各属性，置脏。</summary>
    public void AddModifiers(Modifier[] modifiers)
    {
        if (modifiers == null) return;
        for (int i = 0; i < modifiers.Length; i++)
        {
            AttrState s = EnsureState(modifiers[i].attribute);
            s.modifiers.Add(modifiers[i]);
            s.dirty = true;
            _states[modifiers[i].attribute] = s;   // struct: 写回
        }
    }

    /// <summary>移除一组 modifier（buff 销毁时）。按引用移除，置脏。</summary>
    public void RemoveModifiers(Modifier[] modifiers)
    {
        if (modifiers == null) return;
        for (int i = 0; i < modifiers.Length; i++)
        {
            if (!_states.TryGetValue(modifiers[i].attribute, out AttrState s)) continue;
            s.modifiers.RemoveAll(m => m.attribute == modifiers[i].attribute
                                    && m.op == modifiers[i].op
                                    && m.magnitude == modifiers[i].magnitude);
            s.dirty = true;
            _states[modifiers[i].attribute] = s;   // struct: 写回
        }
    }

    /// <summary>读取最终值。dirty 则重算并缓存，否则返 cached。</summary>
    public float GetFinal(string attribute)
    {
        if (!_states.TryGetValue(attribute, out AttrState s))
        {
            // 未注册属性：lazy 创建空桶（base=0, Final=0）+ 一次性 warn
            OneShotWarn.WarnOnce("attr-unknown:" + attribute,
                $"AttributeStore: 未注册属性 '{attribute}'，返回 0。");
            return 0f;
        }
        if (s.dirty)
        {
            s.cached = Compute(s);
            s.dirty = false;
            _states[attribute] = s;   // struct: 写回
        }
        return s.cached;
    }

    /// <summary>池回收：清所有 modifier + 置脏 + 缓存归零。不清 baseValue（属性固有值）。</summary>
    public void Clear()
    {
        foreach (var key in _states.Keys)
        {
            AttrState s = _states[key];
            if (s.modifiers != null) s.modifiers.Clear();
            s.dirty = true;
            s.cached = 0f;
            _states[key] = s;   // struct: 写回（注意：迭代时写回 Dictionary 在 C# 是安全的，因为是改 value 不改 key 结构）
        }
    }

    private AttrState EnsureState(string attribute)
    {
        if (!_states.TryGetValue(attribute, out AttrState s))
        {
            s = new AttrState { modifiers = new List<Modifier>(), dirty = true };
            _states[attribute] = s;
        }
        return _states[attribute];
    }

    private float Compute(AttrState s)
    {
        float F = 0f, M = 0f, G = 0f, P = 1f;
        var list = s.modifiers;
        if (list != null)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Modifier m = list[i];
                switch (m.op)
                {
                    case ModifierOp.AddFlat:      F += m.magnitude; break;
                    case ModifierOp.AddPercent:   M += m.magnitude; break;
                    case ModifierOp.AddFlatFinal: G += m.magnitude; break;
                    case ModifierOp.MulFinal:
                        P *= m.magnitude < 0f ? 1f : m.magnitude;  // 单项 <0 → 1
                        break;
                }
            }
        }
        if (M < 0f) M = 0f;   // 直接乘算累加和 <0 钳 0
        return ((s.baseValue + F) * (1f + M) + G) * P;
    }
}
```

**注意 `Clear()` 的字典迭代写回**：C# 中 `foreach (var key in _states.Keys)` + `_states[key] = s` 修改的是 value 不是 key 集合，安全。但若担心可读性，可改为先收集 key 到数组再写回——本计划用前者，简洁。

- [ ] **Step 2: 等待 Unity 编译**

预期：编译通过。此时 `AttributeStore` 无调用方，未接入。

- [ ] **Step 3: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/AttributeStore.cs
git commit -m "数值系统：新增 AttributeStore 聚合引擎（lazy+脏标记）"
```

---

## Task 3: 改造 `EntityStats.cs` —— 注入 store + SetBase + XxxS 改读 GetFinal

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntityStats.cs`

**注意：此任务后游戏处于中间态**——stats 读 store 但 buffController 还没写 modifier，所有 buff 暂不生效。Task 4-5 必须紧接完成，不停在中间态。

- [ ] **Step 1: 改字段与注入——`_buffController` → `_store`**

把 `EntityStats.cs` 第 24 行：
```csharp
    private BuffController _buffController;
```
改为：
```csharp
    private AttributeStore _store;
```

把第 53-59 行 `BindBuffController` 整个方法：
```csharp
    /// <summary>
    /// 注入 BuffController。Entity 在 PreWarm 中、buffController 被获取后调用。
    /// </summary>
    public void BindBuffController(BuffController bc)
    {
        _buffController = bc;
    }
```
改为：
```csharp
    /// <summary>
    /// 注入 AttributeStore。Entity 在 PreWarm 中构造 store 后调用。
    /// </summary>
    public void Bind(AttributeStore store)
    {
        _store = store;
    }
```

- [ ] **Step 2: 改计算属性 `XxxS`（74-94 行整段）——读 GetFinal + 下游 clamp**

把第 74-94 行整段：
```csharp
    public float MaxHpS => Math.Max(0.001f, _maxHpBase + _buffController.buffValue[BuffType.mhp_delta_value] + _maxHpBase * _buffController.buffValue[BuffType.mhp_delta_percent]);
    public float DefS => _defBase + Math.Max(0, _buffController.buffValue[BuffType.def_delta_value] + _defBase * _buffController.buffValue[BuffType.def_delta_percent]);
    public float MagicResistanceS => Math.Max(0, _magicResistanceBase + _buffController.buffValue[BuffType.mgr_delta_value] + _magicResistanceBase * _buffController.buffValue[BuffType.mgr_delta_percent]);
    public float PhysicalDodgeS => 1 - (1 - _physicalDodgeBase) * (1 - _buffController.buffValue[BuffType.phdoge_delta_rate]);
    public float MagicDodgeS => 1 - (1 - _magicDodgeBase) * (1 - _buffController.buffValue[BuffType.mgdoge_delta_rate]);
    public int BlockOccupationS => Math.Max(0, _blockOccupationBase + (int)_buffController.buffValue[BuffType.blo_delta_value]);
    public int TauntLevel => _tauntLevelBase;  // 嘲讽等级无战斗 buff
    public float AttackS => Math.Max(0, _attackBase + _buffController.buffValue[BuffType.atk_delta_value] + _attackBase * _buffController.buffValue[BuffType.atk_delta_percent]);
    public float BaseAttackTimeS => Math.Max(0.001f, (_baseAttackTimeBase + _buffController.buffValue[BuffType.batkt_delta_value] + _baseAttackTimeBase * _buffController.buffValue[BuffType.batkt_delta_percent]) * 100 / Math.Max(1, 100 + _buffController.buffValue[BuffType.atkspd_delta_value]));
    public int AttackNumS
    {
        get
        {
            if (_attackNumBase >= 0)
                return Math.Max(0, _attackNumBase + (int)_buffController.buffValue[BuffType.atkn_delta_value]);
            else
                return -1;
        }
    }
    public int AttackMinNumS => Math.Max(0, _attackMinNumBase + (int)_buffController.buffValue[BuffType.atkminn_delta_value]);
    public float MoveSpeedS => Math.Max(0.01f, _moveSpeedBase + _buffController.buffValue[BuffType.mspeed_delta_value] + _moveSpeedBase * _buffController.buffValue[BuffType.mspeed_delta_percent]);
```
改为：
```csharp
    public float MaxHpS => Math.Max(0.001f, _store.GetFinal(Attributes.MaxHp));
    public float DefS => Math.Max(0, _store.GetFinal(Attributes.Defense));
    public float MagicResistanceS => Math.Max(0, _store.GetFinal(Attributes.MagicResistance));
    public float PhysicalDodgeS => 1 - (1 - _physicalDodgeBase) * _store.GetFinal(Attributes.PhysicalDodge);
    public float MagicDodgeS => 1 - (1 - _magicDodgeBase) * _store.GetFinal(Attributes.MagicDodge);
    public int BlockOccupationS => Math.Max(0, _blockOccupationBase + (int)_store.GetFinal(Attributes.BlockOccupation));
    public int TauntLevel => _tauntLevelBase;  // 嘲讽等级无战斗 buff
    public float AttackS => Math.Max(0, _store.GetFinal(Attributes.Attack));
    public float BaseAttackTimeS => Math.Max(0.001f, _store.GetFinal(Attributes.BaseAttackTime) * 100 / Math.Max(1, _store.GetFinal(Attributes.AttackSpeed)));
    public int AttackNumS
    {
        get
        {
            if (_attackNumBase >= 0)
                return Math.Max(0, _attackNumBase + (int)_store.GetFinal(Attributes.AttackNum));
            else
                return -1;
        }
    }
    public int AttackMinNumS => Math.Max(0, _attackMinNumBase + (int)_store.GetFinal(Attributes.AttackMinNum));
    public float MoveSpeedS => Math.Max(0.01f, _store.GetFinal(Attributes.MoveSpeed));
```

**语义说明**：
- `Attack`/`Defense`/`MagicResistance`/`MaxHp`/`MoveSpeed`/`BaseAttackTime` 的旧公式 `base + F + base*M` 已由 store 的 `((base+F)*(1+M)+G)*P` 完整覆盖（这些属性无 G/P modifier 时退化）。下游只留 clamp（`Max(0,...)` 等）。
- `PhysicalDodge`/`MagicDodge`：store Final 存的是未命中概率连乘积（存法 B），下游 `1 - (1-base)*Final`。
- `AttackSpeed`：store base=100，Final = `100 + Σ AddFlat`。下游分母 `Max(1, Final)` 替代旧 `Max(1, 100+delta)`——等价。
- `BlockOccupation`/`AttackNum`/`AttackMinNum`：int 属性，base + AddFlat，下游 `(int)` 截断 + clamp。
- `HpRecover`：见 Step 3。

- [ ] **Step 3: 改 `HpRecover`（103 行）+ `RecoverTick`（172 行）**

第 103 行：
```csharp
    public float HpRecover => Math.Max(0, _buffController.buffValue[BuffType.hprecover_delta_value]);
```
改为：
```csharp
    public float HpRecover => Math.Max(0, _store.GetFinal(Attributes.HpRecover));
```

`RecoverTick`（168-174 行）无需改——它调 `HpRecover` 和 `MaxHpS`，两者已改读 store。

- [ ] **Step 4: 改 `ApplyDamage` 的 phd/mgd（215-216 行）**

第 215-216 行：
```csharp
            0 => Math.Max(damage * multiplyer * minRate, damage * multiplyer - (1 - defPenetrate) * (DefS - defPenetrate_value)) * Math.Max(0, 1 + _buffController.buffValue[BuffType.phd_delta_rate]),
            1 => Math.Max(damage * multiplyer * minRate, damage * multiplyer * (1 - (1 - mgrPenetrate) * (MagicResistanceS - mgrPenetrate_value) / 100)) * Math.Max(0, 1 + _buffController.buffValue[BuffType.mgd_delta_rate]),
```
改为：
```csharp
            0 => Math.Max(damage * multiplyer * minRate, damage * multiplyer - (1 - defPenetrate) * (DefS - defPenetrate_value)) * Math.Max(0, _store.GetFinal(Attributes.PhysicalDamageRate)),
            1 => Math.Max(damage * multiplyer * minRate, damage * multiplyer * (1 - (1 - mgrPenetrate) * (MagicResistanceS - mgrPenetrate_value) / 100)) * Math.Max(0, _store.GetFinal(Attributes.MagicDamageRate)),
```

**语义说明**：旧 `Math.Max(0, 1+rate)`，其中 `rate` 是旧存的 `-0.995`。新 store 存 `1+旧值 = 0.005`（最终乘数本身），`GetFinal` 返回连乘积 `Π(1+rᵢ)`，下游 `Math.Max(0, Final)` 保留"不为负"兜底。效果与旧一致（无减免 buff 时 Final=1，伤害不变）。

- [ ] **Step 5: 改 `AttributesCaculateFirst`（131-165 行）——加 SetBase + AttackSpeed=100**

在第 164 行 `_moveSpeedBase = moveSpeed;` 之后、方法结束的 `}` 之前，插入 `SetBase` 调用块。即在原第 164 行后加：

```csharp
        _moveSpeedBase = moveSpeed;

        // === 注入基础值到 AttributeStore ===
        // AttackSpeed 的 base 是设计常量 100（"100 攻速 = 正常速度"），非来自 EntityData。
        // HpRecover base=0（纯增量属性）。Dodge/Rate 类属性的 base 留 0——
        // 它们的 base 语义在下游 DodgeS/ApplyDamage 公式里通过 _xxxDodgeBase 等字段表达，
        // store 只管 modifier 叠加。
        _store.SetBase(Attributes.MaxHp, _maxHpBase);
        _store.SetBase(Attributes.Defense, _defBase);
        _store.SetBase(Attributes.MagicResistance, _magicResistanceBase);
        _store.SetBase(Attributes.PhysicalDodge, 0f);      // dodge base 在下游 _physicalDodgeBase 体现
        _store.SetBase(Attributes.MagicDodge, 0f);
        _store.SetBase(Attributes.BlockOccupation, _blockOccupationBase);
        _store.SetBase(Attributes.Attack, _attackBase);
        _store.SetBase(Attributes.BaseAttackTime, _baseAttackTimeBase);
        _store.SetBase(Attributes.AttackSpeed, 100f);       // 设计常量
        _store.SetBase(Attributes.AttackNum, _attackNumBase);
        _store.SetBase(Attributes.AttackMinNum, _attackMinNumBase);
        _store.SetBase(Attributes.MoveSpeed, _moveSpeedBase);
        _store.SetBase(Attributes.HpRecover, 0f);           // 纯增量
        _store.SetBase(Attributes.PhysicalDamageRate, 1f);  // 无减免 buff 时 Final=1
        _store.SetBase(Attributes.MagicDamageRate, 1f);
    }
```

**关键**：`PhysicalDamageRate`/`MagicDamageRate` 的 base 设 1（无 buff 时伤害不变）。`PhysicalDodge`/`MagicDodge` 的 base 设 0（store 只叠加 modifier 的未命中概率，下游用 `_physicalDodgeBase` 做基础闪避）。

- [ ] **Step 6: 等待 Unity 编译**

预期：**编译失败**——`Entity.PreWarm` 还在调 `BindBuffController`（已改名 `Bind`），且没注入 store。这是预期，Task 4 紧接修复。

- [ ] **Step 4: Commit（此任务暂不单独 commit，与 Task 4 一起提交以避免中间态进入 git 历史）**

> 说明：Task 3 + Task 4 + Task 5 必须连续完成才编译通过。下面 Task 4-5 紧接进行，全部完成后一次性 commit。若需中途保存，可 `git stash`。

---

## Task 4: 改造 `Entity.cs` PreWarm 注入 AttributeStore

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs` (PreWarm, ~194-202 行)

- [ ] **Step 1: 先确认 Entity 是否已有 `_attributeStore` 字段**

搜索 `Entity.cs` 里的字段声明区（通常在类顶部）。若**没有** `private AttributeStore _attributeStore;` 字段，则在 `_stats` 字段附近添加：
```csharp
    private AttributeStore _attributeStore;
```
若已有则跳过。

- [ ] **Step 2: 改 PreWarm 注入（194-195 行）**

第 194-195 行：
```csharp
        _stats = new EntityStats(this);
        if (buffController != null) _stats.BindBuffController(buffController);
```
改为：
```csharp
        _attributeStore = new AttributeStore();
        _stats = new EntityStats(this);
        _stats.Bind(_attributeStore);
        if (buffController != null) buffController.Bind(_attributeStore);
```

**注意**：`Stats.AttributesCaculateFirst(EntityData)`（原第 202 行）保持在其当前位置——它在 `_stats.Bind` 之后调用，正好能 SetBase 到已注入的 store。顺序：new store → new stats → stats.Bind(store) → buffController.Bind(store) → AttributesCaculateFirst（SetBase）。

- [ ] **Step 3: 暴露 store 给 BuffController**

`BuffController` 需要能拿到 store。检查 `Entity.cs` 里 `buffController` 是怎么获取/赋值的——若 `BuffController` 是 `Entity` 上的 MonoBehaviour 组件（`GetComponent`），则 `Bind` 已在 Step 2 调用。确认 `buffController` 在 PreWarm 此处非 null（`if (buffController != null)` 已保护）。无需额外改动。

- [ ] **Step 4: 暂不单独编译/commit**——Task 5 改 `BuffController` 后才能编译通过。

---

## Task 5: 改造 `BuffController.cs` —— 删账本 + Buff 字段换 modifiers + 签名改 Modifier[]

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/BuffController.cs`

这是最大的一个任务。按区块逐步替换。

- [ ] **Step 1: 删 `BuffType` 枚举（7-93 行）**

删除第 7-93 行整个 `public enum BuffType { ... }` 块（含上方 `using` 里若只为它服务的也清理，但 `using static PlasticGui...` 那行可疑——保留不动，避免误删）。

- [ ] **Step 2: 改 `Buff` 类（94-112 行）**

第 94-112 行整个 `Buff` 类：
```csharp
[Serializable]
public class Buff
{
    public float[] buff_values;
    public float buff_time;
    public string buff_name;
    public Entity origin_entity;
    public BuffType[] buff_types;
    public GameObject buff_effect;
    public Buff(float buff_time, string buff_name, Entity origin_entity, BuffType[] buff_types, GameObject buff_effect)
    {
        this.buff_values = new float[buff_types.Length];
        this.buff_time = buff_time;
        this.buff_name = buff_name;
        this.origin_entity = origin_entity;
        this.buff_types = buff_types;
        this.buff_effect = buff_effect;
    }
}
```
改为：
```csharp
[Serializable]
public class Buff
{
    public Modifier[] modifiers;       // 不可变快照，所有改动经 SetBuffValues 重建
    public float buff_time;
    public string buff_name;
    public Entity origin_entity;
    public GameObject buff_effect;
    public Buff(float buff_time, string buff_name, Entity origin_entity, Modifier[] modifiers, GameObject buff_effect)
    {
        this.modifiers = modifiers ?? System.Array.Empty<Modifier>();
        this.buff_time = buff_time;
        this.buff_name = buff_name;
        this.origin_entity = origin_entity;
        this.buff_effect = buff_effect;
    }
}
```

- [ ] **Step 3: 改 `BuffController` 字段——删 buffValue，加 _store（139-144 行）**

第 139-144 行：
```csharp
    private Entity _thisEntity;
    private List<Buff> white_list_buffs;
    private List<Buff> normal_buffs;
    private float[] _abnormalStateTime;
    private List<DOTData> _dotDatas;
    public Dictionary<BuffType, float> buffValue;
```
改为：
```csharp
    private Entity _thisEntity;
    private List<Buff> white_list_buffs;
    private List<Buff> normal_buffs;
    private float[] _abnormalStateTime;
    private List<DOTData> _dotDatas;
    private AttributeStore _store;
```

- [ ] **Step 4: 加 `Bind` 方法**

在 `Buffs` 属性（145-154 行）之后、`FixedUpdate`（156 行）之前，加：
```csharp
    /// <summary>
    /// 注入 AttributeStore。Entity 在 PreWarm 中调用。
    /// </summary>
    public void Bind(AttributeStore store)
    {
        _store = store;
    }
```

- [ ] **Step 5: 改 `CreateBuff`（175-222 行）——签名 BuffType[]+float[] → Modifier[]**

第 175-222 行整个 `CreateBuff` 方法：
```csharp
    public Buff CreateBuff(BuffType[] buffTypes, GameObject buffEffect, string buffName, float[] buffValue, float buffTime, bool isWhiteList)
    {
        Buff addBuff;
        if (isWhiteList)
        {
            foreach (Buff b in white_list_buffs)
            {
                if (b.buff_name == buffName)
                {
                    buffEffect = b.buff_effect;
                    addBuff = new Buff(buffTime, buffName, _thisEntity, buffTypes, buffEffect);
                    SetBuffValues(buffValue, addBuff);
                    white_list_buffs.Add(addBuff);
                    return addBuff;
                }
            }
            if (buffEffect)
            {
                buffEffect = Instantiate(buffEffect, _thisEntity.TempContainer.position, Quaternion.identity, _thisEntity.TempContainer);
            }
            addBuff = new Buff(buffTime, buffName, _thisEntity, buffTypes, buffEffect);
            SetBuffValues(buffValue, addBuff);
            white_list_buffs.Add(addBuff);
            return addBuff;
        }
        else
        {
            foreach (Buff b in normal_buffs)
            {
                if (b.buff_name == buffName)
                {
                    buffEffect = b.buff_effect;
                    addBuff = new Buff(buffTime, buffName, _thisEntity, buffTypes, buffEffect);
                    SetBuffValues(buffValue, addBuff);
                    normal_buffs.Add(addBuff);
                    return addBuff;
                }
            }
            if (buffEffect)
            {
                buffEffect = Instantiate(buffEffect, _thisEntity.TempContainer.position, Quaternion.identity, _thisEntity.TempContainer);
            }
            addBuff = new Buff(buffTime, buffName, _thisEntity, buffTypes, buffEffect);
            SetBuffValues(buffValue, addBuff);
            normal_buffs.Add(addBuff);
            return addBuff;
        }
    }
```
改为：
```csharp
    public Buff CreateBuff(Modifier[] modifiers, GameObject buffEffect, string buffName, float buffTime, bool isWhiteList)
    {
        Buff addBuff;
        if (isWhiteList)
        {
            foreach (Buff b in white_list_buffs)
            {
                if (b.buff_name == buffName)
                {
                    buffEffect = b.buff_effect;
                    addBuff = new Buff(buffTime, buffName, _thisEntity, modifiers, buffEffect);
                    SetBuffValues(modifiers, addBuff);
                    white_list_buffs.Add(addBuff);
                    return addBuff;
                }
            }
            if (buffEffect)
            {
                buffEffect = Instantiate(buffEffect, _thisEntity.TempContainer.position, Quaternion.identity, _thisEntity.TempContainer);
            }
            addBuff = new Buff(buffTime, buffName, _thisEntity, modifiers, buffEffect);
            SetBuffValues(modifiers, addBuff);
            white_list_buffs.Add(addBuff);
            return addBuff;
        }
        else
        {
            foreach (Buff b in normal_buffs)
            {
                if (b.buff_name == buffName)
                {
                    buffEffect = b.buff_effect;
                    addBuff = new Buff(buffTime, buffName, _thisEntity, modifiers, buffEffect);
                    SetBuffValues(modifiers, addBuff);
                    normal_buffs.Add(addBuff);
                    return addBuff;
                }
            }
            if (buffEffect)
            {
                buffEffect = Instantiate(buffEffect, _thisEntity.TempContainer.position, Quaternion.identity, _thisEntity.TempContainer);
            }
            addBuff = new Buff(buffTime, buffName, _thisEntity, modifiers, buffEffect);
            SetBuffValues(modifiers, addBuff);
            normal_buffs.Add(addBuff);
            return addBuff;
        }
    }
```

- [ ] **Step 6: 改 `DestroyBuff`（227-236 行）**

第 227-236 行：
```csharp
    public void DestroyBuff(Buff destroyBuff)
    {
        SetBuffValues(new float[destroyBuff.buff_types.Length], destroyBuff);
        white_list_buffs.Remove(destroyBuff);
        normal_buffs.Remove(destroyBuff);
        if (!(white_list_buffs.Contains(destroyBuff) || normal_buffs.Contains(destroyBuff)))
        {
            Destroy(destroyBuff.buff_effect);
        }
    }
```
改为：
```csharp
    public void DestroyBuff(Buff destroyBuff)
    {
        // 移除该 buff 的所有 modifier（置脏）
        if (destroyBuff.modifiers != null) _store.RemoveModifiers(destroyBuff.modifiers);
        white_list_buffs.Remove(destroyBuff);
        normal_buffs.Remove(destroyBuff);
        if (!(white_list_buffs.Contains(destroyBuff) || normal_buffs.Contains(destroyBuff)))
        {
            Destroy(destroyBuff.buff_effect);
        }
    }
```

- [ ] **Step 7: 删 `FetchBuff`（242-259 行）**

删除第 237-259 行（含上方注释）整个 `FetchBuff` 方法（死代码）。

- [ ] **Step 8: 改 `SetBuffValues`（263-273 行）+ 删 `BuffResultStatistic`（274-301 行）**

第 260-301 行（`SetBuffValues` + `BuffResultStatistic` 两个方法，含注释）：
```csharp
    /// <summary>
    /// 设置Buff数值
    /// </summary>
    /// <param name="newBuffValues">要设置的Buff数值（数组长度必须和原来相等）</param>
    /// <param name="setTarget">需要设置的目标Buff</param>
    public void SetBuffValues(float[] newBuffValues, Buff setTarget)
    {
        for (int i = 0; i < newBuffValues.Length; i++)
        {
            if (newBuffValues[i] != setTarget.buff_values[i])
                BuffResultStatistic(setTarget.buff_types[i], newBuffValues[i] - setTarget.buff_values[i]);
        }
        setTarget.buff_values = newBuffValues;
    }
    private void BuffResultStatistic(BuffType buffType, float value)
    {
        buffValue[buffType] += buffType switch
        {
            ...  // 整个 switch
        };
    }
```
替换为（只保留 `SetBuffValues`，签名改 `Modifier[]`，删掉 `BuffResultStatistic`）：
```csharp
    /// <summary>
    /// 设置 Buff 的 modifier 数组（不可变快照替换）。
    /// 所有 buff 数值改动（创建赋值、动态更新、销毁）统一经此入口：
    /// 移除旧快照 → 替换为新快照 → 加入新快照（store 置脏）。
    /// </summary>
    /// <param name="newModifiers">新的 modifier 数组</param>
    /// <param name="setTarget">目标 Buff</param>
    public void SetBuffValues(Modifier[] newModifiers, Buff setTarget)
    {
        Modifier[] old = setTarget.modifiers;
        if (old != null) _store.RemoveModifiers(old);
        setTarget.modifiers = newModifiers ?? System.Array.Empty<Modifier>();
        _store.AddModifiers(setTarget.modifiers);
    }
```

- [ ] **Step 9: 改 `Dormancy`（549-568 行）——加 store.Clear()**

第 559-562 行 `Dormancy` 里的 buffValue 重置块：
```csharp
        for (int i = 0; i < buffValue.Count; i++)
        {
            buffValue[(BuffType)i] = 0;
        }
```
替换为：
```csharp
        if (_store != null) _store.Clear();
```

- [ ] **Step 10: 改 `PreWarm`（532-544 行）——删 buffValue 初始化**

第 532-544 行 `PreWarm`：
```csharp
    public void PreWarm()
    {
        white_list_buffs = new List<Buff>();
        normal_buffs = new List<Buff>();
        _thisEntity = this.transform.GetComponent<Entity>();
        buffValue = new Dictionary<BuffType, float>();
        _abnormalStateTime = new float[4];
        _dotDatas = new List<DOTData>();
        for (int i = 0; i < System.Enum.GetNames(typeof(BuffType)).Length; i++)
        {
            buffValue.Add((BuffType)i, 0);
        }
    }
```
改为：
```csharp
    public void PreWarm()
    {
        white_list_buffs = new List<Buff>();
        normal_buffs = new List<Buff>();
        _thisEntity = this.transform.GetComponent<Entity>();
        _abnormalStateTime = new float[4];
        _dotDatas = new List<DOTData>();
    }
```
（`_store` 由 `Entity.PreWarm` 调 `Bind` 注入，不在此初始化。）

- [ ] **Step 11: 检查 `using` 与残留 `BuffType` 引用**

搜索 `BuffController.cs` 内是否还有 `BuffType` 残留（DOT/AbnormalState 区块应无）。确认第 5 行 `using static PlasticGui.PlasticTableCell;` 是否仍需要——若它只为 `print` 之类服务且 DOT 区块用了 `print`，保留；否则可删。**保守起见保留不动**。

- [ ] **Step 12: 等待 Unity 编译**

预期：`BuffController.cs` + `EntityStats.cs` + `Entity.cs` 编译通过。但**所有调用方**（ApplyBuff、各 Skill/Talent 脚本、Cards）仍在传 `BuffType[]`/`float[]` 或读 `buff_values`——**编译失败在调用方**。这是预期，Task 6+ 逐个修复。

- [ ] **Step 13: 一次性 commit Task 3-5 的核心改造**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntityStats.cs \
        Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs \
        Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/BuffController.cs
git commit -m "数值系统：EntityStats/BuffController/Entity 改读 AttributeStore（核心改造）"
```
> 调用方尚未迁移，此次 commit 后项目编译失败属预期，下一任务起逐个修复。

---

## Task 6: 迁移 `ApplyBuff.cs`（数据驱动入口，三 CSV）

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/ApplyBuff.cs`

- [ ] **Step 1: 改字段（21-37 行）——buffTypes/buffValues → attributes/ops/magnitudes**

第 21-37 行的字段块里，把：
```csharp
        private Func<BuffType[]> _types;
        private Func<float[]> _values;
```
改为：
```csharp
        private Func<string[]> _attributes;
        private Func<string[]> _ops;
        private Func<float[]> _magnitudes;
```

- [ ] **Step 2: 改 `OnInit` 解析（39-52 行）**

第 42-43 行：
```csharp
            _types  = p.GetStringArrayLazy("buffTypes",  null, bb, s => (BuffType)Enum.Parse(typeof(BuffType), s));
            _values = p.GetFloatArrayLazy("buffValues", null, bb);
```
改为：
```csharp
            _attributes = p.GetStringArrayLazy("attributes", null, bb);
            _ops         = p.GetStringArrayLazy("ops",        null, bb);
            _magnitudes  = p.GetFloatArrayLazy ("magnitudes", null, bb);
```

- [ ] **Step 3: 加 `BuildModifiers` 辅助方法**

在 `OnTrigger` 方法之前（53 行附近）加：
```csharp
        /// <summary>
        /// 把三 CSV（attributes/ops/magnitudes）按下标对齐构造成 Modifier[]。
        /// 长度不一致取最短 + 一次性 warn（沿用 AttackEventValueModifier 容错模式）。
        /// </summary>
        private Modifier[] BuildModifiers()
        {
            string[] attrs = _attributes() ?? System.Array.Empty<string>();
            string[] ops   = _ops()        ?? System.Array.Empty<string>();
            float[]  mags  = _magnitudes() ?? System.Array.Empty<float>();
            int len = System.Math.Min(System.Math.Min(attrs.Length, ops.Length), mags.Length);
            if (attrs.Length != ops.Length || ops.Length != mags.Length)
            {
                OneShotWarn.WarnOnce("apply-buff-csv-length",
                    $"ApplyBuff: attributes/ops/magnitudes 长度不一致 ({attrs.Length}/{ops.Length}/{mags.Length}); 取最短 {len}。");
            }
            var result = new Modifier[len];
            for (int i = 0; i < len; i++)
            {
                ModifierOp op = (ModifierOp)Enum.Parse(typeof(ModifierOp), ops[i].Trim());
                result[i] = new Modifier(attrs[i].Trim(), op, mags[i]);
            }
            return result;
        }
```

- [ ] **Step 4: 改 `OnTrigger` normal 模式（54-101 行）——用 BuildModifiers**

第 57 行：
```csharp
            if (_types().Length == 0) return;
```
改为：
```csharp
            Modifier[] modifiers = BuildModifiers();
            if (modifiers.Length == 0) return;
```

第 90 行：
```csharp
                Buff created = t.buffController.CreateBuff(_types(), null, _buffId(), _values(), _buffTime(), _isWhiteList());
```
改为：
```csharp
                Buff created = t.buffController.CreateBuff(modifiers, null, _buffId(), _buffTime(), _isWhiteList());
```

- [ ] **Step 5: 改 `SyncAura`（103-168 行）**

第 116-117 行：
```csharp
            BuffType[] types = _types();
            float[] values = _values();
            if (types.Length != values.Length)
            {
                OneShotWarn.WarnOnce(
                    "apply-buff-aura-length",
                    $"ApplyBuff: buffTypes/buffValues length mismatch ({types.Length}/{values.Length}); skipping aura sync.");
                return;
            }
```
改为：
```csharp
            Modifier[] modifiers = BuildModifiers();
            if (modifiers.Length == 0)
            {
                OneShotWarn.WarnOnce(
                    "apply-buff-aura-empty",
                    "ApplyBuff: aura 模式下 modifiers 为空，跳过同步。");
                return;
            }
```

第 141 行：
```csharp
                    target.buffController.SetBuffValues(values, tracked);
```
改为：
```csharp
                    target.buffController.SetBuffValues(modifiers, tracked);
```

第 146-147 行：
```csharp
                    tracked = target.buffController.CreateBuff(
                        types, null, _buffId(), values, _buffTime(), _isWhiteList());
```
改为：
```csharp
                    tracked = target.buffController.CreateBuff(
                        modifiers, null, _buffId(), _buffTime(), _isWhiteList());
```

- [ ] **Step 6: 等待 Unity 编译**

预期：`ApplyBuff.cs` 编译通过。但 `.asset` 文件还是旧字段名 `buffTypes`/`buffValues`——运行时 `_attributes()` 返回空数组，`BuildModifiers` 返回空，buff 不生效。Task 10 改 `.asset` 后才恢复。

- [ ] **Step 7: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/ApplyBuff.cs
git commit -m "数值系统：ApplyBuff 改三 CSV（attributes/ops/magnitudes）解析"
```

---

## Task 7: 迁移 12 处代码 CreateBuff 调用方（含 Nsabr bug 修复）

每处把 `new BuffType[]{...}` + `float[]{...}` 换成 `new Modifier[]{...}`，按换算表处理 phd/mgd/phdoge/mgdoge 数值。逐文件改、逐文件 commit。

**通用模式**：旧
```csharp
buffController.CreateBuff(new BuffType[]{BuffType.atk_delta_percent}, effect, "name", new float[]{0.45f}, -5f, false);
```
新：
```csharp
buffController.CreateBuff(new Modifier[]{new Modifier(Attributes.Attack, ModifierOp.AddPercent, 0.45f)}, effect, "name", -5f, false);
```

### 7a: `Nsabr/ts1.cs`（含绕 setter bug 修复）

**Files:** Modify `Assets/.../Nsabr/ts1.cs`（路径需确认，搜索 `ts1.cs`）

- [ ] **Step 1: 读 `ts1.cs` 确认当前内容与行号**

Run: 在 Unity 项目里定位 `Nsabr/ts1.cs`，Read 它。预期：第 15 行 `CreateBuff` 传 `BuffType[]{atkspd_delta_value, atk_delta_value}` + `float[]{-95, -95}`；第 19-20、29-30 行直接写 `buff.buff_values[0] = 95` 绕 setter。

- [ ] **Step 2: 改 CreateBuff（~15 行）**

把传 `BuffType[]` + `float[]` 的调用改为：
```csharp
_buff = buffController.CreateBuff(
    new Modifier[]
    {
        new Modifier(Attributes.AttackSpeed, ModifierOp.AddFlat, -95f),
        new Modifier(Attributes.Attack, ModifierOp.AddFlat, -95f),
    },
    null, "狂暴速度扣除", -5f, false);
```

- [ ] **Step 3: 修复绕 setter 的直接赋值（~19-20、29-30 行）**

把直接写 `buff.buff_values[0] = 95;` 之类改为走 `SetBuffValues` 重建 modifier 数组。例如若原意是"把 -95 改成 95"：
```csharp
buffController.SetBuffValues(
    new Modifier[]
    {
        new Modifier(Attributes.AttackSpeed, ModifierOp.AddFlat, 95f),
        new Modifier(Attributes.Attack, ModifierOp.AddFlat, 95f),
    },
    _buff);
```
（具体数值以读到的原代码为准——关键是把直接改 `buff_values[0]` 换成 `SetBuffValues(new Modifier[]{...}, buff)`。）

- [ ] **Step 4: 编译 + commit**

```bash
git add <ts1.cs 路径>
git commit -m "数值系统：Nsabr/ts1 迁移 Modifier + 修复绕 setter bug"
```

### 7b: `MachineTalent1.cs`

**Files:** Modify `Assets/.../MachineTalent1.cs`

- [ ] **Step 1: Read 确认行号。** 第 28 行 `CreateBuff` 传 `mspeed_delta_percent={_v-1}`；第 112 行 `SetBuffValues` 动态更新。

- [ ] **Step 2: 改 CreateBuff（~28 行）**
```csharp
_machineBuff = buffController.CreateBuff(
    new Modifier[]{ new Modifier(Attributes.MoveSpeed, ModifierOp.AddPercent, _v - 1f) },
    null, "machine", -5f, false);
```

- [ ] **Step 3: 改 SetBuffValues（~112 行）**
```csharp
buffController.SetBuffValues(
    new Modifier[]{ new Modifier(Attributes.MoveSpeed, ModifierOp.AddPercent, _v - 1f) },
    _machineBuff);
```
（`_v` 变量名以原代码为准。）

- [ ] **Step 4: 编译 + commit**
```bash
git add <MachineTalent1.cs 路径>
git commit -m "数值系统：MachineTalent1 迁移 Modifier"
```

### 7c: `MCEnvironmentalDevice.cs`

**Files:** Modify `Assets/.../MCEnvironmentalDevice.cs`

- [ ] **Step 1: Read 确认行号。** 第 56 行 `CreateBuff` 传 `atk_delta_percent=0.15, atkspd_delta_value=30`；第 88/95/102 行读 `buff.buff_values[0]` 判断状态；第 90/97/104 行 `SetBuffValues`。

- [ ] **Step 2: 改 CreateBuff（~56 行）**
```csharp
_hungryBuff = buffController.CreateBuff(
    new Modifier[]
    {
        new Modifier(Attributes.Attack, ModifierOp.AddPercent, 0.15f),
        new Modifier(Attributes.AttackSpeed, ModifierOp.AddFlat, 30f),
    },
    effect, "hungryBuff", -5f, true);
```

- [ ] **Step 3: 改读 `buff.buff_values[0]`（~88/95/102 行）为读 `buff.modifiers[0].magnitude`**

旧：`_hungryBuff.buff_values[0]`
新：`_hungryBuff.modifiers[0].magnitude`

（索引 0 对应第一个 modifier 即 Attack percent，语义一致。核对每个比较点的索引对应关系。）

- [ ] **Step 4: 改三处 SetBuffValues（~90/97/104 行）**

每处按当前状态构造 `Modifier[]` 传入。例如"满"状态：
```csharp
buffController.SetBuffValues(
    new Modifier[]
    {
        new Modifier(Attributes.Attack, ModifierOp.AddPercent, 0.15f),
        new Modifier(Attributes.AttackSpeed, ModifierOp.AddFlat, 30f),
    },
    _hungryBuff);
```
（具体数值以原代码三个状态的目标值为准——读原 `SetBuffValues(new float[]{...})` 的数组内容对照。）

- [ ] **Step 5: 编译 + commit**
```bash
git add <MCEnvironmentalDevice.cs 路径>
git commit -m "数值系统：MCEnvironmentalDevice 迁移 Modifier（读 modifiers[0].magnitude）"
```

### 7d: `Eyjafjalla/Talent1.cs`（含 phd/mgd 换算）

**Files:** Modify `Assets/.../Eyjafjalla/Talent1.cs`

- [ ] **Step 1: Read 确认行号。** 第 16 行声明 `phd_delta_rate=-0.995, mgd_delta_rate=-0.995`；第 61 行 `CreateBuff` "lavaStrength"；第 60/89 行 `SetBuffValues` "lavaBuff"（atk_percent 动态）。

- [ ] **Step 2: 改 phd/mgd 声明（~16 行）——换算 `1+旧值`**

旧：`-0.995f`
新：`1 + (-0.995f) = 0.005f`，且 op 从隐式 rate 改为显式 `MulFinal`。

- [ ] **Step 3: 改 lavaStrength CreateBuff（~61 行）**
```csharp
_lavaStrength = buffController.CreateBuff(
    new Modifier[]
    {
        new Modifier(Attributes.PhysicalDamageRate, ModifierOp.MulFinal, 0.005f),
        new Modifier(Attributes.MagicDamageRate, ModifierOp.MulFinal, 0.005f),
    },
    effect, "lavaStrength", -5f, false);
```

- [ ] **Step 4: 改 lavaBuff CreateBuff + SetBuffValues（~101、60、89 行）**

lavaBuff 是 `atk_delta_percent` 动态值。CreateBuff：
```csharp
_lavaBuff = buffController.CreateBuff(
    new Modifier[]{ new Modifier(Attributes.Attack, ModifierOp.AddPercent, 0f) },
    effect, "lavaBuff", -5f, true);
```
SetBuffValues（动态更新 atk_percent）：
```csharp
buffController.SetBuffValues(
    new Modifier[]{ new Modifier(Attributes.Attack, ModifierOp.AddPercent, <原动态值>) },
    _lavaBuff);
```

- [ ] **Step 5: 编译 + commit**
```bash
git add <Eyjafjalla/Talent1.cs 路径>
git commit -m "数值系统：Eyjafjalla/Talent1 迁移 Modifier（phd/mgd 换算 1+旧值）"
```

### 7e: `Eyjafjalla/Skill1.cs`

**Files:** Modify `Assets/.../Eyjafjalla/Skill1.cs`

- [ ] **Step 1: Read 确认。** 第 10 行 `atkspd_delta_value=120`；第 21 行 `CreateBuff`；第 28 行 `DestroyBuff`（无需改，DestroyBuff 签名不变）。

- [ ] **Step 2: 改 CreateBuff（~21 行）**
```csharp
_skill1Buff = buffController.CreateBuff(
    new Modifier[]{ new Modifier(Attributes.AttackSpeed, ModifierOp.AddFlat, 120f) },
    effect, "EyjafjallaSkill1", -5f, false);
```

- [ ] **Step 3: 编译 + commit**
```bash
git add <Eyjafjalla/Skill1.cs 路径>
git commit -m "数值系统：Eyjafjalla/Skill1 迁移 Modifier"
```

### 7f: `WdslmSkill2.cs`

**Files:** Modify `Assets/.../WdslmSkill2.cs`

- [ ] **Step 1: Read 确认。** 第 11 行声明 `atk_delta_percent=-0.45, mhp_delta_percent=6.5`；第 30 行 `CreateBuff` "incite"。

- [ ] **Step 2: 改 CreateBuff（~30 行）**
```csharp
_inciteBuff = buffController.CreateBuff(
    new Modifier[]
    {
        new Modifier(Attributes.Attack, ModifierOp.AddPercent, -0.45f),
        new Modifier(Attributes.MaxHp, ModifierOp.AddPercent, 6.5f),
    },
    effect, "incite", -5f, true);
```
（percent 类原值不换算。）

- [ ] **Step 3: 编译 + commit**
```bash
git add <WdslmSkill2.cs 路径>
git commit -m "数值系统：WdslmSkill2 迁移 Modifier"
```

### 7g: `WitherAttack.cs`

**Files:** Modify `Assets/.../WitherAttack.cs`

- [ ] **Step 1: Read 确认。** 第 12 行 `CreateBuff` 传 `atkminn_delta_value=3`。

- [ ] **Step 2: 改 CreateBuff（~12 行）**
```csharp
buffController.CreateBuff(
    new Modifier[]{ new Modifier(Attributes.AttackMinNum, ModifierOp.AddFlat, 3f) },
    effect, "threeheadattack", -5f, true);
```

- [ ] **Step 3: 编译 + commit**
```bash
git add <WitherAttack.cs 路径>
git commit -m "数值系统：WitherAttack 迁移 Modifier"
```

### 7h: `WitherTalent3.cs`

**Files:** Modify `Assets/.../WitherTalent3.cs`

- [ ] **Step 1: Read 确认。** 第 20 行 `CreateBuff` 传 `def/mgr/atk/atkspd_delta_value={变量}, mspeed_delta_percent=-{变量}` "witherShield"。

- [ ] **Step 2: 改 CreateBuff（~20 行）**
```csharp
_witherShield = buffController.CreateBuff(
    new Modifier[]
    {
        new Modifier(Attributes.Defense, ModifierOp.AddFlat, _defUp),
        new Modifier(Attributes.MagicResistance, ModifierOp.AddFlat, _mgrUp),
        new Modifier(Attributes.Attack, ModifierOp.AddFlat, _atkUp),
        new Modifier(Attributes.AttackSpeed, ModifierOp.AddFlat, _atkspdUp),
        new Modifier(Attributes.MoveSpeed, ModifierOp.AddPercent, -_speedown),
    },
    effect, "witherShield", -5f, true);
```
（变量名以原代码为准。）

- [ ] **Step 3: 编译 + commit**
```bash
git add <WitherTalent3.cs 路径>
git commit -m "数值系统：WitherTalent3 迁移 Modifier"
```

### 7i: `HeadSeterTalent1.cs`

**Files:** Modify `Assets/.../HeadSeterTalent1.cs`

- [ ] **Step 1: Read 确认。** 第 43 行 `CreateBuff` 传 `mspeed_delta_percent=-0.35` "slowSpeed"。

- [ ] **Step 2: 改 CreateBuff（~43 行）**
```csharp
_slowSpeed = buffController.CreateBuff(
    new Modifier[]{ new Modifier(Attributes.MoveSpeed, ModifierOp.AddPercent, -0.35f) },
    effect, "slowSpeed", -5f, false);
```

- [ ] **Step 3: 编译 + commit**
```bash
git add <HeadSeterTalent1.cs 路径>
git commit -m "数值系统：HeadSeterTalent1 迁移 Modifier"
```

### 7j: 全项目编译检查

- [ ] **Step 1: 在 Unity 里触发编译，确认无 `BuffType`/`buffValue`/`buff_values`/`buff_types` 残留引用**

搜索全项目 `BuffType`、`buff_values`、`buff_types`、`BindBuffController`——预期只剩 `Cards.cs`（Task 8 处理）和 `.asset` 文件（Task 10，非代码不报编译错）。代码侧应全部清零。

---

## Task 8: 迁移 `Cards.cs`（UI 读 modifiers）

**Files:**
- Modify: `Assets/.../Cards.cs`（搜索定位，~141-168 行）

- [ ] **Step 1: Read `Cards.cs` 确认行号。** 第 141-144 行 `Dictionary<BuffType, string>` 显示名表（仅 1 条 `atkminn_delta_value → "最小攻击数"`）；第 161-168 行 `BuffCard.UpdateBuffCardMessage` 读 `buff.buff_values`/`buff.buff_types`。

- [ ] **Step 2: 改显示名表（~141-144 行）**
```csharp
private static readonly Dictionary<string, string> BuffDisplayNames = new Dictionary<string, string>
{
    { Attributes.AttackMinNum, "最小攻击数" },
};
```

- [ ] **Step 3: 改 `UpdateBuffCardMessage`（~161-168 行）——读 `buff.modifiers`**

把遍历 `buff.buff_types` + `buff.buff_values` 改为遍历 `buff.modifiers`：
```csharp
foreach (Modifier m in buff.modifiers)
{
    string name = BuffDisplayNames.TryGetValue(m.attribute, out var dn) ? dn : m.attribute;
    // 展示 name + m.op + m.magnitude（沿用原展示格式）
    ...
}
```
（具体展示字符串格式以原代码为准——只把数据源从 `buff_types[i]`/`buff_values[i]` 换成 `modifiers[i].attribute`/`.op`/`.magnitude`。）

- [ ] **Step 4: 编译 + commit**
```bash
git add <Cards.cs 路径>
git commit -m "数值系统：Cards UI 读 buff.modifiers 展示"
```

---

## Task 9: 迁移 13 处 `.asset` 文件（三 CSV + 数值换算）

每处把 `buffTypes`/`buffValues` 两个 CSV 字段改成 `attributes`/`ops`/`magnitudes` 三个 CSV，按换算表处理 phd/mgd/phdoge/mgdoge。

**通用模式**：旧
```yaml
buffTypes: "atk_delta_percent,batkt_delta_value"
buffValues: "0.45,1.3"
```
新：
```yaml
attributes: "Attack,BaseAttackTime"
ops:        "AddPercent,AddFlat"
magnitudes: "0.45,1.3"
```

逐文件改、改完进 Unity 加载验证（看 ApplyBuff 组件 Inspector 是否正确解析三字段）。

### 9a: `Characters/3/Spot/skills/spot_s1.asset`

- [ ] **Step 1: Read `spot_s1.asset`（~34-39 行）确认。** 旧 `buffTypes: "atk_delta_percent,batkt_delta_value"`, `buffValues: "0.45,1.3"`。
- [ ] **Step 2: 改为：**
```yaml
attributes: "Attack,BaseAttackTime"
ops:        "AddPercent,AddFlat"
magnitudes: "0.45,1.3"
```
- [ ] **Step 3: Unity 里加载 spot_s1 prefab/asset，确认 ApplyBuff Inspector 显示三字段。**

### 9b: `Characters/3/Spot/talents/spot_t1.asset`（含 phdoge 换算）

- [ ] **Step 1: Read 确认。** 旧 `buffTypes: "phdoge_delta_rate"`, `buffValues: "0.25"`。
- [ ] **Step 2: 改为（phdoge 换算 `1-0.25=0.75`）：**
```yaml
attributes: "PhysicalDodge"
ops:        "MulFinal"
magnitudes: "0.75"
```

### 9c: `Characters/3/Stward/talents/stward_t1.asset`

- [ ] **Step 1: Read 确认。** 旧 `buffTypes: "atk_delta_percent"`, `buffValues: "0.06"`。
- [ ] **Step 2: 改为：**
```yaml
attributes: "Attack"
ops:        "AddPercent"
magnitudes: "0.06"
```

### 9d: `Characters/3/Hibisc/talents/hibisc_t1.asset`

- [ ] **Step 1: Read 确认。** 旧 `atk_delta_percent`, `0.08`。
- [ ] **Step 2: 改为 `attributes: "Attack"`, `ops: "AddPercent"`, `magnitudes: "0.08"`。

### 9e: `Characters/3/Hibisc/skills/hibisc_s1.asset`

- [ ] **Step 1: Read 确认。** 旧 `atk_delta_percent`, `0.5`。
- [ ] **Step 2: 改为 `attributes: "Attack"`, `ops: "AddPercent"`, `magnitudes: "0.5"`。

### 9f: `Characters/3/Melan/talents/melan_t1.asset`

- [ ] **Step 1: Read 确认。** 旧 `atk_delta_percent`, `0.08`。
- [ ] **Step 2: 改为 `attributes: "Attack"`, `ops: "AddPercent"`, `magnitudes: "0.08"`。

### 9g: `Characters/3/Melan/skills/melan_s1.asset`

- [ ] **Step 1: Read 确认。** 旧 `atk_delta_percent`, `0.5`。
- [ ] **Step 2: 改为 `attributes: "Attack"`, `ops: "AddPercent"`, `magnitudes: "0.5"`。

### 9h: `Characters/6/Ebnhlz/skills/ebnhlz_s3.asset`

- [ ] **Step 1: Read 确认。** 旧 `buffTypes: "atkspd_delta_value,atk_delta_percent"`, `buffValues: "80,0.65"`。
- [ ] **Step 2: 改为：**
```yaml
attributes: "AttackSpeed,Attack"
ops:        "AddFlat,AddPercent"
magnitudes: "80,0.65"
```

### 9i: `Monsters/MC/Zombie/talents/zombie_t1.asset`（两个 ApplyBuff）

- [ ] **Step 1: Read 确认两处。** 第一处（~82-87 行）旧 `buffTypes: "atk_delta_percent,atkspd_delta_value,mspeed_delta_percent"`, `buffValues: "0.5,60,0.6"`；第二处（~202-207 行）旧 `buffTypes: "atk_delta_percent,atkspd_delta_value"`, `buffValues: "0.3,20"`。
- [ ] **Step 2: 第一处改为：**
```yaml
attributes: "Attack,AttackSpeed,MoveSpeed"
ops:        "AddPercent,AddFlat,AddPercent"
magnitudes: "0.5,60,0.6"
```
- [ ] **Step 3: 第二处改为：**
```yaml
attributes: "Attack,AttackSpeed"
ops:        "AddPercent,AddFlat"
magnitudes: "0.3,20"
```

### 9j: 批量 commit 所有 .asset

```bash
git add Assets/.../Characters/ Assets/.../Monsters/
git commit -m "数值系统：13 处 .asset 迁移三 CSV 格式（含 phd/mgd/phdoge/mgdoge 换算）"
```

> Creeper 的 `creeper_t1.asset` 含 ApplyAbnormalState（type 0/2），不涉及 buff 数值，**不改**。Zombie 的 ApplyAbnormalState（type 3）同理不改。

---

## Task 10: 全项目编译 + 跑游戏验证

- [ ] **Step 1: Unity 全量编译，确认零错误零警告**

搜索全项目残留：`BuffType`、`buffValue`、`buff_values`、`buff_types`、`BindBuffController`、`BuffResultStatistic`——全部应为零。

- [ ] **Step 2: 进 Play Mode，验证核心场景**

跑一个完整关卡，重点观察：
1. **角色攻击力 buff 生效**：选 Spot/ Hibisc/ Melan（有 atk_delta_percent 天赋），看攻击数值是否随天赋变化。
2. **攻击速度 buff 生效**：Eyjafjalla Skill1（atkspd +120）、Ebnhlz S3（atkspd+80），看攻速变化。
3. **移动速度 buff**：HeadSeter slowSpeed（-35%）、MachineTalent（动态），看移速。
4. **伤害减免（phd/mgd）**：Eyjafjalla Talent1 lavaStrength（0.005），看熔岩泡受到的伤害是否接近 0。
5. **闪避（phdoge）**：Spot T1（0.75 未命中概率），看物理闪避是否提升。
6. **DOT/AbnormalState 不受影响**：中毒 DOT、眩晕/无敌，行为与重构前一致。
7. **池复用不残留**：同一实体多次进出池，属性数值正确（Dormancy 的 store.Clear 生效）。

- [ ] **Step 3: 数值偏差排查**

若某属性数值与重构前不符，优先查：
- phd/mgd 是否换算成 `1+旧值`（`-0.995 → 0.005`）
- phdoge/mgdoge 是否换算成 `1-旧值`（`0.25 → 0.75`）
- AttackSpeed base=100 是否注入（`AttributesCaculateFirst` 里 `SetBase(AttackSpeed, 100f)`）
- PhysicalDamageRate/MagicDamageRate base=1 是否注入
- `MulFinal` 空桶是否返回 1（移除所有减免 buff 后伤害不应归零）

- [ ] **Step 4: 最终 commit（若有验证修复）**
```bash
git add -A
git commit -m "数值系统重构完成：Modifier + AttributeStore 全链路验证通过"
```

---

## 自审

**1. Spec 覆盖**：
- Modifier readonly struct / ModifierOp / Attributes → Task 1 ✅
- AttributeStore（lazy+脏标记+四段公式+空桶→1+Clear） → Task 2 ✅
- EntityStats 改读 store + SetBase + AttackSpeed=100 + phd/mgd 改读 → Task 3 ✅
- Entity PreWarm 注入 → Task 4 ✅
- BuffController 瘦身（删 BuffType/buffValue/BuffResultStatistic/FetchBuff，Buff 字段换 modifiers，签名改 Modifier[]，Dormancy store.Clear） → Task 5 ✅
- ApplyBuff 三 CSV → Task 6 ✅
- 12 处代码 CreateBuff + 4 处 SetBuffValues（含 Nsabr bug） → Task 7 ✅
- Cards UI → Task 8 ✅
- 13 处 .asset（含 4 项换算） → Task 9 ✅
- DOT/AbnormalState 零改动 → Task 5 保留区块 ✅
- FetchBuff 删除 → Task 5 Step 7 ✅

**2. 占位符扫描**：Task 7/8/9 里多处写了"变量名以原代码为准""具体数值以原代码为准"——这是有意的，因为这些文件的精确变量名/数值需 Read 后才能确定，但模式已完整给出。不是占位符，是"读后对号入座"的明确指令。无 TBD/TODO。

**3. 类型一致性**：
- `Bind(AttributeStore)` — Task 3（EntityStats）、Task 4（Entity 调 buffController.Bind）、Task 5（BuffController.Bind）三处一致 ✅
- `CreateBuff(Modifier[], GameObject, string, float, bool)` — Task 5 定义、Task 6/7 调用一致 ✅
- `SetBuffValues(Modifier[], Buff)` — Task 5 定义、Task 6/7 调用一致 ✅
- `DestroyBuff(Buff)` — 签名不变，Task 7e 提到"无需改" ✅
- `Attributes.Xxx` 常量名 — Task 1 定义、Task 3/7 引用一致 ✅
- `ModifierOp.AddFlat/AddPercent/AddFlatFinal/MulFinal` — Task 1 定义、全文引用一致 ✅
- `store.Clear()` — Task 2 定义、Task 5 Dormancy 调用一致 ✅

**4. 风险点覆盖**：
- 中间态不可跑 → Task 3 注释 + Task 3-5 合并 commit ✅
- phd/mgd/phdoge/mgdoge 换算 → 换算表 + Task 7d/9b/9i ✅
- AttackSpeed base=100 → Task 3 Step 5 ✅
- MulFinal 空桶→1 → Task 2 Compute ✅
- MCEnvironmentalDevice 读 modifiers[0] → Task 7c Step 3 ✅

无遗漏。
