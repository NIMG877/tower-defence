# Ability Steps

The AbilitySystem stores behavior as event-driven rules. Each rule owns an
ordered step sequence, so one matching trigger can select targets, apply an
effect, yield, and continue without splitting the flow across unrelated
top-level entries.

## Stored shape

```text
AbilityConfig
└─ rules[]: AbilityRuleConfig
   ├─ triggers[]: ConditionConfig
   ├─ reentry: IgnoreWhileRunning | Restart | Parallel
   └─ steps[]: StepConfig
      ├─ op: string
      ├─ args: ParamList
      ├─ condition: ConditionGroup[]
      ├─ steps[]: StepConfig
      └─ elseSteps[]: StepConfig
```

`rules` is the top-level execution list. `op` is an open string registry, so
adding an operation does not require an enum or serialized-schema change.
`args` uses the `(key, type, value, fromBlackboard)` encoding documented in
[Skill Components](skill-components/README.md).

Conceptual authoring example (the Unity YAML expands every `args` entry):

```yaml
rules:
  - triggers:
      - { triggerEvent: OnAfterAttack, groups: [] }
    reentry: IgnoreWhileRunning
    steps:
      - { op: select_targets, args: { ... } }
      - { op: apply_damage, args: { ... } }
      - { op: delay, args: { seconds: 0.3 } }
      - { op: apply_damage, args: { ... } }
      - { op: wait_until, condition: [ ... ] }
```

Author assets with `rules[].steps[]` and canonical operation names. The runtime
also recognizes serialized component payloads and PascalCase component names as
compatibility inputs; validators and authoring tools use the canonical shape.

## Trigger and sequence semantics

Each `ConditionConfig` selects one `TriggerEvent`. Its `groups` are OR-ed;
units inside a group are AND-ed. An empty group list passes unconditionally.
When an event is dispatched, every matching trigger entry is evaluated against
the entity's shared Blackboard. Every passing entry requests a start of its
owning rule.

Steps run in array order. An operation either completes immediately or remains
running. Immediate operations advance to the next step in the same call. A
running operation pauses only its sequence; the runner advances it on later
ticks until it completes, then continues with the next step.

The runner snapshots all existing executions across all abilities before it
advances any of them. A sequence started while that snapshot is being advanced
runs its immediate `Tick(0)` prefix, but it does not consume that FixedUpdate's
`deltaTime`; its first timed advance happens on the next runner tick.

This creates a **synchronous prefix**: all leading immediate steps finish
before event dispatch returns. Put combat-event mutation and other
dispatch-time effects in that prefix. Once a sequence yields (`delay`, an
unsatisfied `wait_until`, or a yielding nested operation), the source event has
already continued through the rest of the game pipeline; a later step cannot
retroactively change its completed outcome.

One pump advances at most 1,024 synchronous step transitions. This prevents an
unbounded immediate loop from freezing a frame. If a sequence exhausts that
budget, its remaining "immediate" work resumes on a later runner tick and is
therefore outside the dispatch-time prefix. Keep event mutation near the front
and put an actual yielding operation inside intentionally unbounded loops.

Sequences belonging to different rules are independent. They communicate only
through the entity's shared Blackboard and the game state touched by their
operations.

## Reentry

`reentry` applies when the same rule is requested again while it has at least
one running sequence. A sequence made entirely from immediate operations has
already finished before another event can reenter it.

| Value | Behavior |
|---|---|
| `IgnoreWhileRunning` | Ignore the new request and let the existing sequence continue. |
| `Restart` | Cancel the existing sequence, clean up its active operations, and start again from step 0. |
| `Parallel` | Start another independent sequence alongside existing runs. Each run owns its own operation execution state. |

Reentry is per rule, not per operation name. Repeated passing trigger entries
therefore pass through the same policy.

`Parallel` isolates sequence cursors and native POCO operation objects only.
Parallel runs still share the entity Blackboard, and component-backed steps
still share the one component instance bound to that configured step. Use
run-specific Blackboard keys and avoid stateful component adapters when runs
must not interfere.

## Canonical operations

`RegisteredOps` exposes canonical names only. The 24 component-backed
operations use snake_case. PascalCase component names are lookup aliases.

| Canonical `op` | Backing implementation / reference |
|---|---|
| `apply_abnormal_state` | [ApplyAbnormalState](skill-components/ApplyAbnormalState.md) |
| `apply_animation_override` | [ApplyAnimationOverride](skill-components/ApplyAnimationOverride.md) |
| `apply_buff` | [ApplyBuff](skill-components/ApplyBuff.md) |
| `apply_damage` | [ApplyDamage](skill-components/ApplyDamage.md) |
| `apply_impulse` | [ApplyImpulse](skill-components/ApplyImpulse.md) |
| `attack_behavior_override` | [AttackBehaviorOverride](skill-components/AttackBehaviorOverride.md) |
| `attack_behavior_restore` | [AttackBehaviorRestore](skill-components/AttackBehaviorRestore.md) |
| `attack_event_value_modifier` | [AttackEventValueModifier](skill-components/AttackEventValueModifier.md) |
| `attack_range_override` | [AttackRangeOverride](skill-components/AttackRangeOverride.md) |
| `attack_range_restore` | [AttackRangeRestore](skill-components/AttackRangeRestore.md) |
| `charge_attack_damage_modifier` | [ChargeAttackDamageModifier](skill-components/ChargeAttackDamageModifier.md) |
| `charge_attack_reserve_pool` | [ChargeAttackReservePool](skill-components/ChargeAttackReservePool.md) |
| `charge_state_controller` | [ChargeStateController](skill-components/ChargeStateController.md) |
| `destroy_abnormal_state` | [DestroyAbnormalState](skill-components/DestroyAbnormalState.md) |
| `destroy_buff` | [DestroyBuff](skill-components/DestroyBuff.md) |
| `destroy_entity` | [DestroyEntity](skill-components/DestroyEntity.md) |
| `filter_targets` | [EntityFilter](skill-components/EntityFilter.md) |
| `force_reset_attack` | [ForceResetAttack](skill-components/ForceResetAttack.md) |
| `random_roll` | [RandomRoll](skill-components/RandomRoll.md) |
| `remove_animation_override` | [RemoveAnimationOverride](skill-components/RemoveAnimationOverride.md) |
| `select_targets` | [EntitySelector](skill-components/EntitySelector.md) |
| `share_attack_target` | [ShareAttackTarget](skill-components/ShareAttackTarget.md) |
| `shared_target_extra_attack` | [SharedTargetExtraAttack](skill-components/SharedTargetExtraAttack.md) |
| `write_blackboard` | [WriteBlackboard](skill-components/WriteBlackboard.md) |
| `delay` | Yielding primitive; see below. |
| `wait_until` | Yielding condition primitive; see below. |
| `branch` | Conditional composite primitive; see below. |
| `loop` | Repeating composite primitive; see below. |
| `spawn_entity` | Entity creation primitive; see below. |

## Primitive operations

Primitive operations use the same `StepConfig` and `ParamList` representation
as component-backed operations. Arguments marked as BB-capable honor
`fromBlackboard`; their value is resolved when execution first reaches that
operation.

### `delay`

| Argument | Type | Default | Meaning |
|---|---|---:|---|
| `seconds` | `Float`, BB-capable | `0` | Duration measured by accumulated ability-runner tick delta. |

The timer starts with a zero-delta execution during trigger dispatch, then
advances on later runner ticks. Negative tick deltas and negative durations are
clamped to zero. A zero duration completes synchronously.

### `wait_until`

| Field | Type | Meaning |
|---|---|---|
| `condition` | OR-of-AND expression | Predicate re-evaluated while the operation is running. |

Evaluates the step's `condition` using the normal OR-of-AND condition
semantics. It completes synchronously when the condition already passes;
otherwise it re-evaluates on later ticks. An empty condition passes. There is
no timeout argument; cancellation or teardown is the escape path for a
condition that never becomes true.

### `branch`

| Field | Type | Meaning |
|---|---|---|
| `condition` | OR-of-AND expression | Predicate sampled once on entry. |
| `steps` | nested steps | Selected when the predicate passes. |
| `elseSteps` | nested steps | Selected when the predicate fails. |

Evaluates `condition` once when reached. A passing condition executes nested
`steps`; a failing condition executes `elseSteps`. The branch completes when
the selected child sequence completes. An empty condition selects `steps`.

### `loop`

| Argument/field | Type | Default | Meaning |
|---|---|---:|---|
| `count` | `Int`, BB-capable | `1` without `condition`; `-1` with one | Maximum iterations. A negative value means no count limit. |
| `condition` | OR-of-AND expression | empty | Continue predicate, evaluated before every iteration. |
| `steps` | nested steps | empty | Loop body. |

Both limits apply when both are present: the loop starts an iteration only
while the count has not been reached and the condition passes. A count of zero
completes without running the body. Every iteration receives fresh child
execution state, while iterations share the event context and Blackboard. A
negative count with an empty/always-true condition is unbounded and must contain
a yielding operation to avoid repeatedly consuming the synchronous pump
budget.

### `spawn_entity`

Creates an entity through `EntityPoolManager` and `EntityManager`. A missing
pool, invalid id, or failed creation returns `Failed` and terminates the owning
sequence.

| Argument | Type | Default | Meaning |
|---|---|---:|---|
| `entityId` | `String`, BB-capable | empty | Combined id in `category-number` or `category:number` form. |
| `entityCategory` | `String`, BB-capable | empty | Category used when `entityId` is absent. |
| `entityNumber` | `Int`, BB-capable | `0` | Number paired with `entityCategory`. |
| `positionMode` | `String`, BB-capable | `self` | `self`, `eventTarget`, `blackboard`, or `fixed`. |
| `positionKey` | `String`, BB-capable | empty | BB key for `positionMode=blackboard`; accepts `Vector2`, `Vector2Int`, or `Entity`. |
| `position` | `Vector2Int`, BB-capable | `(0,0)` | Position for `positionMode=fixed`. |
| `offset` | `Vector2Int`, BB-capable | `(0,0)` | Added after resolving the base position. |
| `camp` | `Int`, BB-capable | source camp, otherwise `1` | Spawned entity camp; negative selects the default. |
| `placement` | `String`, BB-capable | `auto` | `static`, `auto` (pool data decides), or another value for movable. |
| `orientation` | `Int`, BB-capable | `0` | Static-entity orientation. |
| `pathSerial` | `Int`, BB-capable | `0` | Movable-entity path serial. |
| `outputKey` | `String`, BB-capable | empty | If set, stores the spawned `Entity` in the shared Blackboard. |

`eventTarget` resolves damage-event targets and hurt-event origins; without a
usable event target it falls back to self. An unreadable Blackboard position
falls back to `(0,0)`. Asset data should use this operation instead of directly
instantiating scene objects.

## Operation lifecycle

Native POCO operations and component-backed adapters have related but distinct
lifecycles.

For every native step activation, the registry factory creates a fresh
`AbilityStepOp`. The executor calls `OnInit` once, `OnTick(..., 0)` immediately,
and then `OnTick` on runner ticks while the status is `Running`. Completion or
failure calls `OnTeardown`. Cancellation (including `Restart`) calls `OnCancel`
and then `OnTeardown`. `Failed` terminates the whole sequence.

Component-backed steps bind one existing `AbilityComponentBase` instance per
configured step when the runtime is built. Entity initialization calls the
component's `OnInit`; reaching the step calls `OnTrigger` and the adapter
completes immediately. While the ability is active, the runner continues to
call the component's normal `OnTick`; ability removal/runner teardown calls its
normal `OnTeardown`.

Every actual `SetActive` state transition first cancels executions from the
current state lifetime, then changes the state and dispatches the corresponding Begin
or End rules. Thus `OnAbilityEnd` rules may yield during a normal inactive
lifetime, but a later `SetActive(true)` cancels their unfinished work before
`OnAbilityBegin` is dispatched. Runner teardown and extra-ability removal cancel
again after End dispatch, so an End rule fired as part of destruction or removal
is guaranteed only its synchronous prefix.

`StepConfig` and its args are configuration and must not hold mutable execution
state. Native POCO state is isolated between `Parallel` runs because every
activation has its own operation object. Component-backed adapters share their
bound component instance, so a stateful component is not made
parallel-safe merely by selecting `RuleReentry.Parallel`; review that component
before using parallel reentry.

## Extending the registry

An operation is a POCO runtime implementation registered under a unique
canonical lowercase snake_case string that describes one reusable behavior.
The auto-registry discovers attributed operations at
startup:

```csharp
[RegisterAbilityStepOp("my_operation")]
public sealed class MyOperation : AbilityStepOp
{
    public override void OnInit(AbilityContext ctx)
    {
        // Resolve ctx.step.args and initialize this activation's state.
    }

    public override AbilityStepStatus OnTick(AbilityContext ctx, float deltaTime)
    {
        return AbilityStepStatus.Completed;
    }

    public override void OnCancel(AbilityContext ctx) { }
    public override void OnTeardown(AbilityContext ctx) { }
}
```

Code-owned registration can instead call
`AbilityStepOpRegistry.Register(canonicalOp, factory, aliases)`. Registry
factories must return a new operation object for every call. `RegisteredOps`
contains sorted canonical names; `IsRegistered`, `Create`, and
`ResolveCanonical` also accept compatibility aliases. A duplicate canonical
name or alias fails registration with `InvalidOperationException`; registration
order never silently changes which implementation an asset resolves.

Do not add an enum member or dispatch `switch` for an operation. Register one
canonical name and configure aliases only for serialized compatibility.
Unknown operation names are
configuration errors and should be caught by asset validation before play.
