# DestroyEntity

Ends the lifecycle of one or more active entities by calling `Entity.Die()`.

**Canonical op:** `destroy_entity`
**Component registration:** `DestroyEntity`
**Class:** `AbilitySystem.Components.DestroyEntity`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/DestroyEntity.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `toSelf` | Bool | `True` | Destroy `ctx.entity` when `blackboardKey` is empty. |
| `blackboardKey` | String | `""` | Optional input `List<Entity>` key; takes precedence over `toSelf`. |

Targets are deduplicated. Null and inactive entities are skipped. An entity
without a `Stats` component is skipped as well. A `blackboardKey` whose list
is missing on read is a silent no-op (an upstream writer hasn't run yet).
For a timed self-destruction ability, trigger this component on
`OnAbilityEnd`.
