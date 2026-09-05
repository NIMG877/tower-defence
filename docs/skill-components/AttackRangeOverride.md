# AttackRangeOverride

Replaces the selected entity's `EntityVision.Range`.

**Canonical op:** `attack_range_override`
**Component registration:** `AttackRangeOverride`
**Class:** `AbilitySystem.Components.AttackRangeOverride`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/AttackRangeOverride.cs`
**Pair with:** `AttackRangeRestore`.

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `toSelf` | Bool | `True` | Target `ctx.entity`. |
| `targetEntity` | String | `""` | When `toSelf=false`, Blackboard key containing one `Entity`. Empty/unset or missing key = no-op. |
| `range` | Vector2Int array | empty | New range. Literal format is a JSON array such as `[[0,0],[1,0]]`. A null or empty range is a no-op. |

The `range` parameter supports a literal JSON array or a Blackboard value of
type `Vector2Int[]`, `Vector2Int`, or string when `fromBlackboard=true`.
A literal is parsed without error signalling: an unparseable coordinate
reads as `0`, and an entry without exactly two values becomes the zero
vector — check formats carefully.

The override replaces the `EntityVision.Range` **computed value**. An
orientation change (`EntityVision.SetOrientation`) recomputes `Range` from
`BaseRange` and discards the override — re-apply it after any turn.

Trigger on `OnAbilityBegin` and use `AttackRangeRestore` on `OnAbilityEnd` for
a temporary skill range.
