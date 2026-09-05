# ChargeAttackDamageModifier

Multiplies damage dealt by stored ChargeAttack energy. It subscribes to
`ChargeAttack.OnBeforeChargeTakeDamage`, so the multiplier affects each stored
energy projectile but not the normal attack projectile.

**Canonical op:** `charge_attack_damage_modifier`
**Component registration:** `ChargeAttackDamageModifier`
**Class:** `AbilitySystem.Components.ChargeAttackDamageModifier`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/ChargeAttackDamageModifier.cs`

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

For a persistent talent, use an `OnAbilityBegin` rule. For a temporary skill,
use one rule with `OnAbilityBegin` and `OnAbilityEnd` trigger entries and one
`charge_attack_damage_modifier` step; the bound component instance then owns
both subscription and removal.
