# AttackTargetCountModifier

Rewrites the target-count bounds (`selectMaxNum` / `selectMinNum`) of the
in-flight `AttackTargetSelect` through the event bridge's `ref` writeback.
The candidate list is already priority-ordered and not yet trimmed at
`OnBeforeTargetSelect`, so the change means "this attack hits/heals one more
(or fewer)" for the current attack only.

**Canonical op:** `attack_target_count_modifier`
**Component registration:** `AttackTargetCountModifier`
**Class:** `AbilitySystem.Components.AttackTargetCountModifier`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/AttackTargetCountModifier.cs`

The component is structurally the twin of `AttackEventValueModifier`: the
same three parallel CSVs (`fields` / `methods` / `values`), the same
`mult` / `add` / `set` / `div` methods, applied with integer semantics via
`MathOps.ApplyIntMixed`.

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `fields` | StringCsv | *(empty)* | Field names to rewrite. Whitelist: `selectmaxnum`, `selectminnum` (case-insensitive, trimmed). Unknown names are skipped with a one-shot warning. |
| `methods` | StringCsv | *(empty)* | `ModifierOp`-style method per entry: `mult` / `add` / `set` / `div`. Unknown methods are skipped with a one-shot warning. |
| `values` | FloatCsv | *(empty)* | Operand per entry, parsed as `float`; the apply site truncates to `int` for the integer fields. |

The three CSVs are index-aligned; mismatched lengths take the shortest
(entries beyond that are ignored without a warning).

## Behavior

- The rule must trigger on `OnBeforeTargetSelect`. On any other event the
  component warns once and skips.
- `int` fields (`add` / `set` / `mult` / `div`) use the truncated operand,
  so a `values` entry of `2.7` acts as `2`.
- All three parameters support `fromBlackboard=true`; the CSVs are re-read
  on every trigger.

## Typical wiring

Pair with `random_roll` + `branch` in the same rule for a chance-gated
"this attack hits one extra target":

```
rule (trigger: OnBeforeTargetSelect).steps:
  - random_roll   (mode=probability, input=0.5, outputKey=rolled_ok)
  - branch        (condition: BB["rolled_ok"]=="True")
      steps:
        - attack_target_count_modifier
          (fields=selectmaxnum, methods=add, values=1)
```
