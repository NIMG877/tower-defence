---
name: td-extend-ability-component
description: Assess whether an unmet AbilitySystem behavior in this TD Unity project belongs in an existing component-backed step operation, report the extension plan, obtain user approval, and implement the approved extension. Use after $td-create-ability finds that registered operations cannot compose an atomic behavior and identifies a plausible component-backed op. Preserve the op's responsibility and defaults, update its current rules[].steps[].args documentation, recursively verify asset usage and reentry safety, and recommend a separate operation when the Responsibility Gate fails.
---

# Extend TD Component-Backed Operation

Extend one component-backed operation only when the capability stays within
its responsibility, step usage context, and documented defaults.

Read [references/extension-workflow.md](references/extension-workflow.md)
before starting.

## Required Input

Require an operation-gap handoff containing:

- atomic requirement and why registered operations cannot express it;
- candidate canonical component-backed op;
- required targets and expected effect;
- required rule trigger, step placement, and `reentry` behavior;
- required `args` and defaults;
- required Blackboard inputs/outputs.

Discover implementation details from the project. Ask only when missing
information changes the extension assessment or public contract.

## Assessment And Approval

Before editing:

1. Inspect the gap, nearby operations, recursive asset usage, and owning
   subsystem.
2. Decide whether a focused extension passes the Responsibility Gate.
3. If reasonable, report the component-backed op to extend, why the capability
   belongs there, proposed args/API and defaults, component lifecycle behavior,
   reentry impact, and documentation/verification plan.
4. Obtain explicit user approval for that plan.
5. Implement only after approval.

If no reasonable extension exists, do not ask approval for an extension and
do not modify runtime code. Return the assessment and a concise
separate-operation recommendation to `$td-create-ability`.

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
- changing documented argument semantics or defaults without explicit
  approval;
- adding arbitrary reflection or a generic behavior-execution framework.

If an approved extension cannot be implemented within this scope, stop.
Explain why it needs a separate operation or broader subsystem change.

## Responsibility Gate

Proceed only when all are true:

1. The extension remains inside the component's existing responsibility.
2. The component name still accurately describes the result.
3. Steps that omit added optional args retain their documented behavior.
4. The new API is explicit, restrained, and reusable.
5. Complexity remains proportional to the component's purpose.

Examples:

- Reasonable: let `WriteBlackboard` read event/entity context because it still
  writes values to Blackboard.
- Not reasonable: let `WriteBlackboard` apply Buffs or damage because those
  are gameplay effects with separate ownership.

When any gate fails, do not implement the extension. Return a concise
separate-operation recommendation so `$td-create-ability` can obtain approval
before invoking `$td-create-ability-component`.

## Completion

Implement the approved extension, write the complete current operation
contract, verify configured and added argument paths, and return a concise
handoff so `$td-create-ability` can resume composing the ability asset.
