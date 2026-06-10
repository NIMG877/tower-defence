# ResetAnimation

Resets a comma-separated list of animation state indices on the
entity. Each entry in the CSV is parsed via `int.Parse`; invalid
tokens are silently skipped.

**Registered as:** `ResetAnimation`
**Class:** `SkillSystem.Components.ResetAnimationComponent`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/ResetAnimationComponent.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `resetIndices` | String | `""` | Comma-separated list of animation state indices to reset (e.g. `0,2,4`). Empty = no-op. |
