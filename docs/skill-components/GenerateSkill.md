# GenerateSkill

Runtime LLM skill generation: submits a battle snapshot to the ability-server
and injects the generated ability as the host's current skill when it completes.

**Canonical op:** `generate_skill`
**Component registration:** `GenerateSkill`
**Class:** `AbilitySystem.Components.GenerateSkill`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/GenerateSkill.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `request` | String | `""` | Free-text intent forwarded to the server Agent as `constraints.request`. Empty = the Agent designs from the battle snapshot alone. Supports `fromBlackboard=true`. |
| `serverUrl` | String | `http://127.0.0.1:8765` | ability-server base URL (host:port, no path). Supports `fromBlackboard=true`. |

## Behavior

Asynchronous fire-and-forget — this op does **not** block the step sequence:

1. **OnTrigger** (guarded: a re-trigger while a task is in flight is ignored —
   do not configure this component on high-frequency events like OnTick):
   builds the payload (`protocolVersion` + `opList` + `BattleSnapshotBuilder`
   snapshot + `hostAssets` + `constraints.request`) and POSTs it to
   `{serverUrl}/generate-ability/async`. The step completes immediately; the
   component then polls.
2. **OnTick** drives the poll loop (0.4s interval, 5s per-request timeout,
   3600s overall deadline — raised from 240s when the server agent moved to the
   multi-minute v2 free loop) and scrolls Agent phase logs to the Console
   (plan/act/review/submit/degraded). Requires the host ability to be
   active while polling.
3. **Completion** (~1-10 minutes with a real LLM): mirrors the Editor probe's
   ordering — server `error` → log error; `status != "ok"` → log rejection
   with server issues; `ok` → client-side `AbilityConfigValidator` final check
   (schema-drift guard) → `AbilityConfigBuilder.FromDto` → destroy the previous
   generated config → `ReplaceSkill` (Skills[0] becomes the new skill).
   A degraded response (`status="ok"` + `degraded=true`: the server exhausted
   its budget and delivers the last validated draft) passes the same path and
   is injected like any other ok result.

## Lifecycle notes

- The generated `AbilityConfig` is a runtime `CreateInstance` SO owned by the
  component; it replaces the current skill **for this battle only**
  (`ReplaceSkill` never touches `EntityData`, so the next battle's PreWarm
  restores the designer-authored skill).
- `OnTeardown` (host dormancy/death) aborts the in-flight request and resets
  the state; it does **not** destroy `_lastGenerated` — that config is still
  the skill currently in play. Redeploy re-triggers an `OnInitialize` rule and
  starts a fresh generation.
- Failures (server down, rejection, timeout) are logged and **not** retried;
  the next trigger (e.g. next deploy) starts a new task.

## Typical configuration

A persistent talent (sp block `totalSp=0` + `Auto` + `NoConsume`, always
active) with one rule: trigger `OnInitialize` → single step
`generate_skill`. Generation fires once per deploy; the original skill stays
playable until the generated one arrives.
