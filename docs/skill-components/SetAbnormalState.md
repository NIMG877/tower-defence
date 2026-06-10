# SetAbnormalState

Adds or removes an abnormal state on `ctx.entity`. The
`stateIndex` matches the project's `AbnormalState` enum.

**Registered as:** `SetAbnormalState`
**Class:** `SkillSystem.Components.SetAbnormalStateComponent`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/SetAbnormalStateComponent.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `stateIndex` | Int | `0` | Abnormal state index to add or remove. |
| `add` | Bool | `True` | `true` = add the abnormal state; `false` = try to remove it. |
