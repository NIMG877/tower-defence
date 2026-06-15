# Ability Creation Workflow

## 1. Locate And Read

1. Locate the requested ability asset under `Assets/`.
2. Read its current YAML without replacing unrelated values such as `icon`.
3. Read nearby ability assets for serialization examples.
4. When useful, inspect the entity's legacy `Skill` subclass, prefab,
   `AnimationResources`, attack implementation, or related talent to recover
   intended semantics. Treat the user's current description as authoritative;
   do not import undescribed legacy effects.
5. Read:
   - `Assets/PublicScripts/GameData/AbilitySystem/SPConfig.cs`
   - `Assets/PublicScripts/GameData/AbilitySystem/ComponentConfig.cs`
   - relevant events/runtime code when timing is uncertain.

Never assume serialized enum numbers from memory. Confirm current enum order or
copy a verified pattern from a current asset.

## 2. Clarify Requirements

Resolve only behaviorally important omissions:

- SP: recovery, activation, consumption, initial SP, total SP, Amount,
  charges, recovery during ability, manual close.
- Effect: value, target, target count/order, damage/heal type, duration.
- Timing: ability begin/end, before/after attack, animation begin, hurt, etc.
- Persistent changes: what must be restored and when.
- Animation: one-time or persistent; exact slots and Named Resource names.

Ask the user when these cannot be discovered or reasonably inferred.

## 3. Decompose Into Atomic Behaviors

Build a short internal table:

| Atomic behavior | Trigger | Target | Inputs | Needs restore/output |
|---|---|---|---|---|
| Example: apply ATK buff | OnAbilityBegin | self | +45% | Buff handle/target |
| Example: restore range | OnAbilityEnd | self | base range | no |

Separate:

- SP engine behavior;
- immediate effects;
- persistent effects;
- restoration/removal;
- target selection;
- forced refresh/reset needed after changing behavior;
- animation replacement.

## 4. Discover Components Progressively

1. Read `docs/skill-components/README.md`.
2. Select candidate components by category.
3. Read only each candidate's detailed `.md`.
4. Inspect implementation only for ambiguity, undocumented behavior, ordering,
   or safety concerns.

Prefer existing components and their documented parameter names. Use
Blackboard only when runtime data must pass between components, such as:

- created Buff objects and targets;
- animation override records;
- original values/snapshots for restoration;
- selected entity lists or computed values.

Do not use Blackboard for constants that can be literal parameters.

## 5. Check Coverage And Handoff

Every atomic behavior must map to:

- SPConfig behavior;
- one or more existing components; or
- an explicitly approved new component.

For a component gap, produce this handoff:

```text
Atomic requirement:
Why existing components cannot satisfy it:
Proposed component name:
General responsibility:
Parameters:
Target selection:
Trigger/lifecycle behavior:
Blackboard input/output:
Expected effect:
Why this generality is appropriate:
```

Obtain approval, then invoke `$td-create-ability-component`.

## 6. Compose The Asset

Order components intentionally because dispatch follows asset order.

Typical persistent pattern:

1. `OnAbilityBegin`: apply/override and write runtime records to Blackboard.
2. Optional refresh/reset component after all relevant changes.
3. `OnAbilityEnd`: destroy/remove/restore using those records.
4. Optional refresh/reset after restoration.

Ensure begin/end keys match exactly. Use namespaced keys such as
`<abilityId>_buffs` and `<abilityId>_animation_overrides`.

For animation overrides:

- verify Named Resource names in the entity's `AnimationResources` asset;
- use group resources for group slots;
- map a general "Attack" request to both `AttackRemote` and `AttackClose`
  when project semantics require both.

## 7. Verify

Check:

- requested fields and SP values;
- trigger enum serialization;
- all parameter names/types/fromBlackboard flags;
- parallel CSV lengths;
- Blackboard producer/consumer key symmetry;
- begin/end restoration symmetry;
- component ordering;
- Named Resource existence and correct single/group kind;
- behavior at ability start/end during an in-flight attack.

Run a relevant build when it provides coverage. State that `.asset` runtime
behavior still needs a Unity PlayMode smoke test when no automated test covers
it. Remember that `Assets/Resources/` is ignored by this repository's Git
configuration, so asset edits may not appear in normal `git status`.
