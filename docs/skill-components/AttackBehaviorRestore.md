# AttackBehaviorRestore

Restores attack `DamageType` and `OrderLogic` from snapshots written by
`AttackBehaviorOverride`.

**Canonical op:** `attack_behavior_restore`
**Component registration:** `AttackBehaviorRestore`
**Class:** `AbilitySystem.Components.AttackBehaviorRestore`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/AttackBehaviorRestore.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `toSelf` | Bool | `True` | Restore records belonging to `ctx.entity`. If false, filter by entities from `blackboardKey`. |
| `blackboardKey` | String | `""` | Blackboard key containing `List<Entity>` when `toSelf=false`. |
| `inputKey` | String | `""` | Blackboard key containing `List<AttackBehaviorSnapshot>`. |

Matching snapshots are restored and consumed. Snapshots are applied in
reverse order, so the **earliest** snapshot wins when several were stacked
on the same entity. The Blackboard key is deleted when no snapshots remain.

Normally trigger this on `OnAbilityEnd`, then trigger `ForceResetAttack` so the
restored targeting behavior takes effect immediately.
