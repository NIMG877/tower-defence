# AttackEventValueModifier

Generic field-rewriter for `DamageEventBase` events (`BeforeAttackEvent`,
`AfterAttackEvent`, `BeforeTakeDamageEvent`, `AfterTakeDamageEvent`).
Three parallel CSVs parameterise the rewrite: each triple at the same
index is `(field, value, method)` and is applied in order.

Common configurations:

| Purpose | CSV arguments |
|---|---|
| Multiply damage by `1.5` | `fields=multiplyer` `values=1.5` `methods=mult` |
| Set combo count to `3` | `fields=cumbo` `values=3` `methods=set` |

**Canonical op:** `attack_event_value_modifier`
**Component registration:** `AttackEventValueModifier`
**Class:** `AbilitySystem.Components.AttackEventValueModifier`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/AttackEventValueModifier.cs`

## Parameters

Literal CSVs are parsed once at `OnInit` and the arrays cached; entries with
`fromBlackboard=true` are re-read (and re-parsed) on every trigger.

| Key | Type | Default | Description |
|---|---|---|---|
| `fields` | StringCsv | *(empty)* | Comma-separated field names to rewrite. Must be from the whitelist below. |
| `values` | FloatCsv | *(empty)* | Comma-separated numeric values, one per field, parsed as `float`. A non-numeric token is a config error — `float.Parse` throws and surfaces at init. |
| `methods` | StringCsv | *(empty)* | Comma-separated operator per field. One of `mult` (`field = field * value`), `add` (`field = field + value`), `set` (`field = value`), `div` (`field = field / value`). |

If the three CSVs differ in length, a warning is logged and the
shortest length is applied. A key absent from the config yields an empty
array (the rewrite is a no-op).

## Field whitelist

Field names use the spellings declared by the event classes
(`multiplyer` not `multiplier`; `cumbo` not `combo`).

| Name | Type | Source event |
|---|---|---|
| `multiplyer` | float | `DamageEventBase` (all 4 events) |
| `defPenetrate` | float | `DamageEventBase` |
| `mgrPenetrate` | float | `DamageEventBase` |
| `defPenetrate_value` | float | `DamageEventBase` |
| `mgrPenetrate_value` | float | `DamageEventBase` |
| `damageType` | int | `DamageEventBase` (`3` = healing; see project convention in README) |
| `applyType` | int | `DamageEventBase` |
| `cumbo` | int | `BeforeAttackEvent` only — skipped (logged) on the other three |

Values are stored as float; the int fields cast at the apply site
(`MathOps.ApplyIntMixed`): `mult` and `div` use the float operand and round
(`Mathf.RoundToInt`), so a designer who typed `1.5` for `cumbo` still gets a
sensible result. `add` and `set` use the operand truncated to int, keeping
integer intent exact (`2.7` acts as `2`).

## Lenient matching

All four methods are accepted on every field. The operation does not
validate semantic soundness — e.g. `mult` on `damageType` is a valid
config even though it makes no gameplay sense. Designer is responsible
for choosing sensible combinations.

## Unknown inputs

- **Unknown field name** → `LogWarning`, that entry is skipped.
- **Unknown method** → one-shot warning per `(method, field)` pair, that entry
  is skipped (field left unchanged).
- **Event is not a `DamageEventBase`** → `LogError`, the whole trigger is skipped.
