---
name: td-extend-ability-component
description: Assess whether an unmet AbilitySystem behavior in this TD Unity project can be implemented by reasonably extending an existing component, report the extension plan and obtain user approval, then implement an approved extension. Use after $td-create-ability finds that current components cannot compose an atomic behavior. Preserve existing configurations, update component documentation, and verify compatibility. Exit and recommend a new component when no reasonable extension preserves an existing component's responsibility and normal usage context.
---

# Extend TD Ability Component

Assess an ability-component gap, then extend one existing component only when
the extension preserves its responsibility, usage context, and existing
configurations.

Read [references/extension-workflow.md](references/extension-workflow.md)
before starting.

## Required Input

Require a component-gap handoff containing:

- atomic requirement and why current composition cannot satisfy it;
- relevant existing component candidates, if known;
- required targets and expected effect;
- required trigger/lifecycle behavior;
- required Blackboard inputs/outputs.

Discover implementation details from the project. Ask only when missing
information changes the extension assessment or public contract.

## Assessment And Approval

Before editing:

1. Inspect the gap, nearby components, existing usage, and owning subsystem.
2. Decide whether a focused extension passes the Responsibility Gate.
3. If reasonable, report the component to extend, why the capability belongs
   there, proposed parameters/API and defaults, behavior/lifecycle changes,
   compatibility impact, and documentation/verification plan.
4. Obtain explicit user approval for that plan.
5. Implement only after approval.

If no reasonable extension exists, do not ask approval for an extension and
do not modify runtime code. Return the assessment and a concise new-component
recommendation to `$td-create-ability`.

## Hard Scope

Allowed changes:

- the approved existing ability component;
- its `docs/skill-components/<Component>.md`;
- tightly related shared AbilitySystem support code only when required by the
  approved extension;
- focused tests for the extension;
- `docs/skill-components/README.md` only when its component summary must
  change.

Forbidden changes:

- configuring or modifying ability `.asset` files;
- implementing unrelated cleanup or refactors;
- changing another component's behavior merely for convenience;
- changing existing parameter semantics or defaults without explicit approval;
- adding arbitrary reflection or a generic behavior-execution framework.

If an approved extension cannot be implemented within this scope, stop.
Explain why it needs a new component or broader subsystem change.

## Responsibility Gate

Proceed only when all are true:

1. The extension remains inside the component's existing responsibility.
2. The component name still accurately describes the result.
3. Existing configurations retain their behavior by default.
4. The new API is explicit, restrained, and reusable.
5. Complexity remains proportional to the component's purpose.

Examples:

- Reasonable: let `WriteBlackboard` read event/entity context because it still
  writes values to Blackboard.
- Not reasonable: let `WriteBlackboard` apply Buffs or damage because those
  are gameplay effects with separate ownership.

When any gate fails, do not implement the extension. Return a concise
new-component recommendation so `$td-create-ability` can obtain approval
before invoking `$td-create-ability-component`.

## Completion

Implement the approved extension, update its documentation, verify existing
and new behavior, and return a concise handoff so `$td-create-ability` can
resume composing the ability asset.
