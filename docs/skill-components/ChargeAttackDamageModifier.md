# ChargeAttackDamageModifier

Multiplies damage dealt by stored ChargeAttack energy. It subscribes to
`ChargeAttack.OnBeforeChargeTakeDamage`, so the multiplier affects each stored
energy projectile but not the normal attack projectile.

**Registered as:** `ChargeAttackDamageModifier`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `toSelf` | Bool | `True` | Target `ctx.entity`. If false, read `List<Entity>` from `blackboardKey`. |
| `blackboardKey` | String | `""` | Blackboard key containing target entities when `toSelf=false`. |
| `multiplier` | Float | `1` | Multiplier applied to stored energy damage. |

## Lifecycle

- On a non-`OnAbilityEnd` trigger, subscribe selected entities that have a
  `ChargeAttack`.
- On `OnAbilityEnd`, unsubscribe all entities previously subscribed by this
  component instance.
- On teardown, unsubscribe defensively.

For a persistent talent, trigger only on `OnAbilityBegin`. For a temporary
skill, configure the same component for both `OnAbilityBegin` and
`OnAbilityEnd`.
