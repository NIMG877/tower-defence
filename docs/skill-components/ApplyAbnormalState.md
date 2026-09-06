# ApplyAbnormalState

Applies one or more abnormal states to self or to entities supplied through
Blackboard. Its `normal` and `aura` modes mirror `ApplyBuff`.

**Canonical op:** `apply_abnormal_state`
**Component registration:** `ApplyAbnormalState`
**Class:** `AbilitySystem.Components.ApplyAbnormalState`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/ApplyAbnormalState.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `mode` | String | `normal` | `normal` for one-shot application; `aura` for target-list synchronization. |
| `abnormalTypes` | IntCsv | `""` | State types: `0` restrain (bans Move), `1` unbalance (bans Move and Attack), `2` silence (bans Attack), `3` invulnerable (unselectable and unhurtable), `4` halt (reduces move speed by 80%). |
| `abnormalTimes` | FloatCsv | `""` | Parallel durations; `-5` or below is permanent. |
| `toSelf` | Bool | `True` | Target `ctx.entity` when `blackboardKey` is empty. |
| `blackboardKey` | String | `""` | Optional input `List<Entity>` key. |
| `outputTarget` | String | `""` | Optional/required-in-aura output `List<Entity>` key. |
| `outputState` | String | `""` | Optional/required-in-aura parallel output `List<int>` key. |

`abnormalTypes` and `abnormalTimes` must have equal lengths. Types outside
`0..4` are skipped with a one-shot warning.

## Modes and lifecycle

`normal` applies every configured state to every target and appends optional
parallel output records. `aura` requires all three Blackboard keys and treats
the current input list as authoritative: it adds or refreshes desired states,
removes recorded states from entities no longer present, and overwrites the
output records. Aura records are removed during teardown.

Pair normal-mode output with `DestroyAbnormalState` for explicit restoration,
typically through a separate `OnAbilityEnd` rule. For aura synchronization,
use an `OnTick` rule and place `apply_abnormal_state` after the step that writes
its target list.

## Limitation

`BuffController` stores one timer per abnormal-state type and exposes no
source handle or reference count. Removing a state can therefore also remove
the same type applied by another source.
