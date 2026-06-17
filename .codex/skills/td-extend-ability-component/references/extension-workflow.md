# Ability Component Extension Workflow

## 1. Confirm The Gap Handoff

Restate:

- atomic requirement and why current composition cannot satisfy it;
- relevant existing component candidates;
- required targets and expected effect;
- required trigger/lifecycle behavior;
- required Blackboard inputs/outputs.

Ask only when missing information changes the assessment or public contract.

## 2. Inspect Existing Usage

Read:

1. `docs/skill-components/README.md`;
2. detailed documents for relevant components;
3. relevant component implementations;
4. every ability asset currently using candidate components;
5. tightly related shared APIs and tests.

Record:

- each candidate's responsibility, parameters, defaults, and behavior;
- Blackboard input/output types;
- trigger and lifecycle assumptions;
- existing asset configurations that must remain compatible;
- nearby components whose responsibility must not be absorbed.

## 3. Assess The Responsibility Gate

For each plausible candidate, confirm:

1. The extension changes how the component performs its existing job, not
   what job it performs.
2. The normal call site and usage context remain recognizable.
3. Existing configurations need no migration and preserve identical behavior.
4. The extension does not require arbitrary reflection, unrelated subsystem
   access, or a large collection of unrelated modes.
5. A separate component would not provide a clearer atomic responsibility.

If no candidate passes, stop without editing. Return why reasonable extension
is unsuitable and a concise new-component recommendation to
`$td-create-ability`.

## 4. Design And Request Approval

Prefer:

- optional parameters with defaults matching old behavior;
- explicit enums/strings and whitelisted fields;
- lazy `ParamList` getters consistent with nearby code;
- defensive no-op behavior for unavailable optional context;
- one-shot warnings for invalid designer-authored values;
- preserving existing Blackboard data shapes and lifecycle rules.

Avoid:

- changing meanings or defaults of existing parameters;
- implicit migrations;
- arbitrary property paths or reflection;
- adding gameplay actions to data-handoff components;
- adding data-handoff concerns to effect components without a concrete need.

Before editing, report:

```text
Atomic requirement:
Why current composition cannot satisfy it:
Existing component to extend:
Why the capability belongs there:
Proposed parameters/API and defaults:
Behavior and lifecycle changes:
Compatibility impact:
Documentation and verification plan:
```

Obtain explicit user approval. If the user rejects or materially changes the
plan, do not implement until the revised plan is approved.

## 5. Implement

Implement only after approval. Modify the smallest necessary surface:

1. Extend the approved component.
2. Modify tightly related shared support code only if required by the approved
   plan.
3. Add or update focused tests when the project has a suitable test surface.
4. Do not modify ability assets; `$td-create-ability` owns asset composition.

Do not broaden the approved plan during implementation. Ask for approval again
if discoveries require a materially different API, behavior change, or scope.

## 6. Document

Update `docs/skill-components/<Component>.md` with:

- new parameters and defaults;
- supported modes/paths/fields;
- compatibility behavior;
- Blackboard types;
- lifecycle and ordering notes;
- limitations and concise examples.

Update `docs/skill-components/README.md` only when its summary is no longer
accurate. Do not create a second component document for an extension.

## 7. Verify

Verify:

- old configurations still behave as before;
- new parameters are optional unless explicitly approved otherwise;
- parameter names/types/defaults match documentation;
- unsupported values fail safely;
- no unrelated component or ability asset changed;
- component documentation and README remain accurate;
- relevant build/tests pass.

Inspect existing ability assets using the component after implementation to
check compatibility assumptions.

## Ability-Creator Handoff

On success, return:

```text
Extended component:
Preserved responsibility:
New parameters/API:
Compatibility behavior:
Recommended ability composition:
Blackboard keys/types:
Ordering/lifecycle notes:
Verification:
```

When no reasonable extension exists, return:

```text
Extension assessment:
Why candidates fail the Responsibility Gate:
Recommended new component responsibility:
Suggested parameters and lifecycle:
```
