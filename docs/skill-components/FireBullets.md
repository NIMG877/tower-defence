# FireBullets

Fires one **visual carrier bullet** per point in a blackboard
`List<Vector2>` — no entity target, zero damage. When each bullet reaches
its point and is destroyed, it dispatches `BulletLandedEvent` (trigger
`OnBulletLanded`, position = actual landing point including parabola
deviation) on the host runner, for "spawn / damage at landing point" rules
to consume.

**Canonical op:** `fire_bullets`
**Component registration:** `FireBullets`
**Class:** `AbilitySystem.Components.FireBullets`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/FireBullets.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `pointsKey` | String | `""` | Blackboard key holding `List<Vector2>` target points. |
| `effectDataIndex` | Int | `0` | Index into the host `AttackBase._extraEffectDatas`. |

## Bullet configuration home

ParamList is string-only and cannot carry GameObject references, so the
bullet data (prefabs, speed, type, spawn bone) lives on the attack
component: `AttackBase._extraEffectDatas` (serialized list), referenced by
index — the same registry-by-index pattern as `SpawnEntity.spawnIndex`.
`BulletData.AllowNoTarget` must be true for carrier flight without an
entity target.

## Semantics

- Carrier profile is fixed (damage 0, multiplyer 1, no penetration) — this
  component's contract is "harmless carrier"; damage belongs to
  landing-point rules.
- **The consumer rule must live on an always-active runtime** (a Talent,
  or a duration skill that is still active when bullets land). The runner
  only dispatches events to active abilities — an instant-cast skill is
  already inactive by the time its bullets land, so its own trigger-19
  rules never fire. Eyjafjalla S2 fires the bullets; the landing→spawn
  rule lives on her Talent (`eyjafjalla_t1.asset`).
- The landing callback closes over the host entity at fire time. If the
  host dies mid-flight the event still dispatches; a missing runner is
  skipped.
- A host without an `AttackBase` is a configuration error — the component
  cannot resolve effect data and logs an error.
- Config errors (missing points key, effect index out of range, missing
  spawn transform) log an error and skip.

## Tests

`Assets/Tests/Editor/AbilitySystem/FireBulletsTests.cs` covers the config
error paths; the bullet assembly and landing dispatch are PlayMode-verified
(same policy as the `OnBeforeTargetSelect` bridge).
