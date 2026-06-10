# DamageRadiusFalloff

Tier-based AOE damage with knockback impulse. On trigger, the
component gathers monsters and turrets within `maxRadius` of the
caster, then applies a damage/impulse pair per target based on the
target's distance:

| Distance band | Multiplier | Impulse |
|---|---|---|
| `r ≤ EntityR` | `tier1Mul` | `impulse1` |
| `r ≤ 2 * EntityR` | `tier2Mul` | `impulse2` |
| `r ≤ sqrt(2) + EntityR` | `tier3Mul` | `impulse3` |
| otherwise | `tier4Mul` | `impulse4` |

The final damage is `_baseDamage * tierMul`, applied via
`Entity.TakeDamage(..., _damageType, _applyType)`.

**Registered as:** `DamageRadiusFalloff`
**Class:** `SkillSystem.Components.DamageRadiusFalloffComponent`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/DamageRadiusFalloffComponent.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `maxRadius` | Float | `1.5` | Outer radius of the AOE in world units. |
| `baseDamage` | Float | `1` | Base damage value, multiplied by the per-tier multiplier (`tier1Mul`..`tier4Mul`). |
| `damageType` | Int (DamageType convention) | `0` (Physical) | Damage type. `0`=Physical, `1`=Magical, `2`=Siege, `3`=TrueDamage (bypasses defence). |
| `applyType` | Int | `1` | Apply type passed to `TakeDamage` (project-specific; `0`=once, `1`=per-tick, etc.). |
| `tier1Radius` | Float | `0.5` | Inner tier radius in world units. Inside this radius = tier 1 (full multiplier). |
| `tier2Radius` | Float | `1` | Tier 2 outer radius. Inside = tier 2. |
| `tier3Radius` | Float | `1.5` | Tier 3 outer radius. Inside = tier 3. Beyond = tier 4. |
| `tier1Mul` | Float | `1` | Damage multiplier for tier 1. |
| `tier2Mul` | Float | `0.5` | Damage multiplier for tier 2. |
| `tier3Mul` | Float | `0.25` | Damage multiplier for tier 3. |
| `tier4Mul` | Float | `0.1` | Damage multiplier for tier 4 (outermost). |
| `impulse1` | Int | `5` | Knockback impulse magnitude for tier 1. |
| `impulse2` | Int | `4` | Knockback impulse magnitude for tier 2. |
| `impulse3` | Int | `3` | Knockback impulse magnitude for tier 3. |
| `impulse4` | Int | `2` | Knockback impulse magnitude for tier 4. |
