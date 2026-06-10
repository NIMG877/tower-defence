# AttackMultiplierBoost

Multiplicative boost to `bae.multiplyer` on `BeforeAttackEvent`.
The boost is **multiplicative** with any prior multiplier: if another
component has already set `multiplyer = 1.2` and this component sets
`multiplier = 1.5`, the final value is `1.2 × 1.5 = 1.8`.

(Note the historical spelling: `multiplyer`, not `multiplier`. The
event class field uses this misspelling by project-wide convention.)

**Registered as:** `AttackMultiplierBoost`
**Class:** `SkillSystem.Components.AttackMultiplierBoost`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/AttackMultiplierBoost.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `multiplier` | Float | `1` | Multiplicative boost to `bae.multiplyer` (e.g. `1.5` = +50% attack multiplier). `1` = no change. |
