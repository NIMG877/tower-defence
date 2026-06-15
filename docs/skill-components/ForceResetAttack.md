# ForceResetAttack

Immediately calls `AttackBase.ForceResetAttack()` on selected entities. This
resets the attack timer, selects targets using current attack behavior, and
attempts a forced attack transition.

**Registered as:** `ForceResetAttack`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `toSelf` | Bool | `True` | Target `ctx.entity`. If false, read `List<Entity>` from `blackboardKey`. |
| `blackboardKey` | String | `""` | Blackboard key containing target entities when `toSelf=false`. |

Place it after components that change targeting, damage type, range, or
animation overrides. Component execution follows the order in the ability
asset, so ordering matters.
