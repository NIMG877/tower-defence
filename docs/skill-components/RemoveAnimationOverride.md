# RemoveAnimationOverride

Removes persistent animation overrides previously created by
`ApplyAnimationOverride`.

**Registered as:** `RemoveAnimationOverride`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `toSelf` | Bool | `True` | Remove records belonging to `ctx.entity`. If false, filter by entities from `blackboardKey`. |
| `blackboardKey` | String | `""` | Blackboard key containing `List<Entity>` when `toSelf=false`. |
| `inputKey` | String | `""` | Blackboard key containing `List<AnimationOverrideRecord>`. |

Matching records are removed from their animation machines and consumed from
the Blackboard list. The Blackboard key is deleted when no records remain.

Normally trigger this on `OnAbilityEnd` using the same key configured as the
paired `ApplyAnimationOverride.outputKey`.
