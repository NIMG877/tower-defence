# DestroyEntity

Ends the lifecycle of one or more active entities by calling `Entity.Die()`.

**Canonical op:** `destroy_entity`
**Component registration:** `DestroyEntity`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `toSelf` | Bool | `True` | Destroy `ctx.entity` when `blackboardKey` is empty. |
| `blackboardKey` | String | `""` | Optional input `List<Entity>` key; takes precedence over `toSelf`. |

Targets are deduplicated. Null and inactive entities are skipped. For a timed
self-destruction ability, trigger this component on `OnAbilityEnd`.
