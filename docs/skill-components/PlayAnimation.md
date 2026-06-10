# PlayAnimation

Plays a specific animation state on the entity. Invoked on
`OnTrigger`; the entity's `entityAM` is the animation manager.

**Registered as:** `PlayAnimation`
**Class:** `SkillSystem.Components.PlayAnimationComponent`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/PlayAnimationComponent.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `targetState` | Int | `1` | Target animation state index. |
| `force` | Bool | `True` | Force-restart the animation even if it is already playing. |
