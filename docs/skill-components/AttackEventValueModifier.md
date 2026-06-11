# AttackEventValueModifier

Generic field-rewriter for `DamageEventBase` events (`BeforeAttackEvent`,
`AfterAttackEvent`, `BeforeTakeDamageEvent`, `AfterTakeDamageEvent`).
Three parallel CSVs parameterise the rewrite: each triple at the same
index is `(field, value, method)` and is applied in order.

The original `AttackMultiplierBoost` (`multiplyer *= N`) and
`SetAttackCombo` (`cumbo = N`) were folded into this component as
two-line CSV configs:

| Old component | Equivalent CSV |
|---|---|
| `AttackMultiplierBoost { multiplier = 1.5 }` | `fields=multiplyer` `values=1.5` `methods=mult` |
| `SetAttackCombo { cumbo = 3 }` | `fields=cumbo` `values=3` `methods=set` |

**Registered as:** `AttackEventValueModifier`
**Class:** `SkillSystem.Components.AttackEventValueModifier`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/AttackEventValueModifier.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `fields` | StringCsv | `""` | Comma-separated field names to rewrite. Must be from the whitelist below. |
| `values` | StringCsv | `""` | Comma-separated numeric values, one per field. Each value is parsed as both float and int at init; the wrong-type slot is never read. A non-numeric value silently becomes `0`. |
| `methods` | StringCsv | `""` | Comma-separated operator per field. One of `mult` (`field = field * value`), `add` (`field = field + value`), `set` (`field = value`). |

If the three CSVs differ in length, a warning is logged and the
shortest length is applied.

## Field whitelist

Field names use the historical spellings from the event classes
(`multiplyer` not `multiplier`; `cumbo` not `combo`).

| Name | Type | Source event |
|---|---|---|
| `multiplyer` | float | `DamageEventBase` (all 4 events) |
| `defPenetrate` | float | `DamageEventBase` |
| `mgrPenetrate` | float | `DamageEventBase` |
| `defPenetrate_value` | float | `DamageEventBase` |
| `mgrPenetrate_value` | float | `DamageEventBase` |
| `damageType` | int | `DamageEventBase` (3 = true damage, see project convention in README) |
| `applyType` | int | `DamageEventBase` |
| `cumbo` | int | `BeforeAttackEvent` only — silently skipped on the other three |

`mult` on int fields uses the float-parsed value and rounds
(`Mathf.RoundToInt(current * floatValue)`) so a designer who typed
`1.5` for `cumbo` still gets a sensible result. `add` and `set` on
int fields use the int-parsed value to keep designer intent exact.

## Lenient matching

All three methods (`mult`, `add`, `set`) are accepted on every field.
The component does not validate semantic soundness — e.g. `mult` on
`damageType` is a valid config even though it makes no gameplay sense.
Designer is responsible for choosing sensible combinations.

## Unknown inputs

- **Unknown field name** → `LogWarning`, that entry is skipped.
- **Unknown method** → `LogWarning`, that entry is skipped (field left unchanged).
- **Event is not a `DamageEventBase`** → `LogError`, the whole trigger is skipped.
