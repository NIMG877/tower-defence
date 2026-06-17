# WriteBlackboard

Writes a value to the per-Entity shared Blackboard. Two modes:

- **`set`** (default): writes the value as-is to the configured key. The
  value's runtime type is determined by `ParamEntry.type` (`Int` / `Float` /
  `Bool` / `String` / `Vector2Int`, plus the 4 Unity asset types which
  fall through to string). Both `key` and `value` support `fromBlackboard=true`.
- **`add` / `mult` / `div`**: reads the existing value at the key, applies
  the operation with the configured value, and writes the result back.
  Only supported for numeric existing values (`int` / `float` / `double`);
  bool/string/Vector2Int log a warning and skip. Math semantics for each
  method mirror `AttackEventValueModifier`.

**Registered as:** `WriteBlackboard`
**Class:** `AbilitySystem.Components.WriteBlackboard`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/WriteBlackboard.cs`

## Parameters

All parameters are read via the `ParamList` lazy API; each `Func<T>`
re-evaluates the source on every call.

| Key | Type | Default | Description |
|---|---|---|---|
| `key` | String | `""` | The BlackBoard key to write to. Empty = component is a no-op. |
| `value` | typed (per `ParamEntry.type`) | `null` | The value to write (`set`) or the operand to apply (`add`/`mult`/`div`). |
| `method` | String | `set` | One of `set` / `add` / `mult` / `div`. Unknown method → `LogWarning` + skip. |

| Key | Type | Default | Description |
|---|---|---|---|
| `source` | String | `value` | Value source: `value`, `event`, or `entity`. |
| `path` | String | `""` | Context field used by `source=event` or `source=entity`. |
| `asString` | Bool | `False` | Convert non-entity-list context values to invariant strings before writing. Useful for `ConditionEvaluator`. |

## Context sources

`source=value` preserves the original behavior. `source=event` and
`source=entity` read the current trigger context instead of `value`.

Supported event paths:

- Damage events: `target`, `multiplyer`/`multiplier`, `defPenetrate`,
  `mgrPenetrate`, `defPenetrate_value`, `mgrPenetrate_value`, `damageType`,
  `applyType`
- Hurt events: `origin`, `damage`, the same multiplier/penetration/type
  fields, and `isDeadly`
- Specific events: `isDeadly` on after-attack/after-damage events and `cumbo`
  on before-attack events

Supported entity paths:

- empty path or `self`
- `camp`, `currentHp`, `currentHpRate`, `maxHp`, `attack`, `monsterStatus`

Entity-valued paths (`event.target`, `event.origin`, and `entity.self`) write
`List<Entity>` so they can feed components such as `ApplyBuff`. Unsupported
paths emit a one-shot warning and skip the write.

### Value type

`value` is the only param whose runtime type is determined by the
`ParamEntry.type` field (rather than by a fixed `GetXxxLazy` call). The
underlying API is `ParamList.GetValueLazy`, which dispatches to:

| `ParamEntry.type` | Parse / store |
|---|---|
| `Int` | `int.TryParse`, store as `int` |
| `Float` | `float.TryParse`, store as `float` |
| `Bool` | `bool.TryParse`, store as `bool` |
| `String` | literal, store as `string` |
| `Vector2Int` | `x,y` parse, store as `Vector2Int` |
| `AnimationRef` / `Prefab` / `EntityId` / `Color` | store the raw string as-is (no typed parser exists; designer responsibility) |

`value` can also have `fromBlackboard=true` — the component reads the
configured key from BlackBoard at apply time and uses whatever is stored.

## Modify-mode semantics

For `add` / `mult` / `div`:

1. If `value` is null, no-op (prevents an unconfigured param from
   zeroing out the BlackBoard via `mult`).
2. If no existing value at `key`, no-op (use `set` first to seed).
3. Read existing value, use its runtime type to select the math path:
   `int` / `float` / `double`. `Convert.ToX(value)` widens/narrows as
   needed; conversion failure is caught and logged.
4. Apply the op (`add` = +, `mult` = *, `div` = /), write result back.

Non-numeric existing types log a warning and skip. `div` by zero is
**not** guarded — matches `AttackEventValueModifier` behavior (int throws
`DivideByZeroException` caught by the try/catch; float/double produce
`±Infinity` / `NaN`, stored as-is).

## Changelog

- 2026-06-15: added `event` and `entity` context sources plus optional string
  conversion for conditional-trigger handoffs.
- 2026-06-12: initial implementation. `GetValueLazy` (returns
  `Func<object>`) was added to `ParamList` to support type-dispatched
  reads driven by `ParamEntry.type`.
