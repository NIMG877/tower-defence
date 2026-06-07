# Phase 2 Smoke Test — Manual Verification

**Goal:** Verify the SkillRunner framework is wired correctly without breaking any existing entity.

## Prerequisites
- Unity 2022.3+ with this project open
- All Phase 1 + Phase 2 commits applied
- The `SkillSystem` namespace compiles without errors (check Console)

## Steps

1. **Compile check**
   - Open Unity, wait for compile.
   - Open Console — there should be no errors related to `SkillSystem` namespace.

2. **No-regression check**
   - Open the main game scene.
   - Press Play.
   - Verify that:
     - All existing entities spawn and behave normally (no skill system involvement).
     - No NullReferenceException is thrown from `SkillRunner` (entities without `SkillRunner` should silently skip).
   - Press Stop.

3. **Add SkillRunner to a test entity (optional)**
   - Pick any existing entity prefab (e.g. a simple monster).
   - Add a `SkillSystem.SkillRunner` MonoBehaviour component.
   - Leave the entity's `EntityData.Skills` list empty.
   - Press Play with that entity spawned.
   - Verify: no errors; the entity behaves identically to before (empty Skills list = no-op).
   - The console should show the SkillRunner's `PreWarm` ran (no log message unless `Debug.Log` is added — that's expected).

## Expected Results
- Phase 2 introduces no functional changes for entities that don't have a `SkillRunner` component.
- Entities that do have `SkillRunner` with empty `Skills` are no-ops.
- This milestone is fully backward-compatible with the existing 28+ `Skill`/`Talent` MonoBehaviour scripts.

## Next Phase
Phase 3 introduces ~15 basic `ISkillComponent` implementations that can be referenced from `EntityData.Skills`. Phase 3 components are pure data-driven and don't depend on the legacy skill scripts.
