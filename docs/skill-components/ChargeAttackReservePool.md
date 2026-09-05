# ChargeAttackReservePool

Adds an extra stored-energy pool to `ChargeAttack`. The default unrestricted
pool remains unchanged; extra pools fill only after the default pool is full.
When attacking, each pool is consumed only if its target rule accepts the
current target. Ineligible pool charges remain stored.

**Canonical op:** `charge_attack_reserve_pool`
**Component registration:** `ChargeAttackReservePool`
**Class:** `AbilitySystem.Components.ChargeAttackReservePool`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/ChargeAttackReservePool.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `toSelf` | Bool | `True` | Target `ctx.entity`. If false, read `List<Entity>` from `blackboardKey`. |
| `blackboardKey` | String | `""` | Blackboard key containing target entities when `toSelf=false`. |
| `capacity` | Int | `1` | Additional pool capacity. Values less than or equal to zero add no pool. |
| `minMonsterStatus` | Int | omitted | Optional inclusive minimum accepted `EntityData.MonsterStatus`. A configured entry is detected by presence in the config (`HasKey`), not by its value — sentinel-free. |
| `maxMonsterStatus` | Int | omitted | Optional inclusive maximum accepted `EntityData.MonsterStatus`. Presence-detected like `minMonsterStatus`. |

When neither MonsterStatus bound exists, the extra pool can be used against
any target. When either bound exists, targets without `EntityData` are
rejected.

## Lifecycle

- On a non-`OnAbilityEnd` trigger, register one pool per selected ChargeAttack.
- On `OnAbilityEnd`, remove all pools registered by this component instance.
- On teardown, remove them defensively.

For a persistent talent, use an `OnAbilityBegin` rule. For a temporary skill,
use one rule with `OnAbilityBegin` and `OnAbilityEnd` trigger entries and one
`charge_attack_reserve_pool` step; the bound component instance then removes
the pools it registered.

## Visual Limitation

The default unrestricted pool has its own serialized capacity, which defaults
to `4` and is independent from `_chargeEffects`. Additional stored energy does
not require extra prefab effects: visible charge effects are capped at the
existing effect count, and projectiles beyond that count reuse the final
effect's spawn position.
