# Ability System — Unified Skill / Talent / ExtraAbility Design

> **Document status:** architecture history for Skill, Talent, and ExtraAbility
> unification. Ability asset authoring and runtime sequence semantics are
> defined by [`Ability Steps`](../../ability-steps.md).

**Date:** 2026-06-11
**Status:** Historical architecture reference
**Scope:** Generalize the existing Skill system to host three semantic kinds (Skill / Talent / ExtraAbility) under one runtime, one dispatcher, and one component model. Add a runtime Add/Remove API for ExtraAbility.

---

## 1. Motivation

### 1.1 The three systems today

The current Skill system is documented in `2026-06-07-skill-talent-refactor-design.md` and subsequent specs. It already supports both skills and talents (talents = `SkillConfig` with `sp == null`, no active window). What it does **not** support is:

- **ExtraAbility** — a third category where the ability is added and removed at runtime mid-battle (e.g. by a card system, an item, or another ability's component).
- A **single uniform mental model** for "something composed of components that listens to entity events". Today the active-window gate and SPEngine are entangled with the Skill naming, even though they would also be the right model for a Talent (always active) or an ExtraAbility (active between Add and Remove).

### 1.2 Goals

- **G1.** A single `AbilityRuntime` replaces `SkillRuntime`. It hosts all three kinds with one unified `isActive` field; each kind drives that field from its own source.
- **G2.** `OnSkillBegin` / `OnSkillEnd` are renamed to `OnAbilityBegin` / `OnAbilityEnd` and now fire whenever `isActive` transitions in either direction, regardless of kind. Talents get one such fire at OnInitialize; Skills fire on SPEngine transitions; ExtraAbilities fire on Add/Remove.
- **G3.** `AddExtraAbility(AbilityConfig cfg)` and `RemoveExtraAbility(string runtimeId)` are public APIs. Add constructs a fresh runtime and runs `OnInit`; Remove runs `OnTeardown` and discards. Both broadcast `OnAbilityAdded` / `OnAbilityRemoved` to all abilities on the same entity.
- **G4.** All 22 existing `ISkillComponent` implementations work unchanged. No component code is touched.
- **G5.** Cleanly rename `SkillConfig` → `AbilityConfig`, `skillId` → `abilityId`, `skillName` → `abilityName`, `SkillRuntime` → `AbilityRuntime`, `EntityData.Skills` → `EntityData.Abilities`. (Project is early; no asset migration needed.)

### 1.3 Non-goals (YAGNI)

- Cleaning up the legacy `Entity.skill[]` field and the ~12 prefabs that still use the old API — that's a separate plan.
- Visual scripting / editor UI for the new `Kind` dropdown — the `[CreateAssetMenu]` field is just an enum; existing Inspector already renders enums.
- Networked ability state — single-player.
- Stacking or refreshing an existing ExtraAbility — Add is idempotent; stacking is not supported (see §5.4).

---

## 2. Architecture

### 2.1 One runtime, three kinds

```
AbilityConfig (ScriptableObject)
  abilityId
  abilityName
  description
  icon
  Kind: Skill | Talent | ExtraAbility   ← NEW
  sp: SPConfig                          ← Skill uses, others null
  components: ComponentConfig[]
```

```
AbilityRuntime (POCO, replaces SkillRuntime)
  config : AbilityConfig
  Kind   : passthrough
  runtimeId : string
  spEngine : SPEngine                   ← Skill only
  isActive : bool                       ← single source of truth
  SetActive(bool) : void                ← transition entry point
  OnAbilityBegin / OnAbilityEnd : C# event  ← fires on transition
  components / tickingComponents / componentParams
  componentsByTrigger
  isInitialized
  _wireTeardown : Action                ← unhook the C# events safely
```

### 2.2 Lifecycle, kind by kind

| Phase | Skill | Talent | ExtraAbility |
|---|---|---|---|
| `BuildAbilityRuntime` (PreWarm) | create `SPEngine`; hook `spEngine.OnBegin/End → SetActive`; components OnInit | components OnInit | (no runtime yet) |
| `OnInitialize` | `spEngine.Reset()`; re-OnInit components; dispatch `InitializeEvent` | re-OnInit components; dispatch `InitializeEvent`; **call `SetActive(true)` once** | (no runtime yet) |
| `AddExtraAbility(cfg)` | — | — | build runtime; components OnInit; hook SPEngine if any; **add to list**; `SetActive(true)`; dispatch `OnAbilityAdded`; return `runtimeId` |
| `RemoveExtraAbility(id)` | — | — | dispatch `OnAbilityRemoved`; `SetActive(false)`; components OnTeardown; unwire; remove from list |
| `OnTeardown` (entity death / pool return) | components OnTeardown; `SetActive(false)` to fire `OnAbilityEnd` | components OnTeardown; `SetActive(false)` to fire `OnAbilityEnd` | inline-remove any remaining active extras (same path as `RemoveExtraAbility` minus the id lookup) |

### 2.3 What drives `isActive`

```
Talent        OnInitialize calls SetActive(true) once, never flips again
Skill         SPEngine.OnBegin → SetActive(true); SPEngine.OnEnd → SetActive(false)
ExtraAbility  AddExtraAbility → SetActive(true); RemoveExtraAbility → SetActive(false)
```

The dispatcher never branches on `Kind`. It reads `a.isActive` directly.

### 2.4 The 4 new events (and 2 renamed ones)

```
TriggerEvent changes:
  removed:  OnSkillBegin, OnSkillEnd
  added:    OnAbilityBegin, OnAbilityEnd
  added:    OnAbilityAdded, OnAbilityRemoved
  (the 12 other events are unchanged)
```

```
AbilityBeginEvent    : SkillEvent  { ability }
AbilityEndEvent      : SkillEvent  { ability }
AbilityAddedEvent    : SkillEvent  { ability }
AbilityRemovedEvent  : SkillEvent  { ability }
```

### 2.5 Event routing matrix

| Event | Talent | Skill | ExtraAbility |
|---|---|---|---|
| `OnPreWarm` | ✅ | ✅ | ❌ (runtime doesn't exist yet) |
| `OnInitialize` | ✅ | ✅ | ❌ (same) |
| `OnAbilityBegin` | ✅ (once at OnInitialize) | ✅ (SPEngine fire) | ✅ (on Add) |
| `OnAbilityEnd` | ✅ (on OnTeardown) | ✅ (SPEngine end) | ✅ (on Remove) |
| `OnAbilityAdded` | ✅ (broadcast) | ✅ (broadcast) | ✅ (broadcast + self-listen) |
| `OnAbilityRemoved` | ✅ (broadcast) | ✅ (broadcast) | ✅ (broadcast + self-listen) |
| Other 9 (attack / hurt / anim / die) | ✅ (always active) | ✅ (while active window) | ✅ (while active) |

The first six events bypass the active-window gate; the other nine are gated. See §3.

---

## 3. Dispatcher

### 3.1 Dispatch path is the same shape as today

```csharp
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

    var evalCtx = new ConditionEvalContext {
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
```

`PrepareContext`, `ConditionEvalContext`, and `Evaluate` are unchanged.

### 3.2 C# event → SkillEvent bridge

`BuildAbilityRuntime` and `AddExtraAbility` install two handlers and stash the unhook action:

```csharp
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
```

Calling `SetActive(true)` triggers `OnAbilityBegin` → handler → `DispatchEvent(AbilityBeginEvent)` → broadcast to every ability's bucket. Self-listening is therefore automatic (the broadcasting ability's own bucket is visited too).

### 3.3 SPEngine → SetActive

```csharp
if (cfg.sp != null)
{
    runtime.spEngine = new SPEngine(cfg.sp);
    runtime.spEngine.OnBegin += () => runtime.SetActive(true);
    runtime.spEngine.OnEnd   += () => runtime.SetActive(false);
}
```

`SPEngine` itself is unchanged.

### 3.4 `Tick` is untouched

```csharp
public void Tick(float dt)
{
    for (int i = 0; i < _abilities.Count; i++)
        _abilities[i].spEngine?.OnTick(dt, 1f);
    for (int i = 0; i < _abilities.Count; i++)
    {
        var a = _abilities[i];
        if (!a.isActive) continue;
        for (int c = 0; c < a.tickingComponents.Count; c++)
        {
            var ctx = PrepareContext(a.MakeContext(a.tickingComponents[c], null));
            a.tickingComponents[c].OnTick(ctx, dt);
        }
    }
}
```

The active-window check works for all three kinds uniformly.

---

## 4. PreWarm / OnInitialize / OnTeardown

### 4.1 `PreWarm`

```csharp
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
```

PreWarm is construction-only. Talents are NOT activated here — activation is deferred to OnInitialize (§4.2) so that component re-OnInit happens before the active window opens. Skills wait for `SPEngine`. Extras are not yet constructed.

### 4.2 `OnInitialize`

```csharp
public void OnInitialize()
{
    Subscribe();
    for (int i = 0; i < _abilities.Count; i++)
        _abilities[i].spEngine?.Reset();
    for (int i = 0; i < _abilities.Count; i++)
    {
        var a = _abilities[i];
        for (int c = 0; c < a.components.Count; c++)
        {
            var teardownCtx = a.MakeContext(a.components[c], null);
            a.components[c].OnTeardown(teardownCtx);
            var initCtx = a.MakeContext(a.components[c], null);
            a.components[c].OnInit(initCtx, a.componentParams[c]);
        }
    }
    sharedBlackboard.Clear();
    DispatchEvent(new InitializeEvent());

    // Activate talents AFTER components are re-initialized and InitializeEvent
    // has been dispatched. SetActive(true) on a Talent fires OnAbilityBegin once.
    // Skills stay inactive (they wait for SPEngine); extras don't exist yet.
    for (int i = 0; i < _abilities.Count; i++)
    {
        var a = _abilities[i];
        if (a.Kind == AbilityKind.Talent) a.SetActive(true);
    }
}
```

Order rationale: components are first re-OnInit'd and `InitializeEvent` is broadcast, **then** talents transition to active. This way, anything listening to `OnAbilityBegin` for a talent sees components in their post-Init state. (Skills wait for `SPEngine`. Extras are not yet constructed.)

### 4.3 `OnTeardown`

```csharp
public void OnTeardown()
{
    Unsubscribe();

    for (int i = _abilities.Count - 1; i >= 0; i--)
    {
        var a = _abilities[i];
        if (a.isActive) a.SetActive(false);   // fires OnAbilityEnd
    }

    for (int i = 0; i < _abilities.Count; i++)
    {
        var a = _abilities[i];
        for (int c = 0; c < a.components.Count; c++)
        {
            var ctx = a.MakeContext(a.components[c], null);
            a.components[c].OnTeardown(ctx);
        }
    }

    for (int i = _abilities.Count - 1; i >= 0; i--)
    {
        UnwireRuntime(_abilities[i]);
        _abilities.RemoveAt(i);
    }
}
```

Talents and still-active extras fire `OnAbilityEnd` (so listeners can clean up). Extras and Skills are not re-added; the runner resets via the next `OnInitialize`. We do not need to broadcast `OnAbilityRemoved` on entity teardown — that's reserved for explicit `RemoveExtraAbility` calls. The iteration order (deactivate all, then OnTeardown all, then unwire all) avoids concurrent modification.

---

## 5. AddExtraAbility / RemoveExtraAbility

### 5.1 Public API

```csharp
public string AddExtraAbility(AbilityConfig cfg);
public bool  RemoveExtraAbility(string runtimeId);
public bool  HasExtraAbility(string runtimeId);   // convenience
```

`AddExtraAbility` returns the new `runtimeId` (or the existing one if cfg is already added). `RemoveExtraAbility` returns true if something was removed.

### 5.2 `AddExtraAbility` — full sequence

```
1. validate cfg != null and cfg.Kind == ExtraAbility; else error
2. idempotency check: scan _abilities for a with a.config == cfg; if found, return a.runtimeId
3. runtime = BuildAbilityRuntime(cfg)        // constructs, hooks spEngine, OnInit's components
4. _abilities.Add(runtime)                    // runtime is now reachable from the dispatcher
5. runtime.SetActive(true)                    // fires OnAbilityBegin, broadcasts to all abilities
6. DispatchEvent(new AbilityAddedEvent { ability = runtime })   // broadcasts Added
7. return runtime.runtimeId
```

Order matters: `SetActive(true)` at step 5 happens after `_abilities.Add` at step 4, so any events fired by `OnAbilityBegin`-listeners are routed to the new runtime correctly (it is in the list and is now active).

### 5.3 `RemoveExtraAbility` — full sequence

```
1. find _abilities.IndexOf(a => a.runtimeId == id); else return false
2. validate a.Kind == ExtraAbility; else error
3. DispatchEvent(new AbilityRemovedEvent { ability = a })   // broadcast BEFORE mutation
4. a.SetActive(false)                                        // fires OnAbilityEnd
5. components OnTeardown (each)
6. UnwireRuntime(a)
7. _abilities.RemoveAt(idx)
8. return true
```

`OnAbilityRemoved` fires before `OnTeardown` so listeners can still inspect the soon-to-be-removed ability (read its config, see which components it had).

### 5.4 Idempotency

| Call | Result |
|---|---|
| `AddExtraAbility(cfg)` when `cfg` is not yet added | New runtime, return new id |
| `AddExtraAbility(cfg)` when `cfg` is already added | Return existing id; no new runtime, no events |
| `RemoveExtraAbility(id)` for an id that exists | Remove, fire events, return true |
| `RemoveExtraAbility(id)` for an id that doesn't exist | Return false, no events, no error |

`Add` after `Remove` constructs a fresh runtime (new OnInit, new spEngine if any). The old runtime is gone and not reused.

### 5.5 `runtimeId` generation

```csharp
private int _extraCounter = 0;
private string GenerateRuntimeId(AbilityConfig cfg)
{
    if (cfg.Kind != AbilityKind.ExtraAbility) return cfg.abilityId;
    return $"{cfg.abilityId}_{_extraCounter++}";
}
```

Skills and Talents use `cfg.abilityId` directly. Extras get a per-runner counter suffix to guarantee uniqueness even when the same cfg is added and removed multiple times (id from a previous Add must not collide with a future Add).

---

## 6. Config validation

Warnings logged at `BuildAbilityRuntime` time. Do not block construction.

| Configuration | Action |
|---|---|
| Talent has `OnPreWarm` / `OnInitialize` trigger | allow (Talent is alive at PreWarm) |
| Talent has `OnAbilityBegin` / `OnAbilityEnd` trigger | warn — fires only once at OnInitialize, rare use |
| Skill has `OnAbilityAdded` / `OnAbilityRemoved` trigger | warn — Skill cannot itself be added/removed |
| ExtraAbility has `OnPreWarm` / `OnInitialize` trigger | warn — ExtraAbility does not exist during those phases |
| ExtraAbility has `OnAbilityAdded` / `OnAbilityRemoved` trigger | allow (self-listen is legitimate) |
| Talent / ExtraAbility has `sp != null` | error, abort construction |

`EntityData.Abilities` validation (Editor / PreWarm):

| Config | Requirement |
|---|---|
| `Kind == Skill` | `sp != null` (else warn — a skill with no SP is just a talent) |
| `Kind == Talent` | `sp == null` |
| `Kind == ExtraAbility` | `sp == null` (extras are passive by convention; can be relaxed later if needed) |

---

## 7. File & Folder Layout

### 7.1 New files

```
Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/AbilityRuntime.cs
  (replaces SkillRuntime.cs; same field shape, plus Kind, isActive, SetActive, OnAbilityBegin/End)

Assets/PublicScripts/GameData/SkillSystem/AbilityConfig.cs
  (replaces SkillConfig.cs; same fields, plus Kind)

Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillEvents.cs
  (adds 4 event classes: AbilityBeginEvent, AbilityEndEvent, AbilityAddedEvent, AbilityRemovedEvent)

Assets/Tests/SkillSystem/AbilityRuntimeTests.cs
Assets/Tests/SkillSystem/AbilityAddRemoveTests.cs
```

### 7.2 Modified files

```
Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs
  - rename _skills → _abilities; field type List<SkillRuntime> → List<AbilityRuntime>
  - rename BuildSkillRuntime → BuildAbilityRuntime; takes AbilityConfig
  - rename DispatchToSkill → DispatchToAbility; accepts AbilityRuntime
  - expand bypassActiveGate set to 6 events
  - add WireRuntime / UnwireRuntime helpers
  - add AddExtraAbility / RemoveExtraAbility / HasExtraAbility public methods
  - PreWarm / OnInitialize / OnTeardown read data.Abilities, handle 3 kinds
  - handle "extras add/remove" in OnTeardown

Assets/PublicScripts/GameData/SkillSystem/ComponentConfig.cs
  - add `public enum AbilityKind { Skill, Talent, ExtraAbility }`
  - rename `TriggerEvent.OnSkillBegin` → `OnAbilityBegin`
  - rename `TriggerEvent.OnSkillEnd`   → `OnAbilityEnd`
  - add `TriggerEvent.OnAbilityAdded`, `OnAbilityRemoved`

Assets/PublicScripts/GameData/EntityData/EntityData.cs
  - rename field `Skills` → `Abilities`
```

### 7.3 Files touched for rename propagation

```
Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs
Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs  (replaced by AbilityRuntime.cs)
Assets/PublicScripts/GameData/SkillSystem/SkillConfig.cs                     (replaced by AbilityConfig.cs)
Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Components/Cards.cs
```

All renames are mechanical: `SkillConfig` → `AbilityConfig`, `skillId` → `abilityId`, `skillName` → `abilityName`, `SkillRuntime` → `AbilityRuntime`, `_skills` → `_abilities`, `_selectSkillConfig` → `_selectAbilityConfig`.

### 7.4 Files NOT modified

- All 22 `ISkillComponent` implementations in `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/` — zero changes.
- `SPConfig.cs`, `SPEngine.cs`, `Blackboard.cs`, `ConditionEvaluator.cs`, `ComponentFactory.cs`, `ComponentAutoRegistry.cs`, `ParamList` parser, `BuffParamParser.cs` — zero changes.
- `Entity.cs`, `BuffController`, `AttackBase`, `EntityManager` — zero changes.

### 7.5 Legacy coexistence

`Entity.skill[]` (per `MEMORY.md` ~12 prefabs) is unaffected by this spec. The new `EntitySkillRunner._abilities` list is independent. Cleanup of the legacy field is a separate plan.

---

## 8. Testing Strategy

### 8.1 Unit tests (EditMode, NUnit + Unity Test Framework)

`Assets/Tests/SkillSystem/AbilityRuntimeTests.cs`:

| Test | Verifies |
|---|---|
| `Talent_OnInitialize_FiresAbilityBeginOnce` | Talent's `SetActive(true)` at OnInitialize broadcasts `AbilityBeginEvent` exactly once |
| `Skill_Fire_TriggersSetActiveAndBroadcastsBegin` | `SPEngine.FireSkill()` → `isActive = true` → `AbilityBeginEvent` dispatched |
| `Skill_End_TriggersSetActiveAndBroadcastsEnd` | duration expires → `isActive = false` → `AbilityEndEvent` dispatched |
| `Event_BroadcastToAllAbilities` | any ability's `SetActive` transition reaches every other ability's bucket |
| `ActiveGate_BlocksNonLifecycleEventsToInactive` | `isActive = false` ability does not receive `OnBeforeAttack` etc. |
| `ActiveGate_AllowsLifecycleEventsToInactive` | `OnAbilityBegin/End`, `OnAbilityAdded/Removed` are always delivered |
| `ConfigValidation_TalentWithSp_Aborts` | Talent + `sp != null` → construction aborts with error log |
| `ConfigValidation_ExtraAbilityWithPreWarmTrigger_Warns` | warn logged, construction continues |

`Assets/Tests/SkillSystem/AbilityAddRemoveTests.cs`:

| Test | Verifies |
|---|---|
| `Add_ConstructsRuntime_CallsOnInit_FiresAdded` | `AddExtraAbility(cfg)` returns new id, components' `OnInit` ran, `OnAbilityAdded` dispatched |
| `Add_Idempotent_ReturnsExistingId` | second `Add` of same cfg returns the same id; no second `OnInit`; no duplicate `OnAbilityAdded` |
| `AddAfterRemove_CreatesFreshRuntime` | `RemoveExtraAbility` then `AddExtraAbility` → new runtime, new `OnInit` |
| `Remove_FiresRemoved_FiresOnAbilityEnd_CallsOnTeardown` | `RemoveExtraAbility(id)` order: `OnAbilityRemoved` → `OnAbilityEnd` → `OnTeardown` |
| `Remove_NotFound_ReturnsFalse` | `RemoveExtraAbility("bogus")` returns false, no error |
| `Remove_NotExtraAbility_Rejects` | calling `RemoveExtraAbility` on a Skill's id returns false with error log |
| `OnTeardown_DeactivatesTalentsAndExtras` | entity teardown fires `OnAbilityEnd` for talents and any active extras |
| `OnTeardown_LeavesInactiveExtrasAlone` | already-removed extras are not double-fired |

### 8.2 Component regression

All 22 existing component implementations are untouched. The existing snapshot tests (if any, from earlier specs) should continue to pass without modification. If snapshot tests don't exist, this spec does not require creating them — that is a separate concern tracked elsewhere.

### 8.3 Performance

- Dispatch loop: O(N) per event, N = total ability count. For a typical entity (1-2 skills + 0-3 talents + 0-2 extras), N is small.
- Add/Remove: O(N) for `FindIndex` / `RemoveAt`. Switch to dictionary if profiling shows hot.
- The 6-way type check in `bypassActiveGate` is a few `is` operations per dispatch — negligible.

---

## 9. Definition of Done

- [ ] `AbilityConfig` exists with `Kind` field; `SkillConfig` deleted.
- [ ] `AbilityRuntime` exists with `isActive`, `SetActive`, `OnAbilityBegin/End`, `_wireTeardown`; `SkillRuntime` deleted.
- [ ] `EntityData.Skills` renamed to `EntityData.Abilities`.
- [ ] `TriggerEvent` enum has `OnAbilityBegin/End/Added/Removed`; `OnSkillBegin/End` removed.
- [ ] `SkillEvents.cs` has 4 new event classes.
- [ ] `EntitySkillRunner` compiles and runs with all 22 components unchanged.
- [ ] `AddExtraAbility` / `RemoveExtraAbility` / `HasExtraAbility` work as specified (incl. idempotency).
- [ ] All 8 unit tests in `AbilityRuntimeTests` pass.
- [ ] All 8 unit tests in `AbilityAddRemoveTests` pass.
- [ ] Config validation warnings/errors emit in the documented cases.
- [ ] `LevelMessagePanel` and `Cards.cs` reference the renamed fields/types and compile.
- [ ] Spec committed to `docs/superpowers/specs/2026-06-11-ability-system-design.md`.
- [ ] Implementation plan written via `superpowers:writing-plans`.
