# SpawnEntity

Spawns an entity through `EntityPoolManager` / `EntityManager`. The summon
source is locked to the host's `EntityData.CanSpawnEntityIds` registry;
`spawnIndex` picks the entry — arbitrary entity ids are not accepted.

**Canonical op:** `spawn_entity`
**Component registration:** `SpawnEntity`
**Class:** `AbilitySystem.Components.SpawnEntity`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/SpawnEntity.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `spawnIndex` | Int | `0` | Index into the host's `CanSpawnEntityIds`. Out of range (or an empty registry) logs an error and skips this spawn. |
| `positionMode` | String | `self` | `self`, `eventTarget`, `fixed`, or `event`. Any other value logs an error and skips. (No `blackboard` mode — read positions from the Blackboard via `fromBlackboard` on the parameters instead.) |
| `position` | Vector2Int | `(0,0)` | Position for `positionMode=fixed`. |
| `offset` | Vector2Int | `(0,0)` | Added after resolving the base position. (Random scatter is not built in — compose `RandomRoll` + Blackboard-fed `position`/`offset` instead.) |
| `camp` | Int | `-1` | Spawned entity camp. Negative values (the default included) resolve in order: the detached snapshot's camp, the host's camp, then `1`. |
| `placement` | String | `auto` | `static`, `move`, or `auto` (pool data decides). Any other value logs an error and skips. |
| `orientation` | Int | `0` | Static-entity orientation. |
| `pathSerial` | Int | host's current path | Movable-entity path serial; absent inherits the host's `CurrentPathSerial`. |
| `outputKey` | String | `""` | If set, stores the spawned `Entity` in the shared Blackboard. Single-entity overwrite — the last spawn wins. |
| `appendToListKey` | String | `""` | If set, appends the spawned `Entity` to the `List<Entity>` at this Blackboard key (get-or-create, then write back preserving the list instance). Use this to maintain a "summon roster"; pairing with `watch_summon_death` on the same key gives the roster death events. |
| `passStat` | String | `""` | Host stat path written into the spawned entity's Blackboard at `passStatKey` (spawn-time snapshot). Uses the `write_blackboard` `source=entity` path vocabulary (`attack`, `maxhp`, `currenthp`, ...). |
| `passStatKey` | String | `""` | Blackboard key on the **spawned** entity receiving the `passStat` value. |

All parameters are BB-capable. `eventTarget` resolves damage-event targets and
hurt-event origins; without a usable event target it falls back to self.
`event` reads the position carried by the current event (`BulletLandedEvent`
actual landing point, `SummonDeathEvent` death snapshot) — use it for
"spawn at the bullet's real landing point" flows; a current event that
carries no position logs an error and skips.

Configuration errors (missing registry entry, out-of-range `spawnIndex`,
unknown `positionMode`/`placement` token, missing entity pool) only log an
error and skip the spawn — the sequence continues. The component path has no
failure semantics; if "fail and abort the sequence" is ever needed, the
component layer needs a status channel first.

In detached executions the registry, the camp default, the `self` position,
and the `pathSerial` default all resolve from the fork-time snapshot; the live
host is never consulted (it may be pool-recycled). Lazy getters bind at
`OnTrigger` against the executing context's Blackboard (the fork-time clone
for detached rules), not the host board captured at `OnInit`. A detached
spawn has no summoner entity, so nothing is written to `summoner@spawn_entity`.

## Summoner data pass-through

Every spawn writes the summoner `Entity` (`ctx.entity`) into
the spawned entity's own Blackboard at the fixed protocol key
**`summoner@spawn_entity`** — right after the spawn call returns (the board
exists — pool checkout synchronously builds the runner, and the board is only
cleared at the next checkout; a spawn product without a board logs a warning
and records nothing). Detached spawns have no live summoner and write
nothing. The `@组件名` suffix keeps the key from
colliding with designer-chosen keys. Consumers read the fixed key rather than
configuring their own; today that is `apply_damage`'s
`attackerMode=summoner` (damage attribution to the summoner).

This is the channel for "the summon outlives its summoner" data: the summon's
detached rules read the fork clone of that board, so the summoner reference
survives both the summon's death and the host's death. The reference stays
valid as data — recycled entities keep their `EntityData` — but re-checkouts
reuse instances, so treat a summoner reference older than one recycle window
as attribution-only, never as a live stat source. `passStat`+`passStatKey`
(optional, designer-keyed) snapshot host stats onto the same board for the
same lifetime reasons — both keys must be non-empty for the write to happen.
