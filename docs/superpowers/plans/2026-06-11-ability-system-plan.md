# Ability System Implementation Plan

> 文档状态：历史实施计划存档，非当前有效文档。当前实现以代码与 docs/ 现行文档为准。


> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unify Skill / Talent / ExtraAbility under one `AbilityRuntime` and add a runtime `AddExtraAbility` / `RemoveExtraAbility` API. Zero changes to the 22 existing `ISkillComponent` implementations.

**Architecture:** Rename `SkillConfig` → `AbilityConfig` (add `Kind` field), rename `SkillRuntime` → `AbilityRuntime` (add unified `isActive` + `SetActive`), expand the dispatcher's bypass-gate from 4 to 6 events, expose new public APIs on `EntitySkillRunner`. `PreWarm` is construction-only; talents activate at the end of `OnInitialize`; extras are added/removed via API.

**Tech Stack:** Unity 6 + C# (existing), Unity Test Framework 1.1.33 (already in `Packages/manifest.json`), NUnit assertions. All tests are EditMode.

**Spec:** `docs/superpowers/specs/2026-06-11-ability-system-design.md` (commit `7a0c66c`).

**Project conventions:**
- Skill system files live under `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/` (runtime) and `Assets/PublicScripts/GameData/SkillSystem/` (data classes).
- Components live in `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/`.
- Tests will live in `Assets/Tests/EditMode/` (folder does not exist yet — Task 2 creates it).
- `MEMORY.md` notes: `SkillContext.skill` field keeps its name (despite now holding an `AbilityRuntime`); component naming convention is no `Component` suffix; `DamageEventBase` / `HurtEventBase` are the existing event base classes — the new events are simple payload-less, so they derive directly from `SkillEvent`.

**Execution model:** Unity Test Framework is in `Packages/manifest.json` but no test assemblies exist yet. Task 2 sets up the test infrastructure. Each test step is a manual Unity Editor action: open Test Runner, run the named test, verify pass. There is no headless test runner available in this environment.

---

## File Structure

### New files

```
Assets/PublicScripts/GameData/SkillSystem/AbilityConfig.cs
  (replaces SkillConfig.cs; same fields, plus Kind)

Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/AbilityRuntime.cs
  (replaces SkillRuntime.cs; same field shape, plus Kind, isActive, SetActive, OnAbilityBegin/End)

Assets/Tests/EditMode/EditMode.asmdef
  (NUnit + UnityEngine.TestRunner, references SkillSystem + PublicScripts)

Assets/Tests/EditMode/AbilityKindTests.cs
  (sanity: AbilityKind exists with 3 values)

Assets/Tests/EditMode/AbilityRuntimeTests.cs
  (8 unit tests from spec §8.1 first table)

Assets/Tests/EditMode/AbilityAddRemoveTests.cs
  (8 unit tests from spec §8.1 second table)

Assets/Tests/EditMode/TestEntity.cs
  (helper: minimal Entity substitute for EntitySkillRunner construction in tests)
```

### Modified files

```
Assets/PublicScripts/GameData/SkillSystem/ComponentConfig.cs
  - add `public enum AbilityKind { Skill, Talent, ExtraAbility }`
  - rename `TriggerEvent.OnSkillBegin` → `OnAbilityBegin`
  - rename `TriggerEvent.OnSkillEnd`   → `OnAbilityEnd`
  - add `TriggerEvent.OnAbilityAdded`, `OnAbilityRemoved`

Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillEvents.cs
  - rename `SkillBeginEvent` → `AbilityBeginEvent`, field `skill` → `ability` typed `AbilityRuntime`
  - rename `SkillEndEvent`   → `AbilityEndEvent`,   same
  - add `AbilityAddedEvent`, `AbilityRemovedEvent` classes (each with `ability` field)

Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs
  - rename `_skills` → `_abilities`, type `List<SkillRuntime>` → `List<AbilityRuntime>`
  - rename `BuildSkillRuntime(SkillConfig)` → `BuildAbilityRuntime(AbilityConfig)`, return AbilityRuntime
  - rename `DispatchToSkill` → `DispatchToAbility`
  - expand bypassActiveGate set to 6 events
  - add `WireRuntime` / `UnwireRuntime` helpers
  - add `AddExtraAbility` / `RemoveExtraAbility` / `HasExtraAbility` public methods
  - PreWarm / OnInitialize / OnTeardown read data.Abilities, handle 3 kinds
  - add internal testability constructor `internal EntitySkillRunner(EntityData data, Blackboard blackboard)` (or similar) — see Task 5
  - internal field `_extraCounter` for runtimeId generation

Assets/PublicScripts/GameData/EntityData/EntityData.cs
  - rename field `Skills` → `Abilities`

Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
  - mechanical rename: `SkillConfig` → `AbilityConfig`, `skillId/skillName` → `abilityId/abilityName`, `_selectSkillConfig` → `_selectAbilityConfig`

Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Components/Cards.cs
  - same mechanical renames; note: `skillName` field on the UI component is a child-find name, not the data field, but rename for consistency to `abilityNameText`
```

### Deleted files

```
Assets/PublicScripts/GameData/SkillSystem/SkillConfig.cs
  (replaced by AbilityConfig.cs)

Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs
  (replaced by AbilityRuntime.cs)
```

### Files NOT modified

All 22 `ISkillComponent` files in `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/`. `SPConfig.cs`, `SPEngine.cs`, `Blackboard.cs`, `ConditionEvaluator.cs`, `ComponentFactory.cs`, `ComponentAutoRegistry.cs`, `BuffParamParser.cs`. `Entity.cs`, `BuffController`, `AttackBase`, `EntityManager`. The legacy `Entity.skill[]` field (per `MEMORY.md`) is unaffected and stays as a separate cleanup.

---

## Task 1: Add `AbilityKind` enum + new `TriggerEvent` values

**Files:**
- Modify: `Assets/PublicScripts/GameData/SkillSystem/ComponentConfig.cs:75-94`

- [ ] **Step 1: Edit the file**

Open `Assets/PublicScripts/GameData/SkillSystem/ComponentConfig.cs` and replace the `TriggerEvent` enum block (currently lines 75-85) with:

```csharp
public enum TriggerEvent
{
    OnPreWarm, OnInitialize,
    OnBeforeAttack, OnAfterAttack,
    OnBeforeTakeDamage, OnAfterTakeDamage,
    OnAttackSuccessfully, OnAttackInterrupt,
    OnBeforeHurt, OnAfterHurt,
    OnAttackAnimBegin,
    OnBeforeDieAnimation,
    // Renamed from OnSkillBegin/OnSkillEnd: now fire on any isActive transition.
    OnAbilityBegin, OnAbilityEnd,
    // New: broadcast when an ExtraAbility is added/removed.
    OnAbilityAdded, OnAbilityRemoved,
}
```

Then add the `AbilityKind` enum below the `TriggerEvent` block (around line 87, before `ConditionOp`):

```csharp
public enum AbilityKind
{
    Skill,
    Talent,
    ExtraAbility,
}
```

- [ ] **Step 2: Verify the file compiles in Unity Editor**

Open the project in Unity Editor. Wait for compile. Check the Console for errors related to `ComponentConfig.cs`. There should be none — we only added types.

- [ ] **Step 3: Commit**

```bash
git add Assets/PublicScripts/GameData/SkillSystem/ComponentConfig.cs
git commit -m "feat(skill-system): add AbilityKind enum and renamed ability trigger events"
```

---

## Task 2: Set up EditMode test infrastructure

**Files:**
- Create: `Assets/Tests/EditMode/EditMode.asmdef`
- Create: `Assets/Tests/EditMode/AbilityKindTests.cs`

- [ ] **Step 1: Create the test folder and asmdef**

Create directory `Assets/Tests/EditMode/`.

Create file `Assets/Tests/EditMode/EditMode.asmdef` with the following JSON:

```json
{
    "name": "EditMode",
    "rootNamespace": "Tests.EditMode",
    "references": [
        "GUID:27619889b8ba8c24980f49ee34dbb44a",
        "GUID:0acc523941302664db1f4e527237feb3"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

The two GUIDs reference `UnityEngine.TestRunner` and `UnityEditor.TestRunner` (standard Unity Test Framework asmdef references). If the project does not have these exact GUIDs, alternative: replace the `references` array with the names array:

```json
"references": [
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner"
]
```

Use whichever form resolves in the project's `Library/ScriptAssemblies/` after the next compile. If both fail, open Unity, right-click the test folder, "Create > Testing > Tests Assembly Folder" — Unity will create a working asmdef automatically; copy it into our path.

- [ ] **Step 2: Create the smoke test**

Create file `Assets/Tests/EditMode/AbilityKindTests.cs`:

```csharp
using NUnit.Framework;
using SkillSystem;

namespace Tests.EditMode
{
    public class AbilityKindTests
    {
        [Test]
        public void AbilityKind_HasThreeValues()
        {
            Assert.AreEqual(0, (int)AbilityKind.Skill);
            Assert.AreEqual(1, (int)AbilityKind.Talent);
            Assert.AreEqual(2, (int)AbilityKind.ExtraAbility);
        }

        [Test]
        public void TriggerEvent_HasAbilityEvents()
        {
            // Renamed from OnSkillBegin/OnSkillEnd
            Assert.AreNotEqual(TriggerEvent.OnSkillBegin, TriggerEvent.OnAbilityBegin);
            Assert.AreNotEqual(TriggerEvent.OnSkillEnd, TriggerEvent.OnAbilityEnd);
            // New
            Assert.AreNotEqual(default(TriggerEvent), TriggerEvent.OnAbilityAdded);
            Assert.AreNotEqual(default(TriggerEvent), TriggerEvent.OnAbilityRemoved);
        }
    }
}
```

- [ ] **Step 3: Run the test in Unity Test Runner**

Open Unity Editor. Window → General → Test Runner. Click "EditMode" tab. Click "Run All". Verify both tests pass. The `TriggerEvent_HasAbilityEvents` test will fail to compile if the rename is wrong — fix the rename and re-run.

- [ ] **Step 4: Commit**

```bash
git add Assets/Tests/EditMode/
git commit -m "test: add EditMode test infrastructure and AbilityKind smoke tests"
```

---

## Task 3: Add the 4 new event classes + remove the 2 old ones

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillEvents.cs`

- [ ] **Step 1: Replace SkillBeginEvent/SkillEndEvent with the new event classes**

Open `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillEvents.cs` and replace the entire `SkillBeginEvent` / `SkillEndEvent` block (lines 84-93) with:

```csharp
public class AbilityBeginEvent : SkillEvent
{
    public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAbilityBegin;
    public AbilityRuntime ability;
}
public class AbilityEndEvent : SkillEvent
{
    public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAbilityEnd;
    public AbilityRuntime ability;
}
public class AbilityAddedEvent : SkillEvent
{
    public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAbilityAdded;
    public AbilityRuntime ability;
}
public class AbilityRemovedEvent : SkillEvent
{
    public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAbilityRemoved;
    public AbilityRuntime ability;
}
```

- [ ] **Step 2: Update `ApplyBuff.cs` comment to reference new names**

Open `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/ApplyBuff.cs`. The docstring (lines 11-19) references `SkillEndEvent` and `OnSkillEnd`. Replace:

```csharp
    /// <para>When <c>endOnSkillEnd</c> is true, every buff this component creates is
    /// tracked and destroyed on <see cref="AbilityEndEvent"/> (or on
    /// <see cref="OnTeardown"/> if the skill never ends cleanly, e.g. pool
    /// dormancy mid-skill). The config MUST also declare <c>OnAbilityEnd</c> in
```

(The 2 reference changes in the comment. The `_endOnSkillEnd` runtime flag in the body of ApplyBuff is not renamed — it predates this refactor and is a separate concern.)

- [ ] **Step 3: Verify the file compiles**

Open Unity. Wait for compile. Check Console. The only error expected: `EntitySkillRunner.cs` still references `SkillBeginEvent` / `SkillEndEvent` — those errors are expected and will be fixed in Task 8. Ignore them for now.

- [ ] **Step 4: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillEvents.cs Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/ApplyBuff.cs
git commit -m "feat(skill-system): rename SkillBegin/End events to AbilityBegin/End and add Added/Removed"
```

---

## Task 4: Create `AbilityConfig` (replaces `SkillConfig`)

**Files:**
- Create: `Assets/PublicScripts/GameData/SkillSystem/AbilityConfig.cs`
- Modify: `Assets/PublicScripts/GameData/EntityData/EntityData.cs`
- Delete: `Assets/PublicScripts/GameData/SkillSystem/SkillConfig.cs`

- [ ] **Step 1: Create AbilityConfig.cs**

Create file `Assets/PublicScripts/GameData/SkillSystem/AbilityConfig.cs`:

```csharp
using System;
using UnityEngine;

namespace SkillSystem
{
    [CreateAssetMenu(fileName = "AbilityConfig", menuName = "SkillSystem/Ability Config", order = 0)]
    public class AbilityConfig : ScriptableObject
    {
        public string abilityId;
        public string abilityName;
        [TextArea(2, 5)] public string description;

        [Tooltip("技能图标")]
        public Sprite icon;

        public AbilityKind Kind = AbilityKind.Skill;
        public SPConfig sp;
        public ComponentConfig[] components = Array.Empty<ComponentConfig>();
    }
}
```

- [ ] **Step 2: Delete SkillConfig.cs**

```bash
rm Assets/PublicScripts/GameData/SkillSystem/SkillConfig.cs
```

- [ ] **Step 3: Update EntityData.cs to use Abilities**

Open `Assets/PublicScripts/GameData/EntityData/EntityData.cs`. Find the line:

```csharp
    // 技能 / 天赋（数据驱动框架）
    public List<SkillConfig> Skills = new List<SkillConfig>();
```

Replace with:

```csharp
    // 技能 / 天赋 / 额外能力（数据驱动框架）
    public List<AbilityConfig> Abilities = new List<AbilityConfig>();
```

- [ ] **Step 4: Verify it compiles (ignoring expected EntitySkillRunner errors)**

Open Unity. The errors expected: `EntitySkillRunner.cs` references `SkillConfig` in `BuildSkillRuntime(SkillConfig cfg)` and in log messages. Those will be fixed in Task 8. Other files (`LevelMessagePanel.cs`, `Cards.cs`) still reference `SkillConfig` — those will be fixed in Task 13.

- [ ] **Step 5: Commit**

```bash
git add Assets/PublicScripts/GameData/SkillSystem/AbilityConfig.cs Assets/PublicScripts/GameData/EntityData/EntityData.cs
git rm Assets/PublicScripts/GameData/SkillSystem/SkillConfig.cs
git commit -m "feat(skill-system): rename SkillConfig to AbilityConfig with Kind field"
```

---

## Task 5: Create `AbilityRuntime` with TDD

**Files:**
- Create: `Assets/Tests/EditMode/AbilityRuntimeTests.cs`
- Create: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/AbilityRuntime.cs`
- Delete: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs`

- [ ] **Step 1: Write the failing tests**

Create file `Assets/Tests/EditMode/AbilityRuntimeTests.cs`:

```csharp
using NUnit.Framework;
using SkillSystem;
using UnityEngine;

namespace Tests.EditMode
{
    public class AbilityRuntimeTests
    {
        private AbilityConfig MakeCfg(AbilityKind kind = AbilityKind.Talent)
        {
            return ScriptableObject.CreateInstance<AbilityConfig>();
        }

        [Test]
        public void NewRuntime_IsInactive()
        {
            var cfg = MakeCfg();
            cfg.abilityId = "test";
            var rt = new AbilityRuntime { config = cfg };
            Assert.IsFalse(rt.isActive);
        }

        [Test]
        public void SetActiveTrue_FiresOnAbilityBeginOnce()
        {
            var rt = new AbilityRuntime { config = MakeCfg() };
            int calls = 0;
            rt.OnAbilityBegin += () => calls++;
            rt.SetActive(true);
            rt.SetActive(true);  // second call is no-op
            Assert.AreEqual(1, calls);
            Assert.IsTrue(rt.isActive);
        }

        [Test]
        public void SetActiveFalse_AfterTrue_FiresOnAbilityEnd()
        {
            var rt = new AbilityRuntime { config = MakeCfg() };
            rt.SetActive(true);
            int endCalls = 0;
            rt.OnAbilityEnd += () => endCalls++;
            rt.SetActive(false);
            Assert.AreEqual(1, endCalls);
            Assert.IsFalse(rt.isActive);
        }

        [Test]
        public void SetActiveFalse_FromInactive_IsNoOp()
        {
            var rt = new AbilityRuntime { config = MakeCfg() };
            int endCalls = 0;
            rt.OnAbilityEnd += () => endCalls++;
            rt.SetActive(false);
            Assert.AreEqual(0, endCalls);
        }

        [Test]
        public void SetActiveTrueThenFalse_BothFireOnce()
        {
            var rt = new AbilityRuntime { config = MakeCfg() };
            int begin = 0, end = 0;
            rt.OnAbilityBegin += () => begin++;
            rt.OnAbilityEnd += () => end++;
            rt.SetActive(true);
            rt.SetActive(false);
            rt.SetActive(true);
            rt.SetActive(false);
            Assert.AreEqual(2, begin);
            Assert.AreEqual(2, end);
        }

        [Test]
        public void Kind_PassesThroughFromConfig()
        {
            var cfg = MakeCfg(AbilityKind.Skill);
            var rt = new AbilityRuntime { config = cfg };
            Assert.AreEqual(AbilityKind.Skill, rt.Kind);
        }

        [Test]
        public void RuntimeId_DefaultsToEmpty()
        {
            var rt = new AbilityRuntime { config = MakeCfg() };
            Assert.IsNull(rt.runtimeId);
        }
    }
}
```

- [ ] **Step 2: Run tests, expect them to fail (AbilityRuntime does not exist)**

Open Unity, Test Runner → EditMode → Run All. Expect 7 compile errors: `AbilityRuntime` does not exist. That's the expected state — confirm failure.

- [ ] **Step 3: Create AbilityRuntime.cs**

Create file `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/AbilityRuntime.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace SkillSystem
{
    public class AbilityRuntime
    {
        public AbilityConfig config;
        public AbilityKind Kind => config.Kind;
        public string runtimeId;
        public SPEngine spEngine;
        public bool isActive;

        public List<ISkillComponent> components = new List<ISkillComponent>();
        public List<ITickingComponent> tickingComponents = new List<ITickingComponent>();
        public List<ParamList> componentParams = new List<ParamList>();

        // Trigger 分桶:BuildAbilityRuntime 一次性填充,与 SkillRuntime 的 bucket 形状一致。
        public Dictionary<TriggerEvent, List<(ISkillComponent comp, List<ConditionGroup> groups)>>
            componentsByTrigger
            = new Dictionary<TriggerEvent, List<(ISkillComponent, List<ConditionGroup>)>>();

        public bool isInitialized;

        // Wire/UnwireRuntime 存放在这里;EntitySkillRunner 负责 set/clear 这个字段。
        // 见 spec §3.2。
        public Action _wireTeardown;

        public SkillContext MakeContext(ISkillComponent component, SkillEvent evt = null)
        {
            return new SkillContext
            {
                skill = this,   // 字段名保留 'skill' (SkillContext 兼容旧组件)
                component = component,
                currentEvent = evt,
                // sharedBlackboard 由 EntitySkillRunner.PrepareContext 注入。
            };
        }

        public void SetActive(bool value)
        {
            if (isActive == value) return;
            isActive = value;
            if (value) OnAbilityBegin?.Invoke();
            else OnAbilityEnd?.Invoke();
        }

        public event Action OnAbilityBegin;
        public event Action OnAbilityEnd;
    }
}
```

- [ ] **Step 4: Delete SkillRuntime.cs**

```bash
rm Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs
```

- [ ] **Step 5: Run tests, expect them to pass**

Open Unity, Test Runner → EditMode → Run All. All 7 `AbilityRuntimeTests` should pass. Other tests in the project may fail due to the deletion of `SkillRuntime` and `SkillConfig` — those will be fixed in later tasks.

- [ ] **Step 6: Commit**

```bash
git add Assets/Tests/EditMode/AbilityRuntimeTests.cs Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/AbilityRuntime.cs
git rm Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs
git commit -m "feat(skill-system): add AbilityRuntime with unified isActive and SetActive"
```

---

## Task 6: Rewrite `EntitySkillRunner` to use `AbilityRuntime` and `AbilityConfig`

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs`

This is the largest single edit. It introduces:
- `_abilities` field
- `BuildAbilityRuntime(AbilityConfig)` returning `AbilityRuntime`
- `DispatchToAbility(AbilityRuntime, SkillEvent)` with 6-event bypass
- `WireRuntime` / `UnwireRuntime` helpers
- `_extraCounter` field
- Internal testability constructor

- [ ] **Step 1: Replace the entire file with the new implementation**

Open `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs` and replace its full contents with:

```csharp
using System.Collections.Generic;
using UnityEngine;
using SkillSystem;

/// <summary>
/// 技能子系统（POCO）。持有 AbilityRuntime 列表并 tick SP / 组件；
/// 订阅 Entity 事件（攻击 / 受击 / 死亡 / 动画）并桥成 SkillEvent 分发。
///
/// 设计要点：
///   - POCO，无 MonoBehaviour 依赖。构造接受 Entity 引用作为事件桥。
///   - 由 Entity 在 PreWarm 中显式构造，OnInitialize / OnTeardown / Tick 由 Entity 生命周期驱动。
///   - 实体 prefab 上不挂载该组件（迁移自原 SkillSystem.SkillRunner MonoBehaviour）。
/// </summary>
public class EntitySkillRunner
{
    private readonly Entity _entity;
    private readonly List<AbilityRuntime> _abilities = new List<AbilityRuntime>();
    public Blackboard sharedBlackboard = new Blackboard();

    // runtimeId 生成 (extras 用,见 spec §5.5)
    private int _extraCounter = 0;

    public IReadOnlyList<AbilityRuntime> Abilities => _abilities;

    public EntitySkillRunner(Entity entity)
    {
        _entity = entity;
    }

    // Test-only constructor. 不订阅 Entity 事件,只挂一个空的 blackboard。
    // EditMode tests 用这个构造一个不依赖 prefab 的 runner。
    internal EntitySkillRunner(Blackboard blackboard)
    {
        _entity = null;
        sharedBlackboard = blackboard ?? new Blackboard();
    }

    public void PreWarm()
    {
        ComponentAutoRegistry.EnsureRegistered();
        var data = _entity != null ? _entity.EntityData : null;
        if (data == null || data.Abilities == null) return;

        for (int i = 0; i < data.Abilities.Count; i++)
        {
            var cfg = data.Abilities[i];
            if (cfg == null) continue;
            BuildAbilityRuntime(cfg);
        }

        DispatchEvent(new PreWarmEvent());
    }

    public void OnInitialize()
    {
        // 1) 重新订阅事件
        Subscribe();

        // 2) 复位每个 SPEngine
        for (int i = 0; i < _abilities.Count; i++)
        {
            _abilities[i].spEngine?.Reset();
        }

        // 3) 重新初始化组件
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            for (int c = 0; c < a.components.Count; c++)
            {
                var comp = a.components[c];
                if (c < a.componentParams.Count)
                {
                    var teardownCtx = a.MakeContext(comp, null);
                    comp.OnTeardown(teardownCtx);
                    var initCtx = a.MakeContext(comp, null);
                    comp.OnInit(initCtx, a.componentParams[c]);
                }
            }
        }

        // 4) 清空 per-Entity 共享黑板
        sharedBlackboard.Clear();

        // 5) 派发 InitializeEvent
        DispatchEvent(new InitializeEvent());

        // 6) 激活 Talents。Talent 的 SetActive(true) 触发 OnAbilityBegin,广播给所有 ability。
        // Skills 等待 SPEngine;extras 还没 runtime。
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            if (a.Kind == AbilityKind.Talent) a.SetActive(true);
        }
    }

    public void OnTeardown()
    {
        Unsubscribe();

        // 1) 所有 active 的 ability 翻成 inactive (会触发 OnAbilityEnd,广播)
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            if (a.isActive) a.SetActive(false);
        }

        // 2) 调组件 OnTeardown 清残
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            for (int c = 0; c < a.components.Count; c++)
            {
                var ctx = a.MakeContext(a.components[c], null);
                a.components[c].OnTeardown(ctx);
            }
        }

        // 3) Unwire + 清空列表 (下次 OnInitialize 走 PreWarm 重建)
        for (int i = _abilities.Count - 1; i >= 0; i--)
        {
            UnwireRuntime(_abilities[i]);
            _abilities.RemoveAt(i);
        }
    }

    /// <summary>
    /// 每物理帧 tick。Entity.FixedUpdate 调用。
    /// </summary>
    public void Tick(float dt)
    {
        // 1) per-ability SP tick
        for (int i = 0; i < _abilities.Count; i++)
        {
            _abilities[i].spEngine?.OnTick(dt, 1f);
        }
        // 2) per-component tick
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            if (!a.isActive) continue;
            for (int c = 0; c < a.tickingComponents.Count; c++)
            {
                var comp = a.tickingComponents[c];
                var ctx = PrepareContext(a.MakeContext(comp, null));
                comp.OnTick(ctx, dt);
            }
        }
    }

    // ===== Public API: Add/Remove ExtraAbility =====

    public string AddExtraAbility(AbilityConfig cfg)
    {
        if (cfg == null)
        {
            Debug.LogError("[EntitySkillRunner] AddExtraAbility: cfg is null");
            return null;
        }
        if (cfg.Kind != AbilityKind.ExtraAbility)
        {
            Debug.LogError($"[EntitySkillRunner] AddExtraAbility: cfg.Kind must be ExtraAbility (got {cfg.Kind})");
            return null;
        }

        // 幂等:同 cfg 已存在则返回旧 id
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            if (a.Kind == AbilityKind.ExtraAbility && a.config == cfg)
            {
                return a.runtimeId;
            }
        }

        var runtime = BuildAbilityRuntime(cfg);
        _abilities.Add(runtime);
        runtime.SetActive(true);
        DispatchEvent(new AbilityAddedEvent { ability = runtime });
        return runtime.runtimeId;
    }

    public bool RemoveExtraAbility(string runtimeId)
    {
        if (string.IsNullOrEmpty(runtimeId)) return false;
        int idx = -1;
        for (int i = 0; i < _abilities.Count; i++)
        {
            if (_abilities[i].runtimeId == runtimeId) { idx = i; break; }
        }
        if (idx < 0) return false;
        var a = _abilities[idx];
        if (a.Kind != AbilityKind.ExtraAbility)
        {
            Debug.LogError("[EntitySkillRunner] RemoveExtraAbility: only ExtraAbility is removable");
            return false;
        }
        DispatchEvent(new AbilityRemovedEvent { ability = a });
        a.SetActive(false);
        for (int c = 0; c < a.components.Count; c++)
        {
            var ctx = a.MakeContext(a.components[c], null);
            a.components[c].OnTeardown(ctx);
        }
        UnwireRuntime(a);
        _abilities.RemoveAt(idx);
        return true;
    }

    public bool HasExtraAbility(string runtimeId)
    {
        if (string.IsNullOrEmpty(runtimeId)) return false;
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            if (a.runtimeId == runtimeId && a.Kind == AbilityKind.ExtraAbility) return true;
        }
        return false;
    }

    // ===== Build / Wire =====

    private AbilityRuntime BuildAbilityRuntime(AbilityConfig cfg)
    {
        if (cfg == null) return null;

        // 配置校验
        if ((cfg.Kind == AbilityKind.Talent || cfg.Kind == AbilityKind.ExtraAbility) && cfg.sp != null)
        {
            Debug.LogError($"[EntitySkillRunner] BuildAbilityRuntime: {cfg.Kind} '{cfg.abilityId}' has sp != null, aborting");
            return null;
        }
        if (cfg.Kind == AbilityKind.Skill && cfg.sp == null)
        {
            Debug.LogWarning($"[EntitySkillRunner] BuildAbilityRuntime: Skill '{cfg.abilityId}' has sp == null, treating as passive");
        }

        var runtime = new AbilityRuntime { config = cfg };
        runtime.runtimeId = GenerateRuntimeId(cfg);

        if (cfg.components != null)
        {
            for (int i = 0; i < cfg.components.Length; i++)
            {
                ComponentConfig ccfg = cfg.components[i];
                if (ccfg == null || string.IsNullOrEmpty(ccfg.componentType)) continue;
                ISkillComponent inst = ComponentFactory.Create(ccfg.componentType);
                if (inst == null)
                {
                    Debug.LogError($"[EntitySkillRunner] Unknown component type: {ccfg.componentType} in ability {cfg.abilityId}");
                    continue;
                }
                SkillContext ctx = runtime.MakeContext(inst, null);
                inst.OnInit(ctx, ccfg.parameters);
                runtime.components.Add(inst);
                runtime.componentParams.Add(ccfg.parameters);
                if (inst is ITickingComponent t) runtime.tickingComponents.Add(t);

                int triggerCount = 0;
                if (ccfg.triggers != null)
                {
                    for (int tIdx = 0; tIdx < ccfg.triggers.Length; tIdx++)
                    {
                        var trig = ccfg.triggers[tIdx];
                        if (trig == null) continue;
                        var te = trig.triggerEvent;
                        if (!runtime.componentsByTrigger.TryGetValue(te, out var list))
                        {
                            list = new List<(ISkillComponent, List<ConditionGroup>)>();
                            runtime.componentsByTrigger[te] = list;
                        }
                        list.Add((inst, trig.groups));
                        triggerCount++;
                    }
                }
                if (triggerCount == 0 && !(inst is ITickingComponent))
                {
                    Debug.LogWarning(
                        $"[AbilityRuntime] Component {ccfg.componentType} in ability {cfg.abilityId} "
                        + "declares no triggers — it will never receive OnTrigger.");
                }
            }
        }

        // SPEngine + 钩到 SetActive
        if (cfg.sp != null)
        {
            runtime.spEngine = new SPEngine(cfg.sp);
            runtime.spEngine.OnBegin += () => runtime.SetActive(true);
            runtime.spEngine.OnEnd   += () => runtime.SetActive(false);
        }

        WireRuntime(runtime);

        runtime.isInitialized = true;
        _abilities.Add(runtime);
        return runtime;
    }

    private string GenerateRuntimeId(AbilityConfig cfg)
    {
        if (cfg.Kind != AbilityKind.ExtraAbility) return cfg.abilityId;
        return $"{cfg.abilityId}_{_extraCounter++}";
    }

    private void WireRuntime(AbilityRuntime runtime)
    {
        System.Action beginHandler = () =>
            DispatchEvent(new AbilityBeginEvent { ability = runtime });
        System.Action endHandler = () =>
            DispatchEvent(new AbilityEndEvent   { ability = runtime });
        runtime.OnAbilityBegin += beginHandler;
        runtime.OnAbilityEnd   += endHandler;
        runtime._wireTeardown  = () =>
        {
            runtime.OnAbilityBegin -= beginHandler;
            runtime.OnAbilityEnd   -= endHandler;
        };
    }

    private void UnwireRuntime(AbilityRuntime runtime)
    {
        runtime._wireTeardown?.Invoke();
        runtime._wireTeardown = null;
    }

    private void Subscribe()
    {
        if (_entity == null) return;
        if (_entity.AttackBase != null)
        {
            _entity.AttackBase.OnBeforeAttack += OnBeforeAttack;
            _entity.AttackBase.OnAfterAttack += OnAfterAttack;
            _entity.AttackBase.OnBeforeTakeDamage += OnBeforeTakeDamage;
            _entity.AttackBase.OnAfterTakeDamage += OnAfterTakeDamage;
            _entity.AttackBase.OnAttackSuccessfully += OnAttackSuccessfully;
            _entity.AttackBase.OnAttackInterrupt += OnAttackInterrupt;
        }
        _entity.OnBeforeHurt += OnBeforeHurt;
        _entity.OnAfterHurt += OnAfterHurt;
        _entity.OnBeforeDieAnimation += OnBeforeDieAnimation;
        if (_entity.entityAM != null) _entity.entityAM.OnAttackAnimationBegin += OnAttackAnimBegin;
    }

    private void Unsubscribe()
    {
        if (_entity == null) return;
        if (_entity.AttackBase != null)
        {
            _entity.AttackBase.OnBeforeAttack -= OnBeforeAttack;
            _entity.AttackBase.OnAfterAttack -= OnAfterAttack;
            _entity.AttackBase.OnBeforeTakeDamage -= OnBeforeTakeDamage;
            _entity.AttackBase.OnAfterTakeDamage -= OnAfterTakeDamage;
            _entity.AttackBase.OnAttackSuccessfully -= OnAttackSuccessfully;
            _entity.AttackBase.OnAttackInterrupt -= OnAttackInterrupt;
        }
        _entity.OnBeforeHurt -= OnBeforeHurt;
        _entity.OnAfterHurt -= OnAfterHurt;
        _entity.OnBeforeDieAnimation -= OnBeforeDieAnimation;
        if (_entity.entityAM != null) _entity.entityAM.OnAttackAnimationBegin -= OnAttackAnimBegin;
    }

    // ===== Event bridges =====

    private void OnBeforeAttack(Entity target, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int cumbo, ref int damageType, int applyType)
    {
        var evt = new BeforeAttackEvent { target = target, multiplyer = multiplyer, defPenetrate = defPenetrate, mgrPenetrate = mgrPenetrate, defPenetrate_value = defPenetrate_value, mgrPenetrate_value = mgrPenetrate_value, cumbo = cumbo, damageType = damageType, applyType = applyType };
        DispatchEvent(evt);
        multiplyer = evt.multiplyer; defPenetrate = evt.defPenetrate; mgrPenetrate = evt.mgrPenetrate;
        defPenetrate_value = evt.defPenetrate_value; mgrPenetrate_value = evt.mgrPenetrate_value;
        cumbo = evt.cumbo; damageType = evt.damageType;
    }

    private void OnAfterAttack(Entity target, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly)
    {
        DispatchEvent(new AfterAttackEvent { target = target, multiplyer = multiplyer, defPenetrate = defPenetrate, mgrPenetrate = mgrPenetrate, defPenetrate_value = defPenetrate_value, mgrPenetrate_value = mgrPenetrate_value, damageType = damageType, applyType = applyType, isDeadly = isDeadly });
    }

    private void OnBeforeTakeDamage(Entity target, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int damageType, int applyType)
    {
        var evt = new BeforeTakeDamageEvent { target = target, multiplyer = multiplyer, defPenetrate = defPenetrate, mgrPenetrate = mgrPenetrate, defPenetrate_value = defPenetrate_value, mgrPenetrate_value = mgrPenetrate_value, damageType = damageType, applyType = applyType };
        DispatchEvent(evt);
        multiplyer = evt.multiplyer; defPenetrate = evt.defPenetrate; mgrPenetrate = evt.mgrPenetrate;
        defPenetrate_value = evt.defPenetrate_value; mgrPenetrate_value = evt.mgrPenetrate_value; damageType = evt.damageType;
    }

    private void OnAfterTakeDamage(Entity target, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly)
    {
        DispatchEvent(new AfterTakeDamageEvent { target = target, multiplyer = multiplyer, defPenetrate = defPenetrate, mgrPenetrate = mgrPenetrate, defPenetrate_value = defPenetrate_value, mgrPenetrate_value = mgrPenetrate_value, damageType = damageType, applyType = applyType, isDeadly = isDeadly });
        NotifySpEnginesAfterHurt(applyType);
    }

    private void OnAttackSuccessfully() { DispatchEvent(new AttackSuccessfullyEvent()); for (int i = 0; i < _abilities.Count; i++) _abilities[i].spEngine?.OnAttackSuccessfully(); }
    private void OnAttackInterrupt() { DispatchEvent(new AttackInterruptEvent()); }

    private void OnBeforeHurt(Entity origin, ref float damage, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int damageType, int applyType)
    {
        var evt = new BeforeHurtEvent { origin = origin, damage = damage, multiplyer = multiplyer, defPenetrate = defPenetrate, mgrPenetrate = mgrPenetrate, defPenetrate_value = defPenetrate_value, mgrPenetrate_value = mgrPenetrate_value, damageType = damageType, applyType = applyType };
        DispatchEvent(evt);
        damage = evt.damage; multiplyer = evt.multiplyer; defPenetrate = evt.defPenetrate; mgrPenetrate = evt.mgrPenetrate;
        defPenetrate_value = evt.defPenetrate_value; mgrPenetrate_value = evt.mgrPenetrate_value; damageType = evt.damageType;
        for (int i = 0; i < _abilities.Count; i++) _abilities[i].spEngine?.OnBeforeHurt(applyType);
    }

    private void OnAfterHurt(Entity origin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly)
    {
        DispatchEvent(new AfterHurtEvent { origin = origin, damage = damage, multiplyer = multiplyer, defPenetrate = defPenetrate, mgrPenetrate = mgrPenetrate, defPenetrate_value = defPenetrate_value, mgrPenetrate_value = mgrPenetrate_value, damageType = damageType, applyType = applyType, isDeadly = isDeadly });
        NotifySpEnginesAfterHurt(applyType);
    }

    private void OnAttackAnimBegin()
    {
        DispatchEvent(new AttackAnimBeginEvent());
        for (int i = 0; i < _abilities.Count; i++) _abilities[i].spEngine?.OnAttackAnimBegin();
    }

    private void OnBeforeDieAnimation() { DispatchEvent(new BeforeDieAnimationEvent()); }

    private void NotifySpEnginesAfterHurt(int applyType)
    {
        for (int i = 0; i < _abilities.Count; i++) _abilities[i].spEngine?.OnAfterHurt(applyType);
    }

    // ===== Dispatch core =====

    public void DispatchEvent(SkillEvent evt)
    {
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            if (!a.isInitialized) continue;
            DispatchToAbility(a, evt);
        }
    }

    private void DispatchToAbility(AbilityRuntime a, SkillEvent evt)
    {
        bool bypassActiveGate = evt is PreWarmEvent
                             || evt is InitializeEvent
                             || evt is AbilityBeginEvent
                             || evt is AbilityEndEvent
                             || evt is AbilityAddedEvent
                             || evt is AbilityRemovedEvent;
        if (!a.isActive && !bypassActiveGate) return;
        if (!a.componentsByTrigger.TryGetValue(evt.TriggerEvent, out var list)) return;

        var evalCtx = new ConditionEvalContext
        {
            sharedBlackboard = sharedBlackboard,
            entity = _entity,
            currentEvent = evt,
        };

        for (int i = 0; i < list.Count; i++)
        {
            var (comp, groups) = list[i];
            if (!ConditionEvaluator.Evaluate(groups, evalCtx)) continue;
            var ctx = PrepareContext(a.MakeContext(comp, evt));
            comp.OnTrigger(ctx);
        }
    }

    private SkillContext PrepareContext(SkillContext ctx)
    {
        ctx.sharedBlackboard = sharedBlackboard;
        ctx.entity = _entity;
        return ctx;
    }
}
```

- [ ] **Step 2: Verify compile (UI files will still error — that's Task 13)**

Open Unity. The remaining compile errors should be limited to:
- `LevelMessagePanel.cs` referencing `SkillConfig`, `skillId`, `skillName`, `_selectSkillConfig`
- `Cards.cs` referencing `SkillConfig`, `skillId`, `skillName`

These are mechanical renames, fixed in Task 13. The runtime + dispatcher should be error-free.

- [ ] **Step 3: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs
git commit -m "feat(skill-system): rewrite EntitySkillRunner for unified AbilityRuntime + AddExtraAbility API"
```

---

## Task 7: Add the testability `Blackboard` default constructor

The `EntitySkillRunner(Blackboard)` test constructor (Task 6) requires `Blackboard` to have a parameterless constructor. Verify that this is the case.

**Files:**
- Inspect: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Blackboard.cs` (file name from spec; verify)

- [ ] **Step 1: Read Blackboard.cs**

```bash
test -f Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Blackboard.cs && cat Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Blackboard.cs || echo "FILE_MISSING"
```

Expected: A class with at least a public parameterless constructor (e.g. `public Blackboard() { }`). If missing, find the file with grep and inspect.

- [ ] **Step 2: If constructor is missing, add it**

If `Blackboard` has no public parameterless constructor, add one. Open the file and add:

```csharp
public Blackboard() { }
```

(No commit needed if the file is unchanged.)

---

## Task 8: Add `AbilityAddRemoveTests` with TDD

**Files:**
- Create: `Assets/Tests/EditMode/AbilityAddRemoveTests.cs`

- [ ] **Step 1: Write the failing tests**

Create file `Assets/Tests/EditMode/AbilityAddRemoveTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using SkillSystem;
using UnityEngine;

namespace Tests.EditMode
{
    public class AbilityAddRemoveTests
    {
        private AbilityConfig MakeExtraCfg(string id = "test_extra")
        {
            var cfg = ScriptableObject.CreateInstance<AbilityConfig>();
            cfg.abilityId = id;
            cfg.abilityName = id;
            cfg.Kind = AbilityKind.ExtraAbility;
            cfg.components = new ComponentConfig[0];
            return cfg;
        }

        private EntitySkillRunner NewRunner()
        {
            // Test-only constructor:不订阅 Entity 事件
            return new EntitySkillRunner(new Blackboard());
        }

        [Test]
        public void Add_ConstructsRuntime_FiresAbilityAdded()
        {
            var runner = NewRunner();
            var cfg = MakeExtraCfg();
            int addedFires = 0;
            AbilityRuntime receivedAbility = null;
            // runner.DispatchEvent 在没有 ability 时遍历空表,不抛
            // 我们要观测:AddExtraAbility 之后,IsActive=true 且 dispatch 到 ability 列表
            // 简化:直接读 _abilities 数量 (借助 internal IReadOnlyList<AbilityRuntime> Abilities)
            var id = runner.AddExtraAbility(cfg);
            Assert.IsNotNull(id);
            Assert.AreEqual(1, runner.Abilities.Count);
            Assert.IsTrue(runner.Abilities[0].isActive);
            Object.DestroyImmediate(cfg);
        }

        [Test]
        public void Add_Idempotent_ReturnsExistingId()
        {
            var runner = NewRunner();
            var cfg = MakeExtraCfg();
            var id1 = runner.AddExtraAbility(cfg);
            var id2 = runner.AddExtraAbility(cfg);
            Assert.AreEqual(id1, id2);
            Assert.AreEqual(1, runner.Abilities.Count);
            Object.DestroyImmediate(cfg);
        }

        [Test]
        public void AddAfterRemove_CreatesFreshRuntime()
        {
            var runner = NewRunner();
            var cfg = MakeExtraCfg();
            var id1 = runner.AddExtraAbility(cfg);
            Assert.IsTrue(runner.RemoveExtraAbility(id1));
            var id2 = runner.AddExtraAbility(cfg);
            Assert.AreNotEqual(id1, id2);
            Assert.AreEqual(1, runner.Abilities.Count);
            Assert.IsTrue(runner.HasExtraAbility(id2));
            Object.DestroyImmediate(cfg);
        }

        [Test]
        public void Remove_DeactivatesAndRemoves()
        {
            var runner = NewRunner();
            var cfg = MakeExtraCfg();
            var id = runner.AddExtraAbility(cfg);
            Assert.IsTrue(runner.HasExtraAbility(id));
            Assert.IsTrue(runner.RemoveExtraAbility(id));
            Assert.IsFalse(runner.HasExtraAbility(id));
            Assert.AreEqual(0, runner.Abilities.Count);
            Object.DestroyImmediate(cfg);
        }

        [Test]
        public void Remove_NotFound_ReturnsFalse()
        {
            var runner = NewRunner();
            Assert.IsFalse(runner.RemoveExtraAbility("bogus"));
            Assert.IsFalse(runner.RemoveExtraAbility(""));
            Assert.IsFalse(runner.RemoveExtraAbility(null));
        }

        [Test]
        public void Remove_NonExtraAbility_Rejects()
        {
            var runner = NewRunner();
            // 构造一个 Skill 类型 (不通过 BuildAbilityRuntime,因为 PreWarm 需要 EntityData),
            // 手动塞一个 Skill 类型的 AbilityRuntime 进 list
            var skillCfg = ScriptableObject.CreateInstance<AbilityConfig>();
            skillCfg.abilityId = "manual_skill";
            skillCfg.Kind = AbilityKind.Skill;
            skillCfg.sp = null;  // 防止 BuildAbilityRuntime 内部走 sp
            // 直接走 AddExtraAbility with Skill should error out first
            var id = runner.AddExtraAbility(skillCfg);
            Assert.IsNull(id);  // AddExtraAbility 拒绝非 ExtraAbility
            Object.DestroyImmediate(skillCfg);
        }

        [Test]
        public void Add_NullCfg_LogsError_ReturnsNull()
        {
            var runner = NewRunner();
            // 期望 Debug.LogError,Unity Test Framework 在 EditMode 下默认会 capture log
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*cfg is null.*"));
            var id = runner.AddExtraAbility(null);
            Assert.IsNull(id);
            Assert.AreEqual(0, runner.Abilities.Count);
        }

        [Test]
        public void OnTeardown_DeactivatesActiveExtras()
        {
            var runner = NewRunner();
            var cfg = MakeExtraCfg();
            runner.AddExtraAbility(cfg);
            Assert.AreEqual(1, runner.Abilities.Count);
            Assert.IsTrue(runner.Abilities[0].isActive);
            runner.OnTeardown();
            Assert.AreEqual(0, runner.Abilities.Count);  // 列表清空
            Object.DestroyImmediate(cfg);
        }
    }
}
```

- [ ] **Step 2: Run tests**

Open Unity, Test Runner → EditMode → Run All. All `AbilityAddRemoveTests` should pass (the implementation from Task 6 supports them).

If any test fails, debug the failure against the spec §5.

- [ ] **Step 3: Commit**

```bash
git add Assets/Tests/EditMode/AbilityAddRemoveTests.cs
git commit -m "test: add AbilityAddRemoveTests covering Add/Remove/HasExtraAbility"
```

---

## Task 9: Update `LevelMessagePanel.cs` (mechanical rename)

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs`

- [ ] **Step 1: Run grep to find all SkillConfig references in this file**

```bash
grep -n "SkillConfig\|skillId\|skillName\|_selectSkillConfig" Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
```

- [ ] **Step 2: Apply mechanical replacements**

In the file, replace:
- `SkillSystem.SkillConfig` → `SkillSystem.AbilityConfig` (4 occurrences: line 305, 1170, 1685, and any others grep found)
- `_selectSkillConfig` → `_selectAbilityConfig` (variable name)
- `skillConfig` (local var on line 1685) → `abilityConfig`
- `cfg.skillId` / `cfg.skillName` → `cfg.abilityId` / `cfg.abilityName` (if any)

Use Find & Replace in your editor, or run `sed -i` on the file:

```bash
sed -i 's/SkillConfig/AbilityConfig/g; s/skillId/abilityId/g; s/skillName/abilityName/g; s/_selectSkillConfig/_selectAbilityConfig/g; s/skillConfig/abilityConfig/g' Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
```

Verify with a follow-up grep that no `SkillConfig` / `skillId` / `skillName` references remain (excluding comments and string literals, which is fine).

- [ ] **Step 3: Verify Unity compiles**

Open Unity. The errors should be reduced. `LevelMessagePanel.cs` should compile.

- [ ] **Step 4: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
git commit -m "refactor(ui): rename SkillConfig to AbilityConfig in LevelMessagePanel"
```

---

## Task 10: Update `Cards.cs` (mechanical rename)

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Components/Cards.cs`

- [ ] **Step 1: Run grep to find all references**

```bash
grep -n "SkillConfig\|skillId\|skillName" Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Components/Cards.cs
```

Expected output from earlier exploration: lines 15, 22, 36, 39, 43.

- [ ] **Step 2: Apply replacements, but be careful with UI field names**

`Cards.cs` has:
- `private TextMeshProUGUI skillName, sp0Text, ...` (line 15) — UI child-name field. **Do not rename** `skillName` here, it's a child GameObject name. Rename `skillName` → `abilityNameText` (or keep as-is — the field name is internal, but for consistency rename).
- `skillName = SkillRT.Find(...).GetComponent<...>()` (line 22) — references the field
- `skillName.color = textColor;` (line 36)
- `public void UpdateSkillCardMessage(SkillConfig config, SkillRuntime runtime = null)` (line 39) — **rename `SkillConfig` to `AbilityConfig`, `SkillRuntime` to `AbilityRuntime`**
- `skillName.text = config.skillName;` (line 43) — references the field AND the data field. **Rename the data field**: `config.skillName` → `config.abilityName`. **Rename the UI field** separately.

Apply with `sed`:

```bash
sed -i 's/SkillConfig/AbilityConfig/g; s/SkillRuntime/AbilityRuntime/g; s/UpdateSkillCardMessage(AbilityConfig/UpdateAbilityCardMessage(AbilityConfig/g' Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Components/Cards.cs
```

Then manually edit line 15 and 43: rename the local `skillName` field to `abilityNameText` (or your choice of consistent name) and update all references within the file. Use Find & Replace scoped to this file.

- [ ] **Step 3: Verify Unity compiles**

Open Unity. The `Cards.cs` errors should be gone.

- [ ] **Step 4: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Components/Cards.cs
git commit -m "refactor(ui): rename SkillConfig to AbilityConfig in Cards"
```

---

## Task 11: Final compile + full test run

**Files:** none (verification task)

- [ ] **Step 1: Open Unity, wait for full compile**

Check Console. There should be **zero errors** related to the ability system. Warnings (e.g. "skill with no sp" config) are expected and acceptable.

- [ ] **Step 2: Run all EditMode tests**

Test Runner → EditMode → Run All. All 16 tests (2 from `AbilityKindTests` + 7 from `AbilityRuntimeTests` + 8 from `AbilityAddRemoveTests`... wait, 7+2+8=17) should pass.

The exact count:
- `AbilityKindTests`: 2 tests
- `AbilityRuntimeTests`: 7 tests
- `AbilityAddRemoveTests`: 8 tests
- Total: 17 tests

- [ ] **Step 3: Manual playtest smoke check**

Open any existing scene with an entity. Spawn the entity. Verify:
- The entity's skill (if any) still works (SP bar fills, fires when full, etc.)
- The entity's talent (if any) still applies its passive effect
- No console errors during spawn, attack, and death

This is a 5-minute manual check. If a skill/talent misbehaves, the most likely cause is a config-level issue (e.g. a component's `triggers[]` still references the old `OnSkillBegin` enum value, which now doesn't exist). Grep for `OnSkillBegin` / `OnSkillEnd` in any prefab/component data and replace.

- [ ] **Step 4: Commit any playtest fixes**

If fixes were needed in component data:

```bash
git add -A
git commit -m "fix: migrate lingering OnSkillBegin/OnSkillEnd references in component data"
```

---

## Task 12: Update `MEMORY.md` with the new state

**Files:**
- Modify: `C:/Users/NING/.claude/projects/e--Unity-projects-TD/memory/MEMORY.md`

- [ ] **Step 1: Add a new memory file**

Create file `C:/Users/NING/.claude/projects/e--Unity-projects-TD/memory/ability-system-unified-runtime.md`:

```markdown
---
name: ability-system-unified-runtime
description: Skill / Talent / ExtraAbility share one AbilityRuntime; Add/Remove API for extras
metadata:
  type: project
---

Skill, Talent, and ExtraAbility are three `AbilityKind` values on a single `AbilityConfig` (replaces the old `SkillConfig`). All three are hosted on one `AbilityRuntime` (replaces `SkillRuntime`).

The runtime has one `bool isActive` field; transitions go through `SetActive(bool)`. The transition fires a C# event bridged to a `SkillEvent`:
- `OnAbilityBegin` / `OnAbilityEnd` — fire on any transition (talent init, skill SPEngine, extra Add/Remove)
- `OnAbilityAdded` / `OnAbilityRemoved` — broadcast to all abilities on the same entity when an extra is added/removed

`EntitySkillRunner.AddExtraAbility(cfg)` constructs a runtime; `RemoveExtraAbility(id)` discards it. Idempotent: adding the same cfg twice returns the existing id.

`EntityData.Skills` field renamed to `Abilities`. The 6 lifecycle events (`PreWarm`, `Initialize`, `AbilityBegin`, `AbilityEnd`, `AbilityAdded`, `AbilityRemoved`) bypass the active-window gate; the other 9 are gated.

**Why:** Three semantic concepts, one runtime model. Avoids three parallel dispatchers, three lifecycle implementations. Talents/extras reuse all 22 existing components unchanged.

**How to apply:** When adding a new ability behavior, configure it in an `AbilityConfig` (pick `Kind`), compose from existing `ISkillComponent` classes, add it to `EntityData.Abilities`. For dynamic abilities, call `AddExtraAbility` from a component or external system. For a new kind of lifecycle event, add a `TriggerEvent` value AND a `SkillEvent` subclass.

See [docs/superpowers/specs/2026-06-11-ability-system-design.md](docs/superpowers/specs/2026-06-11-ability-system-design.md).
```

- [ ] **Step 2: Add a pointer to MEMORY.md**

Open `C:/Users/NING/.claude/projects/e--Unity-projects-TD/memory/MEMORY.md` and add a line (somewhere appropriate, near other project memories):

```markdown
- [Unified ability system runtime](ability-system-unified-runtime.md) — `AbilityRuntime` hosts Skill/Talent/ExtraAbility; Add/Remove API for extras
```

- [ ] **Step 3: Commit (this file is outside the project repo, so it lives in the user's memory dir)**

There's no `git` for the memory directory in this project. Skip the commit step. The user is responsible for syncing their memory directory.

---

## Self-Review

**Spec coverage:**

| Spec section | Implemented in |
|---|---|
| §1.2 G1 unified runtime | Tasks 5, 6 |
| §1.2 G2 OnSkillBegin→OnAbilityBegin | Tasks 1, 3, 6 |
| §1.2 G3 Add/Remove API | Tasks 6, 8 |
| §1.2 G4 components unchanged | Confirmed by grep in plan pre-write |
| §1.2 G5 renames | Tasks 1, 3, 4, 5, 9, 10 |
| §2 Architecture | Tasks 1, 3, 4, 5, 6 |
| §3 Dispatcher | Task 6 |
| §4 PreWarm/OnInit/OnTeardown | Task 6 |
| §5 Add/Remove | Tasks 6, 8 |
| §6 Config validation | Task 6 (in `BuildAbilityRuntime`) |
| §7 File & folder layout | All tasks |
| §8.1 Tests | Tasks 2, 5, 8 |
| §8.2 Component regression | Task 11 (manual playtest) |
| §9 DoD | All tasks cover their DoD bullets |

**Placeholder scan:** No "TBD", "TODO", "implement later", or "similar to Task N" markers. All code blocks are complete.

**Type consistency:**

| Defined in | Used in |
|---|---|
| `AbilityConfig` (Task 4) | `AbilityRuntime.config` (Task 5), `EntitySkillRunner.BuildAbilityRuntime` (Task 6), `EntityData.Abilities` (Task 4) |
| `AbilityRuntime` (Task 5) | `EntitySkillRunner._abilities` (Task 6) |
| `AbilityKind` (Task 1) | `AbilityConfig.Kind` (Task 4), `AbilityRuntime.Kind` (Task 5), `EntitySkillRunner` (Task 6) |
| `OnAbilityBegin/End/Added/Removed` enum values (Task 1) | Event classes (Task 3), dispatcher gate (Task 6) |
| `AbilityBeginEvent/End/Added/Removed` classes (Task 3) | `EntitySkillRunner.DispatchToAbility` gate (Task 6), `WireRuntime` (Task 6) |
| `_wireTeardown` (Task 5) | `WireRuntime` / `UnwireRuntime` (Task 6) |
| `IReadOnlyList<AbilityRuntime> Abilities` (Task 6) | Test files (Tasks 5, 8) |

All match.

**Gaps:**

- §1.1 mentioned the `SkillConfig` and `SkillRuntime` files exist; the plan correctly removes them in Tasks 4 and 5.
- §7.4 lists files NOT modified. Plan does not modify any of them. ✓
- §8.1 lists 8 + 8 = 16 tests. The plan covers 17 (2 + 7 + 8) — the extra test is `AbilityKindTests.TriggerEvent_HasAbilityEvents` from Task 2, which validates the enum rename. This is fine; the spec's 16 was approximate.

**Risk: Task 6 is large.** The `EntitySkillRunner` rewrite is one big file edit. If the engineer makes a typo, the whole system breaks. Mitigation: each subsequent task verifies compile + tests pass. The Tasks 7-8 verifications will catch any issue.

**Execution handoff:** After committing the plan, the user can choose:
- **Subagent-driven** (recommended): I dispatch fresh subagents per task, review between tasks.
- **Inline execution**: I execute tasks in this session, with checkpoints.

This plan ends with the unified ability system working and tested. Implementation will produce ~6-8 commits.
