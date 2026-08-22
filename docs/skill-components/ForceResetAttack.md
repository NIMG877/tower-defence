# ForceResetAttack

Immediately calls `AttackBase.ForceResetAttack()` on selected entities. This
resets the attack timer, selects targets using current attack behavior, and
attempts a forced attack transition.

**Canonical op:** `force_reset_attack`
**Component registration:** `ForceResetAttack`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `toSelf` | Bool | `True` | Target `ctx.entity`. If false, read `List<Entity>` from `blackboardKey`. |
| `blackboardKey` | String | `""` | Blackboard key containing target entities when `toSelf=false`. |

Place `force_reset_attack` after the steps that change targeting, damage type,
range, or animation overrides. Steps in one rule execute in declaration order.
