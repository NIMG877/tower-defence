# DeathSpawn

On trigger, spawns `num` entities of a given `EntityID` with `gap`
seconds between successive spawns. Each spawn lands at a small
random offset (±0.24 world units in x and y) around the caster.

**Registered as:** `DeathSpawn`
**Class:** `SkillSystem.Components.DeathSpawnComponent`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/DeathSpawnComponent.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `entityId` | String | `""` | `EntityID` in `'category,number'` form (e.g. `'enemy_slime,1'`). The string is split on the first comma; the right side must parse as `int`. See `EntityID` struct. |
| `num` | Int | `1` | Number of entities to spawn. |
| `gap` | Float | `0.1` | Seconds between successive spawns. |
