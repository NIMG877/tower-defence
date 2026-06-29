# Skill Components

Parameter reference for all concrete `AbilityComponentBase` implementations
in the project. One file per component; this README is the index.

When you need to look up what a `key:` row in a `ComponentConfig.parameters`
asset means, click the component name in the list below.

## Storage

All component parameters live in the `ComponentConfig.parameters.entries[]`
array (a `ParamList`). Each entry is a `(key, type, value, fromBlackboard)`
quadruple:

- **key** — the parameter name (must match what the component's `OnInit`
  reads via `p.GetXxxLazy(key, defaultValue, ctx.sharedBlackboard)`).
- **type** — a `ParamValueType` enum tag that controls how `value` is
  parsed at OnInit and (for components using `GetValueLazy`) at apply time.
- **value** — the parameter's value, stored as a string. Parsed per `type`
  on the appropriate typed getter.
- **fromBlackboard** (bool, default `false`) — when `true`, the runtime
  treats `value` as a BlackBoard key name, not a literal. The typed
  getter re-reads the BlackBoard on every invocation, so the same config
  can yield different results across `OnTrigger` calls. See
  `WriteBlackboard` and the `paramlist-lazy-blackboard-getter` memory
  for the full mechanic.

## Type encoding

| `type` tag | Storage form | Example |
|---|---|---|
| `Int` | Decimal integer | `5` |
| `Float` | `float.ToString("R")` (round-trip) | `1.5` |
| `Bool` | `True` / `False` (matches `bool.TryParse`) | `True` |
| `String` | Literal text (BB keys are stored under this tag) | `skill_buff` |
| `Vector2Int` | `x,y` (comma) | `1,1` |
| `AnimationRef` | Asset path / reference string | `Animations/Swing` |
| `Prefab` | Asset path / reference string | `Prefabs/Bullet` |
| `EntityId` | Entity id string | `hero_warrior` |
| `Color` | Hex / `R,G,B,A` string | `#FF8800FF` |

The last 4 have no typed `GetXxxLazy` and currently are only consumed by
`WriteBlackboard` (which falls through to storing the string as-is).

## CSV form

Some components accept CSV strings for parallel-array params. The storage
type is still `String`; the component splits on `,` and trims whitespace
inside its closure (per the lazy migration):

| Convention | Format | Example | Used by |
|---|---|---|---|
| `BuffTypeCsv` | `BuffType` enum names, comma-separated | `AtkSpeed,Bleed` | `ApplyBuff` |
| `FloatCsv` | Plain floats, comma-separated | `0.5,1.0,-0.25` | `ApplyBuff`, `AttackEventValueModifier` |
| `StringCsv` | Plain strings, comma-separated | `multiplyer,cumbo` | `AttackEventValueModifier` |

## DamageType encoding (convention)

Components that take a `damageType` parameter accept an int in the
range 0–3:

| Value | Meaning |
|---|---|
| `0` | Physical |
| `1` | Magical |
| `2` | Siege |
| `3` | Healing; attack target selection switches to same-camp entities |

The literal `3` appears in some legacy code; the meaning is project-
wide, not per-component. Search the codebase for `damageType` for
callers.

## Components

### Buffs

- [ApplyBuff](ApplyBuff.md) — applies one or more buffs to one or more
  targets. Optional `blackboardKey` input + `outputTarget` / `outputBuff`
  output for chained consumption.
- [DestroyBuff](DestroyBuff.md) — destroys buffs from a BlackBoard
  pair that `ApplyBuff` wrote.

- [ApplyAbnormalState](ApplyAbnormalState.md) - applies or aura-synchronizes
  abnormal states and optionally writes target/state pairs.
- [DestroyAbnormalState](DestroyAbnormalState.md) - removes abnormal states
  from pairs written by `ApplyAbnormalState`.

### Entity lifecycle

- [DestroyEntity](DestroyEntity.md) - calls `Entity.Die()` for self or a
  Blackboard entity list.

### Combat

- [AttackEventValueModifier](AttackEventValueModifier.md) — generic
  CSV-driven rewriter for `DamageEventBase` event fields
  (`multiplyer`, `damageType`, `cumbo`, etc.). Supersedes the
  removed `AttackMultiplierBoost` and `SetAttackCombo`.
- [ApplyDamage](ApplyDamage.md) - applies direct attack-based or fixed-value
  damage to the event target, self, or a Blackboard entity list.

### Entity selection

- [EntitySelector](EntitySelector.md) - selects entities by subject, event
  target, vision, radius, or range and writes the result/count to Blackboard.
- [EntityFilter](EntityFilter.md) - filters attack target candidates with
  configurable OR groups of AND conditions.

### Charge attacks

- [ChargeAttackDamageModifier](ChargeAttackDamageModifier.md) - multiplies
  damage dealt by each stored ChargeAttack energy projectile.
- [ChargeAttackReservePool](ChargeAttackReservePool.md) - adds an extra
  conditionally consumable ChargeAttack energy pool.

### Attack behavior

- [AttackBehaviorOverride](AttackBehaviorOverride.md) - overrides attack
  damage type and target ordering, optionally saving the original values.
- [AttackBehaviorRestore](AttackBehaviorRestore.md) - restores snapshots
  written by `AttackBehaviorOverride`.
- [ForceResetAttack](ForceResetAttack.md) - immediately reselects targets and
  attempts a forced attack using the current behavior.
- [AttackRangeOverride](AttackRangeOverride.md) - temporarily replaces an
  entity's attack range.
- [AttackRangeRestore](AttackRangeRestore.md) - restores the entity's base
  attack range.

### Animation

- [ApplyAnimationOverride](ApplyAnimationOverride.md) - performs a one-time
  animation replacement or adds a persistent Named Resource override.
- [RemoveAnimationOverride](RemoveAnimationOverride.md) - removes persistent
  animation overrides using records stored in the Blackboard.

### Random

- [RandomRoll](RandomRoll.md) — per-event random roll. Three modes
  (`probability` / `value` / `list`) selected by the `mode` param;
  result is written to a per-Entity Blackboard key. Use for
  probability gates, random damage multipliers, weighted drops, etc.

### BlackBoard utility

- [WriteBlackboard](WriteBlackboard.md) — generic BB write. `set` for
  write-as-is (type from `ParamEntry.type`); `add` / `mult` / `div` for
  modify-numeric. `key` and `value` both support `fromBlackboard=true`.

## Conditional triggers

`OnTick` is dispatched once per physics tick to active abilities. It is useful
for components that must re-evaluate changing Blackboard inputs, such as an
`EntitySelector` followed by `ApplyBuff` in `mode=aura`.

Every `ComponentConfig` has a `triggers[]` array of `ConditionConfig`
entries. Each entry declares a `triggerEvent` plus a condition
expression. At dispatch time, an entry's expression is evaluated
against the entity's shared blackboard (a per-entity key/value
store that components read and write); if it passes, `OnTrigger`
runs on the bound component. Failing entries are skipped (the
`ConditionEvaluator` logs at most one warning per unknown op, ever).

### Expression shape

```
ConditionConfig  { triggerEvent, List<ConditionGroup> groups }
   |                  |
   |                  +- groups is OR across ConditionGroup entries
   |                     (any group passing = the whole config passes)
   |
   +- dispatch-time entry point: ConditionEvaluator.Evaluate(groups, ctx)

ConditionGroup   { List<ConditionUnit> units }
   |
   +- units is AND across ConditionUnit entries
      (all units passing = the group passes)

ConditionUnit    { op, leftKey, rightValue }
   |
   +- a single comparison: op(leftKey, rightValue)
```

An empty `groups` list is treated as "always passes" (unconditional
trigger), equivalent to the legacy `op: 0` (None) short-circuit.

### Operator semantics (from `ConditionEvaluator.cs`)

The trimmed `ConditionOp` whitelist (per commit `54c3465`):

| `op` | Comparison |
|---|---|
| `None` | always passes (unit-level) |
| `Equal`, `NotEqual` | string `==` / `!=` on the key's value |
| `Greater` / `GreaterOrEqual` / `Less` / `LessOrEqual` | `float.TryParse` on both sides, falls back to ordinal string compare if either is unparseable |

Any `ConditionOp` value outside the live whitelist is logged once
(across the application lifetime) as a warning and treated as
"passes" (i.e., the trigger fires unconditionally — the legacy
default).

### Two coexisting expression forms

Designers have two ways to express multi-condition triggers; the
choice is a data-authoring concern, not a dispatch concern:

1. **Nested AND/OR** inside one `ConditionConfig.groups[]` (the new
   shape). Use this when the goal is "fire at most once per event
   if any matching condition is true".

2. **Repeated `triggers[]` entries** with the same `triggerEvent`
   (the pre-existing pattern). Use this when the goal is "fire once
   per passing entry". The runtime does not dedup these — each
   passing entry produces one `OnTrigger` call.
