# Skill Trigger Bucket Dispatch — Design Spec

**Date**: 2026-06-10
**Status**: 已实施
**Scope**: Wire `ConditionConfig.triggerEvent` into `EntitySkillRunner` dispatch path; replace
"broadcast-then-self-filter" with pre-bucketed per-event routing.

## 1. Background & Problem

### Current state

`EntitySkillRunner.DispatchToSkill` ([EntitySkillRunner.cs:272-292](../../../Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs#L272-L292))
iterates **all** components of an active skill and calls `OnTrigger` on each — regardless of
event type. Components then filter themselves via hard-coded `if (!(ctx.currentEvent is XxxEvent)) return;`
guards.

```csharp
// Current dispatch — broadcast to all:
for (int i = 0; i < s.components.Count; i++) {
    var comp = s.components[i];
    var ctx = s.MakeContext(comp, evt);
    comp.OnTrigger(ctx);   // ← every event hits every component
}
```

```csharp
// Current component side — per-component type guard:
public void OnTrigger(SkillContext ctx) {
    if (!(ctx.currentEvent is BeforeAttackEvent bae)) return;
    bae.multiplyer *= _multiplier;
}
```

The `ConditionConfig.triggerEvent` field at [ComponentConfig.cs:99](../../../Assets/PublicScripts/GameData/SkillSystem/ComponentConfig.cs#L99)
that the Inspector edits is **never read** by the runtime — verified by a workspace-wide
`\.triggerEvent` grep (single hit, in a planning document only).

### Problems

1. **O(n) dispatch** — every event walks every component of every active skill on every entity.
2. **Inspector lies** — designers set `triggerEvent` in `ConditionConfig` but it has no effect.
3. **Bug surface** — new components must remember to add a type guard; forgotten guard = wrong-event
   triggering.
4. **Conditional logic scattered** — what should be a single declarative routing decision is repeated
   in every component's `OnTrigger` body.

## 2. Goals & Non-Goals

### Goals

- Bucket components by `TriggerEvent` enum at `BuildSkillRuntime` time; dispatch via dictionary lookup.
- Make `ConditionConfig.triggerEvent` the **single source of truth** for "what events does this
  component see".
- Remove all `is XxxEvent` guards inside component `OnTrigger` bodies.
- Drop dead enum values and dead `SkillEvent` classes (`OnDeath`/`DeathEvent`, `OnIntervalTick`/`IntervalTickEvent`).

### Non-Goals (deferred)

- Wiring `ConditionConfig.op` / `leftKey` / `rightValue` — condition evaluation stays unimplemented
  this iteration.
- Touching SPEngine's internal event flow — it's an orthogonal channel.
- Migrating existing `*.asset` SkillConfig resources to add explicit `triggers[]` entries —
  config authors do this on demand; the runtime logs warnings to flag missing config.

## 3. Architectural Overview

```
┌─────────────────────────────────────────────────────────────┐
│ Entity event (OnBeforeAttack / OnAfterHurt / …)             │
│                              │                              │
│                              ▼                              │
│ EntitySkillRunner.OnXxx() ── new XxxEvent ── DispatchEvent  │
│                                                              │
│  for each SkillRuntime s:                                    │
│      DispatchToSkill(s, evt)                                 │
│          ┌──────────────────────────────┐                    │
│          │ if (!isActive && !bypass)    │ ◀── 4 lifecycle    │
│          │     return                   │     events immune  │
│          ├──────────────────────────────┤                    │
│          │ list = bucket[evt.Trigger]   │ ◀── O(1) lookup    │
│          │ if (list == null) return     │                    │
│          ├──────────────────────────────┤                    │
│          │ foreach c in list:           │                    │
│          │     c.OnTrigger(ctx)         │ ◀── zero guards    │
│          └──────────────────────────────┘                    │
└──────────────────────────────────────────────────────────────┘

Bridge (zero-reflection):
    SkillEvent.TriggerEvent ── abstract virtual property; each subclass declares its enum value
    ConditionConfig.triggerEvent ── same enum value, used directly as dictionary key

Bucket lifecycle:
    BuildSkillRuntime() ── built once; never rebuilt on OnInitialize/OnTeardown
    (mirrors components list lifecycle)
```

### Key principles

1. **Bucket key is the `TriggerEvent` enum itself**, not `System.Type`. Each `SkillEvent` subclass
   declares its enum via `public override TriggerEvent TriggerEvent => …`. No reflection, no
   attribute machinery, IL2CPP-friendly.
2. **All events including lifecycle (PreWarm/Initialize/SkillBegin/SkillEnd) go through the bucket**;
   the `isActive` gate is the only thing that's lifecycle-aware (those 4 events bypass it).
3. **Component `OnTrigger` bodies contain zero event-type guards**. The dispatcher guarantees the
   event matches the declared trigger; components direct-cast.
4. **Interface lifecycle methods (`OnInit`/`OnTeardown`/`OnTick`) are separate from `SkillEvent`
   routing.** They run via `BuildSkillRuntime`/`OnInitialize`/`OnTeardown`/`Tick` paths as today.

## 4. Data Structures

### 4.1 `SkillEvent` base class

```csharp
namespace SkillSystem
{
    public abstract class SkillEvent
    {
        // Every concrete SkillEvent must declare which TriggerEvent enum it routes to.
        // Using abstract (not virtual + default) so the compiler catches missing overrides.
        public abstract TriggerEvent TriggerEvent { get; }
    }
}
```

### 4.2 `SkillEvent` subclass overrides

Each of these 13 classes adds a one-line override:

| Class | Enum value |
|---|---|
| `PreWarmEvent` | `OnPreWarm` |
| `InitializeEvent` | `OnInitialize` |
| `BeforeAttackEvent` | `OnBeforeAttack` |
| `AfterAttackEvent` | `OnAfterAttack` |
| `BeforeTakeDamageEvent` | `OnBeforeTakeDamage` |
| `AfterTakeDamageEvent` | `OnAfterTakeDamage` |
| `AttackSuccessfullyEvent` | `OnAttackSuccessfully` |
| `AttackInterruptEvent` | `OnAttackInterrupt` |
| `BeforeHurtEvent` | `OnBeforeHurt` |
| `AfterHurtEvent` | `OnAfterHurt` |
| `AttackAnimBeginEvent` | `OnAttackAnimBegin` |
| `BeforeDieAnimationEvent` | `OnBeforeDieAnimation` |
| `SkillBeginEvent` | `OnSkillBegin` |
| `SkillEndEvent` | `OnSkillEnd` |

`DeathEvent` and `IntervalTickEvent` are **deleted entirely** (see §6.1).

### 4.3 `TriggerEvent` enum cleanup

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
    // REMOVED: OnDeath, OnIntervalTick
    OnSkillBegin, OnSkillEnd,
}
```

### 4.4 `SkillRuntime` additions

```csharp
public class SkillRuntime
{
    // — existing fields preserved —
    public SkillConfig config;
    public SPEngine spEngine;
    public Blackboard blackboard = new Blackboard();
    public List<ISkillComponent> components = new List<ISkillComponent>();
    public List<ITickingComponent> tickingComponents = new List<ITickingComponent>();
    public List<ParamList> componentParams = new List<ParamList>();
    public bool isInitialized;
    public bool isActive;

    // — NEW —
    public Dictionary<TriggerEvent, List<ISkillComponent>> componentsByTrigger
        = new Dictionary<TriggerEvent, List<ISkillComponent>>();

    // existing OpenActiveWindow / CloseActiveWindow / MakeContext unchanged
}
```

Design rationale:
- **Bucket lives inside `SkillRuntime`**, not at `EntitySkillRunner` level — preserves per-skill
  encapsulation, simplifies the `isActive` gate, and is rebuilt-free across pool reuse.
- **Bucket is read-only after `BuildSkillRuntime`.** No locking, no maintenance in `OnInitialize`/`OnTeardown`.
- **Value is `List<ISkillComponent>`** (not `HashSet`) — preserves the existing "config-declaration
  order" execution semantics within a single event.

## 5. Build & Dispatch Logic

### 5.1 `BuildSkillRuntime` — bucket population

Inside the existing component-creation loop ([EntitySkillRunner.cs:133-151](../../../Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs#L133-L151)),
after `runtime.components.Add(inst)`:

```csharp
// — added after components.Add / componentParams.Add / (optional) tickingComponents.Add —
int triggerCount = 0;
if (ccfg.triggers != null)
{
    for (int t = 0; t < ccfg.triggers.Length; t++)
    {
        var trig = ccfg.triggers[t];
        if (trig == null) continue;
        var te = trig.triggerEvent;
        if (!runtime.componentsByTrigger.TryGetValue(te, out var list))
        {
            list = new List<ISkillComponent>();
            runtime.componentsByTrigger[te] = list;
        }
        list.Add(inst);
        triggerCount++;
    }
}

// Warn if a non-ticking component will never see OnTrigger.
// ITickingComponent gets implicit pass: OnTick is its primary channel.
if (triggerCount == 0 && !(inst is ITickingComponent))
{
    Debug.LogWarning(
        $"[SkillRuntime] Component {ccfg.componentType} in skill {cfg.skillId} "
        + "declares no triggers — it will never receive OnTrigger. "
        + "Add ConditionConfig entries to triggers[] if this is unintended.");
}
```

### 5.2 `DispatchToSkill` — bucket lookup

```csharp
private void DispatchToSkill(SkillRuntime s, SkillEvent evt)
{
    // Active-window gate: skill components only see events while the skill is firing,
    // EXCEPT for these 4 lifecycle events which always pass the gate. They still need
    // to be declared in config.triggers[] to be received.
    bool bypassActiveGate = evt is PreWarmEvent
                         || evt is InitializeEvent
                         || evt is SkillBeginEvent
                         || evt is SkillEndEvent;
    if (!s.isActive && !bypassActiveGate) return;

    if (!s.componentsByTrigger.TryGetValue(evt.TriggerEvent, out var list)) return;
    for (int i = 0; i < list.Count; i++)
    {
        var comp = list[i];
        var ctx = s.MakeContext(comp, evt);
        ctx.sharedBlackboard = sharedBlackboard;
        ctx.entity = _entity;
        comp.OnTrigger(ctx);
    }
}
```

### 5.3 `EntitySkillRunner.Tick` — IntervalTickEvent removal

Current ([EntitySkillRunner.cs:111-121](../../../Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs#L111-L121)):

```csharp
for (int c = 0; c < s.tickingComponents.Count; c++)
{
    var ctx = s.MakeContext(s.tickingComponents[c], new IntervalTickEvent { dt = dt });
    ctx.sharedBlackboard = sharedBlackboard;
    s.tickingComponents[c].OnTick(ctx, dt);
}
```

After:

```csharp
for (int c = 0; c < s.tickingComponents.Count; c++)
{
    // IntervalTickEvent removed: OnTick has dt as an explicit parameter; ctx.currentEvent is null.
    var ctx = s.MakeContext(s.tickingComponents[c], null);
    ctx.sharedBlackboard = sharedBlackboard;
    ctx.entity = _entity;
    s.tickingComponents[c].OnTick(ctx, dt);
}
```

`SkillContext.currentEvent == null` during OnTick is acceptable; the `dt` parameter is the
canonical timing source, and an audit confirms no component reads `ctx.currentEvent` inside its
`OnTick` body.

## 6. Migration

### 6.1 Delete unused types

| File | Action |
|---|---|
| `SkillEvents.cs:82` | Delete `public class DeathEvent : SkillEvent { }` |
| `SkillEvents.cs:83-86` | Delete `public class IntervalTickEvent : SkillEvent { public float dt; }` |
| `ComponentConfig.cs:74-85` | Remove `OnDeath` and `OnIntervalTick` from `TriggerEvent` enum |

No production code dispatches `DeathEvent`. `IntervalTickEvent` is only constructed in
`EntitySkillRunner.Tick`; that construction is removed in §5.3.

### 6.2 Component cleanup — remove `is XxxEvent` guards

Apply to 6 components (pattern: direct-cast since dispatcher guarantees event type):

| File | Before | After |
|---|---|---|
| `AttackMultiplierBoost.cs:13-17` | `if (!(ctx.currentEvent is BeforeAttackEvent bae)) return; bae.multiplyer *= _multiplier;` | `var bae = (BeforeAttackEvent)ctx.currentEvent; bae.multiplyer *= _multiplier;` |
| `SetAttackCombo.cs:13-…` | `if (!(ctx.currentEvent is BeforeAttackEvent bae)) return;` | `var bae = (BeforeAttackEvent)ctx.currentEvent;` |
| `CampDamageModifierComponent.cs:17` | `if (!(ctx.currentEvent is BeforeTakeDamageEvent btd)) return;` | `var btd = (BeforeTakeDamageEvent)ctx.currentEvent;` |
| `LockHpShieldComponent.cs:20` | `if (!(ctx.currentEvent is BeforeHurtEvent bhe)) return;` | `var bhe = (BeforeHurtEvent)ctx.currentEvent;` |
| `DeathSpawnComponent.cs:29` | `if (!(ctx.currentEvent is BeforeDieAnimationEvent)) return;` | (just delete — no payload needed) |
| `SelfDamageOnEventComponent.cs:17` | guard + `!atd.isDeadly` check | `var atd = (AfterTakeDamageEvent)ctx.currentEvent; if (!atd.isDeadly) return;` (drop guard, keep deadly check) |

### 6.3 Special case — `ApplyBuffComponent`

[ApplyBuffComponent.cs:68](../../../Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/ApplyBuffComponent.cs#L68)
uses `is BeforeTakeDamageEvent btd ? btd.target : null` for **opportunistic payload extraction**, not
type-guarding — the component is designed to be triggerable by multiple event kinds. After this
refactor, the same component instance can be bucketed into multiple triggers, and its `OnTrigger`
will see different `SkillEvent` subclasses depending on which event fired.

Replacement strategy:

```csharp
private List<Entity> ResolveTargets(SkillContext ctx)
{
    if (!string.IsNullOrEmpty(_inputKey))
        return ctx.blackboard.Get<List<Entity>>(_inputKey, null);

    // Opportunistic target lookup from the current event's payload.
    // Falls back to ctx.entity when the event doesn't carry a target field.
    Entity t = _toSelf
        ? ctx.entity
        : ExtractTargetFromEvent(ctx.currentEvent) ?? ctx.entity;
    if (t == null || t.buffController == null) return null;
    return new List<Entity> { t };
}

private static Entity ExtractTargetFromEvent(SkillEvent evt)
{
    return evt switch
    {
        BeforeTakeDamageEvent btd => btd.target,
        AfterTakeDamageEvent atd  => atd.target,
        BeforeAttackEvent bae     => bae.target,
        AfterAttackEvent aae      => aae.target,
        _ => null,
    };
}
```

Switch-expression covers the events that carry an `Entity target` field; anything else returns null
and the caller falls back to `ctx.entity`. This keeps `ApplyBuff` usable across multiple trigger
configs without re-introducing guard-style early returns.

### 6.4 `EntitySelectorRadiusEffectComponent` — nested sub-component pattern

[EntitySelectorRadiusEffectComponent.cs:31-42](../../../Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/EntitySelectorRadiusEffectComponent.cs#L31-L42)
creates sub-components ad-hoc and invokes their `OnTrigger` with `currentEvent = ctx.currentEvent`.
This **bypasses the dispatcher's type guarantee** — the sub-component receives whatever event the
parent received.

This is **out of scope** for this refactor. The implicit contract is:
> "When configuring `EntitySelectorRadiusEffect`, its `subComponentType` MUST handle the same event
> types as the parent's `triggers[].triggerEvent` configuration. Otherwise the sub-component's
> direct-cast will NRE."

A code comment is added to `OnTrigger` documenting this constraint; deeper fixes (declarative event
metadata on component classes, etc.) belong to a later iteration.

### 6.5 Existing SkillConfig assets — no forced migration

25 existing `*.asset` SkillConfig resources under `Assets/Resources/Prefabs/.../ReferenceAssets/`
are **not** migrated by this change. Most likely have empty `triggers[]` arrays. Behavior after this
refactor:

- Components with empty `triggers[]` → enter the warning path in §5.1 → log warning at PreWarm,
  do not receive `OnTrigger`, but `OnInit`/`OnTick`/`OnTeardown` still run.
- Config authors add `triggers[]` entries on demand as they revisit each skill.

This is a deliberate trade-off: no risky bulk asset migration, at the cost of a deferred per-asset
content pass. The warnings make the gap discoverable.

## 7. Risks

| Risk | Mitigation |
|---|---|
| **A. Config misroute → NRE** — a designer points `AttackMultiplierBoost` at `OnDeath` (or any event without a `BeforeAttackEvent` payload), the direct-cast crashes. | Accepted by design. Crashing loudly beats silent no-op. Future work: editor-side validation that cross-checks `triggers[].triggerEvent` against the component's declared expected event(s). |
| **B. Existing SkillConfig assets sit silent** — `triggers[]` empty → no `OnTrigger`. | Warning at PreWarm; visible in console for QA pass. Configs migrate incrementally. |
| **C. `ITickingComponent` zero-trigger warning noise** — tick-only components shouldn't trigger the warning. | §5.1's `!(inst is ITickingComponent)` clause suppresses. |
| **D. `EntitySelectorRadiusEffectComponent` nested invocation** — sub-component sees parent's event type, may not match sub's expected type. | Comment in source code; out of scope. |
| **E. `IntervalTickEvent` removal breaks unknown callers** — anything that reflects on it or constructs one outside `Tick()`. | Single workspace grep confirms one production callsite (`EntitySkillRunner.Tick`), which is migrated in §5.3. |

## 8. Verification

### 8.1 Compile gate
- Unity compiles without errors after all edits.
- The `abstract TriggerEvent` on `SkillEvent` will fail compile if any subclass forgot the override
  — this is the intended safety net.

### 8.2 Runtime smoke (manual)
- Launch a level scene with at least one deployed operator that has an `AttackMultiplierBoost`
  configured trigger.
- Trigger an attack; confirm the multiplier still applies (damage values match pre-refactor).
- Trigger a skill that uses `ApplyBuff` with `_toSelf = false` on an OnBeforeTakeDamage trigger;
  confirm buff lands on the target (target extraction path of §6.3 verified).
- Watch console: no NRE, no unexpected warnings from skills known to declare triggers.

### 8.3 Negative test
- Pick one SkillConfig asset; deliberately remove its `triggers[]` entry for a known component;
  verify the warning fires at PreWarm and the component is silent at runtime.

## 9. Out-of-Scope (deferred)

- `ConditionConfig.op` / `leftKey` / `rightValue` wiring (separate iteration; same routing
  infrastructure will be extended).
- Editor-side validation of `triggers[].triggerEvent` against component-declared event metadata.
- Migration of existing `*.asset` SkillConfig resources.
- `EntitySelectorRadiusEffectComponent` nested sub-component dispatch model.
- Replacing the per-skill bucket with a per-entity flat dispatcher (premature optimization).
