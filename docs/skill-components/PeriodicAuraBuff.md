# PeriodicAuraBuff

Periodically applies buffs to entities within a radius around the skill
owner. The first `OnTrigger` is a no-op; subsequent `OnTick` calls do
the work. Buffs on entities that leave the radius are destroyed; buffs
on entities that newly enter the radius are created.

**⚠ Historical naming bug:** the `priority` field is actually the buff
**duration in seconds**, not a priority. The 5th positional argument
to `BuffController.CreateBuff` is `_buffTime`, and this component
passes the value there. Renaming the field would require a `.asset`
migration, so the misleading name is kept and surfaced here.

**Registered as:** `PeriodicAuraBuff`
**Class:** `SkillSystem.Components.PeriodicAuraBuffComponent`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/PeriodicAuraBuffComponent.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `radius` | Float | `0` | Aura radius in world units. Entities within this distance from `ctx.entity` are buffed. |
| `buffTypes` | BuffTypeCsv | `""` | Buff types to apply. One per chip. |
| `buffValues` | FloatCsv | `""` | Per-BuffType value, same order as `buffTypes`. |
| `effectName` | String | `""` | Optional VFX/particle binding name. Editor-side only; not consumed at runtime today. |
| `buffId` | String | `aura_buff` | Unique id passed to `BuffController.CreateBuff`. |
| `priority` | Float | `-10` | **⚠ Historical name: this value is the buff DURATION IN SECONDS, not a priority.** Negative = permanent. |
| `toAllies` | Bool | `True` | `true` = buff entities with the same camp; `false` = opposite camp. |
