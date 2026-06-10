# CampDamageModifier

Multiplies damage by `multiplier` when the target's camp matches
`requiredCamp`. The component is invoked on the `BeforeAttackEvent`
dispatch and modifies `bae.multiplyer` in place.

**Registered as:** `CampDamageModifier`
**Class:** `SkillSystem.Components.CampDamageModifierComponent`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/CampDamageModifierComponent.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `requiredCamp` | Int | `2` | Only apply the multiplier when the target's camp equals this value. `0` = use `ctx.entity.Camp` (modifier applies to same-camp targets). |
| `multiplier` | Float | `1` | Multiplicative damage modifier when the camp matches. `1` = no change. |
