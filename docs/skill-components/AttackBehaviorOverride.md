# AttackBehaviorOverride

Overrides an entity's attack `DamageType` and/or target `OrderLogic` (target priority). It can
save the previous values for exact restoration by `AttackBehaviorRestore`.

**Registered as:** `AttackBehaviorOverride`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `toSelf` | Bool | `True` | Target `ctx.entity`. If false, read `List<Entity>` from `blackboardKey`. |
| `blackboardKey` | String | `""` | Blackboard key containing target entities when `toSelf=false`. |
| `damageType` | Int | omitted | New damage type. Changed only when this parameter exists. |
| `targetPriority` | String | omitted | New `OrderLogic` enum name. Changed only when this parameter exists. |
| `outputKey` | String | `""` | Stores `List<AttackBehaviorSnapshot>` containing original values. |

Common values:

- `damageType=3`: healing; attack target selection switches to same-camp entities.
- `targetPriority=Hprate_NoFull_Asc`: exclude full-health targets and prioritize
  the lowest current HP ratio.
- `targetPriority=Defense_Des`: prioritize targets by current `Stats.DefS` from
  highest to lowest.

Changing attack behavior does not automatically restart an in-flight attack.
Pair with `ForceResetAttack` when the new mode must take effect immediately.
