# PlayParticle

Plays or stops a particle system sourced from the blackboard. The
component reads the `ParticleSystem` reference from the blackboard
key `__particleSystem` (a reserved key); if the key is missing or
the reference is null, the component is a no-op.

`play` and `stop` are independent flags — both can be true to
restart the particle system, or only one of them. The `Stop` call
uses `StopEmittingAndClear` so the system doesn't keep emitting
old particles.

**Registered as:** `PlayParticle`
**Class:** `SkillSystem.Components.PlayParticleComponent`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/PlayParticleComponent.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `play` | Bool | `True` | Play the particle system on trigger. |
| `stop` | Bool | `False` | Stop the particle system on trigger. |
