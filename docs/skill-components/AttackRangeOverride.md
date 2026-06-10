# AttackRangeOverride

Overrides the entity's attack range with a set of cells. Each cell
is an `(x, y)` integer pair. The `range` value uses **semicolons**
to separate cells and **commas** to separate `(x, y)` within a
cell — this is a two-level separator, not a single CSV.

**Registered as:** `AttackRangeOverride`
**Class:** `SkillSystem.Components.AttackRangeOverrideComponent`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/AttackRangeOverrideComponent.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `range` | String | `""` | Range cells as `'x1,y1;x2,y2;...'`. **Semicolons** separate cells, **commas** separate `(x,y)` within a cell. Empty = no override. |
