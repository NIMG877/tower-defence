# ApplyDamage

Applies one direct damage instance to each resolved target.

**Canonical op:** `apply_damage`
**Component registration:** `ApplyDamage`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `targetMode` | String | `eventTarget` | Target source: `eventTarget`, `self`, or `blackboard`. |
| `blackboardKey` | String | `""` | Input `List<Entity>` key used by `targetMode=blackboard`. |
| `attackerMode` | String | `self` | Damage-origin source: `self` (`ctx.entity`) or `summoner` (the summoner `Entity` recorded by `spawn_entity` at the fixed protocol key `summoner@spawn_entity`). |
| `baseValueMode` | String | `attack` | `attack` uses the resolved attacker's `Stats.AttackS`; `fixed` uses `baseValue`. |
| `baseValue` | Float | `0` | Base damage used by `baseValueMode=fixed` (supports `fromBlackboard`). |
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

## Detached executions (ctx.entity == null)

The attacker resolves before the base value: `attackerMode=summoner` reads
the forked Blackboard clone (a summoner `Entity` recorded at spawn time
survives the host's death there), `self` yields null. With a null attacker:

- `baseValueMode=fixed` proceeds as **sourceless damage** — the hurt/damage
  pipeline never dereferences the origin, so this is safe; damage-stat
  attribution simply counts it under no one.
- `baseValueMode=attack` is a configuration error (no stat source) and is
  logged and skipped.

Area damage in detached executions composes `select_targets`
(`subjectMode=self` resolves the fork snapshot's position/camp) →
`targetMode=blackboard` + `attackerMode=summoner` + `fixed` base value.

## Rule trigger and step placement

Use after an upstream selector/condition when damage depends on a query result.
Place the Blackboard writer or `select_targets` before `apply_damage` in the
same rule's `steps[]`.
