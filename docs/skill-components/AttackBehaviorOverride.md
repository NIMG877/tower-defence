# AttackBehaviorOverride

Overrides an entity's attack `DamageType` and/or target `OrderLogic` (target priority). It can
save the previous values for exact restoration by `AttackBehaviorRestore`.

**Canonical op:** `attack_behavior_override`
**Component registration:** `AttackBehaviorOverride`
**Class:** `AbilitySystem.Components.AttackBehaviorOverride`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/AttackBehaviorOverride.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `toSelf` | Bool | `True` | Target `ctx.entity`. If false, read `List<Entity>` from `blackboardKey`. |
| `blackboardKey` | String | `""` | Blackboard key containing target entities when `toSelf=false`. |
| `damageType` | Int | omitted | New damage type. Changed only when this parameter exists. |
| `targetPriority` | String | omitted | New `OrderLogic` enum name. Changed only when this parameter exists. An unparseable name logs a warning and the target-priority override is skipped for that trigger. |
| `outputKey` | String | `""` | Stores `List<AttackBehaviorSnapshot>` containing original values. Snapshots are written for every targeted entity with an `AttackBase` whenever this key is set — even when neither override parameter is configured. |

`OrderLogic` values (5, from `OrderLogic.cs`): `ResistFirst_Priority_Des`,
`Priority_Des`, `Hprate_NoFull_Asc`, `Defense_Des`, `VisionFirst_Priority_Des`.

Common values:

- `damageType=3`: healing; attack target selection switches to same-camp entities.
- `targetPriority=Hprate_NoFull_Asc`: exclude full-health targets and prioritize
  the lowest current HP ratio.
- `targetPriority=Defense_Des`: prioritize targets by current `Stats.DefS` from
  highest to lowest.

Changing attack behavior does not automatically restart an in-flight attack.
Pair with `ForceResetAttack` when the new mode must take effect immediately.
