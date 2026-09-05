# Component-Backed Operation Extension Workflow

## 1. Confirm The Gap Handoff

Restate:

- atomic requirement and why registered operations cannot express it;
- candidate canonical component-backed op;
- required targets and expected effect;
- required rule trigger, step placement, and `reentry` behavior;
- required `args` and defaults;
- required Blackboard inputs/outputs.

Ask only when missing information changes the assessment or public contract.

## 2. Inspect Existing Usage

Read:

1. `docs/ability-steps.md`;
2. `docs/skill-components/README.md`;
3. detailed documents and implementations for candidate component-backed ops;
4. every ability asset that references the canonical op or a registered alias,
   including nested `steps` and `elseSteps`;
5. tightly related shared APIs and tests.

Record:

- each candidate's responsibility, canonical op, args, defaults, and behavior;
- Blackboard input/output types;
- rule trigger, step placement, and `reentry` assumptions;
- `AbilityComponentBase` lifecycle and shared-instance behavior;
- configured argument combinations that must remain valid;
- nearby operations whose responsibility must not be absorbed.

## 3. Assess The Responsibility Gate

For each plausible candidate, confirm:

1. The extension changes how the component performs its existing job, not
   what job it performs.
2. The normal `rules[].steps[]` call site and usage context remain recognizable.
3. Steps that omit added optional args follow the documented defaults.
4. The extension does not require arbitrary reflection, unrelated subsystem
   access, or a large collection of unrelated modes.
5. A separate operation would not provide a clearer atomic responsibility.

If no candidate passes, stop without editing. Return why reasonable extension
is unsuitable and a concise separate-operation recommendation to
`$td-create-ability`.

## 4. Design And Request Approval

Prefer:

- optional args with explicit defaults;
- explicit enums/strings and whitelisted fields;
- lazy `ParamList` getters consistent with nearby code;
- one-shot warnings for invalid designer-authored values;
- silent skips only for legitimate "upstream hasn't written yet" states —
  never as a catch-all for bad configuration;
- stable Blackboard data shapes and component lifecycle rules;
- documented behavior for shared component state under `Parallel` reentry.

Avoid:

- changing documented meanings or defaults of configured args;
- arbitrary property paths or reflection;
- adding gameplay actions to data-handoff components;
- adding data-handoff concerns to effect components without a concrete need.

Before editing, report:

```text
Atomic requirement:
Why registered operations cannot express it:
Canonical component-backed op to extend:
Why the capability belongs there:
Proposed args/API and defaults:
Component lifecycle and reentry behavior:
Configured-step impact:
Documentation and verification plan:
```

Obtain explicit user approval. If the user rejects or materially changes the
plan, do not implement until the revised plan is approved.

## 5. Implement

Implement only after approval. Modify the smallest necessary surface:

1. Extend the approved component-backed operation.
2. Modify tightly related shared support code only if required by the approved
   plan.
3. Add or update focused tests when the project has a suitable test surface.
4. Do not modify ability assets; `$td-create-ability` owns asset composition.

Do not broaden the approved plan during implementation. Ask for approval again
if discoveries require a materially different API, behavior change, or scope.

## 6. Document

Write the complete current contract in
`docs/skill-components/<Component>.md`, including:

- arguments and defaults;
- supported modes/paths/fields;
- Blackboard types;
- component lifecycle, step placement, and reentry notes;
- limitations and concise examples.

Update `docs/skill-components/README.md` only when its summary is no longer
accurate. Describe only the complete current contract and keep one component
document per operation.

## 7. Verify

Verify:

- configured argument combinations remain valid;
- added args are optional unless explicitly approved otherwise;
- argument names/types/defaults match documentation;
- unsupported values fail safely;
- no unrelated component or ability asset changed;
- component documentation and README remain accurate;
- relevant build/tests pass.

Recursively inspect ability assets using the canonical op or registered aliases
to check step placement, args, and `reentry` assumptions.

## Ability-Creator Handoff

On success, return:

```text
Extended canonical op:
Preserved responsibility:
Arguments/API and defaults:
Component lifecycle and reentry behavior:
Recommended rule/step composition:
Blackboard keys/types:
Verification:
```

When no reasonable extension exists, return:

```text
Extension assessment:
Why candidates fail the Responsibility Gate:
Recommended separate operation responsibility:
Suggested args and lifecycle:
```
