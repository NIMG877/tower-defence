# RandomRoll

Per-event random roll. Picks a result according to `mode` and writes it
to the per-Entity shared Blackboard at `outputKey`. Three modes are
1:1 with the `input` parameter's expected format:

- **`probability`** (default): `input` is a single float in `[0, 1]`.
  Rolls `RandomHelper.Helper.RandomP(p)` and writes `"True"` or
  `"False"` (matches `bool.TryParse`). Use as a probability gate:
  a following `branch` or another operation reads `BB[outputKey]`.
- **`value`**: `input` is a 2-float CSV `min,max`. Uniform float in
  `[min, max]` is rolled and written as `float.ToString("R")`
  (round-trippable; `ConditionEvaluator`'s numeric compare can
  `float.TryParse` it back).
- **`list`**: `input` is a string CSV `a,b,c,...`. Uniform pick is
  written as the chosen trimmed string.

All three parameters support `fromBlackboard=true`. `input` is always
read as a string regardless of mode (for `value` and `list` this is
obviously needed; for `probability` the designer must store the float
as a string-parseable value in BB — e.g. `"0.6"`, not `0.6`).

**Canonical op:** `random_roll`
**Component registration:** `RandomRoll`
**Class:** `AbilitySystem.Components.RandomRoll`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/RandomRoll.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `input` | String | `""` | Mode-dependent value. See above. Empty → no-op. |
| `mode` | String | `probability` | One of `probability` / `value` / `list`. Unknown → `LogWarning` + skip. |
| `outputKey` | String | `""` | Blackboard key to write the result to. Empty → no-op. |

## Mode semantics

### `probability`

- Parse `input` as `float` (via `float.TryParse`). Unparseable → silent
  no-op.
- Call `RandomHelper.Helper.RandomP(p)`. The helper uses the project's
  shared 200-entry `[0, 1)` array (default `useArray=true`), so the
  global random index advances on every call.
- Write `"True"` or `"False"`. Downstream `ConditionConfig` should
  compare with `Equal leftKey=outputKey rightValue=True`.

### `value`

- Split `input` via `CsvParser.Split<float>` (whitespace-trimmed,
  comma-separated). Take the first 2 numbers; unparseable tokens
  become `0f`. Fewer than 2 numbers → silent no-op.
- Call `RandomHelper.Helper.RandomF(range[0], range[1])`. The helper
  does the `min + (max - min) * u` math internally and silently swaps
  if `min > max` — the consumer doesn't need to re-handle either.
- Write the returned float.

### `list`

- Split `input` via `CsvParser.SplitStrings` (whitespace-trimmed,
  comma-separated). Empty → silent no-op.
- Call `RandomHelper.Helper.RandomL(parts)`. The helper internally
  uses `(int)(u * len) % len` to fold the rare `Random.Range(0f,1f)
  == 1.0` corner back to index 0 — the consumer doesn't need to
  re-clamp.
- Write the returned string.

## Typical wiring

**Probability gate** — fire downstream only 60% of the time:

```
rule.steps:
  - random_roll  (mode=probability, input=0.6, outputKey=rolled_ok)
  - branch       (condition: BB["rolled_ok"]=="True")
      steps:
        - apply_buff
```

Put `random_roll` before the step that reads `rolled_ok`. Steps in one
rule execute in declaration order, so the Blackboard write is visible to
the following `branch` (or any later component-backed step) immediately.
