---
name: td-create-ability
description: Create or complete AbilityConfig .asset files in this TD Unity project from natural-language ability requirements. Use when the user names an ability asset and describes SP rules, effects, targeting, animation overrides, ranges, timing, or lifecycle behavior. Decompose the ability into atomic behaviors, select documented ability components, compose triggers and Blackboard handoffs, fill the asset, and verify it. If existing components cannot satisfy an atomic behavior, obtain user approval before handing a component specification to $td-create-ability-component.
---

# Create TD Ability

Create an ability by composing existing components. Treat the asset as data;
do not add or modify runtime code unless the user explicitly approves the
component-creation handoff.

Read [references/ability-workflow.md](references/ability-workflow.md) before
starting.

## Required Input

Require:

- target `.asset` file or an unambiguous asset name;
- ability behavior and effects;
- enough SP/lifecycle information to determine activation and duration.

Ask a concise question when missing information changes behavior materially.
Examples: unknown target, missing trigger timing, unclear duration, ambiguous
replacement slot/resource, or whether a persistent effect must be restored.
Do not ask for values that can be discovered from the project or safely
derived from the description.

## Component Discovery

Always read `docs/skill-components/README.md` first to obtain the component
overview. Then read only the detailed component documents relevant to the
atomic behaviors. This progressive disclosure order is mandatory.

If documentation and code disagree, inspect the component implementation and
report the discrepancy. Use current code behavior.

## Component Gap

When no existing component can implement an atomic behavior:

1. Stop before editing runtime code.
2. Explain the missing atomic behavior and why existing components fail.
3. Propose a component contract: responsibility, parameters, targets,
   trigger/lifecycle behavior, Blackboard inputs/outputs, and expected effect.
4. Ask the user for approval to create it.
5. Only after approval, invoke `$td-create-ability-component` with that
   contract.
6. Resume this workflow after the new component is complete.

Do not silently weaken or omit an effect to avoid the handoff.

## Completion

Fill the requested asset, preserve unrelated serialized fields, verify enum
values and Named Resources from current project files, and validate the final
component order and lifecycle symmetry.

Summarize:

- atomic behavior decomposition;
- selected components and trigger timing;
- Blackboard handoffs;
- any new component created;
- verification performed and remaining manual checks.
