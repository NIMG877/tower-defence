# SelfDamageOnEvent

Damages self after a lethal hit. Invoked on `AfterTakeDamageEvent`:
if `atd.isDeadly` is true and the entity is still alive, applies
`_damage` to self via `Entity.TakeDamage`.

Default `damageType` is `TrueDamage` (= `3`) because this component
fires only on lethal hits where the entity is already dying — using
a damage type that respects defence would be a no-op.

**Registered as:** `SelfDamageOnEvent`
**Class:** `SkillSystem.Components.SelfDamageOnEventComponent`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/SelfDamageOnEventComponent.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `damage` | Float | `0` | Damage applied to self after a lethal hit. `0` = no effect. |
| `damageType` | Int (DamageType convention) | `3` (TrueDamage) | Damage type. Default `TrueDamage` because this fires only on lethal hits where the entity is already dying. `0`=Physical, `1`=Magical, `2`=Siege, `3`=TrueDamage. |
