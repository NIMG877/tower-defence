# InjectAttackTargets

Injects a Blackboard entity list to the **front** of an attack's target
candidates (`AttackBase.OnBeforeTargetSelect`): list members are removed
from the candidate list first, then inserted at index 0 — injected entities
unconditionally get the highest targeting priority, including entities
outside vision range. Only the subscribed `AttackBase` is affected, so the
priority is per-unit (e.g. one operator's "attack my summons first") and
never leaks into other units' targeting. Complements `EntityFilter`, which
can only remove candidates, never add them.

**Canonical op:** `inject_attack_targets`
**Component registration:** `InjectAttackTargets`
**Class:** `AbilitySystem.Components.InjectAttackTargets`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/InjectAttackTargets.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `toggle` | String | `on` | `on` = subscribe (idempotent), `off` = unsubscribe. |
| `blackboardKey` | String | `""` | Blackboard key of the `List<Entity>` to inject, read live at each target selection. Empty list = no-op injection. |
| `toSelf` | Bool | `True` | `true` = subscribe `ctx.entity`'s own `AttackBase`; `false` = subscribe each entity in `blackboardKey`'s list. |

## Lifecycle

Mirrors `EntityFilter`'s subscription model:

- A non-`AbilityEnd` trigger subscribes when `toggle=on` (already-subscribed
  attacks are skipped — re-running the step is safe) and unsubscribes all
  when `toggle=off`.
- An `OnAbilityEnd` trigger always unsubscribes.
- `OnTeardown` unsubscribes defensively.

The Blackboard reference is captured at `OnInit` (the runner's shared board
is a stable instance across redeploys), but the **list contents** are read
at every target selection, so roster changes apply to the very next attack.

Pair with `force_reset_attack` right after engaging, so the unit re-targets
immediately instead of finishing its current attack cycle first.
