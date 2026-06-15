---
name: td-create-ability-component
description: Create new reusable AbilitySystem component classes in this TD Unity project from a component contract describing requirements, parameters, and expected effects. Use only when a new component is needed. Analyze the owning subsystem, choose useful but restrained generality, create only new component source/meta files, add its docs/skill-components documentation, and update docs/skill-components/README.md. Never modify existing runtime components or other project code; exit and ask the user when new files alone cannot implement the behavior.
---

# Create TD Ability Component

Create a reusable atomic component without changing existing runtime code.
Read [references/component-workflow.md](references/component-workflow.md)
before starting.

## Hard Scope

Allowed changes:

- new component `.cs` file(s) under
  `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/`;
- corresponding new Unity `.meta` file(s);
- one new `docs/skill-components/<Component>.md`;
- updating `docs/skill-components/README.md` to index the component.

Forbidden changes:

- modifying any existing component;
- modifying events, enums, runners, Blackboard, factories, APIs, entity
  systems, assets, prefabs, or other runtime code;
- configuring the calling ability asset.

If the behavior cannot be implemented using only new component files and
existing public APIs, stop immediately. Explain the required existing-code
change and ask the user to expand the task. Do not work around the boundary.

## Generality Rule

Generalize around the real subsystem ownership boundary, not around one
ability name.

Example: if the request changes `AttackBase.DamageType`, inspect nearby
`AttackBase` behavior and consider whether a restrained component that can
change selected attack behavior fields, such as `DamageType` and
`EntityOrderLogic`, is appropriate.

Do not force unrelated fields into one component, expose arbitrary reflection,
or build a highly generic framework. Prefer a small explicit parameter set
that serves several plausible abilities and remains easy to document.

## Required Input

Require a component contract containing:

- atomic requirement;
- parameters and defaults;
- target selection;
- expected effect;
- trigger/lifecycle expectations;
- Blackboard inputs/outputs, if any.

Ask for missing information when it changes the public contract. Discover
implementation details from the project.

## Completion

Create the component, document every parameter and lifecycle behavior, update
the README index, and verify that every registered concrete component has a
document and README entry.

Return a concise handoff contract for the ability creator: registered name,
parameters, recommended triggers, Blackboard keys/types, and ordering notes.
