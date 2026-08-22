# Ability Operation Creation Workflow

## 1. Confirm The Contract

Restate:

- canonical snake_case `op` name;
- one atomic responsibility;
- intended targets;
- arguments/defaults;
- expected state change or event effect;
- `Completed`/`Running`/`Failed` behavior;
- cancellation and teardown behavior;
- nested `steps`/`elseSteps` usage, if any;
- Blackboard data shape;
- expected rule and step placement.

If the request combines independent responsibilities, consider separate small
operations. Pair apply/remove or override/restore operations when exact runtime
records are required and both can be implemented entirely as new files.

## 2. Select The Implementation Form

Choose a native `AbilityStepOp` when any of these apply:

- the operation owns state for one step activation;
- `OnTick` can return `Running`;
- cancellation must clean up active work;
- failure must terminate the owning sequence;
- the operation executes nested `steps` or `elseSteps`;
- parallel rule runs require independent operation instances.

Choose a component-backed operation only when the behavior belongs to
`AbilityComponentBase` lifecycle semantics:

- one component instance is bound to the configured step;
- `OnInit` runs during ability runtime construction;
- `OnTrigger` runs when the step is reached and completes the step immediately;
- component `OnTick` runs while the ability is active, independently of the
  sequence cursor;
- `OnTeardown` runs with the ability lifecycle.

Do not use a component-backed operation to simulate a yielding sequence step.

## 3. Inspect Before Designing

Read:

1. `docs/ability-steps.md` and its canonical operation table.
2. `docs/skill-components/README.md` for component-backed operations.
3. Detailed docs and implementations for nearby operations.
4. `AbilityStepOp`, `AbilityStepOpRegistry`, `AbilityContext`, `StepConfig`,
   `ParamList`, and Blackboard.
5. `AbilityComponentBase` and component registration only when the
   component-backed form is selected.
6. The owning subsystem and its public API.

Prefer:

- `[RegisterAbilityStepOp("snake_case_name")]` for native operations;
- `[RegisterComponent("PascalCaseName")]` for component-backed operations;
- unique canonical names verified through `AbilityStepOpRegistry`;
- lazy argument getters initialized in `OnInit`;
- `ctx.sharedBlackboard` for runtime handoffs;
- documented target conventions such as self, event target, or Blackboard
  entity/list keys;
- explicit records/snapshots for exact removal/restoration;
- defensive no-op behavior for missing targets or optional data.

## 4. Choose Restrained Generality

Use this test:

1. What subsystem owns the requested value or action?
2. Which closely related fields/actions form one coherent operation?
3. Would at least one other plausible ability reuse the operation?
4. Can the API remain explicit and understandable?

Generalize when all answers support it. Otherwise implement the narrower
atomic operation.

Avoid:

- ability-specific class or parameter names;
- arbitrary field names plus reflection;
- mixing buffs, animations, targeting, and damage into one operation;
- speculative options unsupported by current requirements.

## 5. Enforce New-Files-Only Feasibility

Before editing, confirm the operation can work through current public APIs and
attribute-based registration.

Exit and ask the user when implementation requires any existing-code change,
including:

- adding or changing a TriggerEvent/AbilityEvent;
- exposing a private/internal operation;
- changing sequence execution, runner dispatch, registry, or SP behavior;
- extending ParamList/Blackboard;
- changing an enum or data schema;
- modifying another operation or component;
- changing an ability asset, prefab, or entity script.

The only existing files this workflow may edit are the relevant documentation
indexes named in the skill scope.

## 6. Implement

For a native operation, create source files under:

`Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/StepOps/`

Follow these rules:

- use namespace `AbilitySystem`;
- inherit `AbilityStepOp`;
- register one unique canonical snake_case name with
  `[RegisterAbilityStepOp("op_name")]`;
- resolve `ctx.step.args`, conditions, and nested sequences in `OnInit` or when
  execution reaches the relevant phase;
- return `Completed`, `Running`, or `Failed` according to the documented
  contract;
- make `OnCancel` stop active work and make `OnTeardown` release resources;
- keep mutable execution state on the operation instance, never on
  `StepConfig` or `ParamList`;
- make every registry factory activation independent and safe for `Parallel`
  reentry.

For a component-backed operation, create source files under:

`Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/`

Follow project patterns:

- namespace `AbilitySystem.Components`;
- inherit `AbilityComponentBase`;
- register with a unique PascalCase component name;
- verify the adapter's canonical snake_case op name;
- initialize lazy getters in `OnInit`;
- perform the atomic action in `OnTrigger`;
- use `OnTick`/`OnTeardown` only when component lifecycle semantics require
  them;
- support self or Blackboard targets when useful and consistent;
- do not mutate argument-presence flags during triggers;
- accumulate and consume Blackboard records safely when repeated triggers are
  valid.

Create each `.meta` file with a unique GUID.

## 7. Document

For a native operation, create `docs/ability-ops/<op>.md` and link it from the
canonical operation table in `docs/ability-steps.md`.

For a component-backed operation, create
`docs/skill-components/<Component>.md` and link it from both
`docs/skill-components/README.md` and the canonical operation table.

Document:

- responsibility, canonical op, and implementation form;
- target behavior;
- complete argument table with types/defaults and Blackboard capability;
- Blackboard input/output types and consumption semantics;
- `Completed`/`Running`/`Failed`, cancellation, and teardown behavior;
- nested sequence fields and condition sampling, if any;
- rule/step placement, reentry safety, limitations, and a concise example.

## 8. Verify

Verify:

- unique canonical name and alias set;
- all new `.cs` files have `.meta`;
- source compiles when included by the generated project;
- native factories create a fresh operation object for every activation;
- `Running`, `Failed`, cancellation, and teardown paths match the contract;
- component-backed operations resolve to the expected snake_case op;
- nested sequences are recursively covered when present;
- no existing runtime/code/asset files changed;
- operation documentation is linked from the relevant indexes;
- docs match actual argument names and behavior.

Run an appropriate build if the generated `.csproj` includes the new files.
If it does not, do not modify the `.csproj`; report that Unity must regenerate
it before compile verification.

## Ability-Creator Handoff

Return:

```text
Canonical op:
Implementation form:
Atomic responsibility:
Arguments/defaults:
Execution status and yielding:
Cancellation/teardown:
Blackboard keys/types:
Rule/step placement:
Reentry safety:
Verification:
```
