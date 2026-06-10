# EntitySelector

Selects entities in a radius around the skill owner and writes the
result to a blackboard key. Downstream components (e.g. `ApplyBuff`)
read the same key.

The selection uses `EntityManager.Manager.EntitySelector_Radius` and
copies the result into a new `List<Entity>` before mutating, because
the manager may return a pooled/shared list. The blackboard key is
cleared before write so re-trigger does not accumulate stale state.

**Registered as:** `EntitySelector`
**Class:** `SkillSystem.Components.EntitySelector`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/EntitySelector.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `blackboardKey` | BlackboardKey | `""` | Output key. The selected entity list is written here for downstream components (e.g. `ApplyBuff`). |
| `radius` | Float | `1` | Selection radius in world units. |
| `camp` | Int | `0` | Camp id to match. `0` = use `ctx.entity.Camp`. |
| `sameCamp` | Bool | `True` | `true` = select entities with the same camp; `false` = opposite camp. |
| `force` | Bool | `False` | Force-select even when line-of-sight / occupancy would normally block. |
| `selectSelf` | Bool | `False` | Include `ctx.entity` in the result list. |
