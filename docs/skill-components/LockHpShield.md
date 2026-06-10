# LockHpShield

Prevents HP from dropping below `threshold` on a hit. Invoked on
`BeforeHurtEvent`: if the hit would put HP below `threshold`, the
damage is reduced to `HP - threshold` (or 0 if HP is already at or
below threshold). Optionally, `selfDamageOnSave` is applied to the
entity after the shield saves it (a cost for using the shield).

The `damageType` slot of `Entity.TakeDamage` for the self-damage
is the project's `TrueDamage` convention (= `3`).

**Registered as:** `LockHpShield`
**Class:** `SkillSystem.Components.LockHpShieldComponent`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/LockHpShieldComponent.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `active` | Bool | `True` | Enable / disable the shield. `false` = component is a no-op. |
| `threshold` | Float | `0` | Minimum HP that the shield will let the entity drop to before the lethal hit lands. e.g. `1` = always survive with 1 HP. |
| `selfDamageOnSave` | Float | `0` | Damage applied to the entity after the shield saves it. Costs HP for using the shield. `0` = no cost. |
