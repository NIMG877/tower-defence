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

All three parameters are read via the `ParamList` lazy API; each `Func<T>`
re-runs the underlying `SplitCsv` (and float/int double-parse for `values`)
on every call.

| Key | Type | Default | Description |
|---|---|---|---|
| `fields` | StringCsv | `""` | Comma-separated field names to rewrite. Must be from the whitelist below. |
| `values` | StringCsv | `""` | Comma-separated numeric values, one per field. Each value is parsed as both float and int inside the closure; the wrong-type slot is never read. A non-numeric value silently becomes `0` in both. |
| `methods` | StringCsv | `""` | Comma-separated operator per field. One of `mult` (`field = field * value`), `add` (`field = field + value`), `set` (`field = value`), `div` (`field = field / value`). |

If the three CSVs differ in length, a warning is logged and the
shortest length is applied.

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
| `cumbo` | int | `BeforeAttackEvent` only — silently skipped on the other three |

`mult` on int fields uses the float-parsed value and rounds
(`Mathf.RoundToInt(current * floatValue)`) so a designer who typed
`1.5` for `cumbo` still gets a sensible result. `add` and `set` on
int fields use the int-parsed value to keep designer intent exact.
`div` mirrors `mult` for the float-parsed value.

## Lenient matching

All four methods are accepted on every field. The operation does not
validate semantic soundness — e.g. `mult` on `damageType` is a valid
config even though it makes no gameplay sense. Designer is responsible
for choosing sensible combinations.

## Unknown inputs

- **Unknown field name** → `LogWarning`, that entry is skipped.
- **Unknown method** → `LogWarning`, that entry is skipped (field left unchanged).
- **Event is not a `DamageEventBase`** → `LogError`, the whole trigger is skipped.
