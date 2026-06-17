---
name: td-create-ability
description: Create or complete AbilityConfig .asset files in this TD Unity project from natural-language ability requirements. Use when the user names an ability asset and describes SP rules, effects, targeting, animation overrides, ranges, timing, or lifecycle behavior. Decompose the ability into atomic behaviors, select documented ability components, compose triggers and Blackboard handoffs, fill the asset, and verify it. When current components cannot compose an atomic behavior, hand the gap to $td-extend-ability-component for extension assessment before proposing a new component.
---

# Create TD Ability

Create an ability by composing existing components. Treat the asset as data.
Do not add or modify runtime code unless the user explicitly approves either
an existing-component extension or a new-component handoff.

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
3. Invoke `$td-extend-ability-component` with the atomic requirement, why
   current composition fails, relevant component candidates, required
   behavior, targets, triggers/lifecycle, and Blackboard handoffs.
4. Let that skill assess whether a reasonable extension exists, report the
   extension plan, obtain user approval, and implement an approved extension.
5. Resume ability composition after a successful extension handoff.
6. If the extension skill reports that no reasonable extension exists,
   propose a new component contract:
   responsibility, parameters, targets, trigger/lifecycle behavior,
   Blackboard inputs/outputs, and expected effect.
7. Ask the user for approval to create it. Only after approval, invoke
   `$td-create-ability-component`, then resume this workflow.

Do not silently weaken or omit an effect to avoid the handoff.

## Completion

Fill the requested asset, preserve unrelated serialized fields, verify enum
values and Named Resources from current project files, and validate the final
component order and lifecycle symmetry.

Summarize:

- atomic behavior decomposition;
- selected components and trigger timing;
- Blackboard handoffs;
- any existing component extended;
- any new component created;
- verification performed and remaining manual checks.
