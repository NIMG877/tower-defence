# ChargeStateController

Controls a cancellable charge sequence and publishes its current phase to the
shared Blackboard. It does not select entities, apply abnormal states, play
animations, deal damage, or destroy entities.

**Canonical op:** `charge_state_controller`
**Component registration:** `ChargeStateController`
**Class:** `AbilitySystem.Components.ChargeStateController`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/ChargeStateController.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `targetCountKey` | String | `""` | Input Blackboard key containing the current target count as a numeric string. Empty or unset means the count never reads as present and the machine stays in `idle`. |
| `minimumTargetCount` | Int | `1` | Required target count. Values below `1` are treated as `1`. |
| `phaseKey` | String | `charge_phase` | Output string key containing the current phase. |
| `phaseEnteredKey` | String | `charge_phase_entered` | Output string key set to `True` on a phase-transition tick (`OnInit` writes the initial `idle` entry as `True` too); every later trigger resets it to `False` first. |
| `chargeDuration` | Float | `1` | Seconds targets must remain present before entering `attack`. |
| `backoutAnimation` | String | `""` | Named animation whose duration controls the `backout` phase. Empty/unset, or unresolvable on the entity's animation machine, gives the phase a zero duration (a single tick). |
| `attackAnimation` | String | `""` | Named animation whose duration controls the `attack` phase. Empty/unset, or unresolvable, gives the phase a zero duration (a single tick). |
| `animationDurationScale` | Float | `0.9` | Non-negative multiplier applied to both resolved animation durations. |

## Phases and lifecycle

The phases are `idle`, `charging`, `backout`, `attack`, `detonate`, and
`finished`.

- `idle` enters `charging` when the input count reaches the configured minimum.
- Losing the required targets during `charging` enters `backout`; after the
  named animation duration it returns to `idle` and can charge again.
- Completing the charge enters `attack`, then enters `detonate` after the
  scaled named animation duration.
- `detonate` lasts one trigger tick before becoming `finished`.

Both outputs are strings so they can be read directly by `ConditionEvaluator`.
The component removes its output keys during teardown.

## Rule trigger and step placement

Use an `OnTick` rule. Place `select_targets` before `charge_state_controller`
so `targetCountKey` is current, then place phase-conditioned animation,
abnormal-state, selection, damage, impulse, and destruction steps after it.

For one-shot phase actions, require both `phaseKey=<phase>` and
`phaseEnteredKey=True` in the same condition group.
