# FlashMove

Flashes the entity by a distance in world units. The component reads
the entity's current path, computes the destination at distance
`moveDis` along the path's "next waypoint" direction, and teleports
the entity.

**⚠ Known edge case:** when `moveDis = 0`, the algorithm enters a
zero-length branch: the entity's position is overwritten with the
current waypoint target. This may be intentional ("flash zero distance
= snap to current waypoint") or a bug ("flash zero distance = no-op").
The implementation is **not** fixed in this version.

**Registered as:** `FlashMove`
**Class:** `SkillSystem.Components.FlashMoveComponent`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/FlashMoveComponent.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `moveDis` | Float | `0` | Distance in world units to flash. **⚠ `0` is a known edge case** (see component doc above). |
