# ApplyDamage

Applies one direct damage instance to each resolved target.

**Canonical op:** `apply_damage`
**Component registration:** `ApplyDamage`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `targetMode` | String | `eventTarget` | Target source: `eventTarget`, `self`, or `blackboard`. |
| `blackboardKey` | String | `""` | Input `List<Entity>` key used by `targetMode=blackboard`. |
| `baseValueMode` | String | `attack` | `attack` uses `ctx.entity.Stats.AttackS`; `fixed` uses `baseValue`. |
| `baseValue` | Float | `0` | Base damage used by `baseValueMode=fixed`. |
| `multiplier` | Float | `1` | Damage multiplier. |
| `defPenetrate` | Float | `0` | Percentage physical-defense penetration. |
| `mgrPenetrate` | Float | `0` | Percentage magical-resistance penetration. |
| `defPenetrateValue` | Float | `0` | Flat physical-defense penetration. |
| `mgrPenetrateValue` | Float | `0` | Flat magical-resistance penetration. |
| `damageType` | Int | `0` | Damage type; `0` physical, `1` magical, `2` siege, `3` healing. |
| `applyType` | Int | `2` | Apply type passed to `Entity.TakeDamage`. |

`eventTarget` is available only on events derived from `DamageEventBase`.
Direct damage does not invoke the attacker's `AttackBase.OnAfterTakeDamage`,
so using this component on `OnAfterTakeDamage` does not recursively trigger
itself.

## Rule trigger and step placement

Use after an upstream selector/condition when damage depends on a query result.
Place the Blackboard writer or `select_targets` before `apply_damage` in the
same rule's `steps[]`.
