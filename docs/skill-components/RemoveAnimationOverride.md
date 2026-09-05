# RemoveAnimationOverride

Removes animation overrides previously created by `ApplyAnimationOverride` —
persistent entries, and one-shot entries still waiting to be consumed
(e.g. revoking a pending one-shot when its target dies early).

**Canonical op:** `remove_animation_override`
**Component registration:** `RemoveAnimationOverride`
**Class:** `AbilitySystem.Components.RemoveAnimationOverride`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/RemoveAnimationOverride.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `toSelf` | Bool | `True` | Remove records belonging to `ctx.entity`. If false, filter by entities from `blackboardKey`. |
| `blackboardKey` | String | `""` | Blackboard key containing `List<Entity>` when `toSelf=false`. |
| `inputKey` | String | `""` | Blackboard key containing `List<AnimationOverrideRecord>`. |

Matching records are removed from their animation machines and consumed from
the Blackboard list. The Blackboard key is deleted when no records remain.
An empty or unset `inputKey`, or a key with no record list, logs a warning
and skips.

Normally trigger this on `OnAbilityEnd` using the same key configured as the
paired `ApplyAnimationOverride.outputKey`.
