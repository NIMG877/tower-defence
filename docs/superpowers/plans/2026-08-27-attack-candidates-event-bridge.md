# 攻击候选事件桥接与 EntityFilter 职责拆分 · 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 `AttackBase.OnBeforeTargetSelect` 桥接成一等 runner 事件（候选列表挂黑板保留键 `attackCandidates`），EntityFilter/InjectAttackTargets 删除全部订阅管理代码变成纯触发操作。

**Architecture:** 时机由事件提供（`TriggerEvent.OnBeforeTargetSelect`=18，末尾追加）、数据由黑板提供（派发窗口内 Set/Remove `attackCandidates`）、组件纯操作（只认 `blackboardKey`）。规格见 `docs/superpowers/specs/2026-08-27-attack-candidates-event-bridge-design.md`。

**Tech Stack:** Unity 2022 / C# / NUnit EditMode 契约测试 / Unity MCP（编译刷新与测试运行）

**约定：**
- 组件本体（依赖 Entity/AttackBase 场景装配）不做 EditMode 单测，由 PlayMode 验证——沿用 `SummonDeathComponentTests.cs` 文件头声明的项目惯例。
- 测试运行与编译验证通过 **Unity MCP** 完成（执行时先调用 unity-mcp-skill 获取准确工具名）。
- 提交信息末尾加 `Co-Authored-By: Claude <noreply@anthropic.com>`。

---

### Task 1: GameData 契约层（枚举 + 保留键常量 + 事件类 + 契约测试）

**Files:**
- Modify: `Assets/PublicScripts/GameData/AbilitySystem/ComponentConfig.cs`（TriggerEvent 枚举，约 484 行）
- Create: `Assets/PublicScripts/GameData/AbilitySystem/BlackboardKeys.cs`
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/AbilityEvents.cs`
- Test: `Assets/Tests/Editor/AbilitySystem/BeforeTargetSelectBridgeTests.cs`

- [ ] **Step 1: 写失败的契约测试**

创建 `Assets/Tests/Editor/AbilitySystem/BeforeTargetSelectBridgeTests.cs`：

```csharp
using NUnit.Framework;

namespace AbilitySystem.Tests
{
    /// <summary>索敌候选桥接的契约测试：事件映射、枚举末尾追加序号稳定、保留键常量。
    /// 桥接本体依赖 Entity/AttackBase（MonoBehaviour + 场景装配），与既有组件
    /// 同样不做 EditMode 单测，由 PlayMode 验证。</summary>
    public class BeforeTargetSelectBridgeTests
    {
        [Test]
        public void BeforeTargetSelectEvent_MapsToOnBeforeTargetSelect()
        {
            var evt = new BeforeTargetSelectEvent();
            Assert.That(evt.TriggerEvent, Is.EqualTo(TriggerEvent.OnBeforeTargetSelect));
        }

        [Test]
        public void TriggerEvent_OnBeforeTargetSelect_AppendedAfterOnSummonDeath()
        {
            // 既有 asset 按枚举序号序列化；OnBeforeTargetSelect 必须排在末尾且不改变既有序号。
            Assert.That((int)TriggerEvent.OnSummonDeath, Is.EqualTo(17));
            Assert.That((int)TriggerEvent.OnBeforeTargetSelect, Is.EqualTo(18));
        }

        [Test]
        public void BlackboardKeys_AttackCandidates_IsStableLiteral()
        {
            Assert.That(BlackboardKeys.AttackCandidates, Is.EqualTo("attackCandidates"));
        }
    }
}
```

- [ ] **Step 2: 确认失败态**

Run: Unity 刷新编译（MCP）
Expected: 编译错误——`BeforeTargetSelectEvent` / `TriggerEvent.OnBeforeTargetSelect` / `BlackboardKeys` 不存在（CS0246/CS0117）。

- [ ] **Step 3: 实现枚举追加**

`ComponentConfig.cs` 的 `TriggerEvent` 枚举，在 `OnSummonDeath,` 之后追加（保持末尾追加约定）：

```csharp
        // 召唤物相关事件（WatchSummonDeath 桥接到宿主 runner 上派发）。
        // 同样追加在末尾以保持既有 asset 的枚举序号稳定。
        OnSummonDeath,
        // 索敌候选确定后、数量裁剪前派发（EntityAbilityRunner 桥接
        // AttackBase.OnBeforeTargetSelect）。候选列表同步挂到黑板保留键
        // BlackboardKeys.AttackCandidates，派发窗口结束即摘除。
        OnBeforeTargetSelect,
```

- [ ] **Step 4: 实现保留键常量**

创建 `Assets/PublicScripts/GameData/AbilitySystem/BlackboardKeys.cs`：

```csharp
namespace AbilitySystem
{
    /// <summary>黑板保留键。桥接与组件两侧共用一个常量，避免字面量散落。</summary>
    public static class BlackboardKeys
    {
        /// <summary>攻击索敌候选列表（List&lt;Entity&gt;）。仅在
        /// BeforeTargetSelectEvent 派发的同步窗口内存在（桥接前 Set、后 Remove），
        /// 窗口外读取走"键缺失"告警路径暴露配线错误。</summary>
        public const string AttackCandidates = "attackCandidates";
    }
}
```

- [ ] **Step 5: 实现事件类**

`AbilityEvents.cs`：文件头 `using UnityEngine;` 下加一行 `using System.Collections.Generic;`；在 `AttackInterruptEvent` 类之后插入：

```csharp
    // 索敌候选确定后、数量裁剪前派发（AttackBase.AttackTargetSelect 内经
    // EntityAbilityRunner 桥接）。targets 为候选列表本体（引用，订阅方可直接
    // 增删；桥接同步挂到 BlackboardKeys.AttackCandidates）；三个标量派发后回写。
    public class BeforeTargetSelectEvent : AbilityEvent
    {
        public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnBeforeTargetSelect;
        public List<Entity> targets;
        public int selectMaxNum;
        public int selectMinNum;
        public bool sameComp;
    }
```

- [ ] **Step 6: 编译并跑测试通过**

Run: Unity 刷新编译 + EditMode 测试 `AbilitySystem.Tests.Editor` 全套（MCP）
Expected: 新增 3 项通过；既有全部通过。

- [ ] **Step 7: 提交**

```bash
git add Assets/PublicScripts/GameData/AbilitySystem/ComponentConfig.cs Assets/PublicScripts/GameData/AbilitySystem/BlackboardKeys.cs Assets/PublicScripts/GameData/AbilitySystem/BlackboardKeys.cs.meta Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/AbilityEvents.cs Assets/Tests/Editor/AbilitySystem/BeforeTargetSelectBridgeTests.cs Assets/Tests/Editor/AbilitySystem/BeforeTargetSelectBridgeTests.cs.meta
git commit -m "桥接层契约：TriggerEvent.OnBeforeTargetSelect + BeforeTargetSelectEvent + attackCandidates 保留键"
```

---

### Task 2: EntityAbilityRunner 桥接（订阅 + 派发窗口 + 回写）

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntityAbilityRunner.cs`（Subscribe 约 361-377 行、Unsubscribe 约 379-395 行、桥接方法插在 OnAttackInterrupt 之后约 431 行）

说明：桥接本体依赖 MonoBehaviour 场景装配，按项目惯例不做 EditMode 单测；本任务以编译 + 既有 EditMode 套件回归为验证。

- [ ] **Step 1: Subscribe() 加接线**

在 `Subscribe()` 的 AttackBase 事件块（`OnAttackInterrupt += OnAttackInterrupt;` 之后）追加：

```csharp
            _entity.AttackBase.OnBeforeTargetSelect += OnBeforeTargetSelect;
```

- [ ] **Step 2: Unsubscribe() 对称退订**

在 `Unsubscribe()` 的 AttackBase 事件块（`OnAttackInterrupt -= OnAttackInterrupt;` 之后）追加：

```csharp
            _entity.AttackBase.OnBeforeTargetSelect -= OnBeforeTargetSelect;
```

- [ ] **Step 3: 桥接方法**

在 `OnAttackInterrupt` 桥接方法（`private void OnAttackInterrupt() { DispatchEvent(new AttackInterruptEvent()); }`）之后插入：

```csharp
    private void OnBeforeTargetSelect(List<Entity> targets, ref int selectMaxNum, ref int selectMinNum, ref bool sameComp)
    {
        var evt = new BeforeTargetSelectEvent
        {
            targets = targets,
            selectMaxNum = selectMaxNum,
            selectMinNum = selectMinNum,
            sameComp = sameComp,
        };
        // 候选列表挂到保留键（同一 List 引用），派发窗口内组件按普通黑板键读写；
        // 窗口结束即摘除，窗口外读取走"键缺失"告警路径暴露配线错误。
        sharedBlackboard.Set(BlackboardKeys.AttackCandidates, targets);
        DispatchEvent(evt);
        selectMaxNum = evt.selectMaxNum;
        selectMinNum = evt.selectMinNum;
        sameComp = evt.sameComp;
        sharedBlackboard.Remove(BlackboardKeys.AttackCandidates);
    }
```

（文件已含 `using System.Collections.Generic;`——`List<AbilityRuntime>` 字段在用。）

- [ ] **Step 4: 编译 + EditMode 回归**

Run: Unity 刷新编译 + EditMode 测试全套（MCP）
Expected: 编译通过，测试全部通过（桥接方法未被 EditMode 触达，仅确认无回归）。

- [ ] **Step 5: 提交**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntityAbilityRunner.cs
git commit -m "EntityAbilityRunner 桥接 OnBeforeTargetSelect：派发窗口挂载 attackCandidates 键并回写标量"
```

---

### Task 3: EntityFilter 瘦身（纯列表筛选）

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/EntityFilter.cs`（整文件重写）

删除：`_mode`、`_toSelf`、`_subscribedAttacks`、`AbilityEndEvent` 分支、`FilterTargets` 订阅壳、`UnsubscribeAll`、`ResolveTargets`。谓词引擎原样保留。按项目惯例 PlayMode 验证；EditMode 套件中 `SummonDeathComponentTests` 已断言 `filter_targets` 注册，作回归。

- [ ] **Step 1: 整文件重写**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;

namespace AbilitySystem.Components
{
    /// <summary>
    /// Filters the entity list at a blackboard key in place with OR groups of AND
    /// conditions. Attack-system-agnostic: any List&lt;Entity&gt; key works. Typical
    /// sources: a list written by select_targets' outputEntitiesKey (chained
    /// filtering), or the reserved attackCandidates key during
    /// BeforeTargetSelectEvent dispatch (attack preference — timing comes from a
    /// rule triggered on OnBeforeTargetSelect, the list from the blackboard).
    /// </summary>
    [RegisterComponent("EntityFilter")]
    public class EntityFilter : AbilityComponentBase
    {
        private Func<string> _blackboardKey;
        private Func<string[]> _fields;
        private Func<string[]> _ops;
        private Func<string[]> _values;
        private Func<int[]> _groups;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            Blackboard bb = ctx.sharedBlackboard;
            _blackboardKey = p.GetStringLazy("blackboardKey", "", bb);
            _fields = p.GetStringArrayLazy<string>("fields", null, bb);
            _ops = p.GetStringArrayLazy<string>("ops", null, bb);
            _values = p.GetStringArrayLazy<string>("values", null, bb);
            _groups = p.GetIntArrayLazy("groups", null, bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            string key = _blackboardKey();
            if (string.IsNullOrEmpty(key))
            {
                OneShotWarn.WarnOnce("entity-filter-key",
                    "EntityFilter: blackboardKey is required; skipping.");
                return;
            }
            if (ctx.sharedBlackboard == null) return;

            List<Entity> targets = ctx.sharedBlackboard.Get<List<Entity>>(key, null);
            if (targets == null)
            {
                OneShotWarn.WarnOnce("entity-filter:" + key,
                    $"EntityFilter: blackboard key '{key}' holds no entity list; skipping.");
                return;
            }
            RemoveNonMatching(targets);
        }

        private void RemoveNonMatching(List<Entity> targets)
        {
            string[] fields = _fields();
            string[] ops = _ops();
            string[] values = _values();
            int[] groups = _groups();
            int count = Math.Min(Math.Min(fields.Length, ops.Length), Math.Min(values.Length, groups.Length));
            if (count == 0) return;

            for (int i = targets.Count - 1; i >= 0; i--)
            {
                if (!MatchesAnyGroup(targets[i], fields, ops, values, groups, count))
                {
                    targets.RemoveAt(i);
                }
            }
        }

        private static bool MatchesAnyGroup(
            Entity entity,
            string[] fields,
            string[] ops,
            string[] values,
            int[] groups,
            int count)
        {
            if (entity == null) return false;

            var groupResults = new Dictionary<int, bool>();
            for (int i = 0; i < count; i++)
            {
                int group = groups[i];
                bool passed = Evaluate(entity, fields[i], ops[i], values[i]);
                if (groupResults.TryGetValue(group, out bool current))
                {
                    groupResults[group] = current && passed;
                }
                else
                {
                    groupResults[group] = passed;
                }
            }

            foreach (KeyValuePair<int, bool> result in groupResults)
            {
                if (result.Value) return true;
            }
            return false;
        }

        private static bool Evaluate(Entity entity, string field, string op, string expected)
        {
            string normalizedField = Normalize(field);
            if (normalizedField == "idc")
            {
                return entity.EntityData != null
                    && CompareStrings(entity.EntityData.ID.ID_C, op, expected);
            }

            if (!TryGetFieldValue(entity, normalizedField, out float actual))
            {
                OneShotWarn.WarnOnce(
                    "entity-filter-field:" + field,
                    $"EntityFilter: unknown field '{field}'; its condition fails.");
                return false;
            }
            if (!TryParseNumber(field, expected, out float parsed)) return false;

            switch (Normalize(op))
            {
                case "none":
                    return true;
                case "equal":
                case "eq":
                    return actual == parsed;
                case "notequal":
                case "ne":
                    return actual != parsed;
                case "greater":
                case "gt":
                    return actual > parsed;
                case "greaterorequal":
                case "ge":
                    return actual >= parsed;
                case "less":
                case "lt":
                    return actual < parsed;
                case "lessorequal":
                case "le":
                    return actual <= parsed;
                default:
                    OneShotWarn.WarnOnce(
                        "entity-filter-op:" + op,
                        $"EntityFilter: unknown op '{op}'; its condition fails.");
                    return false;
            }
        }

        /// <summary>字符串字段（IdC）比较：仅 Equal/NotEqual，排序类 op 属配线错误。</summary>
        private static bool CompareStrings(string actual, string op, string expected)
        {
            switch (Normalize(op))
            {
                case "none":
                    return true;
                case "equal":
                case "eq":
                    return Normalize(actual) == Normalize(expected);
                case "notequal":
                case "ne":
                    return Normalize(actual) != Normalize(expected);
                default:
                    OneShotWarn.WarnOnce(
                        "entity-filter-op-string:" + op,
                        $"EntityFilter: op '{op}' cannot compare string field 'IdC'; its condition fails.");
                    return false;
            }
        }

        /// <summary>数值字段的比较值解析：解析失败即资产笔误，一次性告警并让该条件失败。</summary>
        private static bool TryParseNumber(string field, string raw, out float value)
        {
            if (!string.IsNullOrEmpty(raw)
                && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
            value = 0;
            OneShotWarn.WarnOnce(
                "entity-filter-value:" + field + ":" + raw,
                $"EntityFilter: field '{field}' expects a number but got '{raw}'; its condition fails.");
            return false;
        }

        private static bool TryGetFieldValue(Entity entity, string field, out float value)
        {
            switch (Normalize(field))
            {
                case "monsterstatus":
                    value = entity.EntityData != null ? entity.EntityData.MonsterStatus : 0;
                    return entity.EntityData != null;
                case "idn":
                    value = entity.EntityData != null ? entity.EntityData.ID.ID_N : 0;
                    return entity.EntityData != null;
                case "camp":
                    value = entity.Camp;
                    return true;
                case "currenthp":
                    value = entity.Stats.CurrentHp;
                    return true;
                case "currenthprate":
                    value = entity.Stats.CurrentHpRate;
                    return true;
                case "maxhp":
                    value = entity.Stats.MaxHpS;
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Trim().ToLowerInvariant();
        }
    }
}
```

- [ ] **Step 2: 编译 + EditMode 回归**

Run: Unity 刷新编译 + EditMode 测试全套（MCP）
Expected: 编译通过（无外部引用被删符号）；测试全部通过。

- [ ] **Step 3: 提交**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/EntityFilter.cs
git commit -m "EntityFilter 瘦身为纯列表筛选：删订阅管理与模式分支，只认 blackboardKey"
```

---

### Task 4: InjectAttackTargets 瘦身（纯优先级插入）

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/InjectAttackTargets.cs`（整文件重写）

- [ ] **Step 1: 整文件重写**

```csharp
using System;
using System.Collections.Generic;

namespace AbilitySystem.Components
{
    /// <summary>
    /// 把黑板实体列表注入攻击索敌候选的最前：先移除候选里已有的列表实体，再整体
    /// 插到索引 0——注入的实体无条件获得最高目标优先级（含视野外实体）。只作用于
    /// 自身的索敌（OnBeforeTargetSelect 经 runner 桥接派发，天然按实体隔离）。
    /// 与 EntityFilter（只能剔除、不能注入）互补。
    ///
    /// <para>用法：规则触发器设为 OnBeforeTargetSelect。触发时读保留键
    /// BlackboardKeys.AttackCandidates（仅派发窗口内存在，缺失=触发时机配错，
    /// 告警跳过）与源 blackboardKey 列表；源列表为空是无操作。</para>
    /// </summary>
    [RegisterComponent("InjectAttackTargets")]
    public class InjectAttackTargets : AbilityComponentBase
    {
        private Func<string> _blackboardKey;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            _blackboardKey = p.GetStringLazy("blackboardKey", "", ctx.sharedBlackboard);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (ctx.sharedBlackboard == null) return;

            List<Entity> candidates = ctx.sharedBlackboard.Get<List<Entity>>(BlackboardKeys.AttackCandidates, null);
            if (candidates == null)
            {
                OneShotWarn.WarnOnce("inject-attack-targets-window",
                    "InjectAttackTargets: attackCandidates key missing (rule must trigger on OnBeforeTargetSelect); skipping.");
                return;
            }

            List<Entity> inject = ReadInjectList(ctx);
            if (inject == null || inject.Count == 0) return;

            // 先移除候选里已有的列表实体（避免重复），再整体插到最前。
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                if (inject.Contains(candidates[i]))
                {
                    candidates.RemoveAt(i);
                }
            }
            candidates.InsertRange(0, inject);
        }

        private List<Entity> ReadInjectList(AbilityContext ctx)
        {
            string key = _blackboardKey();
            if (string.IsNullOrEmpty(key)) return null;
            return ctx.sharedBlackboard.Get<List<Entity>>(key, null);
        }
    }
}
```

- [ ] **Step 2: 编译 + EditMode 回归**

Run: Unity 刷新编译 + EditMode 测试全套（MCP）
Expected: 编译通过；`SummonDeathComponentTests` 中 `inject_attack_targets` 注册断言仍通过。

- [ ] **Step 3: 提交**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/InjectAttackTargets.cs
git commit -m "InjectAttackTargets 瘦身为纯优先级插入：删订阅管理，读 attackCandidates 保留键"
```

---

### Task 5: 资产迁移

**Files:**
- Modify: `Assets/Resources/Prefabs/Characters/6/Ebnhlz/skills/ebnhlz_s3.asset`（规则2，约 112-140 行）
- Modify: `Assets/Resources/Prefabs/Characters/6/Eyjafjalla/talents/eyjafjalla_t1.asset`（第 31 行）
- Delete: `Assets/Resources/Prefabs/Characters/3/Kroos/talents/test_talent.asset` 及 `.meta`（孤儿资产，GUID `47b0e6b2e1342f84491dbbc675c7d51f` 全库无引用）

- [ ] **Step 1: ebnhlz_s3 规则2 改触发与参数**

把（双触发 + 无 blackboardKey）：

```yaml
  - triggers:
    - triggerEvent: 2
      groups: []
    - triggerEvent: 3
      groups: []
    reentry: 0
    steps:
    - op: filter_targets
      args:
        entries:
        - key: fields
```

改为（单触发 18 + blackboardKey 条目插在最前）：

```yaml
  - triggers:
    - triggerEvent: 18
      groups: []
    reentry: 0
    steps:
    - op: filter_targets
      args:
        entries:
        - key: blackboardKey
          value: attackCandidates
          fromBlackboard: 0
          type: 3
        - key: fields
```

其余 entries（fields/ops/values/groups）不动。

- [ ] **Step 2: eyjafjalla_t1 删失效的 mode 条目**

第 31 行 `args: {entries: [{key: mode, value: list, fromBlackboard: 0, type: 3}, {key: blackboardKey, ...` 中删除 `{key: mode, value: list, fromBlackboard: 0, type: 3}, `，blackboardKey 成为第一个条目。

- [ ] **Step 3: 删除 test_talent**

```bash
git rm "Assets/Resources/Prefabs/Characters/3/Kroos/talents/test_talent.asset" "Assets/Resources/Prefabs/Characters/3/Kroos/talents/test_talent.asset.meta"
```

- [ ] **Step 4: Unity 刷新重导入 + 编译**

Run: Unity 刷新（MCP）
Expected: 资产重导入无报错；控制台无 YAML Parser Failure（.asset 不支持注释，勿在 YAML 内加注释）。

- [ ] **Step 5: 提交**

```bash
git add "Assets/Resources/Prefabs/Characters/6/Ebnhlz/skills/ebnhlz_s3.asset" "Assets/Resources/Prefabs/Characters/6/Eyjafjalla/talents/eyjafjalla_t1.asset"
git commit -m "资产迁移：ebnhlz_s3 过滤规则改 OnBeforeTargetSelect 单触发；eyjafjalla_t1 删失效 mode 条目；删孤儿 test_talent"
```

---

### Task 6: 文档同步

**Files:**
- Modify: `docs/skill-components/EntityFilter.md`
- Modify: `docs/skill-components/InjectAttackTargets.md`
- Modify: `docs/abilities-inventory.md`

- [ ] **Step 1: 重写 EntityFilter.md**

```markdown
# EntityFilter

Filters the entity list at a Blackboard key in place with OR groups containing
AND conditions, matching the shape of `ConditionConfig.groups`. The component
is attack-system-agnostic: any `List<Entity>` key works. Two typical sources:

- a list written by `select_targets`' `outputEntitiesKey` (chained filtering);
- the reserved `attackCandidates` key during `BeforeTargetSelectEvent`
  dispatch (attack preference — see "Attack preference wiring" below).

**Canonical op:** `filter_targets`
**Component registration:** `EntityFilter`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `blackboardKey` | String | `""` | The `List<Entity>` key to filter in place. For attack preference use the reserved key `attackCandidates`. |
| `fields` | StringCsv | `""` | Entity fields, one per condition. |
| `ops` | StringCsv | `""` | Comparison operations, one per condition. |
| `values` | StringCsv | `""` | Comparison values, one per condition. Numeric fields parse their value as float (invariant culture); string fields compare literally. |
| `groups` | IntCsv | `""` | Group id per condition. Conditions sharing an id are ANDed; distinct ids are ORed. |

All four condition arrays are parallel. Only entries up to the shortest array
length are evaluated. With no complete conditions, the list is left
unchanged.

## Attack preference wiring

Timing comes from the event system, data from the blackboard — the component
itself is stateless:

```text
triggers = OnBeforeTargetSelect
steps    = filter_targets { blackboardKey = attackCandidates, fields = ..., ... }
```

`attackCandidates` exists only during the synchronous dispatch window of
`BeforeTargetSelectEvent` (the runner bridge sets it before dispatch and
removes it after). Reading it outside that window warns once and skips — a
wiring error is exposed, not masked. Skill scoping comes free from the
dispatch-side `isActive` gate: an inactive skill's rules never receive the
event, so a temporary skill needs a single trigger (no subscribe/unsubscribe
pair). Pair with `force_reset_attack` after engaging when the filtered target
set must take effect immediately.

## Supported fields

- `MonsterStatus`
- `Camp`
- `CurrentHp`
- `CurrentHpRate`
- `MaxHp`
- `IdN` — `EntityData.ID.ID_N` (numeric)
- `IdC` — `EntityData.ID.ID_C` (string; `Equal`/`NotEqual` only — ordering ops
  warn once and fail the condition)

`IdC` + `IdN` together identify an entity kind, e.g. `t,1` = the `t/1`
entity (LavaBubble).

## Supported operations

- `None`
- `Equal` / `Eq`
- `NotEqual` / `Ne`
- `Greater` / `Gt`
- `GreaterOrEqual` / `Ge`
- `Less` / `Lt`
- `LessOrEqual` / `Le`

Unknown fields or operations make that condition fail and emit a one-shot
warning. A numeric field whose value does not parse as a number (invariant
culture) fails the same way. A missing key or a non-list value at the key
warns once and skips.

## Example

Accept elite or leader monsters:

```text
blackboardKey = attackCandidates
fields = MonsterStatus,MonsterStatus
ops    = GreaterOrEqual,LessOrEqual
values = 1,2
groups = 0,0
```

Keep only `t/1` entities (e.g. LavaBubbles) in a selected list:

```text
blackboardKey = my_bubbles
fields = IdC,IdN
ops    = Equal,Equal
values = t,1
groups = 0,0
```
```

- [ ] **Step 2: 重写 InjectAttackTargets.md**

```markdown
# InjectAttackTargets

Injects a Blackboard entity list to the **front** of this unit's attack
target candidates: list members are removed from the candidate list first,
then inserted at index 0 — injected entities unconditionally get the highest
targeting priority, including entities outside vision range. The candidate
list is per-unit (each entity's `OnBeforeTargetSelect` is bridged to its own
runner), so the priority never leaks into other units' targeting.
Complements `EntityFilter`, which can only remove candidates, never add
them.

**Canonical op:** `inject_attack_targets`
**Component registration:** `InjectAttackTargets`
**Class:** `AbilitySystem.Components.InjectAttackTargets`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/InjectAttackTargets.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `blackboardKey` | String | `""` | Blackboard key of the `List<Entity>` to inject. Empty list = no-op injection. |

## Usage

Wire the rule's trigger to `OnBeforeTargetSelect`. The destination is the
reserved key `BlackboardKeys.AttackCandidates` (hardcoded — this component's
purpose is attack-candidate injection), which exists only during the
synchronous dispatch window of `BeforeTargetSelectEvent`. If the key is
missing (wrong trigger wired), the step warns once and skips — the wiring
error is exposed, not masked.

The list contents are read at every target selection, so roster changes
apply to the very next attack. Pair with `force_reset_attack` right after
engaging, so the unit re-targets immediately instead of finishing its
current attack cycle first.
```

- [ ] **Step 3: 更新 abilities-inventory.md 三处**

1. 第 155 行 EntityFilter 行改为：

```
| **EntityFilter** | 原地过滤黑板 List<Entity>（OR组AND条件；attackCandidates 保留键即攻击偏好） | blackboardKey,fields,ops,values,groups | 读 blackboardKey | 无（纯触发操作） |
```

2. 第 199 行 test_talent 整行删除。
3. 第 206 行 ebnhlz_s3 行中 `EntityFilter(2&3,仅精英1-2)` 改为 `EntityFilter(18,仅精英1-2)`。
4. 第 289 行 `| **目标列表注入(插到最前)** | EyjafjallaTalent1(气泡优先) | EntityFilter(只能剔除不能插入) | 🟡 中 |` 改为：

```
| **目标列表注入(插到最前)** | EyjafjallaTalent1(气泡优先) | InjectAttackTargets 已实现(事件触发接线,资产待落地) | 🟢 低 |
```

（末列难度从 🟡 中 调整为 🟢 低：机制已就位，仅剩资产配置。）

- [ ] **Step 4: 核对 ability-steps.md**

`docs/ability-steps.md:126` 的 `filter_targets → EntityFilter` 映射行：op 名不变，预计无需改动；核对确认无 mode 相关描述需要清理，若有则一并更新。

- [ ] **Step 5: 提交**

```bash
git add docs/skill-components/EntityFilter.md docs/skill-components/InjectAttackTargets.md docs/abilities-inventory.md
git commit -m "文档同步：EntityFilter/InjectAttackTargets 事件桥接用法与清单更新"
```

---

### Task 7: 编译与 EditMode 全量回归

- [ ] **Step 1: Unity 刷新编译（MCP）**

Expected: 0 errors。

- [ ] **Step 2: EditMode 全套测试（MCP Test Runner，EditMode 全部）**

Expected: 全部通过（含新增 `BeforeTargetSelectBridgeTests` 3 项、既有 `SummonDeathComponentTests` / `AbilityStepRuntimeTests` 等）。

- [ ] **Step 3: 控制台检查（MCP）**

Expected: 无 `OneShotWarn` 新增告警（资产未跑 PlayMode 前不应触发任何组件路径）。

---

### Task 8: PlayMode 验证（用户协作）

自动化部分（MCP 进 PlayMode 冒烟）+ 用户确认清单：

- [ ] **Step 1: ebnhlz_s3 验证**：开技能 → 仅攻击精英/领袖（MonsterStatus 1–2）；关技能 → 恢复正常索敌；蓄力 ×1.4 天赋联动不变。
- [ ] **Step 2: eyjafjalla_t1 回归**：重部署后泡泡重统计（攻击增层恢复）不回归。
- [ ] **Step 3: InjectAttackTargets 冒烟**：eyjafjalla 测试场景临时挂 `trigger 18 → inject_attack_targets {blackboardKey: eyjafjalla_t1_bubbles}` 规则，验证优先攻击泡泡，验证后移除该规则。
- [ ] **Step 4: Kroos 回归**：删除 test_talent 后行为无变化（实际天赋 kroos_t1）。
- [ ] **Step 5: 全部通过后按需更新记忆文件**（ability 系统相关 memory：事件桥接新增、EntityFilter/InjectAttackTargets 新形态）。
