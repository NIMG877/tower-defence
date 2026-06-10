# SpawnBullet

Spawns a bullet. **The `BulletData` itself is not a `ParamList`
parameter** — it is bound by the migration tool via the public
`SetBullet(BulletData)` method. The only parameter the
`ParamList`-driven config can set is the bullet's `speed`.

The runtime `OnTrigger` is currently a no-op: the original
`SkeletonTalent1` reference implementation is mirrored in code but
concrete bullet instantiation is performed by the editor / migration
pipeline, not at skill runtime.

**Registered as:** `SpawnBullet`
**Class:** `SkillSystem.Components.SpawnBulletComponent`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/SpawnBulletComponent.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `speed` | Float | `10` | Bullet travel speed in world units / second. |
