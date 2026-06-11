# Condition Trigger AND/OR + Blackboard Unification Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `triggers[]` a real conditional gate at dispatch time, add nested AND/OR composition in `ConditionConfig` (3 new `[Serializable]` classes), and unify on a single per-Entity blackboard by deleting `SkillRuntime.blackboard` and migrating 6 components from `ctx.blackboard` to `ctx.sharedBlackboard`.

**Architecture:**
- Data layer: `ConditionConfig` keeps its `triggerEvent` for designer / `.asset` use; bucket stores the projected `List<ConditionGroup>` (the `groups` field) since `triggerEvent` is redundant with the dictionary key.
- Dispatch: `DispatchToSkill` constructs one `ConditionEvalContext` per event and single-pass-iterates the bucket, calling `ConditionEvaluator.Evaluate(groups, ctx)` for each entry. No dedup; multiple entries with the same `(component, triggerEvent)` produce multiple `OnTrigger` calls.
- Blackboard: the only allowed blackboard is the per-Entity shared one. `SkillRuntime.blackboard` and `SkillContext.blackboard` are removed; 6 component files rename `ctx.blackboard` → `ctx.sharedBlackboard`.

**Tech Stack:** Unity 2020+ (C# 8.0), no test infrastructure (project ships no EditMode/PlayMode test runner as of 2026-06-11). Verification = Unity Editor compile + subagent spec/code review + PlayMode smoke.

**Spec:** `docs/superpowers/specs/2026-06-11-condition-trigger-AND-OR-design.md` (committed at `916c24c`).

---

## File Inventory

### Modify

- `Assets/PublicScripts/GameData/SkillSystem/ComponentConfig.cs` — add `ConditionUnit` / `ConditionGroup`; change `ConditionConfig` field shape (remove `op` / `leftKey` / `rightValue`, add `groups`).
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/ConditionEvaluator.cs` — reshape `ConditionEvalContext` (`Blackboard` → `sharedBlackboard`, `object Event` → `SkillEvent currentEvent`, add `entity`); rewrite `Evaluate` to take `List<ConditionGroup>`; add nested `EvaluateGroup` / `EvaluateUnit`; add one-shot `WarnUnknownOpAndPass`.
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs` — delete `blackboard` field; change `componentsByTrigger` value type to `List<(ISkillComponent, List<ConditionGroup>)>`; remove `blackboard` assignment in `MakeContext`.
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/ISkillComponent.cs` — delete `SkillContext.blackboard` field; update `sharedBlackboard` comment.
- `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs` — `BuildSkillRuntime` projects `ConditionConfig` to `(inst, groups)`; `DispatchToSkill` builds `ConditionEvalContext` and single-pass-iterates the bucket calling `ConditionEvaluator.Evaluate(groups, ctx)`.
- 6 component files (7 touch-points): `ctx.blackboard` → `ctx.sharedBlackboard`:
  - `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/ApplyBuff.cs` (line 103)
  - `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/PlayParticleComponent.cs` (line 20)
  - `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/EntitySelector.cs` (lines 46, 47)
  - `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/EntitySelectorRadiusEffectComponent.cs` (line 51)
  - `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/CampDamageModifierComponent.cs` (line 20)
  - `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/CoroutineLoopComponent.cs` (lines 37, 38)
- `docs/skill-components/README.md` — rewrite the "Conditional triggers" section (§114-135) to document the nested AND/OR shape and the two coexisting expression forms.

### Do Not Modify

- `Assets/Resources/Prefabs/Characters/3/Kroos/skills/SK_Kroos_1.asset` — Unity silently drops the removed `op` / `leftKey` / `rightValue` fields; the new `groups` field defaults to empty; existing 3 entries behave identically to before.

---

## Task Decomposition Rationale

Tasks are ordered so each one leaves the project in a compilable state (Unity Editor Console: zero errors). The order is data-first, runtime-second, cleanup-last:

1. **Data model first** (Tasks 1, 2): `ConditionConfig` shape and `ConditionEvaluator` API. The project will not compile cleanly between 1 and 2 (Task 1 introduces the new classes; Task 1.5 isn't needed because the existing `ConditionEvaluator` is rewritten in Task 2, not split). In practice these are committed as a single 2-step task pair.
2. **Bucket data structure** (Task 3): `SkillRuntime.componentsByTrigger` value type change. Requires `ConditionGroup` to be visible — satisfied by Task 1.
3. **Blackboard deletion** (Task 4): `SkillRuntime.blackboard` removal. Independent of tasks 1-3.
4. **SkillContext.blackboard removal** (Task 5): depends on Task 4 (the field must be gone before any consumer can compile).
5. **5-component rename** (Task 6): depends on Task 5 (components still reference the deleted field until they migrate).
6. **Dispatch pipeline** (Task 7): `BuildSkillRuntime` projection + `DispatchToSkill` Evaluate gate. Depends on Tasks 1, 2, 3, 5.
7. **README** (Task 8): documentation only; no compile dependency. Last so all behaviour is locked before docs describe it.
8. **Final verification** (Task 9): re-read all touched files, run grep pass criteria.

The TDD pattern (red-green-refactor) is replaced with **compile-check + subagent-review** because the project has no test runner. The "write the failing test" step is replaced with "re-read the target file and write a 1-paragraph statement of the expected compile state". The "run the test" step is replaced with "open Unity, observe Console". Subagent spec-review + code-quality-review replace unit-level test coverage.

---

### Task 1: Add `ConditionUnit` and `ConditionGroup`; reshape `ConditionConfig`

**Files:**
- Modify: `Assets/PublicScripts/GameData/SkillSystem/ComponentConfig.cs` (the file contains `ParamList`, `TriggerEvent`, `ConditionOp`, `ConditionConfig` — add the two new classes, change the `ConditionConfig` field set).

- [ ] **Step 1: Re-read `ComponentConfig.cs` to confirm the current field layout**

Read `Assets/PublicScripts/GameData/SkillSystem/ComponentConfig.cs` and confirm:
- `ConditionOp` enum still has only the trimmed whitelist (`None` / `Equal` / `NotEqual` / `Greater` / `GreaterOrEqual` / `Less` / `LessOrEqual`); if anything else is present, STOP and surface the discrepancy to the user.
- `ConditionConfig` currently has fields `{triggerEvent, op, leftKey, rightValue}` with `op = ConditionOp.None` as default.

- [ ] **Step 2: Write the new file contents**

Replace the **entire** contents of `ComponentConfig.cs` with the version below. This adds `ConditionUnit` and `ConditionGroup` above `ConditionConfig`, and reduces `ConditionConfig` to the trigger event plus the groups list. The rest of the file (`ParamValueType`, `ParamEntry`, `ParamList`, `TriggerEvent`, `ConditionOp`) is unchanged from the current state.

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

    public enum TriggerEvent
    {
        OnPreWarm, OnInitialize,
        OnBeforeAttack, OnAfterAttack,
        OnBeforeTakeDamage, OnAfterTakeDamage,
        OnAttackSuccessfully, OnAttackInterrupt,
        OnBeforeHurt, OnAfterHurt,
        OnAttackAnimBegin,
        OnBeforeDieAnimation,
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

    // A single comparison: op(leftKey, rightValue). The runtime semantics
    // are documented in ConditionEvaluator.EvaluateUnit.
    [Serializable]
    public class ConditionUnit
    {
        public ConditionOp op = ConditionOp.None;
        public string leftKey;
        public string rightValue;
    }

    // A list of units combined with AND. An empty list is treated as
    // "passes" (matches the legacy op: 0 / None short-circuit).
    [Serializable]
    public class ConditionGroup
    {
        public List<ConditionUnit> units = new List<ConditionUnit>();
    }

    // A trigger expression: outer list is OR across groups, inner list
    // is AND across units. Empty groups list is treated as "always
    // passes". Designer-facing field; the runtime bucket projects
    // `cond.groups` (the List<ConditionGroup>) for fast iteration.
    [Serializable]
    public class ConditionConfig
    {
        public TriggerEvent triggerEvent;
        public List<ConditionGroup> groups = new List<ConditionGroup>();
    }

    [Serializable]
    public class ComponentConfig
    {
        [ComponentTypeRef]
        public string componentType;
        public ParamList parameters = new ParamList();
        public ConditionConfig[] triggers = Array.Empty<ConditionConfig>();
    }
}
```

> **Note on `ConditionOp`:** the enum still contains the legacy ops
> (`HasBuff`, `IsInAbnormalState`, `HasBlackboardKey`, etc.) because
> removing them is out of scope (commit `54c3465` already trimmed
> their call sites; the enum surface is preserved for `.asset`
> forward-compat). `ConditionEvaluator` only matches the live
> whitelist; unknown values fall through to the warning path
> (Task 2).

> **Note on `using`:** the file already has `using System;` and
> `using UnityEngine;`. `List<T>` is in `System.Collections.Generic`
> and is reachable because the existing `ConditionConfig` did not use
> `List<T>` — but the **new** `ConditionGroup` does. Add
> `using System.Collections.Generic;` if the file does not already
> have it. (Re-read first to confirm; if absent, add it before the
> `namespace SkillSystem {` line.)

- [ ] **Step 3: Open Unity and confirm Console is clean**

Open the project in Unity Editor. Wait for the compile to finish. The Console must show zero errors. Warnings are OK at this stage (legacy `ConditionOp` values still reference the removed-elsewhere type surface), but should be investigated if they reference this file. **Do not proceed to Task 2 if errors are present.**

- [ ] **Step 4: Commit**

```bash
git add Assets/PublicScripts/GameData/SkillSystem/ComponentConfig.cs
git commit -m "feat(skill-conditions): add ConditionUnit + ConditionGroup; reshape ConditionConfig

Three [Serializable] classes for nested AND/OR trigger expressions:
  ConditionUnit   = single op(leftKey, rightValue) comparison
  ConditionGroup  = AND across List<ConditionUnit>
  ConditionConfig = OR across List<ConditionGroup>, plus triggerEvent

Legacy flat fields (op / leftKey / rightValue) are removed from
ConditionConfig. Unity silently drops them on the only .asset that
carried them (SK_Kroos_1.asset) since groups defaults to empty,
which Evaluate treats as 'always passes' (same as old op: 0 / None
short-circuit)."
```

---

### Task 2: Reshape `ConditionEvalContext`; rewrite `Evaluate` for nested AND/OR

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/ConditionEvaluator.cs` (full file rewrite — only ~80 lines).

- [ ] **Step 1: Re-read `ConditionEvaluator.cs` to confirm the current shape**

Read `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/ConditionEvaluator.cs`. Confirm:
- `ConditionEvalContext` has `Blackboard` and `object Event` fields.
- `Evaluate` accepts `ConditionConfig` and dispatches on the flat `op` field.

- [ ] **Step 2: Write the new file contents**

Replace the entire file with:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace SkillSystem
{
    // The runtime context for one dispatch pass. sharedBlackboard is
    // the per-Entity shared blackboard (EntitySkillRunner.sharedBlackboard
    // is injected at dispatch time). entity and currentEvent are
    // exposed for future ConditionOp extensions (HasBuff etc.); this
    // spec does not read them.
    public class ConditionEvalContext
    {
        public Blackboard sharedBlackboard = new Blackboard();
        public Entity entity;
        public SkillEvent currentEvent;
    }

    public static class ConditionEvaluator
    {
        // AND/OR evaluation:
        //   - empty / null groups -> true (unconditional pass)
        //   - otherwise, iterate groups; first group to pass wins (OR)
        public static bool Evaluate(List<ConditionGroup> groups, ConditionEvalContext ctx)
        {
            if (groups == null || groups.Count == 0) return true;

            for (int g = 0; g < groups.Count; g++)
            {
                if (EvaluateGroup(groups[g], ctx)) return true;
            }
            return false;
        }

        // AND across units:
        //   - empty / null units -> true
        //   - otherwise, all units must pass (AND)
        private static bool EvaluateGroup(ConditionGroup group, ConditionEvalContext ctx)
        {
            if (group == null) return true;
            if (group.units == null || group.units.Count == 0) return true;

            for (int u = 0; u < group.units.Count; u++)
            {
                if (!EvaluateUnit(group.units[u], ctx)) return false;
            }
            return true;
        }

        // Single comparison. Unknown ops log a one-shot warning and
        // return true (matches the legacy default: return true).
        private static bool EvaluateUnit(ConditionUnit unit, ConditionEvalContext ctx)
        {
            if (unit == null) return true;
            if (unit.op == ConditionOp.None) return true;

            switch (unit.op)
            {
                case ConditionOp.Equal:
                    return ctx.sharedBlackboard.Get<string>(unit.leftKey) == unit.rightValue;
                case ConditionOp.NotEqual:
                    return ctx.sharedBlackboard.Get<string>(unit.leftKey) != unit.rightValue;
                case ConditionOp.Greater:
                    return CompareNumeric(ctx, unit.leftKey, unit.rightValue) >  0;
                case ConditionOp.GreaterOrEqual:
                    return CompareNumeric(ctx, unit.leftKey, unit.rightValue) >= 0;
                case ConditionOp.Less:
                    return CompareNumeric(ctx, unit.leftKey, unit.rightValue) <  0;
                case ConditionOp.LessOrEqual:
                    return CompareNumeric(ctx, unit.leftKey, unit.rightValue) <= 0;
                default:
                    return WarnUnknownOpAndPass(unit.op);
            }
        }

        private static int CompareNumeric(ConditionEvalContext ctx, string leftKey, string rightValueStr)
        {
            var left = ctx.sharedBlackboard.Get<string>(leftKey, "");
            if (float.TryParse(left, out var l) && float.TryParse(rightValueStr, out var r))
                return l.CompareTo(r);
            return string.Compare(left, rightValueStr, System.StringComparison.Ordinal);
        }

        // One-shot warning per unknown op value across the application
        // lifetime. Bounded log spam if configuration is reused.
        private static readonly HashSet<ConditionOp> _warnedOps = new HashSet<ConditionOp>();
        private static bool WarnUnknownOpAndPass(ConditionOp op)
        {
            if (_warnedOps.Add(op))
            {
                Debug.LogWarning(
                    $"ConditionEvaluator: unknown ConditionOp {(int)op} treated as 'pass'. " +
                    "If this fires, a new op was added without a case in Evaluate.");
            }
            return true;   // backwards-compatible: unknown op -> pass
        }
    }
}
```

- [ ] **Step 3: Open Unity and confirm Console is clean**

Open the project. The Console must show zero errors. The `unknown ConditionOp` warning is **expected not to fire** because `ConditionOp` is the trimmed whitelist (commit `54c3465`). If a warning fires here, stop and surface to the user — it means a new op was added without extending the evaluator.

- [ ] **Step 4: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/ConditionEvaluator.cs
git commit -m "refactor(skill-conditions): reshape ConditionEvalContext; nested Evaluate

ConditionEvalContext:
  - Blackboard  -> sharedBlackboard (rename to align with SkillContext)
  - object Event -> SkillEvent currentEvent (typed)
  - add entity field (hook for future HasBuff / IsInAbnormalState ops)

Evaluate(List<ConditionGroup>, ConditionEvalContext) is the dispatch-time
entry point. It iterates groups (OR), each group iterates units (AND),
each unit dispatches on op. Empty groups -> pass. Unknown ops log a
one-shot warning and pass (matches the legacy default: return true)."
```

---

### Task 3: Change `SkillRuntime.componentsByTrigger` value type; delete `SkillRuntime.blackboard`

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs`.

- [ ] **Step 1: Re-read `SkillRuntime.cs` to confirm the current shape**

Read `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs`. Confirm:
- Line 9 has `public Blackboard blackboard = new Blackboard();`.
- Line 16-17 has `public Dictionary<TriggerEvent, List<ISkillComponent>> componentsByTrigger`.
- Line 24-33 `MakeContext` sets `blackboard = blackboard`.

- [ ] **Step 2: Write the new file contents**

Replace the entire file with:

```csharp
using System.Collections.Generic;

namespace SkillSystem
{
    public class SkillRuntime
    {
        public SkillConfig config;
        public SPEngine spEngine;
        public List<ISkillComponent> components = new List<ISkillComponent>();
        public List<ITickingComponent> tickingComponents = new List<ITickingComponent>();
        // 与 components 并行：保存每个组件的初始参数，供 OnInitialize 时 re-OnInit。
        public List<ParamList> componentParams = new List<ParamList>();
        // Trigger 分桶：BuildSkillRuntime 一次性填充，OnInitialize/OnTeardown 不重建。
        // key 是 ConditionConfig.triggerEvent 的 enum；value 是按 config 声明顺序排好的
        // (component, conditionExpression) 对，conditionExpression 是从 ConditionConfig
        // 投影出的 List<ConditionGroup>（见 §2.1 of the spec）。
        public Dictionary<TriggerEvent, List<(ISkillComponent comp, List<ConditionGroup> groups)>>
            componentsByTrigger
            = new Dictionary<TriggerEvent, List<(ISkillComponent, List<ConditionGroup>)>>();
        public bool isInitialized;
        public bool isActive; // true while skill is firing (SPEngine.IsActive)

        public void OpenActiveWindow()  { isActive = true;  }
        public void CloseActiveWindow() { isActive = false; }

        public SkillContext MakeContext(ISkillComponent component, SkillEvent evt = null)
        {
            return new SkillContext
            {
                skill = this,
                component = component,
                currentEvent = evt,
                // sharedBlackboard 由 EntitySkillRunner.PrepareContext 注入。
            };
        }
    }
}
```

- [ ] **Step 3: Open Unity and confirm Console is clean**

Open the project. The Console must show zero errors. **Expected warnings/errors at this stage**: `EntitySkillRunner.cs` references the old `componentsByTrigger` value type (was `List<ISkillComponent>`, now `List<(ISkillComponent, List<ConditionGroup>)>`); the `BuildSkillRuntime` and `DispatchToSkill` code in that file will not compile. The `ApplyBuff.cs` and other components will not compile because `SkillContext.blackboard` is still present (Task 4) — but they read `ctx.blackboard`, which is still a field on the type at this point, so they should still compile (the field is just being deleted in Task 5, not yet).

If `EntitySkillRunner.cs` fails to compile, **stop and surface** — Task 7 fixes the dispatch pipeline; this task is only meant to break the project mid-way. The compile error here is expected and is the trigger to do Task 7.

- [ ] **Step 4: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs
git commit -m "refactor(skill-runtime): bucket stores (comp, groups); delete blackboard

Two unrelated changes committed together because both are mechanical
field-shape updates that don't compile in isolation:

  - componentsByTrigger value type:
      List<ISkillComponent>
        -> List<(ISkillComponent comp, List<ConditionGroup> groups)>
    The bucket now stores the projected condition expression rather
    than the full ConditionConfig. triggerEvent is the dictionary
    key, so storing it inside the value was redundant.

  - delete SkillRuntime.blackboard field. The only allowed blackboard
    is the per-Entity shared one (EntitySkillRunner.sharedBlackboard),
    reached via SkillContext.sharedBlackboard. MakeContext no longer
    assigns the per-skill field.

EntitySkillRunner dispatch and 6 component files (still reading
SkillContext.blackboard) will not compile until Tasks 5 and 7 land.
That is expected — the next tasks close those gaps."
```

---

### Task 4: Delete `SkillContext.blackboard`; update `sharedBlackboard` comment

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/ISkillComponent.cs` (delete the `blackboard` field on `SkillContext`; update the `sharedBlackboard` comment).

- [ ] **Step 1: Re-read `ISkillComponent.cs` to confirm the current shape**

Read `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/ISkillComponent.cs`. Confirm:
- Line 19: `public Blackboard blackboard;          // per-skill`.
- Line 20: `public Blackboard sharedBlackboard;    // per-Entity SkillRunner`.

- [ ] **Step 2: Edit the file**

In the `SkillContext` class, delete the `blackboard` field and update the `sharedBlackboard` comment. The new file:

```csharp
using UnityEngine;

namespace SkillSystem
{
    public abstract class SkillEvent
    {
        // Every concrete SkillEvent must declare which TriggerEvent enum value
        // it routes to. Abstract (not virtual) so the compiler catches missing
        // overrides — that's the safety net for the dispatch bridge table.
        public abstract TriggerEvent TriggerEvent { get; }
    }

    public class SkillContext
    {
        public Entity entity;
        public SkillRuntime skill;
        public ISkillComponent component;
        public SkillEvent currentEvent;
        // The only blackboard. Per-Entity, set by EntitySkillRunner.PrepareContext
        // from the per-Entity sharedBlackboard field on the runner. Components
        // read this directly; there is no per-skill blackboard.
        public Blackboard sharedBlackboard;
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

    public static class SkillSystemBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Init()
        {
            ComponentAutoRegistry.EnsureRegistered();
        }
    }
}
```

- [ ] **Step 3: Open Unity and confirm 6 component files now fail to compile**

Open the project. **Expected**: 6 component files (`ApplyBuff.cs`, `PlayParticleComponent.cs`, `EntitySelector.cs`, `EntitySelectorRadiusEffectComponent.cs`, `CampDamageModifierComponent.cs`, `CoroutineLoopComponent.cs`) fail to compile because they read `ctx.blackboard`. **`EntitySelectorRadiusEffectComponent.cs` has one read at line 51 (`blackboard = ctx.blackboard`) which passes the value to a sub-component — the fix is to pass `ctx.sharedBlackboard` instead, but the rename is a Task 6 step. At this point, the project will not compile.** This is the trigger for Task 6.

- [ ] **Step 4: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/ISkillComponent.cs
git commit -m "refactor(skill-context): delete SkillContext.blackboard

The per-skill blackboard field on SkillContext is removed; the only
blackboard is the per-Entity shared one (set by
EntitySkillRunner.PrepareContext from the runner's sharedBlackboard
field). Components that previously read ctx.blackboard will fail to
compile until Task 6 renames them to ctx.sharedBlackboard. That
failure is the trigger for the next task."
```

---

### Task 5: Rename `ctx.blackboard` to `ctx.sharedBlackboard` in 6 component files

**Files:**
- Modify (mechanical rename, 7 touch-points total):
  - `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/ApplyBuff.cs` (line 103)
  - `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/PlayParticleComponent.cs` (line 20)
  - `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/EntitySelector.cs` (lines 46, 47)
  - `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/EntitySelectorRadiusEffectComponent.cs` (line 51)
  - `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/CampDamageModifierComponent.cs` (line 20)
  - `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/CoroutineLoopComponent.cs` (lines 37, 38)

- [ ] **Step 1: Re-read each file to confirm the exact line and the surrounding context**

For each of the 6 files, read the file and confirm the line numbers. If any line has shifted (because of edits in earlier tasks — none expected, but verify), adjust the line numbers in this plan before continuing.

- [ ] **Step 2: Apply the rename**

For each touch-point, replace `ctx.blackboard` with `ctx.sharedBlackboard`. Use `Edit` with `replace_all: false` and the exact old string. The line numbers from Step 1 are illustrative; rely on the `old_string` text.

| File | Touch-point | old_string | new_string |
|---|---|---|---|
| `ApplyBuff.cs` | line 103 | `return ctx.blackboard.Get<List<Entity>>(_inputKey, null);` | `return ctx.sharedBlackboard.Get<List<Entity>>(_inputKey, null);` |
| `PlayParticleComponent.cs` | line 20 | `var ps = ctx.blackboard != null ? ctx.blackboard.Get<ParticleSystem>("__particleSystem", null) : null;` | `var ps = ctx.sharedBlackboard != null ? ctx.sharedBlackboard.Get<ParticleSystem>("__particleSystem", null) : null;` |
| `EntitySelector.cs` | line 46 | `ctx.blackboard.Remove(_outputKey);` | `ctx.sharedBlackboard.Remove(_outputKey);` |
| `EntitySelector.cs` | line 47 | `ctx.blackboard.Set(_outputKey, result);` | `ctx.sharedBlackboard.Set(_outputKey, result);` |
| `EntitySelectorRadiusEffectComponent.cs` | line 51 | `blackboard = ctx.blackboard,` | `blackboard = ctx.sharedBlackboard,` |
| `CampDamageModifierComponent.cs` | line 20 | `if (ctx.blackboard != null && ctx.blackboard.Get<int>("attackerCamp") == _requiredCamp)` | `if (ctx.sharedBlackboard != null && ctx.sharedBlackboard.Get<int>("attackerCamp") == _requiredCamp)` |
| `CoroutineLoopComponent.cs` | line 37 | `if (!string.IsNullOrEmpty(_stopConditionKey) && ctx.blackboard != null` | `if (!string.IsNullOrEmpty(_stopConditionKey) && ctx.sharedBlackboard != null` |
| `CoroutineLoopComponent.cs` | line 38 | `&& !ctx.blackboard.Has(_stopConditionKey))` | `&& !ctx.sharedBlackboard.Has(_stopConditionKey))` |

- [ ] **Step 3: Open Unity and confirm Console is clean**

Open the project. The 6 component files should now compile. **The project as a whole still does not compile** — `EntitySkillRunner.cs` is still using the old `List<ISkillComponent>` bucket value type. That is the trigger for Task 7.

- [ ] **Step 4: Commit**

```bash
git add \
  Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/ApplyBuff.cs \
  Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/PlayParticleComponent.cs \
  Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/EntitySelector.cs \
  Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/EntitySelectorRadiusEffectComponent.cs \
  Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/CampDamageModifierComponent.cs \
  Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/CoroutineLoopComponent.cs
git commit -m "refactor(skill-components): ctx.blackboard -> ctx.sharedBlackboard

7 touch-points across 6 component files (EntitySelector has 2 reads).
All mechanical renames; no logic change. The blackboard each component
reads is now the per-Entity shared one (set by
EntitySkillRunner.PrepareContext)."
```

---

### Task 6: Update `EntitySkillRunner` — `BuildSkillRuntime` projects `ConditionConfig` to `(inst, groups)`; `DispatchToSkill` inserts the Evaluate gate

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs`.

- [ ] **Step 1: Re-read the dispatch and build sections of `EntitySkillRunner.cs`**

Read lines 153-200 (the `BuildSkillRuntime` trigger loop) and lines 312-340 (`DispatchToSkill` and `PrepareContext`). Confirm:
- The trigger loop adds `(inst, ConditionConfig)` to a bucket keyed by `triggerEvent` (around line 169: `list.Add(inst)`).
- `DispatchToSkill` calls `comp.OnTrigger(ctx)` directly without consulting any condition.

- [ ] **Step 2: Update the trigger loop in `BuildSkillRuntime`**

Replace the existing trigger loop (lines 156-172 area) with:

```csharp
// Bucket by trigger. Each ConditionConfig contributes one entry; the
// same component instance can land in multiple buckets when its
// config declares multiple triggers — that's expected.
//
// We project ConditionConfig -> (inst, cond.groups) here so the
// runtime bucket only carries what Evaluate needs. The triggerEvent
// is already encoded in the bucket key.
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
```

The rest of `BuildSkillRuntime` (the `triggerCount == 0` warning) is unchanged.

- [ ] **Step 3: Update `DispatchToSkill` to insert the Evaluate gate**

Replace `DispatchToSkill` (lines 312-331) with:

```csharp
private void DispatchToSkill(SkillRuntime s, SkillEvent evt)
{
    // Active-window gate: a skill's components only see events while the
    // skill is firing, EXCEPT for these four lifecycle events which always
    // bypass the gate. They still need to be declared in config.triggers[]
    // to be received — bypass is gate-only, not bucket-only.
    bool bypassActiveGate = evt is PreWarmEvent
                         || evt is InitializeEvent
                         || evt is SkillBeginEvent
                         || evt is SkillEndEvent;
    if (!s.isActive && !bypassActiveGate) return;
    if (!s.componentsByTrigger.TryGetValue(evt.TriggerEvent, out var list)) return;

    // All (comp, groups) for this event share the same eval context.
    var evalCtx = new ConditionEvalContext
    {
        sharedBlackboard = sharedBlackboard,
        entity = _entity,
        currentEvent = evt,
    };

    // Single-pass over the bucket. No dedup: a component with N
    // ConditionConfig entries for the same triggerEvent produces up
    // to N OnTrigger calls (one per passing entry). Designers who
    // want at-most-one should put the OR inside a single
    // ConditionConfig's groups (see spec §2.4).
    for (int i = 0; i < list.Count; i++)
    {
        var (comp, groups) = list[i];
        if (!ConditionEvaluator.Evaluate(groups, evalCtx)) continue;
        var ctx = PrepareContext(s.MakeContext(comp, evt));
        comp.OnTrigger(ctx);
    }
}
```

`PrepareContext` is unchanged (it already injects `sharedBlackboard` and `entity`).

- [ ] **Step 4: Open Unity and confirm Console is clean**

Open the project. The Console must show zero errors. The `ConditionEvaluator unknown op` warning is **expected not to fire** (the `ConditionOp` enum is the trimmed whitelist).

- [ ] **Step 5: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs
git commit -m "feat(skill-runner): BuildSkillRuntime projects groups; DispatchToSkill gates on Evaluate

Two coupled changes that close the gap left by Tasks 3-4:

  BuildSkillRuntime: the trigger loop now appends (inst, trig.groups)
  rather than the bare inst. The bucket value type from Task 3
  (List<(ISkillComponent, List<ConditionGroup>)>) is now populated.

  DispatchToSkill: single-pass over the bucket. For each entry,
  ConditionEvaluator.Evaluate(groups, evalCtx) gates whether
  comp.OnTrigger is called. evalCtx carries the per-Entity
  sharedBlackboard, the owner entity, and the dispatching event.

Multiple ConditionConfig entries for the same (component, triggerEvent)
produce multiple OnTrigger calls (one per passing entry). Designers
who want at-most-one per event should use the nested groups shape
to express OR inside a single ConditionConfig."
```

---

### Task 7: Update `docs/skill-components/README.md` "Conditional triggers" section

**Files:**
- Modify: `docs/skill-components/README.md` (rewrite §114-135 only; preserve everything above and below).

- [ ] **Step 1: Re-read the current section**

Read lines 114-135 of `docs/skill-components/README.md` to confirm the current text. The expected current text is the "Conditional triggers" table block shown in the spec's spec doc.

- [ ] **Step 2: Replace the section**

Replace the entire "Conditional triggers" section (lines 114 through 135, including the heading) with:

```markdown
## Conditional triggers

Every `ComponentConfig` has a `triggers[]` array of `ConditionConfig`
entries. Each entry declares a `triggerEvent` plus a condition
expression. At dispatch time, an entry's expression is evaluated
against the entity's shared blackboard; if it passes,
`OnTrigger` runs on the bound component. Failing entries are
skipped silently.

### Expression shape

```
ConditionConfig  { triggerEvent, List<ConditionGroup> groups }
   |                  |
   |                  +- groups is OR across ConditionGroup entries
   |                     (any group passing = the whole config passes)
   |
   +- dispatch-time entry point: ConditionEvaluator.Evaluate(groups, ctx)

ConditionGroup   { List<ConditionUnit> units }
   |
   +- units is AND across ConditionUnit entries
      (all units passing = the group passes)

ConditionUnit    { op, leftKey, rightValue }
   |
   +- a single comparison: op(leftKey, rightValue)
```

An empty `groups` list is treated as "always passes" (unconditional
trigger), equivalent to the legacy `op: 0` (None) short-circuit.

### Operator semantics (from `ConditionEvaluator.cs`)

The trimmed `ConditionOp` whitelist (per commit `54c3465`):

| `op` | Comparison |
|---|---|
| `None` | always passes (unit-level) |
| `Equal`, `NotEqual` | string `==` / `!=` on the key's value |
| `Greater` / `GreaterOrEqual` / `Less` / `LessOrEqual` | `float.TryParse` on both sides, falls back to ordinal string compare if either is unparseable |

Any `ConditionOp` value outside the live whitelist is logged once
(across the application lifetime) as a warning and treated as
"passes". The legacy `HasBuff` / `IsInAbnormalState` /
`HasBlackboardKey` values are still in the enum for `.asset`
forward-compat but are no-ops at runtime.

### Two coexisting expression forms

Designers have two ways to express multi-condition triggers; the
choice is a data-authoring concern, not a dispatch concern:

1. **Nested AND/OR** inside one `ConditionConfig.groups[]` (the new
   shape). Use this when the goal is "fire at most once per event
   if any matching condition is true".

2. **Repeated `triggers[]` entries** with the same `triggerEvent`
   (the pre-existing pattern). Use this when the goal is "fire once
   per passing entry". The runtime does not dedup these — each
   passing entry produces one `OnTrigger` call.
```

Preserve the file's surrounding text: the "Storage" / "Type encoding" / "DamageType encoding" / "Components" sections above, and any sections below "Conditional triggers" (none in the current file).

- [ ] **Step 3: Confirm Markdown is well-formed**

Read the file end-to-end and confirm:
- All `##` and `###` headings nest correctly (no skipped levels).
- The code block (the "Expression shape" diagram) renders without errors. (GitHub-flavoured markdown will display the `|` characters literally; that's fine.)
- No broken table delimiters (the operator table has the same `| --- |` separator pattern as the rest of the file).

- [ ] **Step 4: Commit**

```bash
git add docs/skill-components/README.md
git commit -m "docs(skill-components): rewrite Conditional triggers section

The section now describes:
  - the nested AND/OR shape (ConditionConfig -> ConditionGroup -> ConditionUnit)
  - empty-groups = unconditional pass semantics
  - the trimmed ConditionOp whitelist (Equal/NotEqual/Greater*/Less*)
  - the one-shot warning for unknown ops
  - the two coexisting expression forms (nested groups vs repeated
    triggers[] entries)
The old 'No-op operators' table (HasBuff/IsInAbnormalState being
unimplemented) is replaced with the new whitelist table; the same
operators are still in the ConditionOp enum (for .asset forward-
compat) but ConditionEvaluator documents them as no-op+warn."
```

---

### Task 8: Final verification — read every touched file, run the hard pass criteria

**Files:** (re-read all)
- `Assets/PublicScripts/GameData/SkillSystem/ComponentConfig.cs`
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/ConditionEvaluator.cs`
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs`
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/ISkillComponent.cs`
- `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs`
- 6 component files (from Task 6).
- `docs/skill-components/README.md`

- [ ] **Step 1: Run the hard pass criteria from the spec §5.4**

Execute the following grep-based checks. Each must produce the documented result. If any check fails, stop and surface to the user.

```bash
# Check 4: no remaining ctx.blackboard references (excluding the
# ConditionEvalContext field definition in ConditionEvaluator.cs).
# Expected: 0 hits. Use the absolute path; replace $REPO with the
# repository root.
grep -rn "ctx\.blackboard" Assets/PublicScripts/Entity-LevelPublicScripts/ \
  | grep -v "ConditionEvalContext"
# Expected output: empty.

# Check 5: SkillRuntime.cs has no .blackboard field references.
grep -n "\.blackboard" Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs
# Expected output: empty (the field is gone).

# Check 6: SK_Kroos_1.asset opens cleanly. Open Unity, navigate to
# the asset, confirm the inspector shows the three ConditionConfig
# rows with empty groups (no errors in the console). This is a
# manual step — see PlayMode section below for the full check.
```

- [ ] **Step 2: Open Unity and confirm Console is clean**

Open the project. The Console must show zero errors and zero warnings. The `ConditionEvaluator unknown op` warning must not fire.

- [ ] **Step 3: PlayMode smoke (designer in Editor)**

Open a scene that contains an entity with the `SK_Kroos_1.asset` skill
attached. Trigger the entity's attack in PlayMode.

Expected:
- The `ApplyBuff` component's `OnTrigger` is called when the
  `OnBeforeAttack` event fires (its `triggers[]` had `op: 0` in the
  old shape; new `groups = []` is behaviourally equivalent — the
  gate passes).
- `OnTrigger` is called exactly once per `OnBeforeAttack` event
  (the `triggers[]` had a single entry with the same `triggerEvent`).

To exercise the new AND/OR composition (optional, for confidence):
- In the inspector, set `ApplyBuff.triggers[0].groups` to
  `[[{Greater, "hpRate", "0.3"}]]`. With entity HP above 30%, the
  trigger fires; with HP below 30%, it does not.
- Set the group to two units (AND):
  `[[{Greater, "hpRate", "0.3"}, {Equal, "targetCamp", "1"}]]`.
  Vary HP and target camp; both must hold for the trigger to fire.
- Set two groups (OR):
  `[[{Greater, "hpRate", "0.3"}], [{Equal, "has_debuff", "1"}]]`.
  Vary HP and `has_debuff`; either holding is enough.

- [ ] **Step 4: Final commit (if any cleanup was needed)**

If Steps 1-3 found issues, fix them in targeted commits:

```bash
git add <touched files>
git commit -m "fix(skill-conditions): <short description of the fix>"
```

If nothing needed fixing, this step is a no-op.

---

## Self-Review (controller-run, before execution handoff)

- **Spec coverage:** §1 → Tasks 1, 2. §2 → Tasks 3, 6, 8. §3 → Tasks 4, 5. §4 → Task 2. §5 → Task 8.
- **Type consistency:** `ConditionUnit` / `ConditionGroup` / `ConditionConfig` are introduced in Task 1 and referenced identically in Tasks 2, 3, 6, 7. `ConditionEvaluator.Evaluate(List<ConditionGroup>, ConditionEvalContext)` is defined in Task 2 and called identically in Task 6. `List<(ISkillComponent, List<ConditionGroup>)>` is the bucket value type from Task 3 and is the receiving end in Task 6.
- **Placeholder scan:** 0 hits for "TBD" / "TODO" / "implement later" / "类似".
- **Compile ordering:** Tasks 1, 2, 3, 4 are in a state where the project does not compile cleanly mid-task. **This is intentional and expected**: each task is sized to leave the codebase at a known compile state (a "Task 4 is finished" state is "6 component files fail to compile"; a "Task 5 is finished" state is "project compiles, EntitySkillRunner does not"; a "Task 6 is finished" state is "project compiles cleanly"). If a task is paused and resumed, the operator should be able to tell at a glance which tasks have completed and where the project is. The "expected compile state" paragraphs in Steps 3-4 of each task document this.
- **Hot-path performance note:** the dispatch loop allocates one `ConditionEvalContext` per `DispatchToSkill` call. This matches the pre-fix allocation profile (no `HashSet` is allocated per call — see spec §2.5). If profiling shows this is a problem, the context can be pooled. Defer until evidence.
- **No test files created:** project has no test runner. PlayMode smoke in Task 8 Step 3 is the only runtime verification.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-11-condition-trigger-AND-OR.md`. Two execution options:

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — Execute tasks in this session using `executing-plans`, batch execution with checkpoints.

**Which approach?**
