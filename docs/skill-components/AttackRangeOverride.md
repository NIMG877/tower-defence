# AttackRangeOverride

Replaces the selected entity's `EntityVision.Range`.

**Canonical op:** `attack_range_override`
**Component registration:** `AttackRangeOverride`
**Pair with:** `AttackRangeRestore`.

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `toSelf` | Bool | `True` | Target `ctx.entity`. |
| `targetEntity` | String | `""` | When `toSelf=false`, Blackboard key containing one `Entity`. |
| `range` | Vector2Int array | empty | New range. Literal format is a JSON array such as `[[0,0],[1,0]]`. |

The `range` parameter supports a literal JSON array or a Blackboard value of
type `Vector2Int[]`, `Vector2Int`, or string when `fromBlackboard=true`.

Trigger on `OnAbilityBegin` and use `AttackRangeRestore` on `OnAbilityEnd` for
a temporary skill range.
