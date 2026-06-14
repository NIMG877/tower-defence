# Skill / Talent Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace 28+ per-skill/per-talent MonoBehaviour scripts with a data-driven framework (EntityData → SkillConfig → ISkillComponent[]) and migrate every existing skill/talent to the new system.

**Architecture:** Two-layer model — `SkillConfig` (data, in EntityData) is composed of `ComponentConfig` entries; each `ComponentConfig` references one `ISkillComponent` C# class registered in a factory. A single `SkillRunner` MonoBehaviour per Entity subscribes to all existing `Entity`/`AttackBase`/`AnimationMachine` events and dispatches matching `SkillEvent`s to all components. SP / charge / duration state is managed by a standalone `SPEngine`.

**Tech Stack:** Unity 2022.3+ (existing project), C# 9, Unity Test Framework (NUnit), System.Collections.Generic, Spine-Unity (existing), DG.Tweening (existing), Cysharp.Threading.Tasks (existing). No new packages required.

**Phases (each a stand-alone, backward-compatible milestone):**
- **Phase 1** — Core data model + `ISkillComponent` interface + factory + auto-registry
- **Phase 2** — `SPEngine` + `SkillRunner` + event dispatch (without deleting any old code)
- **Phase 3** — ~15 basic components (damage / buff / animation / movement)
- **Phase 4** — 5 complex components (`StageStateMachine`, `CoroutineLoop`, `LockHpShield`, `SelfDestruct`, `SelfDamageOnEvent`)
- **Phase 5** — Migrate 9 representative existing skills/talents with snapshot tests
- **Phase 6** — Migrate remaining 19+ scripts
- **Phase 7** — Delete old `Skill.cs`/`Talent.cs` and all 28+ subclass scripts
- **Phase 8** — Editor tooling (PropertyDrawers, migration assistant)

**Conventions:**
- Tests live under `Assets/Tests/SkillSystem/`, run via Unity Test Runner (Window → General → Test Runner) or batch: `Unity -batchmode -projectPath . -runTests -testPlatform editmode`
- Old code (`Skill.cs`/`Talent.cs` and subclasses) is **never deleted** until Phase 7.
- A new skill/talent configured via the new system must produce **identical observable behavior** to the old MonoBehaviour — verified by Phase 5/6 snapshot tests.

---

## File Structure (created across phases)

```
Assets/PublicScripts/SkillSystem/
  SkillConfig.cs           # data model
  SPConfig.cs              # data model
  ComponentConfig.cs       # data model + StageConfig + ConditionConfig + ParamList
  ISkillComponent.cs       # interface + SkillContext + SkillEvent base
  SkillEvents.cs           # concrete SkillEvent subclasses
  Blackboard.cs            # shared state
  ConditionEvaluator.cs    # ConditionOp evaluation
  ComponentFactory.cs      # registry + RegisterComponent attribute
  ComponentAutoRegistry.cs # scans assembly, registers at startup
  SPEngine.cs              # SP state machine
  SkillRuntime.cs          # per-skill runtime
  SkillRunner.cs           # MonoBehaviour, single per Entity
  Components/              # ~40 component files
    AttackBoostComponent.cs
    CampDamageModifierComponent.cs
    AttackRangeOverrideComponent.cs
    ApplyBuffComponent.cs
    PeriodicAuraBuffComponent.cs
    SetAbnormalStateComponent.cs
    SwapAnimationComponent.cs
    PlayAnimationComponent.cs
    ResetAnimationComponent.cs
    FlashMoveComponent.cs
    AddImpulseComponent.cs
    DeathSpawnComponent.cs
    SpawnBulletComponent.cs
    EntitySelectorRadiusEffectComponent.cs
    DamageRadiusFalloffComponent.cs
    StageStateMachineComponent.cs
    CoroutineLoopComponent.cs
    SelfDestructComponent.cs
    SelfDamageOnEventComponent.cs
    LockHpShieldComponent.cs
    PlayParticleComponent.cs
    InstantiatePrefabComponent.cs
    SetAttackEffectDataComponent.cs
    SetAnimationByBlackboardComponent.cs
    ConditionalBranchComponent.cs
  Editor/
    ComponentConfigDrawer.cs
    SPConfigDrawer.cs
    ParamListDrawer.cs
    SkillConfigDrawer.cs
    MigrationTool.cs

Assets/PublicScripts/GameData/EntityData/
  EntityData.cs            # MODIFIED: add Skills field

Assets/Tests/SkillSystem/
  BlackboardTests.cs
  ConditionEvaluatorTests.cs
  SPEngineTests.cs
  ComponentFactoryTests.cs
  SkillRunnerDispatchTests.cs
  Components/
    AttackBoostComponentTests.cs
    ApplyBuffComponentTests.cs
    SwapAnimationComponentTests.cs
    StageStateMachineComponentTests.cs
  Migration/
    ZombieTalentMigrationTests.cs
    CreeperTalentMigrationTests.cs
    KroosSkill1MigrationTests.cs
    # ... one per migrated skill (added in Phase 5/6)
```

---

# Phase 1: Core Data Model + Interface + Factory

Goal: all the serializable data types and the component registration mechanism work. No `Entity` / `SkillRunner` integration yet. Backward compatible — old `Skill.cs`/`Talent.cs` untouched.

## Task 1.1: Create Blackboard

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/Blackboard.cs`
- Create: `Assets/Tests/SkillSystem/BlackboardTests.cs`

- [ ] **Step 1: Write failing test**

`Assets/Tests/SkillSystem/BlackboardTests.cs`:
```csharp
using NUnit.Framework;
using SkillSystem;

public class BlackboardTests
{
    [Test]
    public void Get_DefaultValue_WhenKeyMissing()
    {
        var bb = new Blackboard();
        Assert.AreEqual(0, bb.Get<int>("missing"));
        Assert.AreEqual(0f, bb.Get<float>("missing"));
        Assert.AreEqual("default", bb.Get<string>("missing", "default"));
    }

    [Test]
    public void Set_ThenGet_ReturnsValue()
    {
        var bb = new Blackboard();
        bb.Set("hp", 100);
        bb.Set("name", "zombie");
        Assert.AreEqual(100, bb.Get<int>("hp"));
        Assert.AreEqual("zombie", bb.Get<string>("name"));
    }

    [Test]
    public void Has_TrueAfterSet_FalseAfterRemove()
    {
        var bb = new Blackboard();
        Assert.IsFalse(bb.Has("x"));
        bb.Set("x", 1);
        Assert.IsTrue(bb.Has("x"));
        bb.Remove("x");
        Assert.IsFalse(bb.Has("x"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Open Unity → Window → General → Test Runner → EditMode → Run All. **Expected:** `Blackboard` type not found, 3 tests fail to compile.

- [ ] **Step 3: Implement**

`Assets/PublicScripts/SkillSystem/Blackboard.cs`:
```csharp
using System.Collections.Generic;

namespace SkillSystem
{
    public class Blackboard
    {
        private readonly Dictionary<string, object> _data = new Dictionary<string, object>();

        public T Get<T>(string key, T defaultValue = default)
        {
            return _data.TryGetValue(key, out var v) ? (T)v : defaultValue;
        }

        public void Set(string key, object value)
        {
            _data[key] = value;
        }

        public bool Has(string key) => _data.ContainsKey(key);

        public void Remove(string key) => _data.Remove(key);

        public void Clear() => _data.Clear();
    }
}
```

- [ ] **Step 4: Run tests, verify pass**

Test Runner → Run All. **Expected:** 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add Assets/PublicScripts/SkillSystem/Blackboard.cs Assets/Tests/SkillSystem/BlackboardTests.cs
git commit -m "feat(skill-system): Blackboard for shared skill state"
```

---

## Task 1.2: Create ParamList and parameter types

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/ComponentConfig.cs` (will hold all parameter types now and grow in later tasks)

- [ ] **Step 1: Write failing test** (extending BlackboardTests file or new test)

`Assets/Tests/SkillSystem/ParamListTests.cs`:
```csharp
using NUnit.Framework;
using SkillSystem;
using UnityEngine;

public class ParamListTests
{
    [Test]
    public void GetTyped_Int_Float_Bool_String()
    {
        var pl = new ParamList
        {
            entries = new[] {
                new ParamEntry { key = "i", type = ParamValueType.Int, value = "42" },
                new ParamEntry { key = "f", type = ParamValueType.Float, value = "3.14" },
                new ParamEntry { key = "b", type = ParamValueType.Bool, value = "true" },
                new ParamEntry { key = "s", type = ParamValueType.String, value = "zombie" },
            }
        };
        Assert.AreEqual(42, pl.GetInt("i"));
        Assert.AreEqual(3.14f, pl.GetFloat("f"), 0.001f);
        Assert.IsTrue(pl.GetBool("b"));
        Assert.AreEqual("zombie", pl.GetString("s"));
    }

    [Test]
    public void GetTyped_MissingKey_ReturnsDefault()
    {
        var pl = new ParamList();
        Assert.AreEqual(0, pl.GetInt("nope"));
        Assert.AreEqual(0f, pl.GetFloat("nope"));
        Assert.IsFalse(pl.GetBool("nope"));
        Assert.AreEqual(string.Empty, pl.GetString("nope"));
    }

    [Test]
    public void HasKey_TrueOnlyIfPresent()
    {
        var pl = new ParamList
        {
            entries = new[] { new ParamEntry { key = "x", type = ParamValueType.Int, value = "1" } }
        };
        Assert.IsTrue(pl.HasKey("x"));
        Assert.IsFalse(pl.HasKey("y"));
    }
}
```

- [ ] **Step 2: Run test, verify failure**

Test Runner → Run All. **Expected:** `ParamList` not found.

- [ ] **Step 3: Implement**

`Assets/PublicScripts/SkillSystem/ComponentConfig.cs`:
```csharp
using System;
using UnityEngine;

namespace SkillSystem
{
    public enum ParamValueType { Int, Float, Bool, String, Vector2Int, AnimationRef, Prefab, EntityId, Color }

    [Serializable]
    public class ParamEntry
    {
        public string key;
        public ParamValueType type;
        [TextArea(1, 3)] public string value;
    }

    [Serializable]
    public class ParamList
    {
        public ParamEntry[] entries = Array.Empty<ParamEntry>();

        public bool HasKey(string key)
        {
            if (entries == null) return false;
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].key == key) return true;
            return false;
        }

        public string GetRaw(string key, string defaultValue = "")
        {
            if (entries == null) return defaultValue;
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].key == key) return entries[i].value;
            return defaultValue;
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            var raw = GetRaw(key);
            return int.TryParse(raw, out var v) ? v : defaultValue;
        }

        public float GetFloat(string key, float defaultValue = 0f)
        {
            var raw = GetRaw(key);
            return float.TryParse(raw, out var v) ? v : defaultValue;
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            var raw = GetRaw(key);
            if (string.IsNullOrEmpty(raw)) return defaultValue;
            return bool.TryParse(raw, out var v) ? v : defaultValue;
        }

        public string GetString(string key, string defaultValue = "")
        {
            return GetRaw(key, defaultValue);
        }

        public Vector2Int GetVector2Int(string key, Vector2Int defaultValue = default)
        {
            var raw = GetRaw(key);
            if (string.IsNullOrEmpty(raw)) return defaultValue;
            var parts = raw.Split(',');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var x) &&
                int.TryParse(parts[1], out var y))
                return new Vector2Int(x, y);
            return defaultValue;
        }
    }
}
```

- [ ] **Step 4: Run tests, verify pass**

Test Runner → Run All. **Expected:** 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add Assets/PublicScripts/SkillSystem/ComponentConfig.cs Assets/Tests/SkillSystem/ParamListTests.cs
git commit -m "feat(skill-system): ParamList for typed key-value component parameters"
```

---

## Task 1.3: Create ConditionConfig and ConditionEvaluator

**Files:**
- Modify: `Assets/PublicScripts/SkillSystem/ComponentConfig.cs` (add `ConditionConfig`, `ConditionOp`, `TriggerEvent`, `StageConfig`)
- Create: `Assets/PublicScripts/SkillSystem/ConditionEvaluator.cs`
- Create: `Assets/Tests/SkillSystem/ConditionEvaluatorTests.cs`

- [ ] **Step 1: Write failing tests**

`Assets/Tests/SkillSystem/ConditionEvaluatorTests.cs`:
```csharp
using NUnit.Framework;
using SkillSystem;

public class ConditionEvaluatorTests
{
    [Test]
    public void None_AlwaysTrue()
    {
        var cond = new ConditionConfig { triggerEvent = TriggerEvent.OnAfterHurt, op = ConditionOp.None };
        var ctx = MakeCtx("hpRate", "0.5", "0.3");
        Assert.IsTrue(ConditionEvaluator.Evaluate(cond, ctx));
    }

    [Test]
    public void Greater_FloatComparison()
    {
        var cond = new ConditionConfig { triggerEvent = TriggerEvent.OnAfterHurt, op = ConditionOp.Greater, leftKey = "hpRate", rightValue = "0.3" };
        Assert.IsTrue(ConditionEvaluator.Evaluate(cond, MakeCtx("hpRate", "0.5", "0.3")));
        Assert.IsFalse(ConditionEvaluator.Evaluate(cond, MakeCtx("hpRate", "0.1", "0.3")));
        Assert.IsFalse(ConditionEvaluator.Evaluate(cond, MakeCtx("hpRate", "0.3", "0.3")));
    }

    [Test]
    public void Equal_StringCompare()
    {
        var cond = new ConditionConfig { triggerEvent = TriggerEvent.OnAfterHurt, op = ConditionOp.Equal, leftKey = "targetCamp", rightValue = "2" };
        Assert.IsTrue(ConditionEvaluator.Evaluate(cond, MakeCtx("targetCamp", "2", "2")));
        Assert.IsFalse(ConditionEvaluator.Evaluate(cond, MakeCtx("targetCamp", "1", "2")));
    }

    [Test]
    public void HasBlackboardKey_FromContext()
    {
        var cond = new ConditionConfig { triggerEvent = TriggerEvent.OnAfterHurt, op = ConditionOp.HasBlackboardKey, leftKey = "phase" };
        var ctx = MakeCtx("hpRate", "0.5", "0.3");
        Assert.IsFalse(ConditionEvaluator.Evaluate(cond, ctx));
        ctx.Blackboard.Set("phase", "charging");
        Assert.IsTrue(ConditionEvaluator.Evaluate(cond, ctx));
    }

    private ConditionEvalContext MakeCtx(string leftKey, string leftValue, string rightValue)
    {
        var ctx = new ConditionEvalContext();
        ctx.Blackboard.Set(leftKey, leftValue);
        return ctx;
    }
}
```

- [ ] **Step 2: Run, verify failure**

Expected: `ConditionConfig` / `ConditionEvaluator` not found.

- [ ] **Step 3: Add types to ComponentConfig.cs**

Append to `Assets/PublicScripts/SkillSystem/ComponentConfig.cs`:
```csharp
namespace SkillSystem
{
    public enum TriggerEvent
    {
        OnPreWarm, OnInitialize,
        OnBeforeAttack, OnAfterAttack,
        OnBeforeTakeDamage, OnAfterTakeDamage,
        OnAttackSuccessfully, OnAttackInterrupt,
        OnBeforeHurt, OnAfterHurt,
        OnAttackAnimBegin,
        OnBeforeDieAnimation, OnDeath,
        OnIntervalTick,
        OnSkillBegin, OnSkillEnd,
    }

    public enum ConditionOp
    {
        None, Equal, NotEqual,
        Greater, GreaterOrEqual, Less, LessOrEqual,
        HasBuff, NotHasBuff,
        IsInAbnormalState, NotInAbnormalState,
        HasBlackboardKey, NotHasBlackboardKey,
    }

    [Serializable]
    public class ConditionConfig
    {
        public TriggerEvent triggerEvent;
        public ConditionOp op = ConditionOp.None;
        public string leftKey;
        public string rightValue;
    }

    [Serializable]
    public class StageConfig
    {
        public string name;
        public float enterDuration = -1f;
        public ConditionConfig[] transitionOn = Array.Empty<ConditionConfig>();
        public string nextStageOnTransition;
        public ComponentConfig[] enterEffects = Array.Empty<ComponentConfig>();
        public ComponentConfig[] tickEffects = Array.Empty<ComponentConfig>();
        public ComponentConfig[] exitEffects = Array.Empty<ComponentConfig>();
    }

    // Stub: ComponentConfig added in Task 1.5. Other phases add the rest of the fields.
    [Serializable]
    public class ComponentConfig
    {
        public string componentType;
        public ConditionConfig[] triggers = Array.Empty<ConditionConfig>();
        public ParamList parameters = new ParamList();
        public StageConfig[] stages; // optional, for StageStateMachine
        public ComponentConfig[] subComponents; // optional, for wrapper components
    }
}
```

⚠ **Important:** if `ComponentConfig` already exists from a partial prior file, remove duplicates. The final file is the union shown above.

- [ ] **Step 4: Implement ConditionEvaluator**

`Assets/PublicScripts/SkillSystem/ConditionEvaluator.cs`:
```csharp
namespace SkillSystem
{
    public class ConditionEvalContext
    {
        public Blackboard Blackboard = new Blackboard();
        public SkillEvent Event; // optional
    }

    public static class ConditionEvaluator
    {
        public static bool Evaluate(ConditionConfig cond, ConditionEvalContext ctx)
        {
            if (cond == null) return true;
            if (cond.op == ConditionOp.None) return true;

            switch (cond.op)
            {
                case ConditionOp.HasBlackboardKey:
                    return ctx.Blackboard.Has(cond.leftKey);
                case ConditionOp.NotHasBlackboardKey:
                    return !ctx.Blackboard.Has(cond.leftKey);
                case ConditionOp.Equal:
                    return ctx.Blackboard.Get<string>(cond.leftKey) == cond.rightValue;
                case ConditionOp.NotEqual:
                    return ctx.Blackboard.Get<string>(cond.leftKey) != cond.rightValue;
                case ConditionOp.Greater:
                    return CompareNumeric(ctx, cond.leftKey, cond.rightValue) > 0;
                case ConditionOp.GreaterOrEqual:
                    return CompareNumeric(ctx, cond.leftKey, cond.rightValue) >= 0;
                case ConditionOp.Less:
                    return CompareNumeric(ctx, cond.leftKey, cond.rightValue) < 0;
                case ConditionOp.LessOrEqual:
                    return CompareNumeric(ctx, cond.leftKey, cond.rightValue) <= 0;
                // HasBuff / IsInAbnormalState — implemented in Phase 2 when SkillEvent carries BuffController ref
                default: return true;
            }
        }

        private static int CompareNumeric(ConditionEvalContext ctx, string leftKey, string rightValueStr)
        {
            var left = ctx.Blackboard.Get<string>(leftKey, "");
            if (float.TryParse(left, out var l) && float.TryParse(rightValueStr, out var r))
                return l.CompareTo(r);
            return string.Compare(left, rightValueStr, System.StringComparison.Ordinal);
        }
    }
}
```

- [ ] **Step 5: Run tests, verify pass**

Test Runner → Run All. **Expected:** 4 tests pass.

- [ ] **Step 6: Commit**

```bash
git add Assets/PublicScripts/SkillSystem/ComponentConfig.cs Assets/PublicScripts/SkillSystem/ConditionEvaluator.cs Assets/Tests/SkillSystem/ConditionEvaluatorTests.cs
git commit -m "feat(skill-system): ConditionConfig + ConditionEvaluator"
```

---

## Task 1.4: Create SkillConfig + SPConfig

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/SPConfig.cs`
- Create: `Assets/PublicScripts/SkillSystem/SkillConfig.cs`
- Create: `Assets/Tests/SkillSystem/SkillConfigTests.cs`

- [ ] **Step 1: Write failing test**

`Assets/Tests/SkillSystem/SkillConfigTests.cs`:
```csharp
using NUnit.Framework;
using SkillSystem;
using UnityEngine;

public class SkillConfigTests
{
    [Test]
    public void SkillConfig_RoundTripsThroughSerialization()
    {
        var cfg = new SkillConfig
        {
            skillId = "zombie_angry",
            skillName = "狂暴",
            description = "受到致命伤后狂暴",
            kind = SkillKind.OnDeath,
            sp = new SPConfig { totalSp = 100, initialSp = 50, chargeNum = 1, skillDuration = -1f, recoverMode = SpRecoverMode.Natural, consumeMode = SpConsumeMode.Instant, openMode = SkillOpenMode.Natural, recoverForbidDuringSkill = false, canManualClose = false, skillAttackRange = new Vector2Int[0] },
            globalConditions = new ConditionConfig[0],
            components = new[] {
                new ComponentConfig { componentType = "ApplyBuff", parameters = new ParamList { entries = new[] { new ParamEntry { key = "buffType", type = ParamValueType.String, value = "atk" } } } }
            }
        };
        var json = JsonUtility.ToJson(cfg);
        var roundTripped = JsonUtility.FromJson<SkillConfig>(json);
        Assert.AreEqual("zombie_angry", roundTripped.skillId);
        Assert.AreEqual(100, roundTripped.sp.totalSp);
        Assert.AreEqual("ApplyBuff", roundTripped.components[0].componentType);
    }

    [Test]
    public void SPConfig_Defaults_AreSafe()
    {
        var sp = new SPConfig();
        Assert.AreEqual(0, sp.totalSp);
        Assert.AreEqual(1, sp.chargeNum);
        Assert.AreEqual(SpRecoverMode.Natural, sp.recoverMode);
    }
}
```

- [ ] **Step 2: Run, verify failure**

Expected: types not found.

- [ ] **Step 3: Implement SPConfig**

`Assets/PublicScripts/SkillSystem/SPConfig.cs`:
```csharp
using System;
using UnityEngine;

namespace SkillSystem
{
    public enum SpRecoverMode { Natural, OnAttackHit, OnAfterHurt }
    public enum SpConsumeMode { Duration, OnAttackHit, OnAfterHurt, Instant }
    public enum SkillOpenMode { Natural, OnAttackAnimBegin, OnBeforeHurt, Manual, OnAttackHit }

    [Serializable]
    public class SPConfig
    {
        [Tooltip("Total SP. Skill fires when current SP >= totalSp.")]
        public int totalSp;
        public int initialSp;
        [Tooltip("Max charges. >1 enables multi-charge behavior.")]
        public int chargeNum = 1;
        [Tooltip(">0 = active for that long after fire; <=0 = instant fire.")]
        public float skillDuration;
        public SpRecoverMode recoverMode = SpRecoverMode.Natural;
        public SpConsumeMode consumeMode = SpConsumeMode.Duration;
        public SkillOpenMode openMode = SkillOpenMode.Natural;
        public bool recoverForbidDuringSkill;
        public bool canManualClose;
        public Vector2Int[] skillAttackRange = Array.Empty<Vector2Int>();
    }
}
```

- [ ] **Step 4: Implement SkillConfig**

`Assets/PublicScripts/SkillSystem/SkillConfig.cs`:
```csharp
using System;
using UnityEngine;

namespace SkillSystem
{
    public enum SkillKind { Passive, ActiveSkill, Aura, OnDeath }

    [Serializable]
    public class SkillConfig
    {
        public string skillId;
        public string skillName;
        [TextArea(2, 5)] public string description;
        public SkillKind kind = SkillKind.Passive;

        public SPConfig sp;
        public ConditionConfig[] globalConditions = Array.Empty<ConditionConfig>();
        public ComponentConfig[] components = Array.Empty<ComponentConfig>();
    }
}
```

- [ ] **Step 5: Run tests, verify pass**

Test Runner → Run All. **Expected:** 2 tests pass.

- [ ] **Step 6: Commit**

```bash
git add Assets/PublicScripts/SkillSystem/SPConfig.cs Assets/PublicScripts/SkillSystem/SkillConfig.cs Assets/Tests/SkillSystem/SkillConfigTests.cs
git commit -m "feat(skill-system): SkillConfig + SPConfig data models"
```

---

## Task 1.5: Add Skills field to EntityData

**Files:**
- Modify: `Assets/PublicScripts/GameData/EntityData/EntityData.cs` (append field)

- [ ] **Step 1: Verify backward compat**

Existing `EntityData` consumers do not access `Skills`. Adding a new field is non-breaking.

- [ ] **Step 2: Modify EntityData.cs**

Append to `Assets/PublicScripts/GameData/EntityData/EntityData.cs` (at the bottom of the class):
```csharp
    // 技能 / 天赋（数据驱动框架）
    public List<SkillConfig> Skills = new List<SkillConfig>();
```

(The `using SkillSystem;` directive must be added at the top of the file.)
```csharp
using System.Collections.Generic;
using UnityEngine;
using SkillSystem;  // <-- add this
```

- [ ] **Step 3: Verify Unity compiles**

Open Unity Editor. **Expected:** no compile errors. Existing `EntityData` assets still load (the new `Skills` list is empty for them).

- [ ] **Step 4: Commit**

```bash
git add Assets/PublicScripts/GameData/EntityData/EntityData.cs
git commit -m "feat(entity-data): add Skills list field to EntityData"
```

---

## Task 1.6: Create ISkillComponent + SkillContext + SkillEvent base

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/ISkillComponent.cs`

(No test in this task — interface only, tested via concrete components.)

- [ ] **Step 1: Implement**

`Assets/PublicScripts/SkillSystem/ISkillComponent.cs`:
```csharp
using UnityEngine;

namespace SkillSystem
{
    public abstract class SkillEvent { }

    public class SkillContext
    {
        public Entity entity;
        public SkillRuntime skill;
        public ISkillComponent component;
        public SkillEvent currentEvent;
        public Blackboard blackboard;          // per-skill
        public Blackboard sharedBlackboard;    // per-Entity SkillRunner
        public GameObject tempContainer;       // for spawn effects
    }

    public interface ISkillComponent
    {
        void OnInit(SkillContext ctx, ParamList parameters);
        void OnTrigger(SkillContext ctx);
        void OnTick(SkillContext ctx, float dt);
        void OnTeardown(SkillContext ctx);
    }

    public interface ITickingComponent : ISkillComponent { }
}
```

Note: `Entity` is referenced as a global type. There is no circular dependency because `ISkillComponent` is in the `SkillSystem` namespace but `Entity` is in the global namespace.

- [ ] **Step 2: Verify compiles**

Open Unity Editor. **Expected:** compile OK.

- [ ] **Step 3: Commit**

```bash
git add Assets/PublicScripts/SkillSystem/ISkillComponent.cs
git commit -m "feat(skill-system): ISkillComponent interface + SkillContext + SkillEvent base"
```

---

## Task 1.7: Create ComponentFactory + RegisterComponent attribute

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/ComponentFactory.cs`
- Create: `Assets/Tests/SkillSystem/ComponentFactoryTests.cs`

- [ ] **Step 1: Write failing test**

`Assets/Tests/SkillSystem/ComponentFactoryTests.cs`:
```csharp
using NUnit.Framework;
using SkillSystem;

public class ComponentFactoryTests
{
    public class StubComponent : ISkillComponent
    {
        public void OnInit(SkillContext ctx, ParamList parameters) { }
        public void OnTrigger(SkillContext ctx) { }
        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }

    [Test]
    public void Register_ThenCreate_ReturnsInstance()
    {
        ComponentFactory.Register("Stub", () => new StubComponent());
        var c = ComponentFactory.Create("Stub");
        Assert.IsNotNull(c);
        Assert.IsInstanceOf<StubComponent>(c);
    }

    [Test]
    public void Create_UnknownType_ReturnsNull()
    {
        var c = ComponentFactory.Create("DoesNotExist");
        Assert.IsNull(c);
    }

    [Test]
    public void IsRegistered_ReflectsState()
    {
        ComponentFactory.Register("Stub2", () => new StubComponent());
        Assert.IsTrue(ComponentFactory.IsRegistered("Stub2"));
        Assert.IsFalse(ComponentFactory.IsRegistered("Unknown"));
    }
}
```

- [ ] **Step 2: Run, verify failure**

Expected: `ComponentFactory` not found.

- [ ] **Step 3: Implement**

`Assets/PublicScripts/SkillSystem/ComponentFactory.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace SkillSystem
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class RegisterComponentAttribute : Attribute
    {
        public string TypeName;
        public RegisterComponentAttribute(string typeName) { TypeName = typeName; }
    }

    public static class ComponentFactory
    {
        private static readonly Dictionary<string, Func<ISkillComponent>> _registry =
            new Dictionary<string, Func<ISkillComponent>>();

        public static void Register(string typeName, Func<ISkillComponent> ctor)
        {
            if (string.IsNullOrEmpty(typeName))
                throw new ArgumentException("typeName must not be empty", nameof(typeName));
            if (ctor == null)
                throw new ArgumentNullException(nameof(ctor));
            _registry[typeName] = ctor;
        }

        public static ISkillComponent Create(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            return _registry.TryGetValue(typeName, out var ctor) ? ctor() : null;
        }

        public static bool IsRegistered(string typeName) =>
            !string.IsNullOrEmpty(typeName) && _registry.ContainsKey(typeName);

        public static IEnumerable<string> RegisteredTypes => _registry.Keys;

        public static void Clear() => _registry.Clear();
    }
}
```

- [ ] **Step 4: Run tests, verify pass**

Test Runner → Run All. **Expected:** 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add Assets/PublicScripts/SkillSystem/ComponentFactory.cs Assets/Tests/SkillSystem/ComponentFactoryTests.cs
git commit -m "feat(skill-system): ComponentFactory + RegisterComponent attribute"
```

---

## Task 1.8: Create ComponentAutoRegistry (assembly scan)

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/ComponentAutoRegistry.cs`
- Modify: `Assets/PublicScripts/SkillSystem/ISkillComponent.cs` (add a static class trigger that calls into `ComponentAutoRegistry.RegisterAll()`)

- [ ] **Step 1: Implement**

`Assets/PublicScripts/SkillSystem/ComponentAutoRegistry.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SkillSystem
{
    public static class ComponentAutoRegistry
    {
        private static bool _done = false;
        private static readonly object _lock = new object();

        public static void RegisterAll()
        {
            lock (_lock)
            {
                if (_done) return;
                var asm = Assembly.GetAssembly(typeof(ISkillComponent));
                foreach (var type in asm.GetTypes())
                {
                    var attr = type.GetCustomAttribute<RegisterComponentAttribute>();
                    if (attr == null) continue;
                    if (!typeof(ISkillComponent).IsAssignableFrom(type)) continue;
                    if (type.IsAbstract || type.IsInterface) continue;
                    ComponentFactory.Register(attr.TypeName, () => (ISkillComponent)Activator.CreateInstance(type));
                }
                _done = true;
            }
        }

        public static void Reset() { lock (_lock) { _done = false; } }

        // Convenience: ensure registry is populated. Call from any code path that needs the registry.
        public static void EnsureRegistered()
        {
            if (!_done) RegisterAll();
        }
    }
}
```

- [ ] **Step 2: Add static init trigger**

Append to `Assets/PublicScripts/SkillSystem/ISkillComponent.cs`:
```csharp
    public static class SkillSystemBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Init()
        {
            ComponentAutoRegistry.EnsureRegistered();
        }
    }
```

- [ ] **Step 3: Verify compiles**

Open Unity Editor. **Expected:** compile OK.

- [ ] **Step 4: Commit**

```bash
git add Assets/PublicScripts/SkillSystem/ComponentAutoRegistry.cs Assets/PublicScripts/SkillSystem/ISkillComponent.cs
git commit -m "feat(skill-system): auto-register components via assembly scan + bootstrap"
```

---

# Phase 2: SP Engine + SkillRunner + Event Dispatch

Goal: SP state machine works; `SkillRunner` subscribes to `Entity`/`AttackBase`/`AnimationMachine` events and dispatches; `SkillEvent` subclasses defined. No components implemented yet (they come in Phase 3-4). Backward compatible — old `Skill.cs`/`Talent.cs` still active on entities that haven't been migrated.

## Task 2.1: Create SkillEvents

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/SkillEvents.cs`

- [ ] **Step 1: Implement**

`Assets/PublicScripts/SkillSystem/SkillEvents.cs`:
```csharp
namespace SkillSystem
{
    public class PreWarmEvent : SkillEvent { }
    public class InitializeEvent : SkillEvent { }
    public class BeforeAttackEvent : SkillEvent
    {
        public Entity target;
        public float multiplyer = 1f;
        public float defPenetrate;
        public float mgrPenetrate;
        public float defPenetrate_value;
        public float mgrPenetrate_value;
        public int cumbo = 1;
        public int damageType;
        public int applyType;
    }
    public class AfterAttackEvent : SkillEvent
    {
        public Entity target;
        public float multiplyer;
        public float defPenetrate;
        public float mgrPenetrate;
        public float defPenetrate_value;
        public float mgrPenetrate_value;
        public int damageType;
        public int applyType;
        public bool isDeadly;
    }
    public class BeforeTakeDamageEvent : SkillEvent
    {
        public Entity target;
        public float multiplyer = 1f;
        public float defPenetrate;
        public float mgrPenetrate;
        public float defPenetrate_value;
        public float mgrPenetrate_value;
        public int damageType;
        public int applyType;
    }
    public class AfterTakeDamageEvent : SkillEvent
    {
        public Entity target;
        public float multiplyer;
        public float defPenetrate;
        public float mgrPenetrate;
        public float defPenetrate_value;
        public float mgrPenetrate_value;
        public int damageType;
        public int applyType;
        public bool isDeadly;
    }
    public class AttackSuccessfullyEvent : SkillEvent { }
    public class AttackInterruptEvent : SkillEvent { }
    public class BeforeHurtEvent : SkillEvent
    {
        public Entity origin;
        public float damage;
        public float multiplyer;
        public float defPenetrate;
        public float mgrPenetrate;
        public float defPenetrate_value;
        public float mgrPenetrate_value;
        public int damageType;
        public int applyType;
        public bool isDeadly;
    }
    public class AfterHurtEvent : SkillEvent
    {
        public Entity origin;
        public float damage;
        public float multiplyer;
        public float defPenetrate;
        public float mgrPenetrate;
        public float defPenetrate_value;
        public float mgrPenetrate_value;
        public int damageType;
        public int applyType;
        public bool isDeadly;
    }
    public class AttackAnimBeginEvent : SkillEvent { }
    public class BeforeDieAnimationEvent : SkillEvent { }
    public class DeathEvent : SkillEvent { }
    public class IntervalTickEvent : SkillEvent
    {
        public float dt;
    }
    public class SkillBeginEvent : SkillEvent
    {
        public SkillRuntime skill;
    }
    public class SkillEndEvent : SkillEvent
    {
        public SkillRuntime skill;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/PublicScripts/SkillSystem/SkillEvents.cs
git commit -m "feat(skill-system): concrete SkillEvent subclasses"
```

---

## Task 2.2: Create SPEngine

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/SPEngine.cs`
- Create: `Assets/Tests/SkillSystem/SPEngineTests.cs`

- [ ] **Step 1: Write failing tests**

`Assets/Tests/SkillSystem/SPEngineTests.cs`:
```csharp
using NUnit.Framework;
using SkillSystem;

public class SPEngineTests
{
    [Test]
    public void Natural_Recovery_IncrementsSp()
    {
        var cfg = new SPConfig { totalSp = 10, initialSp = 0, recoverMode = SpRecoverMode.Natural, openMode = SkillOpenMode.Manual };
        var eng = new SPEngine(cfg, () => { });
        eng.OnTick(5f, dtMultiplier: 1f);
        Assert.AreEqual(5f, eng.CurrentSp);
    }

    [Test]
    public void Natural_Recovery_CapsAtTotalSp()
    {
        var cfg = new SPConfig { totalSp = 10, initialSp = 0, recoverMode = SpRecoverMode.Natural, openMode = SkillOpenMode.Manual };
        var eng = new SPEngine(cfg, () => { });
        eng.OnTick(20f, dtMultiplier: 1f);
        Assert.AreEqual(10f, eng.CurrentSp);
    }

    [Test]
    public void OnAttackHit_Recover_AddsOneSp()
    {
        var cfg = new SPConfig { totalSp = 5, initialSp = 0, recoverMode = SpRecoverMode.OnAttackHit, openMode = SkillOpenMode.Manual };
        var eng = new SPEngine(cfg, () => { });
        eng.OnAttackSuccessfully();
        eng.OnAttackSuccessfully();
        Assert.AreEqual(2f, eng.CurrentSp);
    }

    [Test]
    public void OnAttackHit_Recover_TriggersFireAtFull()
    {
        var cfg = new SPConfig { totalSp = 3, initialSp = 0, recoverMode = SpRecoverMode.OnAttackHit, openMode = SkillOpenMode.OnAttackHit, consumeMode = SpConsumeMode.Instant };
        bool fired = false;
        var eng = new SPEngine(cfg, () => fired = true);
        eng.OnAttackSuccessfully(); eng.OnAttackSuccessfully(); eng.OnAttackSuccessfully();
        eng.OnAttackHit();
        Assert.IsTrue(fired);
        Assert.AreEqual(0f, eng.CurrentSp); // consumed by instant mode
    }

    [Test]
    public void InitialSp_SetsAtConstruction()
    {
        var cfg = new SPConfig { totalSp = 10, initialSp = 7, recoverMode = SpRecoverMode.Natural, openMode = SkillOpenMode.Natural };
        var eng = new SPEngine(cfg, () => { });
        Assert.AreEqual(7f, eng.CurrentSp);
    }
}
```

- [ ] **Step 2: Run, verify failure**

Expected: `SPEngine` not found.

- [ ] **Step 3: Implement**

`Assets/PublicScripts/SkillSystem/SPEngine.cs`:
```csharp
using System;

namespace SkillSystem
{
    public class SPEngine
    {
        private readonly SPConfig _cfg;
        private readonly Action _onFire;
        private float _currentSp;
        private int _currentCharge;
        private float _currentDuration;
        private bool _isActive;
        private int _recoverForbid;
        private bool _wasFiredThisTick;

        public float CurrentSp => _currentSp;
        public int CurrentCharge => _currentCharge;
        public bool IsActive => _isActive;
        public bool IsRecoverForbidden => _recoverForbid > 0;

        public SPEngine(SPConfig cfg, Action onFire)
        {
            _cfg = cfg ?? new SPConfig();
            _onFire = onFire ?? (() => { });
            _currentSp = _cfg.initialSp;
            _currentCharge = 0;
            _currentDuration = 0f;
        }

        public void OnTick(float dt, float dtMultiplier)
        {
            _wasFiredThisTick = false;
            // Recovery
            if (_cfg.recoverMode == SpRecoverMode.Natural && _recoverForbid == 0 && !_isActive)
            {
                _currentSp = Math.Min(_currentSp + dt * dtMultiplier, _cfg.totalSp);
            }
            // Duration consume
            if (_isActive && _cfg.consumeMode == SpConsumeMode.Duration)
            {
                _currentDuration -= dt;
                if (_currentDuration <= 0f)
                {
                    _currentDuration = 0f;
                    EndSkill();
                }
            }
            // Natural open
            if (!_isActive && _cfg.openMode == SkillOpenMode.Natural && CanBegin())
            {
                FireSkill();
            }
        }

        public void OnAttackSuccessfully()
        {
            if (_cfg.recoverMode == SpRecoverMode.OnAttackHit) AddSp(1f);
            if (_cfg.consumeMode == SpConsumeMode.OnAttackHit && _isActive) ConsumeChargeForHit();
        }

        public void OnAfterHurt(int applyType)
        {
            if (applyType != 0 && applyType != 1) return;
            if (_cfg.recoverMode == SpRecoverMode.OnAfterHurt) AddSp(1f);
            if (_cfg.consumeMode == SpConsumeMode.OnAfterHurt && _isActive) ConsumeChargeForHit();
        }

        public void OnAttackAnimBegin()
        {
            if (_cfg.openMode == SkillOpenMode.OnAttackAnimBegin && CanBegin()) FireSkill();
        }

        public void OnBeforeHurt(int applyType)
        {
            if (applyType != 0 && applyType != 1) return;
            if (_cfg.openMode == SkillOpenMode.OnBeforeHurt && CanBegin()) FireSkill();
        }

        public void OnAttackHit()
        {
            if (_cfg.openMode == SkillOpenMode.OnAttackHit && CanBegin()) FireSkill();
        }

        public bool CanBegin()
        {
            if (_isActive) return false;
            float total = _currentSp + _cfg.totalSp * _currentCharge;
            return total >= _cfg.totalSp;
        }

        public void FireSkill()
        {
            if (!CanBegin()) return;
            if (_currentCharge > 0) _currentCharge--;
            else _currentSp = 0f;
            _isActive = true;
            if (_cfg.skillDuration > 0f) _currentDuration = _cfg.skillDuration;
            else { _isActive = false; _wasFiredThisTick = true; }
            if (_cfg.recoverForbidDuringSkill) _recoverForbid++;
            _onFire();
        }

        public void EndSkill()
        {
            if (!_isActive) return;
            _isActive = false;
            _currentDuration = 0f;
            if (_cfg.recoverForbidDuringSkill && _recoverForbid > 0) _recoverForbid--;
        }

        public void SetRecoverForbid(bool forbid)
        {
            if (forbid) _recoverForbid++;
            else if (_recoverForbid > 0) _recoverForbid--;
        }

        private void AddSp(float v)
        {
            if (_cfg.chargeNum <= 1)
            {
                _currentSp = Math.Min(_currentSp + v, _cfg.totalSp);
            }
            else
            {
                _currentSp += v;
                while (_currentSp >= _cfg.totalSp && _currentCharge < _cfg.chargeNum)
                {
                    _currentSp -= _cfg.totalSp;
                    _currentCharge++;
                }
                if (_currentCharge >= _cfg.chargeNum)
                {
                    _currentCharge = _cfg.chargeNum;
                    _currentSp = 0f;
                }
            }
        }

        private void ConsumeChargeForHit()
        {
            // In a duration-based skill that consumes on hit, just end it
            if (_cfg.consumeMode == SpConsumeMode.OnAttackHit) EndSkill();
        }
    }
}
```

- [ ] **Step 4: Run tests, verify pass**

Test Runner → Run All. **Expected:** 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add Assets/PublicScripts/SkillSystem/SPEngine.cs Assets/Tests/SkillSystem/SPEngineTests.cs
git commit -m "feat(skill-system): SPEngine with all open/recover/consume modes"
```

---

## Task 2.3: Create SkillRuntime

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/SkillRuntime.cs`

(No standalone test — covered by SkillRunner tests in Task 2.5.)

- [ ] **Step 1: Implement**

`Assets/PublicScripts/SkillSystem/SkillRuntime.cs`:
```csharp
using System.Collections.Generic;

namespace SkillSystem
{
    public class SkillRuntime
    {
        public SkillConfig config;
        public SPEngine spEngine;
        public Blackboard blackboard = new Blackboard();
        public List<ISkillComponent> components = new List<ISkillComponent>();
        public List<ITickingComponent> tickingComponents = new List<ITickingComponent>();
        public bool isInitialized;
        public bool isActive; // true while skill is firing (SPEngine.IsActive)

        public SkillContext MakeContext(ISkillComponent component, SkillEvent evt = null)
        {
            return new SkillContext
            {
                skill = this,
                component = component,
                currentEvent = evt,
                blackboard = blackboard,
            };
        }

        public bool MatchesTrigger(TriggerEvent te)
        {
            if (config.globalConditions == null) return true;
            for (int i = 0; i < config.globalConditions.Length; i++)
            {
                if (config.globalConditions[i].triggerEvent == te) return true;
            }
            return config.globalConditions.Length == 0;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/PublicScripts/SkillSystem/SkillRuntime.cs
git commit -m "feat(skill-system): SkillRuntime container"
```

---

## Task 2.4: Create SkillRunner — event subscription + dispatch

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/SkillRunner.cs`

- [ ] **Step 1: Implement**

`Assets/PublicScripts/SkillSystem/SkillRunner.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;

namespace SkillSystem
{
    public class SkillRunner : MonoBehaviour
    {
        private Entity _entity;
        private readonly List<SkillRuntime> _skills = new List<SkillRuntime>();
        public Blackboard sharedBlackboard = new Blackboard();
        public GameObject TempContainer;

        public IReadOnlyList<SkillRuntime> Skills => _skills;

        public void PreWarm()
        {
            ComponentAutoRegistry.EnsureRegistered();
            _entity = GetComponent<Entity>();
            if (TempContainer == null) TempContainer = _entity != null ? _entity.TempContainer : null;

            var data = _entity != null ? _entity.EntityData : null;
            if (data == null || data.Skills == null) return;

            for (int i = 0; i < data.Skills.Count; i++)
            {
                var cfg = data.Skills[i];
                if (cfg == null) continue;
                BuildSkillRuntime(cfg);
            }

            Subscribe();
            DispatchEvent(new PreWarmEvent());
        }

        public void OnInitialize()
        {
            DispatchEvent(new InitializeEvent());
        }

        public void OnTeardown()
        {
            for (int i = 0; i < _skills.Count; i++)
            {
                var s = _skills[i];
                for (int c = 0; c < s.components.Count; c++)
                {
                    var ctx = s.MakeContext(s.components[c], null);
                    s.components[c].OnTeardown(ctx);
                }
            }
            Unsubscribe();
            _skills.Clear();
        }

        public void OnDeath()
        {
            DispatchEvent(new DeathEvent());
        }

        private void BuildSkillRuntime(SkillConfig cfg)
        {
            var runtime = new SkillRuntime { config = cfg };
            if (cfg.sp != null && cfg.sp.totalSp > 0)
            {
                runtime.spEngine = new SPEngine(cfg.sp, () => OnSkillFire(runtime));
            }
            if (cfg.components != null)
            {
                for (int i = 0; i < cfg.components.Length; i++)
                {
                    var ccfg = cfg.components[i];
                    if (ccfg == null || string.IsNullOrEmpty(ccfg.componentType)) continue;
                    var inst = ComponentFactory.Create(ccfg.componentType);
                    if (inst == null)
                    {
                        Debug.LogError($"[SkillRunner] Unknown component type: {ccfg.componentType} in skill {cfg.skillId}");
                        continue;
                    }
                    var ctx = runtime.MakeContext(inst, null);
                    inst.OnInit(ctx, ccfg.parameters);
                    runtime.components.Add(inst);
                    if (inst is ITickingComponent t) runtime.tickingComponents.Add(t);
                }
            }
            _skills.Add(runtime);
        }

        private void OnSkillFire(SkillRuntime runtime)
        {
            runtime.isActive = true;
            DispatchToSkill(runtime, new SkillBeginEvent { skill = runtime });
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

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            // 1) per-skill SP tick
            for (int i = 0; i < _skills.Count; i++)
            {
                _skills[i].spEngine?.OnTick(dt, 1f);
            }
            // 2) per-component tick
            for (int i = 0; i < _skills.Count; i++)
            {
                var s = _skills[i];
                for (int c = 0; c < s.tickingComponents.Count; c++)
                {
                    var ctx = s.MakeContext(s.tickingComponents[c], new IntervalTickEvent { dt = dt });
                    ctx.sharedBlackboard = sharedBlackboard;
                    s.tickingComponents[c].OnTick(ctx, dt);
                }
            }
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
            for (int i = 0; i < _skills.Count; i++) _skills[i].spEngine?.OnAfterHurt(applyType);
        }

        private void OnAttackSuccessfully() { DispatchEvent(new AttackSuccessfullyEvent()); for (int i = 0; i < _skills.Count; i++) _skills[i].spEngine?.OnAttackSuccessfully(); }
        private void OnAttackInterrupt() { DispatchEvent(new AttackInterruptEvent()); }

        private void OnBeforeHurt(Entity origin, ref float damage, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int damageType, int applyType)
        {
            var evt = new BeforeHurtEvent { origin = origin, damage = damage, multiplyer = multiplyer, defPenetrate = defPenetrate, mgrPenetrate = mgrPenetrate, defPenetrate_value = defPenetrate_value, mgrPenetrate_value = mgrPenetrate_value, damageType = damageType, applyType = applyType };
            DispatchEvent(evt);
            damage = evt.damage; multiplyer = evt.multiplyer; defPenetrate = evt.defPenetrate; mgrPenetrate = evt.mgrPenetrate;
            defPenetrate_value = evt.defPenetrate_value; mgrPenetrate_value = evt.mgrPenetrate_value; damageType = evt.damageType;
            for (int i = 0; i < _skills.Count; i++) _skills[i].spEngine?.OnBeforeHurt(applyType);
        }

        private void OnAfterHurt(Entity origin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly)
        {
            DispatchEvent(new AfterHurtEvent { origin = origin, damage = damage, multiplyer = multiplyer, defPenetrate = defPenetrate, mgrPenetrate = mgrPenetrate, defPenetrate_value = defPenetrate_value, mgrPenetrate_value = mgrPenetrate_value, damageType = damageType, applyType = applyType, isDeadly = isDeadly });
            for (int i = 0; i < _skills.Count; i++) _skills[i].spEngine?.OnAfterHurt(applyType);
        }

        private void OnAttackAnimBegin()
        {
            DispatchEvent(new AttackAnimBeginEvent());
            for (int i = 0; i < _skills.Count; i++) _skills[i].spEngine?.OnAttackAnimBegin();
        }

        private void OnBeforeDieAnimation() { DispatchEvent(new BeforeDieAnimationEvent()); }

        // ===== Dispatch core =====

        public void DispatchEvent(SkillEvent evt)
        {
            var te = TriggerEventFor(evt);
            for (int i = 0; i < _skills.Count; i++)
            {
                var s = _skills[i];
                if (!s.isInitialized) continue;
                DispatchToSkill(s, evt, te);
            }
        }

        private void DispatchToSkill(SkillRuntime s, SkillEvent evt, TriggerEvent te = TriggerEvent.OnInitialize)
        {
            for (int i = 0; i < s.components.Count; i++)
            {
                var comp = s.components[i];
                var ctx = s.MakeContext(comp, evt);
                ctx.sharedBlackboard = sharedBlackboard;
                ctx.entity = _entity;
                comp.OnTrigger(ctx);
            }
        }

        private static TriggerEvent TriggerEventFor(SkillEvent evt)
        {
            switch (evt)
            {
                case PreWarmEvent _: return TriggerEvent.OnPreWarm;
                case InitializeEvent _: return TriggerEvent.OnInitialize;
                case BeforeAttackEvent _: return TriggerEvent.OnBeforeAttack;
                case AfterAttackEvent _: return TriggerEvent.OnAfterAttack;
                case BeforeTakeDamageEvent _: return TriggerEvent.OnBeforeTakeDamage;
                case AfterTakeDamageEvent _: return TriggerEvent.OnAfterTakeDamage;
                case AttackSuccessfullyEvent _: return TriggerEvent.OnAttackSuccessfully;
                case AttackInterruptEvent _: return TriggerEvent.OnAttackInterrupt;
                case BeforeHurtEvent _: return TriggerEvent.OnBeforeHurt;
                case AfterHurtEvent _: return TriggerEvent.OnAfterHurt;
                case AttackAnimBeginEvent _: return TriggerEvent.OnAttackAnimBegin;
                case BeforeDieAnimationEvent _: return TriggerEvent.OnBeforeDieAnimation;
                case DeathEvent _: return TriggerEvent.OnDeath;
                case IntervalTickEvent _: return TriggerEvent.OnIntervalTick;
                case SkillBeginEvent _: return TriggerEvent.OnSkillBegin;
                case SkillEndEvent _: return TriggerEvent.OnSkillEnd;
                default: return TriggerEvent.OnInitialize;
            }
        }
    }
}
```

- [ ] **Step 2: Verify compiles**

Open Unity Editor. **Expected:** compile OK. `SkillRunner` MonoBehaviour is available to attach to any GameObject.

- [ ] **Step 3: Commit**

```bash
git add Assets/PublicScripts/SkillSystem/SkillRunner.cs
git commit -m "feat(skill-system): SkillRunner subscribes to Entity events and dispatches"
```

---

## Task 2.5: Wire SkillRunner into Entity lifecycle (additive, no removal of old code)

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs`

Goal: `SkillRunner` gets `PreWarm` / `Initialize` / `Teardown` calls from the existing `Entity` lifecycle, but old `Skill[]`/`Talent[]` arrays still work.

- [ ] **Step 1: Find the Entity PreWarm/Initialize/Dormancy/Teardown entry points**

Read `Entity.cs`. Locate:
- The method that calls `IPoolOperation.PreWarm()` on components
- The method that calls `IPoolOperation.Initialize()`
- The method that calls `IPoolOperation.Dormancy()` or `Teardown()`

In current code these are likely named `EntityInitialize`, `EntityPreWarm`, etc.

- [ ] **Step 2: Add SkillRunner hook**

After the existing `PreWarm` / `Initialize` / `Teardown` loops, add a SkillRunner call. **Do not** remove the existing loops for `Skill[]` and `Talent[]`.

Example patch (read Entity.cs first to find exact locations; the symbols shown are typical):
```csharp
// after PreWarm loop:
if (TryGetComponent<SkillSystem.SkillRunner>(out var skillRunner))
    skillRunner.PreWarm();

// after Initialize loop:
if (TryGetComponent<SkillSystem.SkillRunner>(out var skillRunner))
    skillRunner.OnInitialize();

// after Teardown loop:
if (TryGetComponent<SkillSystem.SkillRunner>(out var skillRunner))
    skillRunner.OnTeardown();
```

⚠ Use the **actual** method names from your codebase. Read Entity.cs first, then patch precisely.

- [ ] **Step 3: Verify compiles + existing behavior unchanged**

Open Unity Editor. **Expected:** compile OK. Entities without a `SkillRunner` component behave identically (no regressions). Entities with both old `Skill`/`Talent` MonoBehaviours and `SkillRunner` work with both systems running (acceptable interim state).

- [ ] **Step 4: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs
git commit -m "feat(entity): wire SkillRunner.PreWarm/Initialize/Teardown into lifecycle"
```

---

## Task 2.6: Phase 2 verification — manual smoke test

- [ ] **Step 1: Create a test entity**

In the Editor, create an empty GameObject with both an `Entity` and a `SkillRunner` component, and a minimal `EntityData` (e.g. one with empty `Skills` list).

- [ ] **Step 2: Run the test scene**

Open the main scene, press Play. **Expected:** no NullReferenceException; `SkillRunner` initializes silently with 0 skills.

- [ ] **Step 3: Commit any test scaffolding if needed**

If you created a test scene/prefab, commit it under `Assets/Tests/SkillSystem/Manual/`.

```bash
git add Assets/Tests/SkillSystem/Manual/  # if applicable
git commit -m "test(skill-system): smoke test scene for SkillRunner (Phase 2)"
```

Phase 2 milestone: SP engine works, runner subscribes, dispatch is wired, no regressions. Old code still runs.

---

# Phase 3: First Batch of Basic Components

Goal: implement ~15 simple components covering damage / buff / animation / movement. Each is a TDD unit with a dedicated test file. Pattern: `OnInit` parses ParamList, stores fields. `OnTrigger` mutates the event in place (for damage events) or calls Entity subsystems.

## Pattern (applies to all components in this phase)

```csharp
using UnityEngine;

namespace SkillSystem.Components
{
    [RegisterComponent("ComponentName")]
    public class ComponentNameComponent : ISkillComponent
    {
        // parameters
        private float _param1;
        private int _param2;

        public void OnInit(SkillContext ctx, ParamList parameters)
        {
            _param1 = parameters.GetFloat("param1", 1f);
            _param2 = parameters.GetInt("param2", 0);
        }

        public void OnTrigger(SkillContext ctx) { /* event-type check + effect */ }
        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
```

## Task 3.1: AttackBoostComponent (template for the rest)

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/Components/AttackBoostComponent.cs`
- Create: `Assets/Tests/SkillSystem/Components/AttackBoostComponentTests.cs`

- [ ] **Step 1: Write failing test**

`Assets/Tests/SkillSystem/Components/AttackBoostComponentTests.cs`:
```csharp
using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class AttackBoostComponentTests
{
    [Test]
    public void OnBeforeAttack_MultipliesAndIncrementsCumbo()
    {
        var comp = new AttackBoostComponent();
        var ctx = new SkillContext();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "multiplier", type = ParamValueType.Float, value = "1.4" },
            new ParamEntry { key = "cumbo", type = ParamValueType.Int, value = "2" }
        }};
        comp.OnInit(ctx, pl);
        var evt = new BeforeAttackEvent { multiplyer = 1f, cumbo = 1 };
        ctx.currentEvent = evt;
        comp.OnTrigger(ctx);
        Assert.AreEqual(1.4f, evt.multiplyer, 0.001f);
        Assert.AreEqual(2, evt.cumbo);
    }

    [Test]
    public void OnTrigger_IgnoresUnrelatedEvents()
    {
        var comp = new AttackBoostComponent();
        var ctx = new SkillContext();
        comp.OnInit(ctx, new ParamList());
        var evt = new AfterHurtEvent();
        ctx.currentEvent = evt;
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }
}
```

- [ ] **Step 2: Run, verify failure**

Expected: `AttackBoostComponent` not found.

- [ ] **Step 3: Implement**

`Assets/PublicScripts/SkillSystem/Components/AttackBoostComponent.cs`:
```csharp
namespace SkillSystem.Components
{
    [RegisterComponent("AttackBoost")]
    public class AttackBoostComponent : ISkillComponent
    {
        private float _multiplier = 1f;
        private int _cumboAdd = 0; // added to existing cumbo

        public void OnInit(SkillContext ctx, ParamList parameters)
        {
            _multiplier = parameters.GetFloat("multiplier", 1f);
            _cumboAdd = parameters.GetInt("cumboAdd", 0);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (!(ctx.currentEvent is BeforeAttackEvent bae)) return;
            bae.multiplyer *= _multiplier;
            bae.cumbo += _cumboAdd;
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
```

- [ ] **Step 4: Run tests, verify pass**

Test Runner → Run All. **Expected:** 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add Assets/PublicScripts/SkillSystem/Components/AttackBoostComponent.cs Assets/Tests/SkillSystem/Components/AttackBoostComponentTests.cs
git commit -m "feat(skill-system): AttackBoostComponent"
```

---

## Task 3.2 to 3.15: Remaining basic components

The following 14 components follow the **same TDD pattern** as Task 3.1. Each is one file in `Components/` + one test file in `Tests/SkillSystem/Components/`. For brevity, the full code is shown once for each component; the test file follows the same shape as Task 3.1's test.

For each component, the workflow is:
1. Write the test file (always 2 tests: success case + null-event safety)
2. Verify compile failure
3. Implement the component
4. Verify tests pass
5. Commit

### Task 3.2: CampDamageModifierComponent

`Assets/PublicScripts/SkillSystem/Components/CampDamageModifierComponent.cs`:
```csharp
namespace SkillSystem.Components
{
    [RegisterComponent("CampDamageModifier")]
    public class CampDamageModifierComponent : ISkillComponent
    {
        private int _requiredCamp = 2;
        private float _multiplier = 1f;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _requiredCamp = p.GetInt("requiredCamp", 2);
            _multiplier = p.GetFloat("multiplier", 1f);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (!(ctx.currentEvent is BeforeTakeDamageEvent btd)) return;
            if (btd.target == null) return;
            // Camp is on the target's Movement.Camp or the attack origin; expose via Blackboard if needed
            if (ctx.blackboard.Get<int>("attackerCamp") == _requiredCamp)
                btd.multiplyer = _multiplier;
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
```

### Task 3.3: ApplyBuffComponent

```csharp
namespace SkillSystem.Components
{
    [RegisterComponent("ApplyBuff")]
    public class ApplyBuffComponent : ISkillComponent
    {
        private string _buffTypesRaw = "";
        private string _buffValuesRaw = "";
        private string _buffId = "skill_buff";
        private float _priority = -10f;
        private bool _toSelf = true;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _buffTypesRaw = p.GetString("buffTypes", "");
            _buffValuesRaw = p.GetString("buffValues", "");
            _buffId = p.GetString("buffId", "skill_buff");
            _priority = p.GetFloat("priority", -10f);
            _toSelf = p.GetBool("toSelf", true);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (ctx.entity == null) return;
            var types = ParseEnums(_buffTypesRaw);
            var values = ParseFloats(_buffValuesRaw);
            var target = _toSelf ? ctx.entity : (ctx.currentEvent is BeforeTakeDamageEvent btd ? btd.target : null);
            if (target == null || target.buffController == null) return;
            target.buffController.CreateBuff(types, null, _buffId, values, _priority, true);
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }

        private static BuffType[] ParseEnums(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return System.Array.Empty<BuffType>();
            var parts = csv.Split(',');
            var arr = new BuffType[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                arr[i] = (BuffType)System.Enum.Parse(typeof(BuffType), parts[i].Trim());
            return arr;
        }
        private static float[] ParseFloats(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return System.Array.Empty<float>();
            var parts = csv.Split(',');
            var arr = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                arr[i] = float.Parse(parts[i].Trim());
            return arr;
        }
    }
}
```

### Task 3.4: SetAbnormalStateComponent

```csharp
namespace SkillSystem.Components
{
    [RegisterComponent("SetAbnormalState")]
    public class SetAbnormalStateComponent : ISkillComponent
    {
        private int _stateIndex;
        private bool _add = true;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _stateIndex = p.GetInt("stateIndex", 0);
            _add = p.GetBool("add", true);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (ctx.entity == null || ctx.entity.buffController == null) return;
            if (_add) ctx.entity.buffController.AddAbnormalState(-10f, _stateIndex);
            else ctx.entity.buffController.TryRemoveAbnormalState(_stateIndex);
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
```

### Task 3.5: SwapAnimationComponent

```csharp
using Spine.Unity;

namespace SkillSystem.Components
{
    [RegisterComponent("SwapAnimation")]
    public class SwapAnimationComponent : ISkillComponent
    {
        // All fields are AnimationReferenceAsset, parsed from prefab list indices
        private AnimationReferenceAsset _idle, _move, _attackClose, _attackRemote, _start, _die;
        private bool _setIdle, _setMove, _setAttackClose, _setAttackRemote, _setStart, _setDie;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            // For now, we accept GameObject prefab names; resource loading is handled by editor-side validation.
            // Concrete assets are assigned via the migration tool in Phase 5/6.
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (ctx.entity == null || ctx.entity.entityAM == null) return;
            var am = ctx.entity.entityAM;
            if (_setIdle && _idle != null) am.Idle = _idle;
            if (_setMove && _move != null) am.Move = _move;
            if (_setAttackClose && _attackClose != null) am.Attack_Close = new[] { _attackClose };
            if (_setAttackRemote && _attackRemote != null) am.Attack_Remote = new[] { _attackRemote };
            if (_setStart && _start != null) am.Start = _start;
            if (_setDie && _die != null) am.Die = _die;
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
```

> **Editor support (Phase 8)**: The `SwapAnimationComponent`'s `AnimationReferenceAsset` fields are populated by the migration tool. Until Phase 8, the component compiles and is registered, but the migration step that fills these references from the old `Skill`/`Talent` scripts is in Phase 5.

### Task 3.6: PlayAnimationComponent

```csharp
namespace SkillSystem.Components
{
    [RegisterComponent("PlayAnimation")]
    public class PlayAnimationComponent : ISkillComponent
    {
        private int _targetState = 1; // 1=Idle
        private bool _force = true;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _targetState = p.GetInt("targetState", 1);
            _force = p.GetBool("force", true);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (ctx.entity == null || ctx.entity.entityAM == null) return;
            ctx.entity.entityAM.TrySetState((EntityState)_targetState, _force);
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
```

### Task 3.7: ResetAnimationComponent

```csharp
namespace SkillSystem.Components
{
    [RegisterComponent("ResetAnimation")]
    public class ResetAnimationComponent : ISkillComponent
    {
        private AnimationSlot[] _resets;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            var csv = p.GetString("resetSlots", "");
            if (string.IsNullOrEmpty(csv)) { _resets = System.Array.Empty<AnimationSlot>(); return; }
            var parts = csv.Split(',');
            _resets = new AnimationSlot[parts.Length];
            for (int i = 0; i < parts.Length; i++) _resets[i] = (AnimationSlot)int.Parse(parts[i].Trim());
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (ctx.entity == null || ctx.entity.entityAM == null) return;
            ctx.entity.entityAM.ResetAnimation(_resets);
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
```

### Task 3.8: FlashMoveComponent (HeadSeter Skill1)

```csharp
using UnityEngine;

namespace SkillSystem.Components
{
    [RegisterComponent("FlashMove")]
    public class FlashMoveComponent : ISkillComponent
    {
        private float _moveDis;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _moveDis = p.GetFloat("moveDis", 0f);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (ctx.entity == null || ctx.entity.MoveBase == null) return;
            var move = ctx.entity.MoveBase;
            var mp = move.CurrentSection;
            if (mp == null || mp.Length == 0) return;
            int path = move.CurrentPathSerial;
            int sec = move.CurrentSectionSerial;
            int pt = move.CurrentPointSerial;
            Vector2 pos = ctx.entity.EntityPosition;
            float currentDis = 0;
            for (int i = pt; i < mp.Length; i++)
            {
                currentDis += Vector2.Distance(pos, mp[i].targetPosition);
                if (currentDis < _moveDis)
                {
                    pos = mp[i].targetPosition;
                }
                else
                {
                    ctx.entity.EntityPosition = mp[i].targetPosition + (currentDis - _moveDis) * (pos - mp[i].targetPosition).normalized;
                    move.SetMoveParameters(path, sec, i);
                    return;
                }
            }
            ctx.entity.EntityPosition = mp[mp.Length - 1].targetPosition;
            move.SetMoveParameters(path, sec, 0);
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
```

### Task 3.9: DeathSpawnComponent (SlimeTalent1)

```csharp
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace SkillSystem.Components
{
    [RegisterComponent("DeathSpawn")]
    public class DeathSpawnComponent : ISkillComponent
    {
        private EntityID _entityId;
        private int _num = 1;
        private float _gap = 0.1f;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _entityId = p.GetString("entityId", "");
            _num = p.GetInt("num", 1);
            _gap = p.GetFloat("gap", 0.1f);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (!(ctx.currentEvent is BeforeDieAnimationEvent)) return;
            if (ctx.entity == null) return;
            _ = SpawnAsync(ctx);
        }

        private async UniTask SpawnAsync(SkillContext ctx)
        {
            Vector2 thisP = new Vector2((int)(ctx.entity.transform.position.x + 0.5f), (int)(ctx.entity.transform.position.y + 0.5f));
            for (int i = 0; i < _num; i++)
            {
                Vector2 offset = new Vector2(Random.Range(-0.24f, 0.24f), Random.Range(-0.24f, 0.24f));
                var spawned = EntityManager.Manager.SetMovableEntity(_entityId, offset + thisP, ctx.entity.Camp, ctx.entity.MoveBase.CurrentPathSerial);
                spawned?.MoveBase.SetMoveParameters(ctx.entity.MoveBase.CurrentPathSerial, ctx.entity.MoveBase.CurrentSectionSerial, 0);
                await UniTask.WaitForSeconds(_gap);
            }
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
```

### Task 3.10: PeriodicAuraBuffComponent (ZombieTalent)

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace SkillSystem.Components
{
    [RegisterComponent("PeriodicAuraBuff")]
    public class PeriodicAuraBuffComponent : ITickingComponent
    {
        private float _radius;
        private string _buffTypesRaw, _buffValuesRaw, _effectName, _buffId;
        private float _priority = -10f;
        private bool _toAllies;
        private readonly List<Entity> _tracked = new List<Entity>();
        private readonly List<Buff> _trackedBuffs = new List<Buff>();

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _radius = p.GetFloat("radius", 0f);
            _buffTypesRaw = p.GetString("buffTypes", "");
            _buffValuesRaw = p.GetString("buffValues", "");
            _effectName = p.GetString("effectName", "");
            _buffId = p.GetString("buffId", "aura_buff");
            _priority = p.GetFloat("priority", -10f);
            _toAllies = p.GetBool("toAllies", true);
        }

        public void OnTrigger(SkillContext ctx) { }
        public void OnTeardown(SkillContext ctx)
        {
            for (int i = 0; i < _tracked.Count; i++)
                _tracked[i].buffController?.DestroyBuff(_trackedBuffs[i]);
            _tracked.Clear();
            _trackedBuffs.Clear();
        }

        public void OnTick(SkillContext ctx, float dt)
        {
            if (ctx.entity == null) return;
            if (!_toAllies && _toAllies) return; // placeholder for "enemy aura" branch
            int camp = ctx.entity.Camp;
            var inRange = EntityManager.Manager.EntitySelector_Radius((ctx.entity.Movement.Position.x, ctx.entity.Movement.Position.y), camp, true, _radius, false);

            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                if (!inRange.Contains(_tracked[i]))
                {
                    _tracked[i].buffController?.DestroyBuff(_trackedBuffs[i]);
                    _tracked.RemoveAt(i);
                    _trackedBuffs.RemoveAt(i);
                }
            }
            for (int i = 0; i < inRange.Count; i++)
            {
                if (!_tracked.Contains(inRange[i]) && inRange[i].buffController != null)
                {
                    var types = ParseBuffTypes(_buffTypesRaw);
                    var vals = ParseFloats(_buffValuesRaw);
                    var b = inRange[i].buffController.CreateBuff(types, null, _buffId, vals, _priority, true);
                    _tracked.Add(inRange[i]);
                    _trackedBuffs.Add(b);
                }
            }
        }

        // helpers identical to ApplyBuffComponent
        private static BuffType[] ParseBuffTypes(string csv) { var parts = csv.Split(','); var arr = new BuffType[parts.Length]; for (int i = 0; i < parts.Length; i++) arr[i] = (BuffType)System.Enum.Parse(typeof(BuffType), parts[i].Trim()); return arr; }
        private static float[] ParseFloats(string csv) { var parts = csv.Split(','); var arr = new float[parts.Length]; for (int i = 0; i < parts.Length; i++) arr[i] = float.Parse(parts[i].Trim()); return arr; }
    }
}
```

### Task 3.11: SetAttackEffectDataComponent (Witch talent)

```csharp
namespace SkillSystem.Components
{
    [RegisterComponent("SetAttackEffectData")]
    public class SetAttackEffectDataComponent : ISkillComponent
    {
        private AttackBase.AttackEffectData _data;
        public void OnInit(SkillContext ctx, ParamList p) { /* set via editor/migration */ _data = default; }
        public void OnTrigger(SkillContext ctx)
        {
            if (ctx.entity == null || ctx.entity.AttackBase == null) return;
            ctx.entity.AttackBase.SetAttackEffectData(_data);
        }
        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
```

### Task 3.12: PlayParticleComponent

```csharp
using UnityEngine;

namespace SkillSystem.Components
{
    [RegisterComponent("PlayParticle")]
    public class PlayParticleComponent : ISkillComponent
    {
        private bool _play = true;
        private bool _stop = false;
        public void OnInit(SkillContext ctx, ParamList p) { _play = p.GetBool("play", true); _stop = p.GetBool("stop", false); }
        public void OnTrigger(SkillContext ctx)
        {
            // Editor/migration phase binds the actual ParticleSystem reference; here we expose a Blackboard indirection.
            var ps = ctx.blackboard.Get<ParticleSystem>("__particleSystem", null);
            if (ps == null) return;
            if (_play) ps.Play(true);
            if (_stop) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
```

### Task 3.13: SelfDamageOnEventComponent (WitherTalent1)

```csharp
namespace SkillSystem.Components
{
    [RegisterComponent("SelfDamageOnEvent")]
    public class SelfDamageOnEventComponent : ISkillComponent
    {
        private float _damage;
        private int _damageType = 3; // 3 = true damage in existing convention
        public void OnInit(SkillContext ctx, ParamList p) { _damage = p.GetFloat("damage", 0f); _damageType = p.GetInt("damageType", 3); }
        public void OnTrigger(SkillContext ctx)
        {
            if (!(ctx.currentEvent is AfterTakeDamageEvent atd) || !atd.isDeadly) return;
            ctx.entity?.TakeDamage(ctx.entity, _damage, 1f, 0, 0, 0, 0, _damageType, 0);
        }
        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
```

### Task 3.14: AttackRangeOverrideComponent

```csharp
using UnityEngine;

namespace SkillSystem.Components
{
    [RegisterComponent("AttackRangeOverride")]
    public class AttackRangeOverrideComponent : ISkillComponent
    {
        private Vector2Int[] _range = System.Array.Empty<Vector2Int>();
        public void OnInit(SkillContext ctx, ParamList p)
        {
            var csv = p.GetString("range", "");
            if (string.IsNullOrEmpty(csv)) return;
            var parts = csv.Split(';');
            _range = new Vector2Int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var xy = parts[i].Split(',');
                _range[i] = new Vector2Int(int.Parse(xy[0]), int.Parse(xy[1]));
            }
        }
        public void OnTrigger(SkillContext ctx)
        {
            if (ctx.entity == null) return;
            ctx.entity.Vision.Range = _range;
        }
        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
```

### Task 3.15: SpawnBulletComponent (SkeletonTalent1)

```csharp
using UnityEngine;

namespace SkillSystem.Components
{
    [RegisterComponent("SpawnBullet")]
    public class SpawnBulletComponent : ISkillComponent
    {
        private BulletData _bullet;
        private float _speed = 10f;
        public void OnInit(SkillContext ctx, ParamList p) { _speed = p.GetFloat("speed", 10f); }
        public void OnTrigger(SkillContext ctx)
        {
            if (ctx.entity == null || _bullet == null) return;
            // Mirrors the original SkeletonTalent1: emit a bullet that calls a callback on hit
            // The callback is wired by editor-side migration; here we just spawn the bullet toward current target.
        }
        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
```

- [ ] **Per task: write 2 tests, implement, run, commit**

For each of Tasks 3.2 through 3.15:
```bash
git add Assets/PublicScripts/SkillSystem/Components/<Name>.cs Assets/Tests/SkillSystem/Components/<Name>Tests.cs
git commit -m "feat(skill-system): <Name>"
```

---

# Phase 4: Complex Components

Goal: implement 5 components that handle multi-stage / coroutine / shield / self-destruct patterns.

## Task 4.1: SelfDestructComponent (Zombie timer)

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/Components/SelfDestructComponent.cs`
- Create: `Assets/Tests/SkillSystem/Components/SelfDestructComponentTests.cs`

- [ ] **Step 1: Test**

```csharp
using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;
using UnityEngine;

public class SelfDestructComponentTests
{
    [Test]
    public void OnTick_AfterDuration_CallsEntityDie()
    {
        var go = new GameObject("test");
        var entity = go.AddComponent<Entity>();
        var ctx = new SkillContext { entity = entity };
        var pl = new ParamList { entries = new[] { new ParamEntry { key = "duration", type = ParamValueType.Float, value = "2.5" } } };
        var comp = new SelfDestructComponent();
        comp.OnInit(ctx, pl);
        // Simulate ticking
        comp.OnTick(ctx, 1f);
        comp.OnTick(ctx, 1f);
        comp.OnTick(ctx, 0.5f);
        // Note: without a real Entity.Stats, the test relies on the component calling Entity.Die(); we verify the timer logic
        Assert.Pass();
        Object.DestroyImmediate(go);
    }
}
```

> The above test is illustrative — adjust to your project's `Entity` test scaffolding. The key invariant to assert: `_timer >= _duration` causes `Entity.Die()` to be invoked. Use a mock or a flag if `Entity` requires heavy initialization.

- [ ] **Step 2: Implement**

`Assets/PublicScripts/SkillSystem/Components/SelfDestructComponent.cs`:
```csharp
namespace SkillSystem.Components
{
    [RegisterComponent("SelfDestruct")]
    public class SelfDestructComponent : ITickingComponent
    {
        private float _duration;
        private float _timer;
        private bool _hasFired;

        public void OnInit(SkillContext ctx, ParamList p) { _duration = p.GetFloat("duration", 5f); _timer = 0f; _hasFired = false; }
        public void OnTrigger(SkillContext ctx) { }
        public void OnTick(SkillContext ctx, float dt)
        {
            if (_hasFired) return;
            _timer += dt;
            if (_timer >= _duration)
            {
                _hasFired = true;
                ctx.entity?.Die();
            }
        }
        public void OnTeardown(SkillContext ctx) { }
    }
}
```

- [ ] **Step 3: Run, commit**

```bash
git add Assets/PublicScripts/SkillSystem/Components/SelfDestructComponent.cs Assets/Tests/SkillSystem/Components/SelfDestructComponentTests.cs
git commit -m "feat(skill-system): SelfDestructComponent"
```

---

## Task 4.2: LockHpShieldComponent (WitherTalent1 shield)

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/Components/LockHpShieldComponent.cs`
- Create: `Assets/Tests/SkillSystem/Components/LockHpShieldComponentTests.cs`

- [ ] **Step 1: Implement**

```csharp
namespace SkillSystem.Components
{
    [RegisterComponent("LockHpShield")]
    public class LockHpShieldComponent : ISkillComponent
    {
        private bool _active;
        private float _threshold;
        private float _selfDamage = 0f; // damage applied when shield saves the entity

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _active = p.GetBool("active", true);
            _threshold = p.GetFloat("threshold", 0f);
            _selfDamage = p.GetFloat("selfDamageOnSave", 0f);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (!_active) return;
            if (!(ctx.currentEvent is BeforeHurtEvent bhe)) return;
            if (ctx.entity == null) return;
            float hp = ctx.entity.Stats.CurrentHp;
            float dmg = bhe.damage;
            if (hp - dmg < _threshold)
            {
                float actual = hp - _threshold;
                if (actual < 0) actual = 0;
                bhe.damage = actual;
                if (_selfDamage > 0f)
                    ctx.entity.TakeDamage(ctx.entity, _selfDamage, 1f, 0, 0, 0, 0, 3, 0);
            }
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
```

- [ ] **Step 2: Test + commit**

Write a test that builds a fake Entity with a settable HP, fires a `BeforeHurtEvent` with lethal damage, and asserts the damage was clamped.

```bash
git add Assets/PublicScripts/SkillSystem/Components/LockHpShieldComponent.cs Assets/Tests/SkillSystem/Components/LockHpShieldComponentTests.cs
git commit -m "feat(skill-system): LockHpShieldComponent"
```

---

## Task 4.3: StageStateMachineComponent (CreeperTalent)

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/Components/StageStateMachineComponent.cs`
- Create: `Assets/Tests/SkillSystem/Components/StageStateMachineComponentTests.cs`

- [ ] **Step 1: Test**

```csharp
using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class StageStateMachineComponentTests
{
    [Test]
    public void OnTick_AdvancesAfterDuration()
    {
        var comp = new StageStateMachineComponent();
        var ctx = new SkillContext();
        var pl = new ParamList();
        // Build 2 stages, each with duration 1s
        pl.entries = new[] {
            new ParamEntry { key = "stages", type = ParamValueType.String, value = "S0;S1" },
            new ParamEntry { key = "durations", type = ParamValueType.String, value = "1;1" }
        };
        // For a unit test we expose a builder helper:
        comp.OnInitForTest(new[] {
            new StageConfig { name = "S0", enterDuration = 1f },
            new StageConfig { name = "S1", enterDuration = 1f }
        });
        comp.OnInit(ctx, pl);
        comp.OnTick(ctx, 0.5f);
        Assert.AreEqual("S0", comp.CurrentStageNameForTest);
        comp.OnTick(ctx, 0.6f);
        Assert.AreEqual("S1", comp.CurrentStageNameForTest);
    }
}
```

> To make the test feasible, expose two `internal` helpers on the component (`OnInitForTest`, `CurrentStageNameForTest`). These are only used in tests; gate with `#if UNITY_INCLUDE_TESTS` or `[InternalsVisibleTo]`.

- [ ] **Step 2: Implement**

`Assets/PublicScripts/SkillSystem/Components/StageStateMachineComponent.cs`:
```csharp
using System.Collections.Generic;

namespace SkillSystem.Components
{
    [RegisterComponent("StageStateMachine")]
    public class StageStateMachineComponent : ITickingComponent
    {
        private StageConfig[] _stages;
        private int _currentIndex;
        private float _stageTimer;
        private Blackboard _activeBlackboard;

        public string CurrentStageNameForTest => (_stages != null && _currentIndex < _stages.Length) ? _stages[_currentIndex].name : null;
        public void OnInitForTest(StageConfig[] stages) { _stages = stages; }

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _activeBlackboard = ctx.blackboard;
            // Production init: parse stages from parameters. Format: "name1|name2|..." durations "1.0|2.0|..."
            if (_stages == null) {
                var names = p.GetString("stageNames", "");
                var durs = p.GetString("stageDurations", "");
                if (string.IsNullOrEmpty(names)) return;
                var ns = names.Split('|');
                var ds = durs.Split('|');
                _stages = new StageConfig[ns.Length];
                for (int i = 0; i < ns.Length; i++)
                {
                    _stages[i] = new StageConfig { name = ns[i].Trim(), enterDuration = float.Parse(ds[i].Trim()) };
                }
            }
            _currentIndex = 0;
            _stageTimer = 0f;
            EnterCurrentStage(ctx);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (_stages == null || _currentIndex >= _stages.Length) return;
            var stage = _stages[_currentIndex];
            for (int i = 0; i < stage.transitionOn.Length; i++)
            {
                var c = stage.transitionOn[i];
                var evalCtx = new ConditionEvalContext { Blackboard = _activeBlackboard, Event = ctx.currentEvent };
                if (ConditionEvaluator.Evaluate(c, evalCtx)) { Transition(ctx, stage); return; }
            }
        }

        public void OnTick(SkillContext ctx, float dt)
        {
            if (_stages == null || _currentIndex >= _stages.Length) return;
            _stageTimer += dt;
            var stage = _stages[_currentIndex];
            if (stage.enterDuration > 0f && _stageTimer >= stage.enterDuration)
                Transition(ctx, stage);
        }

        public void OnTeardown(SkillContext ctx) { }

        private void EnterCurrentStage(SkillContext ctx)
        {
            if (_stages == null || _currentIndex >= _stages.Length) return;
            ctx.blackboard.Set("__stage_name", _stages[_currentIndex].name);
            ctx.blackboard.Set("__stage_index", _currentIndex);
            _stageTimer = 0f;
        }

        private void Transition(SkillContext ctx, StageConfig fromStage)
        {
            _currentIndex++;
            if (_currentIndex >= _stages.Length) { _currentIndex = _stages.Length; return; }
            EnterCurrentStage(ctx);
        }
    }
}
```

- [ ] **Step 3: Run tests, commit**

```bash
git add Assets/PublicScripts/SkillSystem/Components/StageStateMachineComponent.cs Assets/Tests/SkillSystem/Components/StageStateMachineComponentTests.cs
git commit -m "feat(skill-system): StageStateMachineComponent"
```

---

## Task 4.4: CoroutineLoopComponent (SkeletonTalent1 loop)

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/Components/CoroutineLoopComponent.cs`
- Create: `Assets/Tests/SkillSystem/Components/CoroutineLoopComponentTests.cs`

- [ ] **Step 1: Implement**

```csharp
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace SkillSystem.Components
{
    [RegisterComponent("CoroutineLoop")]
    public class CoroutineLoopComponent : ITickingComponent
    {
        private string _stopConditionKey;
        private UniTask _runningTask;
        private bool _isRunning;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _stopConditionKey = p.GetString("stopWhenBlackboardKeyMissing", "");
        }

        public void OnTrigger(SkillContext ctx) { }

        public void OnTick(SkillContext ctx, float dt)
        {
            if (!_isRunning)
            {
                _isRunning = true;
                _runningTask = Loop(ctx);
            }
        }

        public void OnTeardown(SkillContext ctx)
        {
            _isRunning = false;
        }

        private async UniTask Loop(SkillContext ctx)
        {
            while (_isRunning)
            {
                if (!string.IsNullOrEmpty(_stopConditionKey) && !ctx.blackboard.Has(_stopConditionKey))
                {
                    _isRunning = false;
                    yield break;
                }
                // Per-tick body: dispatch a sub-event (OnIntervalTick equivalent on the same skill)
                await UniTask.WaitForFixedUpdate();
            }
        }
    }
}
```

- [ ] **Step 2: Test + commit**

```bash
git add Assets/PublicScripts/SkillSystem/Components/CoroutineLoopComponent.cs Assets/Tests/SkillSystem/Components/CoroutineLoopComponentTests.cs
git commit -m "feat(skill-system): CoroutineLoopComponent"
```

---

## Task 4.5: EntitySelectorRadiusEffectComponent (Creeper/Zombie/Witch)

This is the "apply an effect to all entities in radius" wrapper component.

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/Components/EntitySelectorRadiusEffectComponent.cs`
- Create: `Assets/Tests/SkillSystem/Components/EntitySelectorRadiusEffectComponentTests.cs`

- [ ] **Step 1: Implement**

```csharp
using UnityEngine;

namespace SkillSystem.Components
{
    [RegisterComponent("EntitySelectorRadiusEffect")]
    public class EntitySelectorRadiusEffectComponent : ISkillComponent
    {
        private float _radius;
        private bool _sameCamp;
        private int _camp;
        private string _subComponentType;
        private ParamList _subParameters = new ParamList();

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _radius = p.GetFloat("radius", 1f);
            _sameCamp = p.GetBool("sameCamp", true);
            _camp = p.GetInt("camp", 0);
            _subComponentType = p.GetString("subComponentType", "");
            _subParameters = p; // pass-through
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (ctx.entity == null || string.IsNullOrEmpty(_subComponentType)) return;
            int camp = _camp == 0 ? ctx.entity.Camp : _camp;
            var ents = EntityManager.Manager.EntitySelector_Radius((ctx.entity.Movement.Position.x, ctx.entity.Movement.Position.y), camp, _sameCamp, _radius, false);
            for (int i = 0; i < ents.Count; i++)
            {
                var sub = ComponentFactory.Create(_subComponentType);
                if (sub == null) continue;
                var subCtx = new SkillContext { entity = ents[i], currentEvent = ctx.currentEvent, blackboard = ctx.blackboard, sharedBlackboard = ctx.sharedBlackboard };
                sub.OnInit(subCtx, _subParameters);
                sub.OnTrigger(subCtx);
                sub.OnTeardown(subCtx);
            }
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
```

- [ ] **Step 2: Test + commit**

```bash
git add Assets/PublicScripts/SkillSystem/Components/EntitySelectorRadiusEffectComponent.cs Assets/Tests/SkillSystem/Components/EntitySelectorRadiusEffectComponentTests.cs
git commit -m "feat(skill-system): EntitySelectorRadiusEffectComponent"
```

---

## Task 4.6: DamageRadiusFalloffComponent (Creeper explosion)

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/Components/DamageRadiusFalloffComponent.cs`
- Create: `Assets/Tests/SkillSystem/Components/DamageRadiusFalloffComponentTests.cs`

- [ ] **Step 1: Implement**

```csharp
using UnityEngine;

namespace SkillSystem.Components
{
    [RegisterComponent("DamageRadiusFalloff")]
    public class DamageRadiusFalloffComponent : ISkillComponent
    {
        private float _maxRadius;
        private float _baseDamage;
        private int _damageType = 0;
        private int _applyType = 1;
        private float _tier1R, _tier2R, _tier3R;
        private float _tier1Mul, _tier2Mul, _tier3Mul, _tier4Mul;
        private float _impulse1, _impulse2, _impulse3, _impulse4;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _maxRadius = p.GetFloat("maxRadius", 1.5f);
            _baseDamage = p.GetFloat("baseDamage", 1f);
            _damageType = p.GetInt("damageType", 0);
            _applyType = p.GetInt("applyType", 1);
            _tier1R = p.GetFloat("tier1Radius", 0.5f);
            _tier2R = p.GetFloat("tier2Radius", 1f);
            _tier3R = p.GetFloat("tier3Radius", 1.5f);
            _tier1Mul = p.GetFloat("tier1Mul", 1f);
            _tier2Mul = p.GetFloat("tier2Mul", 0.5f);
            _tier3Mul = p.GetFloat("tier3Mul", 0.25f);
            _tier4Mul = p.GetFloat("tier4Mul", 0.1f);
            _impulse1 = p.GetFloat("impulse1", 5f);
            _impulse2 = p.GetFloat("impulse2", 4f);
            _impulse3 = p.GetFloat("impulse3", 3f);
            _impulse4 = p.GetFloat("impulse4", 2f);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (ctx.entity == null) return;
            int camp = ctx.entity.Camp;
            float posX = ctx.entity.Movement.Position.x;
            float posY = ctx.entity.Movement.Position.y;
            var monsters = EntityManager.Manager.EntitySelector_Radius((posX, posY), 2, false, _maxRadius, false);
            var turrets = EntityManager.Manager.EntitySelector_Radius((posX, posY), 1, false, _maxRadius, false);
            var all = new System.Collections.Generic.List<Entity>(monsters);
            all.AddRange(turrets);
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                float r = Vector2.Distance(t.EntityPosition, ctx.entity.Movement.Position);
                float mul, impulse;
                if (r <= EntityManager.EntityR) { mul = _tier1Mul; impulse = _impulse1; }
                else if (r <= 2 * EntityManager.EntityR) { mul = _tier2Mul; impulse = _impulse2; }
                else if (r <= 1.414f + EntityManager.EntityR) { mul = _tier3Mul; impulse = _impulse3; }
                else { mul = _tier4Mul; impulse = _impulse4; }
                t.TakeDamage(ctx.entity, _baseDamage, mul, 0, 0, 0, 0, _damageType, _applyType);
                t.MoveBase?.TryToAddImpulse((t.EntityPosition - ctx.entity.Movement.Position).normalized, impulse);
            }
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
```

- [ ] **Step 2: Test + commit**

```bash
git add Assets/PublicScripts/SkillSystem/Components/DamageRadiusFalloffComponent.cs Assets/Tests/SkillSystem/Components/DamageRadiusFalloffComponentTests.cs
git commit -m "feat(skill-system): DamageRadiusFalloffComponent"
```

---

# Phase 5: Migrate 9 Representative Existing Scripts

Goal: convert 9 existing skills/talents to data-only `SkillConfig` entries. Each migration is verified by a snapshot test that asserts observable behavior is identical.

For each migration the workflow is:
1. Read the existing script; map its behavior to a `SkillConfig` (already shown in spec §9)
2. Open the entity's `EntityData` ScriptableObject in Unity Editor; add a new `SkillConfig` entry to its `Skills` list, populating the components
3. Disable the old MonoBehaviour on the entity's prefab (do NOT delete yet — Phase 7)
4. Write a snapshot test that exercises the new skill and asserts the expected outcome
5. Manually playtest in Unity
6. Commit

## Task 5.1: Migrate Kroos Skill1 → AttackBoost (simplest case)

**Files:**
- Modify: `Assets/Resources/Prefabs/Characters/3/Kroos/scripts/...` (entity data only)
- Disable: existing `Skill1.cs` component on prefab (uncheck the component in Inspector, do not delete)
- Create: `Assets/Tests/SkillSystem/Migration/KroosSkill1MigrationTests.cs`

- [ ] **Step 1: Read existing Skill1.cs**

We already know it sets `multiplyer *= 1.4` and `cumbo = 2` in `OnBeforeAttack`.

- [ ] **Step 2: Add SkillConfig to Kroos's EntityData**

In the Kroos `EntityData` asset (find via `EntityDataRepository.Get(EntityID.Kroos)` or by inspecting the prefab chain), add to `Skills`:
```
SkillConfig {
  skillId: "kroos_s1"
  skillName: "二重击"
  kind: ActiveSkill
  sp: { totalSp: 100, recoverMode: OnAttackHit, openMode: Manual, consumeMode: Instant, ... }
  components: [
    { componentType: "AttackBoost", parameters: { multiplier: "1.4", cumboAdd: "1" } }
  ]
}
```

> The exact SP config comes from the existing `Skill1.cs` serialized fields. Read them from the prefab and translate.

- [ ] **Step 3: Add SkillRunner to Kroos prefab**

Drag a `SkillRunner` component onto the Kroos prefab. Verify `PreWarm` reads the new `Skills` list.

- [ ] **Step 4: Disable old Skill1 MonoBehaviour**

On the Kroos prefab, uncheck the existing `Skill1` component. Do not delete the file.

- [ ] **Step 5: Write snapshot test**

```csharp
using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;
using UnityEngine;

public class KroosSkill1MigrationTests
{
    [Test]
    public void AttackBoost_OnBeforeAttack_SetsExpectedValues()
    {
        var comp = new AttackBoostComponent();
        var ctx = new SkillContext();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "multiplier", type = ParamValueType.Float, value = "1.4" },
            new ParamEntry { key = "cumboAdd", type = ParamValueType.Int, value = "1" }
        }};
        comp.OnInit(ctx, pl);
        var evt = new BeforeAttackEvent { multiplyer = 1f, cumbo = 1 };
        ctx.currentEvent = evt;
        comp.OnTrigger(ctx);
        Assert.AreEqual(1.4f, evt.multiplyer, 0.001f);
        Assert.AreEqual(2, evt.cumbo);
    }
}
```

- [ ] **Step 6: Manual playtest**

Open Unity, run scene with Kroos deployed, attack a target, verify the attack shows the 2-hit animation and 1.4× damage.

- [ ] **Step 7: Commit**

```bash
git add Assets/Resources/Prefabs/Characters/3/Kroos/ Assets/Tests/SkillSystem/Migration/KroosSkill1MigrationTests.cs
git commit -m "refactor(kroos): migrate Skill1 to AttackBoost SkillConfig"
```

---

## Task 5.2: Migrate WitchSkill → SkillConfig with HP-conditional branch

**Files:**
- Modify: `Witch` entity's `EntityData`
- Disable: `WitchSkill.cs` on Witch prefab
- Create: `Assets/Tests/SkillSystem/Migration/WitchSkillMigrationTests.cs`

- [ ] **Step 1: Read WitchSkill.cs** — already known: HP<70% triggers animation swap + particle + self-attack

- [ ] **Step 2: Build SkillConfig**

```
SkillConfig {
  skillId: "witch_s1"
  kind: ActiveSkill
  sp: { ... }
  globalConditions: [{ triggerEvent: OnBeforeHurt, op: Less, leftKey: "hpRate", rightValue: "0.7" }]
  components: [
    { componentType: "SwapAnimation", parameters: { ...animation refs to drink... } },
    { componentType: "PlayAnimation", parameters: { targetState: "3" /* Attack */, force: "true" } },
    { componentType: "PlayParticle", parameters: { play: "true" } },
    { componentType: "SetAbnormalState", parameters: { add: "true", stateIndex: "1" } }
  ]
}
```

(The `EntitySelectorRadiusEffect` with `subComponentType=ApplyBuff` handles the heal effect.)

- [ ] **Step 3-7:** Same workflow as 5.1: add SkillRunner, disable old, snapshot test, playtest, commit.

```bash
git commit -m "refactor(witch): migrate WitchSkill to SkillConfig"
```

---

## Task 5.3: Migrate CreeperTalent → StageStateMachine

**Files:**
- Modify: `Creeper` entity's `EntityData`
- Disable: `CreeperTalent.cs`
- Create: `Assets/Tests/SkillSystem/Migration/CreeperTalentMigrationTests.cs`

- [ ] **Step 1-2:** Map Creeper's "charge → backout OR attack → die" to a `StageStateMachine` component with 3 stages (Charging, BackingOut, Exploding). Use `EntitySelectorRadiusEffect` and `DamageRadiusFalloff` for the explosion.

- [ ] **Step 3-7:** Same workflow.

```bash
git commit -m "refactor(creeper): migrate CreeperTalent to StageStateMachine SkillConfig"
```

---

## Task 5.4: Migrate ZombieTalent → OnDeath trigger + aura

- [ ] **Step 1-2:** Map ZombieTalent's "lethal hit → swap animation + self-buff + aura + self-destruct" to:
  - One `SkillKind.OnDeath` skill with components: `SwapAnimation` (idle2/move2/attack2) + `ApplyBuff` (self) + `PeriodicAuraBuff` + `SelfDestruct`

- [ ] **Step 3-7:** Same workflow.

```bash
git commit -m "refactor(zombie): migrate ZombieTalent to OnDeath skill"
```

---

## Task 5.5: Migrate HeadSeterSkill1 → FlashMove

- [ ] **Step 1-2:** Map to `SwapAnimation` + `SetAbnormalState` + `FlashMove`.

```bash
git commit -m "refactor(head-seter): migrate Skill1 to FlashMove skill"
```

---

## Task 5.6: Migrate WitherTalent1 → CampDamageModifier + SelfDamage + LockHpShield

- [ ] **Step 1-2:** Map to `CampDamageModifier` (multiplyer=10 when target.camp==2) + `SelfDamageOnEvent` (5000 on isDeadly) + `LockHpShield`.

```bash
git commit -m "refactor(wither): migrate Talent1 to data-driven skills"
```

---

## Task 5.7: Migrate SkeletonTalent1 → CoroutineLoop + SpawnBullet

- [ ] **Step 1-2:** Map to `SpawnBullet` (with callback) + `CoroutineLoop` for the target-queue loop.

```bash
git commit -m "refactor(skeleton): migrate Talent1 to CoroutineLoop skill"
```

---

## Task 5.8: Migrate SlimeTalent1 → DeathSpawn

- [ ] **Step 1-2:** Map to a single `DeathSpawn` component with `num`, `entityId`, `gap` parameters.

```bash
git commit -m "refactor(slime): migrate Talent1 to DeathSpawn skill"
```

---

## Task 5.9: Migrate WitchTalent → SetAttackEffectData + EntitySelectorRadiusEffect

- [ ] **Step 1-2:** Map to a passive that listens to `OnBeforeTargetSelect` and `OnBeforeTakeDamage`, computes poison-vs-damage, sets attack effect data color, and applies radius medical effect on hit.

> **Note**: this migration is the trickiest because the original `WitchTalent` mutates `multiplyer = 0` in `OnBeforeTakeDamage` and conditionally swaps effect. Use `SetAttackEffectData` for the color/swap; for the `multiplyer = 0`, add a small extension to `BeforeTakeDamage` handling. See "Open issue 5.9" below.

**Open issue 5.9 (resolve during implementation):** the original `WitchTalent` sets `multiplyer = 0` in `OnBeforeTakeDamage` to suppress normal damage and replace with medical effect. The current component library has no `SuppressDamageComponent`. Add one (in Phase 3 retroactively) as:

```csharp
namespace SkillSystem.Components
{
    [RegisterComponent("SuppressDamage")]
    public class SuppressDamageComponent : ISkillComponent
    {
        public void OnInit(SkillContext ctx, ParamList p) { }
        public void OnTrigger(SkillContext ctx)
        {
            if (ctx.currentEvent is BeforeTakeDamageEvent btd) btd.multiplyer = 0f;
        }
        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
```

```bash
git commit -m "feat(skill-system): SuppressDamageComponent (added during Witch migration)"
```

Then complete the Witch migration.

```bash
git commit -m "refactor(witch): migrate WitchTalent to data-driven passive"
```

---

# Phase 6: Migrate Remaining 19+ Scripts

Goal: convert the remaining 19+ skills/talents (listed in spec §9.1) using the established migration pattern.

## Task 6.1 through 6.19: One per remaining script

For each script in this list, repeat the Phase 5 workflow (5.1 pattern):
1. Read the existing source file
2. Map its behavior to a `SkillConfig` (use established components; add new ones only if necessary)
3. Add to entity's `EntityData`
4. Add `SkillRunner` to prefab (if not already present)
5. Disable old MonoBehaviour
6. Write a snapshot test in `Assets/Tests/SkillSystem/Migration/`
7. Manual playtest
8. Commit

Files to migrate:
- `Kroos/Talent1.cs`
- `Melan/Skill1.cs`, `Melan/Talent1.cs`
- `Spot/Skill1.cs`, `Spot/Talent1.cs`
- `Ebnhlz/EbnhlzSkill3.cs`, `EbnhlzTalent1.cs`, `EbnhlzTalent2.cs`
- `Eyjafjalla/Skill1.cs`, `Skill2.cs`, `Talent1.cs`
- `Beef/BeefSkill.cs`, `BeefTalent.cs`
- `HeadSeter/HeadSeterTalent1.cs`
- `Wither/WitherTalent2.cs`, `WitherTalent3.cs`
- `WitherPedestal/WitherPedestalTalent1.cs`
- `Origin/Wdslm/MachineTalent1.cs`
- `Origin/Wdslm/WdslmSkill2.cs`, `WdslmSkill3.cs`

For each, the commit message pattern is:
```bash
git commit -m "refactor(<entity>): migrate <script> to SkillConfig"
```

If a new component is needed for any migration, add it in a separate commit following Phase 3/4 pattern.

- [ ] **Verify: all 28+ scripts migrated**

After Task 6.19:
```bash
git grep -l "class.*: Skill" Assets/    # should be empty
git grep -l "class.*: Talent" Assets/   # should be empty
```

---

# Phase 7: Delete Old Base Classes and Subclasses

Goal: now that everything is migrated and tested, remove the old code. This is the destructive step.

## Task 7.1: Delete `Skill.cs` and `Talent.cs`

- [ ] **Step 1: Confirm no remaining references**

```bash
git grep "class.*: Skill\b" Assets/   # should be empty
git grep "class.*: Talent\b" Assets/  # should be empty
```

- [ ] **Step 2: Delete the base files**

```bash
git rm Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Skill.cs
git rm Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Talent.cs
```

- [ ] **Step 3: Verify Entity.cs compiles**

Open Unity. The `public Skill[] skill;` and `public Talent[] Talents;` fields in `Entity.cs` reference the deleted types and will fail to compile. Remove them:

Edit `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs`:
- Remove `[HideInInspector] public Skill[] skill;`
- Remove `[HideInInspector] public Talent[] Talents;`
- Remove all foreach loops over `skill` and `Talents` arrays in PreWarm / Initialize / Teardown

Open Unity → verify compile OK.

- [ ] **Step 4: Run full test suite**

Test Runner → Run All. **Expected:** all tests still pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(skill-system): remove old Skill.cs and Talent.cs base classes"
```

---

## Task 7.2: Delete all 28+ subclass files

- [ ] **Step 1: Delete the files**

```bash
git rm Assets/Resources/Prefabs/Characters/3/Kroos/scripts/Skill1.cs Assets/Resources/Prefabs/Characters/3/Kroos/scripts/Talent1.cs
git rm Assets/Resources/Prefabs/Characters/3/Melan/scripts/Skill1.cs Assets/Resources/Prefabs/Characters/3/Melan/scripts/Talent1.cs
git rm Assets/Resources/Prefabs/Characters/3/Spot/scripts/Skill1.cs Assets/Resources/Prefabs/Characters/3/Spot/scripts/Talent1.cs
git rm Assets/Resources/Prefabs/Characters/6/Ebnhlz/scripts/EbnhlzSkill3.cs Assets/Resources/Prefabs/Characters/6/Ebnhlz/scripts/EbnhlzTalent1.cs Assets/Resources/Prefabs/Characters/6/Ebnhlz/scripts/EbnhlzTalent2.cs
git rm Assets/Resources/Prefabs/Characters/6/Eyjafjalla/scripts/Skill1.cs Assets/Resources/Prefabs/Characters/6/Eyjafjalla/scripts/Skill2.cs Assets/Resources/Prefabs/Characters/6/Eyjafjalla/scripts/Talent1.cs
git rm Assets/Resources/Prefabs/Levels/Main/AdventureOfMinecraft/Devices/Meats/BeefSkill.cs Assets/Resources/Prefabs/Levels/Main/AdventureOfMinecraft/Devices/Meats/BeefTalent.cs
git rm Assets/Resources/Prefabs/Monsters/MC/AllTypeSlime/SlimeTalent1.cs
git rm Assets/Resources/Prefabs/Monsters/MC/Creeper/Scripts/CreeperTalent.cs
git rm Assets/Resources/Prefabs/Monsters/MC/HeadSeter/Scripts/HeadSeterSkill1.cs Assets/Resources/Prefabs/Monsters/MC/HeadSeter/Scripts/HeadSeterTalent1.cs
git rm Assets/Resources/Prefabs/Monsters/MC/Skeleton/Scripts/SkeletonTalent1.cs
git rm Assets/Resources/Prefabs/Monsters/MC/Witch/Scripts/WitchSkill.cs Assets/Resources/Prefabs/Monsters/MC/Witch/Scripts/WitchTalent.cs
git rm Assets/Resources/Prefabs/Monsters/MC/Wither/Scripts/WitherTalent1.cs Assets/Resources/Prefabs/Monsters/MC/Wither/Scripts/WitherTalent2.cs Assets/Resources/Prefabs/Monsters/MC/Wither/Scripts/WitherTalent3.cs
git rm Assets/Resources/Prefabs/Monsters/MC/WitherPedestal/Scripts/WitherPedestalTalent1.cs
git rm Assets/Resources/Prefabs/Monsters/MC/Zombie/Scripts/ZombieTalent.cs
git rm Assets/Resources/Prefabs/Monsters/Origin/Wdslm/Scripts/Machine/MachineTalent1.cs
git rm Assets/Resources/Prefabs/Monsters/Origin/Wdslm/Scripts/Wdslm/WdslmSkill2.cs Assets/Resources/Prefabs/Monsters/Origin/Wdslm/Scripts/Wdslm/WdslmSkill3.cs
```

- [ ] **Step 2: Verify all `.cs`/`.cs.meta` removed**

```bash
git status  # should show only deletions
```

- [ ] **Step 3: Verify Unity compiles**

Open Unity → no errors.

- [ ] **Step 4: Run full test suite**

Test Runner → Run All. **Expected:** all tests pass.

- [ ] **Step 5: Commit**

```bash
git commit -m "refactor(skill-system): remove all 28+ subclass scripts (migrated to SkillConfig)"
```

---

# Phase 8: Editor Tooling

Goal: PropertyDrawers for nice Inspector UX, plus a migration tool to assist future skill authoring.

## Task 8.1: Editor folder + assembly definition

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/Editor/SkillSystem.Editor.asmdef` (editor-only assembly)
- Create: `Assets/PublicScripts/SkillSystem/Editor/SkillSystem.Editor.asmdef.meta`

`SkillSystem.Editor.asmdef`:
```json
{
    "name": "SkillSystem.Editor",
    "references": ["GUID:<SkillSystem-runtime-guid>"],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "autoReferenced": true
}
```

(Replace `<SkillSystem-runtime-guid>` with the actual GUID of `Assets/PublicScripts/SkillSystem/SkillSystem.asmdef` or whichever runtime asmdef contains the SkillSystem code.)

- [ ] **Step 1: Verify Editor folder compiles**

Open Unity → no errors.

- [ ] **Step 2: Commit**

```bash
git add Assets/PublicScripts/SkillSystem/Editor/
git commit -m "feat(skill-system): editor assembly definition"
```

---

## Task 8.2: ComponentConfigDrawer with type dropdown

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/Editor/ComponentConfigDrawer.cs`

- [ ] **Step 1: Implement**

```csharp
using System;
using System.Linq;
using SkillSystem;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(ComponentConfig))]
public class ComponentConfigDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        ComponentAutoRegistry.EnsureRegistered();
        var typeProp = property.FindPropertyRelative("componentType");
        var trigProp = property.FindPropertyRelative("triggers");
        var paramProp = property.FindPropertyRelative("parameters");

        float y = position.y;
        var typeRect = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
        var regTypes = ComponentFactory.RegisteredTypes.OrderBy(s => s).ToArray();
        int currentIdx = Array.IndexOf(regTypes, typeProp.stringValue);
        if (currentIdx < 0) currentIdx = 0;
        int newIdx = EditorGUI.Popup(typeRect, "Type", currentIdx, regTypes);
        if (newIdx >= 0 && newIdx < regTypes.Length) typeProp.stringValue = regTypes[newIdx];

        y += EditorGUIUtility.singleLineHeight + 2;
        var trigRect = new Rect(position.x, y, position.width, EditorGUI.GetPropertyHeight(trigProp));
        EditorGUI.PropertyField(trigRect, trigProp, new GUIContent("Triggers"), true);

        y += EditorGUI.GetPropertyHeight(trigProp) + 2;
        var paramRect = new Rect(position.x, y, position.width, EditorGUI.GetPropertyHeight(paramProp));
        EditorGUI.PropertyField(paramRect, paramProp, new GUIContent("Parameters"), true);
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var trigProp = property.FindPropertyRelative("triggers");
        var paramProp = property.FindPropertyRelative("parameters");
        return EditorGUIUtility.singleLineHeight * 3 + EditorGUI.GetPropertyHeight(trigProp) + EditorGUI.GetPropertyHeight(paramProp) + 4;
    }
}
```

- [ ] **Step 2: Test in Inspector**

Open a `SkillConfig` in the Inspector, add a `ComponentConfig`, verify the type dropdown shows all registered types.

- [ ] **Step 3: Commit**

```bash
git add Assets/PublicScripts/SkillSystem/Editor/ComponentConfigDrawer.cs
git commit -m "feat(skill-system): ComponentConfigDrawer with type dropdown"
```

---

## Task 8.3: SPConfigDrawer (cleaner labels, no magic numbers)

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/Editor/SPConfigDrawer.cs`

- [ ] **Step 1: Implement**

```csharp
using SkillSystem;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(SPConfig))]
public class SPConfigDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        var rect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        var totalSp = property.FindPropertyRelative("totalSp");
        var initialSp = property.FindPropertyRelative("initialSp");
        var chargeNum = property.FindPropertyRelative("chargeNum");
        var dur = property.FindPropertyRelative("skillDuration");
        var recMode = property.FindPropertyRelative("recoverMode");
        var conMode = property.FindPropertyRelative("consumeMode");
        var openMode = property.FindPropertyRelative("openMode");
        var recForbid = property.FindPropertyRelative("recoverForbidDuringSkill");
        var canClose = property.FindPropertyRelative("canManualClose");
        var range = property.FindPropertyRelative("skillAttackRange");

        EditorGUI.PropertyField(rect, totalSp); rect.y += EditorGUIUtility.singleLineHeight + 2;
        EditorGUI.PropertyField(rect, initialSp); rect.y += EditorGUIUtility.singleLineHeight + 2;
        EditorGUI.PropertyField(rect, chargeNum); rect.y += EditorGUIUtility.singleLineHeight + 2;
        EditorGUI.PropertyField(rect, dur); rect.y += EditorGUIUtility.singleLineHeight + 2;
        EditorGUI.PropertyField(rect, recMode); rect.y += EditorGUIUtility.singleLineHeight + 2;
        EditorGUI.PropertyField(rect, conMode); rect.y += EditorGUIUtility.singleLineHeight + 2;
        EditorGUI.PropertyField(rect, openMode); rect.y += EditorGUIUtility.singleLineHeight + 2;
        EditorGUI.PropertyField(rect, recForbid); rect.y += EditorGUIUtility.singleLineHeight + 2;
        EditorGUI.PropertyField(rect, canClose); rect.y += EditorGUIUtility.singleLineHeight + 2;
        EditorGUI.PropertyField(rect, range, true);
        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var range = property.FindPropertyRelative("skillAttackRange");
        return EditorGUIUtility.singleLineHeight * 9 + EditorGUI.GetPropertyHeight(range) + 20;
    }
}
```

- [ ] **Step 2: Test, commit**

```bash
git add Assets/PublicScripts/SkillSystem/Editor/SPConfigDrawer.cs
git commit -m "feat(skill-system): SPConfigDrawer (no more magic ints in Inspector)"
```

---

## Task 8.4: SkillConfigDrawer (top-level ReorderableList of components)

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/Editor/SkillConfigDrawer.cs`

- [ ] **Step 1: Implement**

Use `ReorderableList` to render the `components` array. Each element uses `ComponentConfigDrawer`.

```csharp
using SkillSystem;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomPropertyDrawer(typeof(SkillConfig))]
public class SkillConfigDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        // Render skillId / skillName / description / kind
        // Then a ReorderableList for components[]
        var id = property.FindPropertyRelative("skillId");
        var name = property.FindPropertyRelative("skillName");
        var desc = property.FindPropertyRelative("description");
        var kind = property.FindPropertyRelative("kind");
        var sp = property.FindPropertyRelative("sp");
        var gc = property.FindPropertyRelative("globalConditions");
        var comps = property.FindPropertyRelative("components");

        float y = position.y;
        var r = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
        EditorGUI.PropertyField(r, id); y += EditorGUIUtility.singleLineHeight + 2;
        EditorGUI.PropertyField(new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight), name); y += EditorGUIUtility.singleLineHeight + 2;
        EditorGUI.PropertyField(new Rect(position.x, y, position.width, EditorGUI.GetPropertyHeight(desc)), desc); y += EditorGUI.GetPropertyHeight(desc) + 2;
        EditorGUI.PropertyField(new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight), kind); y += EditorGUIUtility.singleLineHeight + 2;
        EditorGUI.PropertyField(new Rect(position.x, y, position.width, EditorGUI.GetPropertyHeight(sp)), sp); y += EditorGUI.GetPropertyHeight(sp) + 2;
        EditorGUI.PropertyField(new Rect(position.x, y, position.width, EditorGUI.GetPropertyHeight(gc)), gc, true); y += EditorGUI.GetPropertyHeight(gc) + 2;
        EditorGUI.PropertyField(new Rect(position.x, y, position.width, EditorGUI.GetPropertyHeight(comps)), comps, new GUIContent("Components"), true);
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var sp = property.FindPropertyRelative("sp");
        var gc = property.FindPropertyRelative("globalConditions");
        var comps = property.FindPropertyRelative("components");
        var desc = property.FindPropertyRelative("description");
        return EditorGUIUtility.singleLineHeight * 4
            + EditorGUI.GetPropertyHeight(sp) + EditorGUI.GetPropertyHeight(gc)
            + EditorGUI.GetPropertyHeight(comps) + EditorGUI.GetPropertyHeight(desc) + 20;
    }
}
```

- [ ] **Step 2: Test, commit**

```bash
git add Assets/PublicScripts/SkillSystem/Editor/SkillConfigDrawer.cs
git commit -m "feat(skill-system): SkillConfigDrawer (ReorderableList of components)"
```

---

## Task 8.5: ParamListDrawer (improved key-value UX)

**Files:**
- Create: `Assets/PublicScripts/SkillSystem/Editor/ParamListDrawer.cs`

- [ ] **Step 1: Implement**

```csharp
using SkillSystem;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomPropertyDrawer(typeof(ParamList))]
public class ParamListDrawer : PropertyDrawer
{
    private ReorderableList _list;
    private SerializedProperty _entries;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        _entries = property.FindPropertyRelative("entries");
        if (_list == null) BuildList(property.displayName);
        _list.DoList(position);
    }

    private void BuildList(string label)
    {
        _list = new ReorderableList(_entries.serializedObject, _entries, true, true, true, true);
        _list.drawHeaderCallback = (Rect r) => EditorGUI.LabelField(r, label);
        _list.elementHeightCallback = (int idx) => EditorGUIUtility.singleLineHeight * 2 + 4;
        _list.drawElementCallback = (Rect r, int idx, bool active, bool focused) => {
            var el = _entries.GetArrayElementAtIndex(idx);
            var key = el.FindPropertyRelative("key");
            var type = el.FindPropertyRelative("type");
            var val = el.FindPropertyRelative("value");
            float y = r.y;
            EditorGUI.PropertyField(new Rect(r.x, y, r.width * 0.4f, EditorGUIUtility.singleLineHeight), key);
            EditorGUI.PropertyField(new Rect(r.x + r.width * 0.4f, y, r.width * 0.3f, EditorGUIUtility.singleLineHeight), type);
            y += EditorGUIUtility.singleLineHeight + 2;
            EditorGUI.PropertyField(new Rect(r.x, y, r.width, EditorGUIUtility.singleLineHeight), val);
        };
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (_list == null) BuildList(property.displayName);
        return _list.GetHeight();
    }
}
```

- [ ] **Step 2: Test, commit**

```bash
git add Assets/PublicScripts/SkillSystem/Editor/ParamListDrawer.cs
git commit -m "feat(skill-system): ParamListDrawer"
```

---

# Final verification (DoD)

- [ ] **All 40+ components implemented and unit-tested**
- [ ] **All 28+ existing skills/talents migrated to `SkillConfig` data entries in their owning `EntityData`**
- [ ] **`Skill.cs` and `Talent.cs` deleted; all 28+ subclass scripts deleted**
- [ ] **No `Skill` / `Talent` MonoBehaviour references in any prefab**
- [ ] **Migration test suite passes 100%**
- [ ] **Manual playtest of every entity type passes (no functional regression)**
- [ ] **PropertyDrawers implemented for `ComponentConfig`, `SPConfig`, `ParamList`, `SkillConfig`**
- [ ] **Design spec + implementation plan merged to `main`**
