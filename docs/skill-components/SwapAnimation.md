# SwapAnimation

Swaps the entity's six animation states (idle, move, attack close,
attack remote, start, die). The component is a per-flag swap: a
skill can swap just one of the six without resetting the rest, by
calling the matching `SetX(...)` method.

**This component takes no `ParamList` parameters.** The animation
assets are bound by the migration tool, not via `OnInit`. The
drawer's "no schema" fallback applies: the `Parameters` field in
the Inspector will show an empty raw list.

**Registered as:** `SwapAnimation`
**Class:** `SkillSystem.Components.SwapAnimationComponent`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/SwapAnimationComponent.cs`

## Parameters

None.

## Notes

- The component exposes `SetIdle`, `SetMove`, `SetAttackClose`,
  `SetAttackRemote`, `SetStart`, `SetDie` for editor-side
  binding.
- Per-flag presence flags (`HasIdle`, `HasMove`, ...) let
  consumers know which slots were populated.
- The `OnInit` body is intentionally empty — the per-flag
  `AnimationReferenceAsset` fields are populated by the migration
  pipeline.
