# SelfDestruct

Self-destructs the entity after `duration` seconds. The `OnTeardown`
+ entity removal is triggered by the entity's own lifecycle; the
component's role is purely to wait for the duration and signal.

**Registered as:** `SelfDestruct`
**Class:** `SkillSystem.Components.SelfDestructComponent`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/SelfDestructComponent.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `duration` | Float | `5` | Seconds until self-destruct. Triggers `OnTeardown` + entity removal. |
