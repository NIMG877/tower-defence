# ApplyImpulse

Applies an outward movement impulse from the ability owner to each resolved
target through `MoveBase.TryToAddImpulse`.

**Canonical op:** `apply_impulse`
**Component registration:** `ApplyImpulse`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `targetMode` | String | `eventTarget` | Target source: `eventTarget`, `self`, or `blackboard`. |
| `blackboardKey` | String | `""` | Input `List<Entity>` key used by `targetMode=blackboard`. |
| `strengthLevel` | Int | `0` | Strength level passed to `MoveBase.TryToAddImpulse`. |

Targets are deduplicated. Targets without `MoveBase` and targets located
exactly at the ability owner's position are skipped. `MoveBase` decides
whether the target's mass permits the configured impulse.

## Rule trigger and step placement

Place `select_targets` before `apply_impulse` when the operation reads a
Blackboard target list. For an explosion, put `apply_damage` and
`apply_impulse` after the selector in the same rule so both consume the same
list.
