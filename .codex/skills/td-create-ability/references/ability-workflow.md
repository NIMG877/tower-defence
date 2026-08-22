# Ability Creation Workflow

## 1. Locate And Read

1. Locate the requested ability asset under `Assets/`.
2. Read its YAML and preserve unrelated values such as `icon`.
3. Read nearby ability assets for current serialization examples.
4. Read `docs/ability-steps.md` and `docs/skill-components/README.md`.
5. Read:
   - `Assets/PublicScripts/GameData/AbilitySystem/AbilityConfig.cs`;
   - `Assets/PublicScripts/GameData/AbilitySystem/ComponentConfig.cs`;
   - `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/AbilityStepRuntime.cs`
     when operation or lifecycle behavior is relevant.
6. Inspect related entity scripts, animations, attacks, talents, and prefabs
   when they define the requested behavior.

Treat the user's description as authoritative. Never assume serialized enum
numbers or operation names from memory.

## 2. Clarify Requirements

Resolve only behaviorally important omissions:

- SP recovery, activation, consumption, initial value, charges, duration, and
  manual close behavior;
- effect values, target source, target count/order, damage type, and duration;
- trigger event and condition;
- sequence timing and the first operation allowed to yield;
- reentry behavior while a sequence is running;
- persistent state that requires removal or restoration;
- exact animation slots and Named Resource names.

Ask the user when these cannot be discovered or safely inferred.

## 3. Decompose Into Rules And Steps

Build a short internal table:

| Rule trigger | Reentry | Ordered behavior | Blackboard flow | Cleanup |
|---|---|---|---|---|
| `OnAbilityBegin` | `IgnoreWhileRunning` | select self → apply buff | `buffs` output | destroy on end |
| `OnAfterAttack` | `Parallel` | select targets → damage → delay → damage | target list | none |

Separate:

- `SPConfig` behavior;
- rule-level triggers and conditions;
- immediate event-mutation steps;
- yielding steps such as `delay` and `wait_until`;
- nested `branch`/`loop` sequences;
- persistent effects and cleanup rules;
- target selection and Blackboard handoffs;
- animation operations and refresh/reset steps.

## 4. Discover Operations Progressively

1. Read the canonical operation table in `docs/ability-steps.md`.
2. Use the primitive-operation sections when sequencing, waiting, branching,
   looping, or spawning is required.
3. Read `docs/skill-components/README.md` for component-backed operations.
4. Read only the detailed documents for candidate operations.
5. Inspect implementations for undocumented parameters, lifecycle behavior,
   statefulness, or safety concerns.

Use literal `args` for constants. Use Blackboard when execution data must pass
between steps, including selected entities, created Buff records, animation
override records, snapshots, counters, or computed values.

## 5. Check Coverage And Handoff

Every atomic behavior must map to:

- `SPConfig` behavior;
- one or more registered operations;
- an approved focused extension of a component-backed operation; or
- an approved reusable operation implementation.

For a plausible component-backed extension, invoke
`$td-extend-ability-component` with:

```text
Atomic requirement:
Why registered operations cannot express it:
Candidate component-backed op:
Required targets and effect:
Required rule/step lifecycle:
Required args:
Required Blackboard inputs/outputs:
Expected effect:
```

If no extension passes its Responsibility Gate, define an operation contract:

```text
Canonical op:
Atomic responsibility:
Arguments/defaults:
Target selection:
Completed/Running/Failed behavior:
Cancellation and teardown behavior:
Nested sequence fields, if any:
Blackboard inputs/outputs:
Expected effect:
Why the operation is reusable:
```

Obtain approval, then invoke `$td-create-ability-component`.

## 6. Compose The Asset

Create one `AbilityRuleConfig` for each coherent trigger/condition and ordered
sequence. Configure:

1. `triggers[]` for dispatch and rule conditions;
2. `reentry` for repeated starts while the sequence is running;
3. `steps[]` in exact execution order;
4. `condition`, `steps`, and `elseSteps` for composite operations;
5. `args.entries[]` for operation parameters.

Keep combat-event mutation in the synchronous prefix. Once a step yields, later
steps cannot affect an event whose dispatch has completed.

Use separate Begin and End rules for persistent apply/remove or override/restore
flows. Keep Blackboard keys and types identical across producer and consumer
steps. Prefer namespaced keys such as `<abilityId>_targets` and
`<abilityId>_animation_overrides`.

For animation operations:

- verify Named Resource names in the entity's `AnimationResources` asset;
- use group resources for group slots;
- map a general Attack request to the exact project slots required by the
  entity.

## 7. Verify

Recursively inspect every rule and nested step. Check:

- requested fields and SP values;
- trigger enum serialization and condition grouping;
- `reentry` behavior for every yielding rule;
- canonical `op` registration;
- argument names, types, values, and `fromBlackboard` flags;
- nested `steps` and `elseSteps` contents;
- parallel CSV lengths where an operation requires them;
- Blackboard producer/consumer key and type symmetry;
- cleanup, cancellation, and teardown behavior;
- component-backed state safety under `Parallel` reentry;
- Named Resource existence and single/group kind;
- behavior during an in-flight attack and active-state transition.

Run focused tests or a relevant build when they cover the change. State that
the asset still needs a Unity PlayMode smoke test when automated coverage does
not execute the configured behavior. `Assets/Resources/` is ignored by this
repository's Git configuration, so asset edits may not appear in normal
`git status`.
