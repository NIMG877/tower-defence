# SpawnEntity

Spawns an entity through `EntityPoolManager` / `EntityManager`. The summon
source is locked to the host's `EntityData.CanSpawnEntityIds` registry;
`spawnIndex` picks the entry — arbitrary entity ids are not accepted.

**Canonical op:** `spawn_entity`
**Component registration:** `SpawnEntity`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `spawnIndex` | Int | `0` | Index into the host's `CanSpawnEntityIds`. Out of range (or an empty registry) logs an error and skips this spawn. |
| `positionMode` | String | `self` | `self`, `eventTarget`, or `fixed`. Any other value logs an error and skips. (No `blackboard` mode — read positions from the Blackboard via `fromBlackboard` on the parameters instead.) |
| `position` | Vector2Int | `(0,0)` | Position for `positionMode=fixed`. |
| `offset` | Vector2Int | `(0,0)` | Added after resolving the base position. (Random scatter is not built in — compose `RandomRoll` + Blackboard-fed `position`/`offset` instead.) |
| `camp` | Int | source camp, otherwise `1` | Spawned entity camp; negative selects the default. |
| `placement` | String | `auto` | `static`, `move`, or `auto` (pool data decides). Any other value logs an error and skips. |
| `orientation` | Int | `0` | Static-entity orientation. |
| `pathSerial` | Int | host's current path | Movable-entity path serial; absent inherits the host's `CurrentPathSerial`. |
| `outputKey` | String | `""` | If set, stores the spawned `Entity` in the shared Blackboard. |

All parameters are BB-capable. `eventTarget` resolves damage-event targets and
hurt-event origins; without a usable event target it falls back to self.

Configuration errors (missing registry entry, out-of-range `spawnIndex`,
unknown `positionMode`/`placement` token, missing entity pool) only log an
error and skip the spawn — the sequence continues. The component path has no
failure semantics; if "fail and abort the sequence" is ever needed, the
component layer needs a status channel first.

In detached executions the registry, the camp default, the `self` position,
and the `pathSerial` default all resolve from the fork-time snapshot; the live
host is never consulted (it may be pool-recycled). Lazy getters bind at
`OnTrigger` against the executing context's Blackboard (the fork-time clone
for detached rules), not the host board captured at `OnInit`.
