# ApplyImpulse

Applies an outward movement impulse from the ability owner to each resolved
target through `MoveBase.TryToAddImpulse`.

**Registered as:** `ApplyImpulse`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `targetMode` | String | `eventTarget` | Target source: `eventTarget`, `self`, or `blackboard`. |
| `blackboardKey` | String | `""` | Input `List<Entity>` key used by `targetMode=blackboard`. |
| `strengthLevel` | Int | `0` | Strength level passed to `MoveBase.TryToAddImpulse`. |

Targets are deduplicated. Targets without `MoveBase` and targets located
exactly at the ability owner's position are skipped. `MoveBase` decides
whether the target's mass permits the configured impulse.

## Recommended trigger and ordering

Use after an `EntitySelector` that writes the configured Blackboard list.
For an explosion, give `ApplyDamage` and `ApplyImpulse` the same target list
and trigger conditions.
