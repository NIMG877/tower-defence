# WatchSummonDeath

Bridges "a summon died" into a host-side `OnSummonDeath` event. Entity death
events only dispatch on the dying entity's own runner — the summoner's rules
cannot hear them. This component subscribes to each entity in a Blackboard
`List<Entity>` and re-dispatches the death on the **host's**
`EntityAbilityRunner` as a `SummonDeathEvent` (trigger
`TriggerEvent.OnSummonDeath`), so "on summon death, the summoner acts"
abilities (explosions, counters, list maintenance) become ordinary rules.

**Canonical op:** `watch_summon_death`
**Component registration:** `WatchSummonDeath`
**Class:** `AbilitySystem.Components.WatchSummonDeath`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/WatchSummonDeath.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `blackboardKey` | String | `""` | Blackboard key holding the watched `List<Entity>`. Empty = no-op. |

## Event payload

`SummonDeathEvent` carries:

- `target` — the dying entity (may already be pool-recycled by the time a
  later step runs; do not read its live state after a `delay`).
- `position` — a `Vector2` snapshot taken at dispatch time. This is the only
  safe position source for delayed steps (`apply_damage` with
  `centerMode=event` reads exactly this snapshot).

## Lifecycle

- **Subscription sync is diff-based.** `OnTrigger` performs one sync; the
  component `OnTick` (every physics frame while the ability is active)
  reconciles: entities newly present in the list get subscribed, entities
  that left the list get unsubscribed. Pair it with a `spawn_entity` step
  that appends to the same list (`appendToListKey`) and the watch follows
  the roster automatically.
- **On watched death:** unsubscribe → remove the entity from the Blackboard
  list → dispatch `SummonDeathEvent` on the host runner. Counters are NOT
  maintained here — keep them as pure arithmetic in the rules
  (`write_blackboard` `add` ±1), one responsibility per piece.
- **`OnTeardown`** unsubscribes everything. Handlers are stored per-entity so
  unsubscription uses the originally added delegate instance.

Consuming rules usually use `reentry: Parallel` so several simultaneous
deaths each run their own sequence (e.g. overlapping explosions).
