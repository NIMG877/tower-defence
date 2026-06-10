# SetAttackEffectData

Replaces the entity's `AttackEffectData` reference. **The
`AttackEffectData` itself is not a `ParamList` parameter** — it is
bound by the migration tool via the public `SetData(AttackEffectData)`
method.

The runtime `OnTrigger` calls `ctx.entity.AttackBase.SetAttackEffectData(_data)`.

**Registered as:** `SetAttackEffectData`
**Class:** `SkillSystem.Components.SetAttackEffectDataComponent`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/SetAttackEffectDataComponent.cs`

## Parameters

None.

## Notes

- The `AttackEffectData` field is set via the public `SetData`
  method, populated by the migration pipeline.
- The `OnInit` body is intentionally empty.
