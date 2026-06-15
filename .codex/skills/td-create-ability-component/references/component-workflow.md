# Ability Component Creation Workflow

## 1. Confirm The Contract

Restate:

- one atomic responsibility;
- intended targets;
- parameters/defaults;
- expected state change or event effect;
- whether restoration/removal is required;
- Blackboard data shape;
- expected trigger timing.

If the request combines independent responsibilities, consider separate small
components. If a restore operation needs an exact runtime snapshot, create a
paired override/restore component only when both can be implemented entirely
as new files.

## 2. Inspect Before Designing

Read:

1. `docs/skill-components/README.md` to avoid duplicating an existing component.
2. Detailed docs for nearby components.
3. Relevant component implementations for local patterns.
4. The owning subsystem and its existing public API.
5. `AbilityComponentBase`, `AbilityContext`, `ParamList`, Blackboard, and
   registration behavior as needed.

Prefer:

- `[RegisterComponent("<Name>")]`;
- lazy parameter getters initialized in `OnInit`;
- `ctx.sharedBlackboard` for runtime handoffs;
- existing target conventions (`toSelf`, Blackboard entity/list key);
- explicit records/snapshots for exact removal/restoration;
- defensive no-op behavior for missing targets or optional data.

## 3. Choose Restrained Generality

Use this test:

1. What subsystem owns the requested value or action?
2. Which closely related fields/actions form one coherent operation?
3. Would at least one other plausible ability reuse the component?
4. Can the API remain explicit and understandable?

Generalize when all answers support it. Otherwise implement the narrower
atomic component.

Avoid:

- ability-specific class or parameter names;
- arbitrary field names plus reflection;
- mixing buffs, animations, targeting, and damage into one component;
- speculative options unsupported by current requirements.

## 4. Enforce New-Files-Only Feasibility

Before editing, confirm the component can work through existing public APIs.

Exit and ask the user when implementation requires any existing-code change,
including:

- adding or changing a TriggerEvent/AbilityEvent;
- exposing a private/internal operation;
- changing runner dispatch or SP behavior;
- extending ParamList/Blackboard;
- changing an enum or data schema;
- modifying another component;
- changing an ability asset, prefab, or entity script.

The only existing file this workflow may edit is
`docs/skill-components/README.md`.

## 5. Implement

Create new source files under:

`Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/`

Follow project patterns:

- namespace `AbilitySystem.Components`;
- inherit `AbilityComponentBase`;
- register with a unique component name;
- initialize lazy getters in `OnInit`;
- perform the atomic action in `OnTrigger`;
- use `OnTick`/`OnTeardown` only when the contract truly requires them;
- support self or Blackboard targets when useful and consistent;
- do not mutate parameter-presence flags during triggers;
- accumulate and consume Blackboard records safely when repeated triggers are
  valid.

Create each `.meta` file with a unique GUID.

## 6. Document

Create `docs/skill-components/<Component>.md` containing:

- responsibility and registered name;
- target behavior;
- complete parameter table with types/defaults;
- Blackboard input/output types and consumption semantics;
- lifecycle and recommended triggers;
- ordering notes, limitations, and a concise example when helpful.

Update `docs/skill-components/README.md` in the appropriate category. Correct
nearby factual errors only when directly necessary to describe the new
component; do not broadly rewrite documentation.

## 7. Verify

Verify:

- unique registration name;
- all new `.cs` files have `.meta`;
- component compiles when included by the generated project;
- no existing runtime/code/asset files changed;
- every concrete registered component has a matching documentation file;
- every component document is linked from README;
- docs match actual parameter names and behavior.

Run an appropriate build if the generated `.csproj` includes the new files.
If it does not, do not modify the `.csproj`; report that Unity must regenerate
it before compile verification.

## Ability-Creator Handoff

Return:

```text
Registered component:
Atomic responsibility:
Parameters/defaults:
Recommended trigger(s):
Blackboard keys/types:
Ordering requirements:
Verification:
```
