# Skill Components

Parameter reference for every `ISkillComponent` in the project. One file
per component; this README is the index.

When you need to look up what a `key:` row in a `SkillConfig.asset`
means, click the component name in the list below.

## Storage

All component parameters live in the `ComponentConfig.parameters.entries[]`
array (a `ParamList`). Each entry is a `(key, type, value)` triple:

- **key** — the parameter name (must match what the component's `OnInit`
  reads via `p.GetXxx(key, defaultValue)`).
- **type** — a `ParamValueType` enum tag (`Int`, `Float`, `Bool`, `String`,
  `Vector2Int`). Pure documentation; the runtime always uses the typed
  getter implied by the component's `OnInit`.
- **value** — the parameter's value, stored as a string.

## Type encoding

| `type` tag | Storage form | Example |
|---|---|---|
| `Int` | Decimal integer | `5` |
| `Float` | `float.ToString("R")` (round-trip) | `1.5` |
| `Bool` | `True` / `False` (capitalised, matches `bool.TryParse`) | `True` |
| `String` | Literal text | `skill_buff` |
| `Vector2Int` | `x,y` (comma) | `1,1` |
| `BuffTypeCsv` | `BuffType` enum names, comma-separated | `AtkSpeed,Bleed` |
| `FloatCsv` | Plain floats, comma-separated | `0.5,1.0,-0.25` |
| `BlackboardKey` | Free-form key string | `targets.list` |

## DamageType encoding (convention)

Components that take a `damageType` parameter accept an int in the
range 0–3:

| Value | Meaning |
|---|---|
| `0` | Physical |
| `1` | Magical |
| `2` | Siege |
| `3` | TrueDamage (bypasses defence/resistance) |

The literal `3` appears in some legacy code; the meaning is project-
wide, not per-component. Search the codebase for `damageType` for
callers.

## Components

### Buff / aura

- [ApplyBuff](ApplyBuff.md) — applies buffs to one or more entities.
- [PeriodicAuraBuff](PeriodicAuraBuff.md) — periodically applies buffs
  to entities in a radius. **⚠** the `priority` field is actually
  the buff duration (historical name).

### Damage / combat

- [AttackMultiplierBoost](AttackMultiplierBoost.md) — multiplicative
  boost to `bae.multiplyer` on `BeforeAttackEvent`.
- [CampDamageModifier](CampDamageModifier.md) — multiplies damage when
  the target's camp matches `requiredCamp`.
- [DamageRadiusFalloff](DamageRadiusFalloff.md) — tier-based AOE damage
  with knockback impulse.
- [LockHpShield](LockHpShield.md) — prevents HP from dropping below
  `threshold` on a hit.
- [SelfDamageOnEvent](SelfDamageOnEvent.md) — damages self after a
  lethal hit.
- [SetAttackCombo](SetAttackCombo.md) — absolute combo count assigned
  to `bae.cumbo`.

### Selection / blackboard

- [EntitySelector](EntitySelector.md) — selects entities in a radius
  and writes the list to a blackboard key.
- [EntitySelectorRadiusEffect](EntitySelectorRadiusEffect.md) — selects
  entities in a radius and forwards a sub-component to each.

### Animation / VFX

- [PlayAnimation](PlayAnimation.md) — plays a specific animation state.
- [ResetAnimation](ResetAnimation.md) — resets a list of animation
  state indices.
- [SwapAnimation](SwapAnimation.md) — *(no parameters; animation
  assets are bound by the migration tool).*
- [PlayParticle](PlayParticle.md) — plays or stops a particle system
  sourced from the blackboard.
- [FlashMove](FlashMove.md) — flashes the entity by a distance. **⚠**
  `moveDis = 0` is a known edge case.

### Spawn / lifecycle

- [DeathSpawn](DeathSpawn.md) — spawns N entities of a given `EntityID`
  on trigger.
- [SpawnBullet](SpawnBullet.md) — *(bullet data is bound by the
  migration tool; only `speed` is a parameter).*
- [SelfDestruct](SelfDestruct.md) — self-destructs the entity after
  `duration` seconds.
- [CoroutineLoop](CoroutineLoop.md) — runs a coroutine loop until a
  blackboard key disappears.

### State / utility

- [SetAbnormalState](SetAbnormalState.md) — adds or removes an
  abnormal state.
- [AttackRangeOverride](AttackRangeOverride.md) — overrides the
  entity's attack range with a set of cells.
- [SetAttackEffectData](SetAttackEffectData.md) — *(no parameters;
  effect data is bound by the migration tool).*

## Conditional triggers

Every `ComponentConfig` also has a `triggers[]` array of
`ConditionConfig` rows. Each row has four fields:

| Field | Meaning |
|---|---|
| `triggerEvent` | which event the row's `OnTrigger` fires on (e.g. `OnBeforeAttack`, `OnAfterTakeDamage`) |
| `op` | the comparison operator |
| `leftKey` | blackboard key the operator reads from |
| `rightValue` | the value to compare against, stored as a string |

Operator semantics (from `ConditionEvaluator.cs`):

| `op` | Comparison |
|---|---|
| `HasBlackboardKey`, `NotHasBlackboardKey` | checks key presence |
| `Equal`, `NotEqual` | string `==` / `!=` on the key's value |
| `Greater` / `GreaterOrEqual` / `Less` / `LessOrEqual` | `float.TryParse` on both sides, falls back to ordinal string compare if either is unparseable |
| `HasBuff` / `NotHasBuff` | **currently a no-op at runtime** (see `ConditionEvaluator.cs:36-37`); the value is stored but ignored. |
| `IsInAbnormalState` / `NotInAbnormalState` | **currently a no-op at runtime** (same line); the value is stored but ignored. |
| `None` | the row is skipped (always passes). |
