# ModifyCost

Modifies the level deployment cost by a signed amount. The change is global
and immediate — it does not route through entities or the Blackboard. Interval
clamping and UI refresh are owned by `LevelResourceManager.ChangeCost`.

**Canonical op:** `modify_cost`
**Component registration:** `ModifyCost`
**Class:** `AbilitySystem.Components.ModifyCost`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/ModifyCost.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `amount` | Int | `0` | Signed cost change. Positive grants cost, negative spends it. Supports `fromBlackboard=true`. |

## Behavior

On each trigger the component calls `LevelResourceManager.Manager.ChangeCost`
with the resolved amount. There is no target selection and no Blackboard
output; the operation is a plain global side effect, so it pairs naturally
with `PreWarm`/`OnAbilityBegin` rules (e.g. deploy-cost talents).

## Placement notes

Because the effect is global rather than per-entity, a rule that triggers on
an entity event applies the same cost change once per passing trigger. Use
the rule's condition expression or a `random_roll` + `branch` gate when the
change must be conditional.
