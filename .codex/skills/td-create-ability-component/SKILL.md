---
name: td-create-ability-component
description: Create a new reusable AbilitySystem step operation in this TD Unity project from an approved operation contract. Use when registered operations cannot compose a behavior and no focused extension of a component-backed operation fits. Select a native AbilityStepOp POCO for per-activation, yielding, cancellable, or sequence-scoped behavior; use AbilityComponentBase only for component lifecycle behavior. Create only new source/meta/test/documentation files and update the relevant operation index. Never modify existing runtime code or ability assets.
---

# Create TD Ability Operation

Create one reusable atomic operation without changing existing runtime code.
Read [references/component-workflow.md](references/component-workflow.md)
before starting.

## Hard Scope

Allowed changes:

- new native op `.cs` file(s) under
  `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/StepOps/`; or
- new component-backed op `.cs` file(s) under
  `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/`;
- corresponding new Unity `.meta` file(s);
- focused new test files and their `.meta` files;
- one operation document under `docs/ability-ops/` or
  `docs/skill-components/`, matching the selected implementation form;
- updating `docs/ability-steps.md` and, for component-backed operations,
  `docs/skill-components/README.md` to index the operation.

Forbidden changes:

- modifying an existing operation or component;
- modifying events, enums, runners, Blackboard, factories, APIs, entity
  systems, assets, prefabs, or other runtime code;
- configuring the calling ability asset.

If the behavior cannot be implemented through a new attributed operation and
current public APIs, stop. Explain the required existing-code change and ask
the user to expand the task.

## Implementation Form

Use a native `AbilityStepOp` when execution starts as the sequence reaches the
step. This is the default for operations that yield, return `Failed`, require
`OnCancel`, own mutable state per activation, or interact with nested steps.

Use an `AbilityComponentBase` implementation only when its responsibility
requires the component lifecycle: one instance bound to the configured step,
ability initialization, immediate `OnTrigger`, ability-wide `OnTick`, and
ability teardown. Confirm that shared component state is safe for the intended
`reentry` policies.

## Generality Rule

Generalize around the owning subsystem boundary, not around one ability name.

Example: if the request changes `AttackBase.DamageType`, inspect nearby
`AttackBase` behavior and consider whether a restrained component that can
change selected attack behavior fields, such as `DamageType` and
`EntityOrderLogic`, is appropriate.

Do not force unrelated fields into one operation, expose arbitrary reflection,
or build a highly generic framework. Prefer a small explicit parameter set
that serves several plausible abilities and remains easy to document.

## Required Input

Require an operation contract containing:

- canonical snake_case `op` name;
- atomic requirement;
- arguments and defaults;
- target selection;
- expected effect;
- `Completed`/`Running`/`Failed` behavior;
- cancellation and teardown behavior;
- nested sequence fields, if any;
- Blackboard inputs/outputs, if any.

Ask for missing information when it changes the public contract. Discover
implementation details from the project.

## Completion

Create the operation, document every argument and lifecycle behavior, update
the relevant index, and verify registration, cancellation, nested execution,
and any Blackboard contract.

Return a concise handoff contract containing the canonical op, implementation
form, arguments, execution status, cancellation behavior, Blackboard keys and
types, step placement, and verification.
