# RecoverSkillSp

Immediately restores SP to the host entity's selected skill ("deploy then
instantly gain N SP" style talents).

**Canonical op:** `recover_skill_sp`
**Component registration:** `RecoverSkillSp`
**Class:** `AbilitySystem.Components.RecoverSkillSp`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/RecoverSkillSp.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `amount` | Int | `0` | SP to restore. Supports `fromBlackboard=true`. |

## Behavior

On each trigger the component calls
`ctx.entity.AbilityRunner.RecoverSkillSp(amount)`. SP recovery semantics —
the SP cap, charge (蓄能) skills, and accumulation — are owned by
`EntityAbilityRunner.RecoverSkillSp`; when the entity has no built skill the
runner reports an error there.

A detached execution (no host entity) logs an error and skips. The component
requires a live host; route it through a rule bound to the entity rather than
a detached dispatch.
