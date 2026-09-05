---
name: td-create-ability
description: Create or complete AbilityConfig .asset files in this TD Unity project from natural-language ability requirements. Use when the user names an ability asset and describes SP behavior, effects, targeting, timing, animation, ranges, conditions, or lifecycle behavior. Decompose the behavior into event-driven rules, compose ordered steps with canonical operations and Blackboard handoffs, choose a reentry policy, fill the asset, and verify nested sequences and operation arguments. Route an uncovered behavior through $td-extend-ability-component or $td-create-ability-component with user approval.
---

# Create TD Ability

Create an `AbilityConfig` as data made of `rules[]`. Each rule owns
`triggers[]`, `reentry`, and an ordered `steps[]` sequence. Do not add or
modify runtime code without the approval required by the operation-gap
workflow.

Read [references/ability-workflow.md](references/ability-workflow.md) before
starting.

## Required Input

Require:

- a target `.asset` file or unambiguous asset name;
- the ability's behavior, effects, and targets;
- enough SP and lifecycle information to determine activation and duration.

Ask a concise question only when missing information materially changes the
behavior. Discover enum values, Named Resources, nearby asset conventions, and
implementation details from the project.

## Required References

Read these in order:

1. `docs/ability-steps.md` for the rule, sequence, reentry, lifecycle, and
   operation-registry contract;
2. `docs/skill-components/README.md` for component-backed operations;
3. only the detailed operation documents needed by the requested behavior.

Inspect runtime code when documentation is incomplete or inconsistent. Treat
current code behavior as authoritative and report documentation discrepancies.

## Operation Coverage

Map every atomic behavior to `SPConfig` or one or more registered operations.
Use the primitive operations `delay`, `wait_until`, `branch`, and `loop` when
their documented contracts fit. Use component-backed
operations through their canonical snake_case names.

When registered operations cannot express an atomic behavior:

1. Stop before editing runtime code.
2. If the capability belongs within an existing component-backed operation's
   responsibility, invoke `$td-extend-ability-component` with the gap contract.
3. If no focused extension fits, define a reusable operation contract covering
   its canonical name, arguments, targets, execution status, cancellation,
   Blackboard inputs/outputs, and expected effect.
4. Obtain user approval, then invoke `$td-create-ability-component` with that
   operation contract.
5. Resume asset composition after the approved implementation is verified.

Do not weaken or omit an effect to avoid the handoff.

## Completion

Preserve unrelated serialized fields. Verify:

- rule triggers and `reentry` policies;
- step order and every nested `steps`/`elseSteps` sequence;
- canonical operation names and `args` key/type/value/fromBlackboard fields;
- synchronous-prefix requirements for event mutation;
- Blackboard producer/consumer key and type symmetry;
- cancellation, teardown, and persistent-effect cleanup;
- Named Resources and serialized enum values from current project files.

Summarize the rule decomposition, selected operations, Blackboard handoffs,
runtime extensions or operations created, verification performed, and any
remaining Unity PlayMode checks.
