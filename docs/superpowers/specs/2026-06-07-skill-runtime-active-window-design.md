# Skill Runtime Active Window — Design Spec

**Date**: 2026-06-07
**Status**: Approved
**Parent design**: [2026-06-07-skill-talent-refactor-design.md](2026-06-07-skill-talent-refactor-design.md)
**Scope**: Foundational infrastructure for the data-driven SkillSystem. Required by Phase 5 (data-driven skill migration): without this gate, no skill that uses event-mutating components (e.g. `AttackBoost`) can be safely migrated from the legacy `Skill.cs` per-skill scripts. Independent of the data format itself — could be implemented before any `SkillConfig` is created.

## Problem

In the data-driven SkillSystem, every component is a stateless event-handler that mutates a freshly-allocated `SkillEvent` object. There is currently no enforcement of the rule:

> **A skill's effects should only apply while that skill is active.**

The existing `SkillRunner.DispatchToSkill` calls `OnTrigger` on every initialized component for every event, regardless of whether the skill is currently active. This means `AttackBoostComponent` — which is configured on a "Kroos S1" skill — would mutate `OnBeforeAttack` even when Kroos has no skill up. The mutation persists for the lifetime of the single `BeforeAttackEvent` object (the next attack creates a new one), so the bug manifests as: **the attack bonus applies to every attack, not just attacks during the skill window.**

The legacy `Skill.cs` base class avoided this by subscribing in `SkillBegin` and unsubscribing in `SkillEnd`. The new system needs an equivalent mechanism, but applied at the dispatch layer (so no component has to reimplement the gate).

## Goal

Add an **active window** to each `SkillRuntime`, with these invariants:

1. Each skill has a boolean `isActive`. It starts `false`.
2. `isActive` becomes `true` exactly once per fire, in response to a `SkillBeginEvent`. It becomes `false` exactly once per fire, in response to a `SkillEndEvent`.
3. The dispatch layer (SkillRunner) only calls a component's `OnTrigger` on events that occur **while `isActive` is `true`**, **with these exceptions**:
   - `SkillBeginEvent` and `SkillEndEvent` themselves are **always** dispatched (they are the mechanism that flips `isActive`).
   - `DeathEvent` and `BeforeDieAnimationEvent` are always dispatched (death is global; a skill ending in the middle of a death sequence should not strand the entity without its death handlers).
4. The fixed-tick dispatch path (`ITickingComponent.OnTick`) follows the same rule: only call `OnTick` for components of an active skill.
5. Components are **not** required to know about the window. They continue to mutate `SkillEvent` fields and to maintain whatever state they want; the dispatch layer handles the "is this in scope" question.
6. Instant-firing skills (`SPConfig.skillDuration <= 0`) emit a `SkillBeginEvent` followed immediately by a `SkillEndEvent`, so even zero-duration skills have a window — it just has zero width. Components that need to do work on Begin/End can do so; components that fire on `OnBeforeAttack` will not see the event (no attack happens in that zero-width window).

## Non-Goals

- No "always-on" component flag (YAGNI). If a future skill needs a passive aura, that is a new component, not a new flag.
- No changes to the `ComponentConfig` schema. No new parameters.
- No changes to any of the 21 existing components. They are dispatch-time unaware and stay that way.
- No changes to the `SkillConfig` or `EntityData` schemas. `skillDuration=0` keeps its current "instant" meaning.

## Design

### `SPEngine` (Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SPEngine.cs)

Add two C# events:

```csharp
public event Action OnBegin;
public event Action OnEnd;
```

Rewrite `FireSkill` so it always emits `OnBegin`, and emits `OnEnd` immediately after when the skill is instant:

```csharp
public void FireSkill()
{
    if (!CanBegin()) return;
    if (_currentCharge > 0) _currentCharge--;
    else _currentSp = 0f;
    _isActive = true;
    if (_cfg.skillDuration > 0f) _currentDuration = _cfg.skillDuration;
    _wasFiredThisTick = true;
    if (_cfg.recoverForbidDuringSkill) _recoverForbid++;
    OnBegin?.Invoke();
    if (_cfg.skillDuration <= 0f) EndSkill();
}
```

Rewrite `EndSkill` so it always emits `OnEnd`:

```csharp
public void EndSkill()
{
    if (!_isActive) return;
    _isActive = false;
    _currentDuration = 0f;
    if (_cfg.recoverForbidDuringSkill && _recoverForbid > 0) _recoverForbid--;
    OnEnd?.Invoke();
}
```

The internal `_isActive` field is **not** the same as `SkillRuntime.isActive`. It means "skill duration in progress" inside `SPEngine`. The two flags serve different purposes:

| Flag | Meaning | Set by |
|---|---|---|
| `SPEngine._isActive` | "duration timer is running" | `SPEngine` itself |
| `SkillRuntime.isActive` | "skill window is open for component dispatch" | `SkillRunner` in response to `OnBegin`/`OnEnd` |

They happen to have the same lifetime for non-instant skills, but for instant skills (`skillDuration <= 0`), `SPEngine._isActive` flips on then off within the same `FireSkill` call, while `SkillRuntime.isActive` would flip on then off in response to two separate event dispatches.

### `SkillRuntime` (Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs)

Add:

```csharp
public bool isActive;

public void OpenActiveWindow()  { isActive = true;  }
public void CloseActiveWindow() { isActive = false; }
```

These are called by `SkillRunner` in response to `SPEngine.OnBegin` / `OnEnd`.

### `SkillRunner` (Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRunner.cs)

Three changes:

1. **Subscribe to SPEngine events at construction.** In `BuildSkillRuntime`, after creating the `SPEngine`, attach two lambdas:

   ```csharp
   if (cfg.sp != null && cfg.sp.totalSp > 0)
   {
       runtime.spEngine = new SPEngine(cfg.sp, () => OnSkillFire(runtime));
       runtime.spEngine.OnBegin += () => OnSkillBeginWindow(runtime);
       runtime.spEngine.OnEnd   += () => OnSkillEndWindow(runtime);
   }
   ```

2. **Two new private methods**:

   ```csharp
   private void OnSkillBeginWindow(SkillRuntime runtime)
   {
       runtime.OpenActiveWindow();
       DispatchEvent(new SkillBeginEvent { skill = runtime });
   }

   private void OnSkillEndWindow(SkillRuntime runtime)
   {
       DispatchEvent(new SkillEndEvent { skill = runtime });
       runtime.CloseActiveWindow();
   }
   ```

   Note: `OnSkillEndWindow` closes the window **after** dispatching the `SkillEndEvent`, so any component that responds to `SkillEndEvent` still sees `isActive = true` while handling it. This is symmetric with `OnSkillBeginWindow` opening the window **before** dispatch.

3. **Gate the dispatch** in `DispatchToSkill`:

   ```csharp
   private void DispatchToSkill(SkillRuntime s, SkillEvent evt, TriggerEvent te = TriggerEvent.OnInitialize)
   {
       bool alwaysDispatch = evt is SkillBeginEvent
                          || evt is SkillEndEvent
                          || evt is DeathEvent
                          || evt is BeforeDieAnimationEvent;
       if (!s.isActive && !alwaysDispatch) return;

       for (int i = 0; i < s.components.Count; i++)
       {
           var comp = s.components[i];
           var ctx = s.MakeContext(comp, evt);
           ctx.sharedBlackboard = sharedBlackboard;
           ctx.entity = _entity;
           comp.OnTrigger(ctx);
       }
   }
   ```

4. **Gate the tick** in `FixedUpdate`'s per-component loop:

   ```csharp
   for (int i = 0; i < _skills.Count; i++)
   {
       var s = _skills[i];
       if (!s.isActive) continue;
       for (int c = 0; c < s.tickingComponents.Count; c++) { ... }
   }
   ```

## Effects on Existing Components

None. The 21 existing components are written against `ISkillComponent` and inspect `ctx.currentEvent`. They are dispatch-time unaware. The new gate filters **before** `OnTrigger` is called, so their logic is unchanged.

In particular, `AttackBoostComponent` does **not** need the `requiresActive` parameter discussed earlier in brainstorming. Its current implementation:

```csharp
public void OnTrigger(SkillContext ctx)
{
    if (!(ctx.currentEvent is BeforeAttackEvent bae)) return;
    bae.multiplyer *= _multiplier;
    bae.cumbo = _cumboSet;
}
```

…works correctly under the new gate: when Kroos has no skill up, `SkillRuntime.isActive == false`, `DispatchToSkill` returns early, `OnTrigger` is not called, and the attack goes out at its base multiplier. When the skill fires, the window opens, the next `OnBeforeAttack` mutates the freshly-allocated event object, the attack fires with the boosted values, and on the following tick the window closes. No state to reset.

## Edge Cases

| Case | Behavior |
|---|---|
| Skill with `totalSp = 0` (no SP engine) | `SkillRuntime.isActive` stays `false` forever. Components are never called. This is fine — such a skill is effectively a no-op placeholder. If a future skill needs to be "always on", it would use a non-zero `skillDuration` with an `OnTick` component, or be expressed as a different system entirely. |
| Instant skill (`skillDuration = 0`) | `FireSkill` → `OnBegin` → `EndSkill` → `OnEnd`. Both `SkillBeginEvent` and `SkillEndEvent` dispatch in the same frame. The window opens and closes in the same tick. Any component that mutates state in `SkillBeginEvent.OnTrigger` and reads it in `SkillEndEvent.OnTrigger` will work; any component that waits for `OnBeforeAttack` will not see it (the next attack is outside the window). |
| `OnDeath` during an inactive window | The event is in the `alwaysDispatch` whitelist, so it is delivered to all skills' components regardless of `isActive`. This prevents death handlers from being stranded. |
| Multiple skills on the same entity | Each `SkillRuntime` has its own `isActive`. A `AttackBoost` on skill A does not affect skill B's components. |
| `OnTeardown` | `SkillRunner.OnTeardown` already calls `s.components[c].OnTeardown(ctx)` for every component on every skill, ignoring `isActive`. That is correct — teardown is structural, not scope-related. |

## Tests

New test file: `Assets/Tests/SkillSystem/SkillRuntimeWindowTests.cs` covering:

- `IsActive_FlipsOnOnBegin_FlipsOffOnEnd`
- `DispatchToSkill_OutsideWindow_DoesNotCallComponent`
- `DispatchToSkill_InsideWindow_CallsComponent`
- `InstantSkill_FiresBeginThenEnd_SameTick`
- `DeathEvent_OutsideWindow_IsDelivered`
- `BeginEvent_IsAlwaysDelivered_OutsideWindow` (so the window can open from any state)
- `EndEvent_IsAlwaysDelivered_InsideWindow` (so cleanup always runs)
- `OnTick_OutsideWindow_NotCalled` (ticking components are gated)
- `OnTick_InsideWindow_Called`

Existing `AttackBoostComponentTests`, `ApplyBuffComponentTests`, etc. should continue to pass without modification, because their setup constructs a context directly and calls `OnTrigger` — bypassing the dispatch gate. The gate is only on the dispatch path, not on the component contract.

## Files Changed

| File | Change |
|---|---|
| `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SPEngine.cs` | Add `OnBegin`/`OnEnd` events; rewrite `FireSkill`/`EndSkill` |
| `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs` | Add `isActive` field + `OpenActiveWindow`/`CloseActiveWindow` methods |
| `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRunner.cs` | Subscribe to SPEngine events; add `OnSkillBeginWindow`/`OnSkillEndWindow`; gate `DispatchToSkill` and `FixedUpdate` tick loop |
| `Assets/Tests/SkillSystem/SkillRuntimeWindowTests.cs` | New test file (8 tests) |

## Out of Scope

- Per-component `alwaysOn` flag (not needed)
- Per-skill `windowBypass` list (not needed; the four-event whitelist is global)
- A `MatchTrigger`-style filter (already exists at `SkillRuntime.MatchesTrigger` but is not currently used; not needed for this change)
- Property drawers for `ParamList` / `ComponentConfig` (Phase 8 work)
- Migration of any specific legacy skill to data (separate Phase 5 task; this spec is the prerequisite for all of them)
