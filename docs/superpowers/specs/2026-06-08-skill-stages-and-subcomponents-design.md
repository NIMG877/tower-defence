# Skill Stages & Sub-Components — Deferred Design Spec

> **Document status:** deferred design archive. Ability sequencing, branching,
> and looping use [`rules[].steps[]`](../../ability-steps.md).
> `ComponentConfig.subComponents` is not part of the active data model.

**Date**: 2026-06-08
**Status**: Archived deferred design
**Parent design**: [2026-06-07-skill-talent-refactor-design.md](2026-06-07-skill-talent-refactor-design.md)
**Scope**: Reserved design for two `ComponentConfig` features that were stubbed in the original refactor and **removed on 2026-06-08** because they were dead code (no runtime consumed them). This spec records the data model + runtime design for when they are re-introduced.

## Why this doc exists

On 2026-06-08 we removed the following from the data model and runtime:

- `ComponentConfig.stages : StageConfig[]`
- `ComponentConfig.subComponents : ComponentConfig[]`
- `StageConfig` class (with `name / enterDuration / transitionOn / nextStageOnTransition / enterEffects / tickEffects / exitEffects`)
- `StageStateMachineComponent` (orphan component that read from `ParamList` strings instead of from `ComponentConfig.stages`)

`grep` confirmed no runtime code consumed `stages` / `subComponents`. `SkillRunner.BuildSkillRuntime` did not pass them to `ComponentFactory.Create`, and `StageStateMachineComponent.OnInit` decoded stage info from a `ParamList` string ("stageNames=a|b|c" / "stageDurations=1.5|0.3|0.5") — i.e. the data flow was bypassed.

The current project has no skill that needs multi-stage state machines or wrapper components. We delete to keep the data model honest. **This spec is the recovery kit** for when such a skill appears.

## Problem (re-introduction trigger)

A skill that cannot be expressed with the current flat `ComponentConfig[]` model. Typical cases:

1. **Multi-stage combo**: "raise sword (0.4s) → strike (0.2s) → recover (0.6s)" — each stage has its own effects.
2. **Charge-and-release**: "charge for up to 2.0s; on release, fire projectile scaled by charge time" — a per-tick effect during charge, an exit effect on release.
3. **Multi-shot burst**: "fire 5 bullets, one every 0.3s" — needs a counter and tick-driven spawn.
4. **Branching**: "if target is enemy, apply DoT; if target is ally, apply heal" — wrapper component that selects sub-component by condition.
5. **Loop**: "while active, every 0.5s apply buff to self" — wrapper that re-invokes sub-component on tick.

Cases 1–3 → need `stages`. Cases 4–5 → need `subComponents`. Both may be needed together (e.g. a multi-stage combo where one stage is itself a branching wrapper).

## Goals

1. **Data-driven**: all of the above expressible via `ComponentConfig` fields, no code changes.
2. **No new dispatch loop**: reuse the existing `SkillRunner.DispatchEvent` + per-component `OnTrigger` / `OnTick` pathway. The state machine and wrappers participate in the same flow, not alongside it.
3. **Blackboard-mediated**: stages share state with sibling components via the existing `SkillRuntime.blackboard` (no new shared state). Stages set keys (`__stage_name`, `__stage_index`, `__stage_elapsed`); other components read them.
4. **Composes with existing conditions**: a `StageConfig.transitionOn` reuses the same `ConditionConfig` (TriggerEvent + op + leftKey + rightValue) as component `triggers`.

## Non-Goals

- **Not** a "skill scripting language". Stages are linear with optional forward-only transitions; no backward edges, no parallel stages, no nested stages. If the design calls for those, that is a different feature.
- **Not** a replacement for `SPConfig`. Stages run *inside* the active window managed by `SPEngine`. They do not change how SP is accumulated, when the skill opens, or how long the active window lasts.
- **Not** a place to put "is skill ready" gates. That remains `SPConfig.openMode` only.
- **Not** a tree of arbitrary nesting depth. `subComponents` is one level of children; if a child needs sub-sub-children, it must itself be a `SubComponentHost` of a different type. This keeps dispatch depth predictable and avoids "where is my blackboard" bugs.

## Design

### Part 1 — `stages` (multi-stage state machine)

#### Data model

Restore `StageConfig` exactly as it was:

```csharp
[Serializable]
public class StageConfig
{
    public string name;
    public float enterDuration = -1f;        // -1 = unlimited
    public ConditionConfig[] transitionOn = Array.Empty<ConditionConfig>();
    public string nextStageOnTransition;     // optional named jump; null/empty = next index
    public ComponentConfig[] enterEffects = Array.Empty<ComponentConfig>();   // fired once on entering
    public ComponentConfig[] tickEffects  = Array.Empty<ComponentConfig>();   // fired every tick while in this stage
    public ComponentConfig[] exitEffects  = Array.Empty<ComponentConfig>();   // fired once on leaving
}

[Serializable]
public class ComponentConfig
{
    public string componentType;
    public ConditionConfig[] triggers = Array.Empty<ConditionConfig>();
    public ParamList parameters = new ParamList();
    public StageConfig[] stages;             // optional, present only when this component is the state machine host
}
```

`stages` is `null` for the 99% of components that are stateless. The single component that owns a non-null `stages[]` per skill is conventionally named `"StageStateMachine"`.

#### Runtime: `StageStateMachineComponent`

```csharp
[RegisterComponent("StageStateMachine")]
public class StageStateMachineComponent : ITickingComponent
{
    private StageConfig[] _stages;     // received from ComponentConfig.stages
    private int _currentIndex;
    private float _stageTimer;
    private List<ISkillComponent> _enterInstances;
    private List<ISkillComponent> _tickInstances;
    private List<ISkillComponent> _exitInstances;
    private bool _isActive = true;
    private bool _enteredCurrent;

    public void OnInit(SkillContext ctx, ComponentConfig cfg, ParamList p)
    {
        // Signat:ure change — OnInit now receives ComponentConfig, not just ParamList.
        _stages = cfg.stages;
        _currentIndex = 0;
        _stageTimer = 0f;
        _enterInstances = BuildSubComponents(ctx, cfg, stages[_currentIndex].enterEffects);
        _tickInstances  = BuildSubComponents(ctx, cfg, stages[_currentIndex].tickEffects);
        _exitInstances  = BuildSubComponents(ctx, cfg, stages[_currentIndex].exitEffects);
        FireEnterEffects(ctx);
    }

    public void OnTrigger(SkillContext ctx) { ... checks transitionOn ... }
    public void OnTick(SkillContext ctx, float dt) { ... increments _stageTimer, dispatches tickEffects, auto-transitions on duration ... }
    public void OnTeardown(SkillContext ctx) { ... fires exitEffects of last stage, disposes sub-instances ... }
}
```

#### Factory wiring (the part that was broken in 2026-06-07 refactor)

`ComponentFactory.Create` and `ComponentAutoRegistry.RegisterAll` need a small change to thread the `ComponentConfig` through:

```csharp
// Old: ComponentFactory.Create(string typeName)
// New: ComponentFactory.Create(string typeName, ComponentConfig cfg)

public interface ISkillComponent
{
    void OnInit(SkillContext ctx, ComponentConfig cfg, ParamList p);
    void OnTrigger(SkillContext ctx);
    void OnTeardown(SkillContext ctx);
}

public interface ITickingComponent : ISkillComponent
{
    void OnTick(SkillContext ctx, float dt);
}
```

This is a **breaking change** to the 21 existing components. To avoid rewriting them all, the migration is:

```csharp
// New interface with default implementation
public interface ISkillComponent
{
    void OnInit(SkillContext ctx, ComponentConfig cfg, ParamList p) => OnInit(ctx, p);
    void OnInit(SkillContext ctx, ParamList p);   // existing signature
    void OnTrigger(SkillContext ctx);
    void OnTeardown(SkillContext ctx);
}
```

The default `OnInit(ctx, cfg, p)` delegates to the old `OnInit(ctx, p)`. The 21 existing components don't change. `StageStateMachineComponent` overrides the new 3-arg version and ignores the 2-arg default. **Result: zero changes to existing components, one signature addition.**

`SkillRunner.BuildSkillRuntime` updates one line:

```csharp
var inst = ComponentFactory.Create(ccfg.componentType, ccfg);   // was: (ccfg.componentType)
```

#### Dispatch integration

`SkillRunner.DispatchToSkill` is unchanged. The state machine is *one of* the skill's components; it receives every event like any other component. Its `OnTrigger` checks `transitionOn` against the current event and stage; its `OnTick` (since it implements `ITickingComponent`) checks the duration timer.

`enterEffects` / `tickEffects` / `exitEffects` are built once in `OnInit` as sub-component instances. They are invoked by the state machine itself, not by the runner. Each sub-effect is a fully-instantiated `ISkillComponent` with its own `OnTrigger`/`OnTick`/`OnTeardown`. They share the parent skill's `blackboard`, so a tick-effect can write `__stage_elapsed` and a transition can read it.

#### Effect lifecycle

| Phase | When | Fires |
| --- | --- | --- |
| `enterEffects` | Once, immediately after entering the stage | each sub-component's `OnInit` then `OnTrigger` (with the same event that caused the transition, if any) |
| `tickEffects` | Every `OnTick` while in this stage | each sub-component's `OnTrigger` (with a synthesized `IntervalTickEvent` of `dt`) and each ticking sub-component's `OnTick` |
| `exitEffects` | Once, just before leaving the stage | each sub-component's `OnTrigger` (with the event that caused the transition) then `OnTeardown` |

#### Blackboard conventions

The state machine sets these blackboard keys, readable by sibling components and by sub-effects:

- `__stage_name : string` — current stage name
- `__stage_index : int` — zero-based index
- `__stage_elapsed : float` — seconds since entering the current stage
- `__stage_transition_event : SkillEvent` — the event that caused the last transition (null on init)

These are reserved keys (prefix `__stage_`). Do not use this prefix in component parameters.

#### Examples

**Charge-and-release skill**

```yaml
# SkillConfig
components:
  - componentType: "StageStateMachine"
    stages:
      - name: "charge"
        enterDuration: 2.0       # max 2s; auto-transition
        enterEffects:
          - componentType: "PlayParticle"
            parameters: { id: "charge_aura", follow: "self" }
        tickEffects:
          - componentType: "ScaleAttackBoost"
            parameters: { factor: "1+0.5*__stage_elapsed" }   # read blackboard
        transitionOn:
          - triggerEvent: OnBeforeHurt
            op: None
          - triggerEvent: OnAttackAnimBegin
            op: None
        # no nextStageOnTransition = goes to next index
      - name: "release"
        enterDuration: 0.0        # instant
        enterEffects:
          - componentType: "SpawnProjectile"
            parameters: { prefab: "fireball", damage: "$charge_damage" }
          - componentType: "PlayParticle"
            parameters: { id: "release_burst" }
        tickEffects: []
```

**Multi-shot burst**

```yaml
- componentType: "StageStateMachine"
  stages:
    - name: "burst1"
      enterDuration: 0.3
      enterEffects:
        - componentType: "SpawnProjectile"
          parameters: { prefab: "bullet", count: "1" }
    - name: "burst2"
      enterDuration: 0.3
      enterEffects:
        - componentType: "SpawnProjectile"
          parameters: { prefab: "bullet", count: "1" }
    # ... up to burst5
```

### Part 2 — `subComponents` (wrapper / host components)

#### Data model

```csharp
[Serializable]
public class ComponentConfig
{
    public string componentType;
    public ConditionConfig[] triggers = Array.Empty<ConditionConfig>();
    public ParamList parameters = new ParamList();
    public ComponentConfig[] subComponents;   // optional, present only when this component is a wrapper host
}
```

`subComponents` is `null` for stateless components. A wrapper host has both `componentType` (the wrapper's own type) and `subComponents[]` (its children).

#### Runtime: common `ISubComponentHost` interface

```csharp
public interface ISubComponentHost : ISkillComponent
{
    IReadOnlyList<ISkillComponent> SubInstances { get; }
    void BuildSubComponents(SkillContext ctx, ComponentConfig cfg);
    void DisposeSubComponents(SkillContext ctx);
}
```

`BuildSubComponents` is called from `OnInit` and is responsible for instantiating each entry of `cfg.subComponents` via `ComponentFactory.Create(c.componentType, c)`, then calling `OnInit(ctx, c, c.parameters)` on each. The host then decides *when* and *how* to invoke them in its own `OnTrigger` / `OnTick` / `OnTeardown`.

#### Concrete wrapper types (suggested)

| `componentType` | Semantics |
| --- | --- |
| `"Selector"` | Picks one sub-component per tick. `parameters.mode = "random" / "roundRobin" / "lowestIndexWithTrueCondition"`. |
| `"ConditionalBranch"` | Reads `parameters.conditionKey` from blackboard, routes to `subComponents[0]` if truthy else `subComponents[1]`. |
| `"Repeat"` | On its own `OnTick`, re-fires the same sub-component. `parameters.interval` (seconds), `parameters.count` (or `infinite`). |
| `"Parallel"` | On its own `OnTrigger`, fires all sub-components in array order in the same event tick. |
| `"Sequence"` | On its own `OnTrigger`, fires `subComponents[0]`; once it self-reports completion (via blackboard key `__done`), fires the next. |

These are the *suggested* first set. The host mechanism is generic; new wrappers are one-class additions.

#### Dispatch integration

`SubInstances` participate in the dispatch flow only via the host. The runner still calls `OnTrigger` on the host; the host's `OnTrigger` decides whether/which sub-components to invoke. Sub-components do **not** appear in `runtime.components` directly — they live in `host.SubInstances`. This avoids double-dispatch (a sub would otherwise receive the event both from the host and from the runner).

If a sub-component needs to be a `ITickingComponent` (e.g. a `Repeat` host's child is a per-tick damage), the host must itself implement `ITickingComponent` and forward `OnTick` to its children's `OnTick`.

#### Blackboard conventions

- `__sub_index : int` — index of the most recently invoked sub-component (for `Sequence` resume).
- `__sub_done : bool` — written by sub-components that want to signal completion to a `Sequence` host.

`__sub_*` is a reserved prefix.

#### Examples

**Branching target effect**

```yaml
- componentType: "ConditionalBranch"
  parameters: { conditionKey: "targetIsEnemy" }
  subComponents:
    - componentType: "ApplyBuff"
      parameters: { buff: "burn", duration: "3" }
    - componentType: "ApplyBuff"
      parameters: { buff: "regen", duration: "3" }
```

**Looping self-buff**

```yaml
- componentType: "Repeat"
  parameters: { interval: "0.5", count: "infinite" }
  subComponents:
    - componentType: "ApplyBuff"
      parameters: { buff: "attack_up", value: "0.1" }
```

### Part 3 — Implementation plan (when re-introduced)

**Do not start until a skill actually needs stages or subComponents.** Below is the work order.

1. Add `ComponentConfig.stages` and `ComponentConfig.subComponents` back. Add `StageConfig` back. (All `Array.Empty<...>()` defaults — no breaking serialized data beyond the keys Unity will re-add.)
2. Change `ISkillComponent.OnInit` to a 3-arg overload with a default that delegates to the existing 2-arg version. Update `ComponentFactory.Create` to take `ComponentConfig`.
3. Implement `StageStateMachineComponent` per the design above. Verify against the "Charge-and-release" example.
4. Implement `ISubComponentHost` interface. Implement `Selector` and `ConditionalBranch` (the two most common wrappers).
5. Add unit tests covering: (a) stage time-based transition, (b) stage event-based transition, (c) stage enter/tick/exit effect ordering, (d) selector's selection modes, (e) branch routing.
6. **Update this doc** with "Status: Implemented" and the actual commit hashes.

### Part 4 — Composition: stages inside subComponents (and vice versa)

Either feature can host the other:

- A `StageConfig.enterEffects` entry can itself be a wrapper host (`componentType: "Selector"`, with its own `subComponents[]`).
- A wrapper's `subComponents` entry can itself be a `StageStateMachine` (a stage machine as one of the branches in a `Selector`).

This is by design — composition is what makes the two features multiply useful. **Caveat**: every level of nesting adds one blackboard lookup cost per event and one extra `OnTrigger` call. Profile before going deeper than 2 levels.

## Out of scope

- Skill scripting languages, visual node editors. Stages are JSON-shaped; we are not building an editor.
- Persistent state across skill activations. Stage state resets when `SPEngine` ends the active window. If cross-fire memory is needed, write to `Entity.Blackboard` from inside the component.
- Network determinism. Stage timing relies on `Time.fixedDeltaTime` and event ordering; multiplayer determinism is a separate concern.
- Per-frame visual state (sprite flashes, tints). Those are effects (`PlayParticle`, `PlayAnimation`), not stage machine outputs.

## References

- Ability authoring and runtime semantics: [Ability Steps](../../ability-steps.md)
- Rule and step data types: [ComponentConfig.cs](../../../Assets/PublicScripts/GameData/AbilitySystem/ComponentConfig.cs)
- Sequence runtime and operation registry: [AbilityStepRuntime.cs](../../../Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/AbilityStepRuntime.cs)
- Parent architecture record: [2026-06-07-skill-talent-refactor-design.md](2026-06-07-skill-talent-refactor-design.md)
- Active-window architecture record: [2026-06-07-skill-runtime-active-window-design.md](2026-06-07-skill-runtime-active-window-design.md)
