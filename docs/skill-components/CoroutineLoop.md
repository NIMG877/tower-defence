# CoroutineLoop

Runs a coroutine loop. The loop body is dispatched per fixed update
on the same skill; it self-terminates when `stopWhenBlackboardKeyMissing`
is set and the corresponding blackboard key is removed.

This is a fire-and-forget loop: the first `OnTick` starts the async
loop, and `OnTeardown` sets `_isRunning = false` so the loop exits
on its next iteration. If the skill ends naturally and the loop is
still running, `OnTeardown` will stop it; if the skill never ends
cleanly (e.g. pool dormancy), the loop terminates on its own when
the stop condition fires.

**Registered as:** `CoroutineLoop`
**Class:** `SkillSystem.Components.CoroutineLoopComponent`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/CoroutineLoopComponent.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `stopWhenBlackboardKeyMissing` | BlackboardKey | `""` | Loop self-terminates when this blackboard key is removed. Empty = never auto-stop (relies on `OnTeardown`). |
