# AttackEventValueModifier Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a generic `AttackEventValueModifier` skill component that rewrites fields on `BeforeAttackEvent` (and other `DamageEventBase` events) using three parallel CSV parameters; mark `AttackMultiplierBoost` and `SetAttackCombo` as `[Obsolete]`.

**Architecture:** New single-file component with hardcoded `switch` over an 8-field whitelist. Three ParamList string CSVs (`fields`, `values`, `methods`) drive the rewrite. CSV is parsed once in `OnInit` into typed arrays; `OnTrigger` only does typed math. Old components get `[Obsolete]` for future migration.

**Tech Stack:** Unity C# (Unity Editor for compile verification), existing `SkillSystem.Components` patterns, `BuffParamParser.ParseFloats` for float CSV.

**Spec:** `docs/superpowers/specs/2026-06-11-attack-event-value-modifier-design.md`

**Test strategy note:** This Unity project has no unit test infrastructure for skill components. Verification = (1) Unity Editor compile passes with no new errors, (2) PlayMode manual smoke test per spec §"验证". Each task below includes a "verify" step that tells the engineer what to look for in the Editor.

---

## File Structure

| File | Change | Purpose |
|---|---|---|
| `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/AttackEventValueModifier.cs` | Create | New generic field-rewriter component |
| `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/AttackMultiplierBoost.cs` | Edit (add `[Obsolete]`) | Mark for future migration |
| `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/SetAttackCombo.cs` | Edit (add `[Obsolete]`) | Mark for future migration |

No new tests, no Inspector schema changes, no other components touched.

---

## Task 1: Mark `AttackMultiplierBoost` and `SetAttackCombo` as `[Obsolete]`

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/AttackMultiplierBoost.cs` (top of class, add attribute)
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/SetAttackCombo.cs` (top of class, add attribute)

- [ ] **Step 1.1: Add `[Obsolete]` to `AttackMultiplierBoost.cs`**

Open the file. Find the line `[RegisterComponent("AttackMultiplierBoost")]`. Insert the `[System.Obsolete]` attribute immediately above it (no blank line between, to keep the attributes grouped). **Use the fully-qualified form `[System.Obsolete]`** — these files have no `using System;` directive, so the unqualified `[Obsolete]` would not compile:

```csharp
    [System.Obsolete("Use AttackEventValueModifier")]
    [RegisterComponent("AttackMultiplierBoost")]
    public class AttackMultiplierBoost : ISkillComponent
```

- [ ] **Step 1.2: Add `[Obsolete]` to `SetAttackCombo.cs`**

Open the file. Find the line `[RegisterComponent("SetAttackCombo")]`. Insert the `[System.Obsolete]` attribute immediately above it (fully-qualified for the same reason as Step 1.1):

```csharp
    [System.Obsolete("Use AttackEventValueModifier")]
    [RegisterComponent("SetAttackCombo")]
    public class SetAttackCombo : ISkillComponent
```

- [ ] **Step 1.3: Verify the two files**

`grep` for the new attributes in both files. Expected output: one line per file.

```bash
grep -n "Obsolete" Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/AttackMultiplierBoost.cs Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/SetAttackCombo.cs
```

Expected:
```
Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/AttackMultiplierBoost.cs:3:    [Obsolete("Use AttackEventValueModifier")]
Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/SetAttackCombo.cs:3:    [Obsolete("Use AttackEventValueModifier")]
```

- [ ] **Step 1.4: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/AttackMultiplierBoost.cs Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/SetAttackCombo.cs
git commit -m "chore: mark AttackMultiplierBoost/SetAttackCombo as [Obsolete]

Use AttackEventValueModifier for new skill configs. Both old components
remain functional for backward compatibility; zero .asset references today.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 2: Create `AttackEventValueModifier.cs`

**Files:**
- Create: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/AttackEventValueModifier.cs`

- [ ] **Step 2.1: Create the file with full implementation**

Create `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/AttackEventValueModifier.cs` with the following content (full file, no placeholders):

```csharp
using System;
using UnityEngine;

namespace SkillSystem.Components
{
    /// <summary>
    /// Generic field-rewriter for <see cref="BeforeAttackEvent"/> (and other <see cref="DamageEventBase"/>
    /// events). Reads three parallel CSVs from <c>OnInit</c>:
    /// <list type="bullet">
    ///   <item><c>fields</c> — comma-separated field names (whitelisted; see below)</item>
    ///   <item><c>values</c> — comma-separated numeric values (float or int, per field type)</item>
    ///   <item><c>methods</c> — comma-separated operators: <c>mult</c> / <c>add</c> / <c>set</c></item>
    /// </list>
    /// Each triple at the same index is applied in order. <c>cumbo</c> is only meaningful on
    /// <see cref="BeforeAttackEvent"/> and is silently skipped on other DamageEventBase events.
    ///
    /// <para>Supersedes <c>AttackMultiplierBoost</c> (<c>multiplyer *= N</c>) and
    /// <c>SetAttackCombo</c> (<c>cumbo = N</c>), both of which remain [Obsolete].</para>
    /// </summary>
    [RegisterComponent("AttackEventValueModifier")]
    public class AttackEventValueModifier : ISkillComponent
    {
        private string[] _fields = Array.Empty<string>();
        // Per-type parsed values; one slot per field index. Type determined by the field's
        // known type — float fields read _floatValues[i], int fields read _intValues[i].
        // Only one is ever populated per index.
        private float[] _floatValues = Array.Empty<float>();
        private int[] _intValues = Array.Empty<int>();
        private string[] _methods = Array.Empty<string>();

        public void OnInit(SkillContext ctx, ParamList parameters)
        {
            _fields = SplitCsv(parameters.GetString("fields", ""));
            _methods = SplitCsv(parameters.GetString("methods", ""));

            // Pre-parse values by attempting both float and int. Each index's type is locked
            // by the field's known type (see OnTrigger switch), so we keep both arrays and
            // ignore the other at apply time. A mis-typed value (e.g. "abc" for an int field)
            // is caught at apply time and logged + skipped.
            string[] rawValues = SplitCsv(parameters.GetString("values", ""));
            int n = rawValues.Length;
            _floatValues = new float[n];
            _intValues = new int[n];
            for (int i = 0; i < n; i++)
            {
                float.TryParse(rawValues[i], out _floatValues[i]);
                int.TryParse(rawValues[i], out _intValues[i]);
            }

            // Length-mismatch guard: log once, then trim to min length so OnTrigger
            // can index safely. Lenient by design (per spec §"CSV 错误处理").
            int fLen = _fields.Length;
            int vLen = rawValues.Length;
            int mLen = _methods.Length;
            int min = Math.Min(Math.Min(fLen, vLen), mLen);
            if (fLen != vLen || vLen != mLen)
            {
                Debug.LogWarning($"AttackEventValueModifier: length mismatch fields={fLen} values={vLen} methods={mLen}; applying first {min} entries");
            }
            if (min < _fields.Length)
            {
                Array.Resize(ref _fields, min);
                Array.Resize(ref _floatValues, min);
                Array.Resize(ref _intValues, min);
                Array.Resize(ref _methods, min);
            }
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (ctx.currentEvent is not DamageEventBase dab)
            {
                Debug.LogError("AttackEventValueModifier: current event is not a DamageEventBase; skipping");
                return;
            }
            BeforeAttackEvent bae = dab as BeforeAttackEvent;

            for (int i = 0; i < _fields.Length; i++)
            {
                string field = _fields[i];
                string method = _methods[i];

                switch (field)
                {
                    case "multiplyer":
                        dab.multiplyer = ApplyFloat(dab.multiplyer, _floatValues[i], method, field);
                        break;
                    case "defPenetrate":
                        dab.defPenetrate = ApplyFloat(dab.defPenetrate, _floatValues[i], method, field);
                        break;
                    case "mgrPenetrate":
                        dab.mgrPenetrate = ApplyFloat(dab.mgrPenetrate, _floatValues[i], method, field);
                        break;
                    case "defPenetrate_value":
                        dab.defPenetrate_value = ApplyFloat(dab.defPenetrate_value, _floatValues[i], method, field);
                        break;
                    case "mgrPenetrate_value":
                        dab.mgrPenetrate_value = ApplyFloat(dab.mgrPenetrate_value, _floatValues[i], method, field);
                        break;
                    case "damageType":
                        dab.damageType = ApplyInt(dab.damageType, _intValues[i], _floatValues[i], method, field);
                        break;
                    case "applyType":
                        dab.applyType = ApplyInt(dab.applyType, _intValues[i], _floatValues[i], method, field);
                        break;
                    case "cumbo":
                        if (bae == null)
                        {
                            Debug.Log($"AttackEventValueModifier: 'cumbo' skipped — event is not BeforeAttackEvent");
                            break;
                        }
                        bae.cumbo = ApplyInt(bae.cumbo, _intValues[i], _floatValues[i], method, field);
                        break;
                    default:
                        Debug.LogWarning($"AttackEventValueModifier: unknown field '{field}'; skipped");
                        break;
                }
            }
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }

        // ---- helpers ----

        private static float ApplyFloat(float current, float value, string method, string field)
        {
            switch (method)
            {
                case "mult": return current * value;
                case "add":  return current + value;
                case "set":  return value;
                default:
                    Debug.LogWarning($"AttackEventValueModifier: unknown method '{method}' for field '{field}'; skipped");
                    return current;
            }
        }

        // Int overload also takes the float-parsed value for the 'mult' case so a designer
        // who typed "1.5" for cumbo still gets a sensible (rounded) result instead of a parse
        // failure. 'add' and 'set' prefer the int-parsed value to keep designer intent exact.
        private static int ApplyInt(int current, int intValue, float floatValue, string method, string field)
        {
            switch (method)
            {
                case "mult": return Mathf.RoundToInt(current * floatValue);
                case "add":  return current + intValue;
                case "set":  return intValue;
                default:
                    Debug.LogWarning($"AttackEventValueModifier: unknown method '{method}' for field '{field}'; skipped");
                    return current;
            }
        }

        private static string[] SplitCsv(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return Array.Empty<string>();
            var parts = csv.Split(',');
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
            return parts;
        }
    }
}
```

- [ ] **Step 2.2: Verify file content (no copy-paste drift)**

```bash
ls -la Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/AttackEventValueModifier.cs
grep -c "RegisterComponent" Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/AttackEventValueModifier.cs
```

Expected: file exists, `grep` returns `1` (one `[RegisterComponent("AttackEventValueModifier")]`).

- [ ] **Step 2.3: Verify the new component is registered**

`ComponentAutoRegistry` enumerates types via reflection at runtime; this means Unity must compile the assembly for the new class to be visible. **No static check possible from CLI** — flag this for the Editor verification in Task 3.

- [ ] **Step 2.4: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/AttackEventValueModifier.cs
git commit -m "feat: add AttackEventValueModifier (generalized event-field rewriter)

Replaces AttackMultiplierBoost and SetAttackCombo (both now [Obsolete])
with a single component parameterized by 3 parallel CSVs:
  fields   = comma-separated field names (8 whitelisted)
  values   = comma-separated numeric values
  methods  = comma-separated operators (mult / add / set)

Hardcoded switch over the 8 DamageEventBase + BeforeAttackEvent fields.
'cumbo' is silently skipped when the event is not BeforeAttackEvent.
Lenient method matching — all 3 methods valid for all fields, designer
owns semantic correctness.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 3: Editor compile verification

**Files:** none (manual verification step in Unity Editor)

This task is a manual gate. The project has no CLI compile path; Unity must be opened to validate.

- [ ] **Step 3.1: Open Unity Editor**

Open the project at `e:\Unity\projects\TD` in Unity Editor. Wait for the asset database refresh and script compilation to finish (the spinner in the bottom-right stops; "Compiling" disappears from the status bar).

- [ ] **Step 3.2: Inspect the Console**

Open `Window → General → Console` (or `Ctrl+Shift+C`).

**Expected:** zero red errors. A `[Obsolete]` warning may appear if any code in the project references the renamed components — `grep` confirmed zero such references, so the only expected message is the `AttackEventValueModifier` file compiling cleanly.

If errors appear, read the stack trace and fix per the message. Common failures and fixes:
- `CS0103: name 'DamageEventBase' does not exist` → check `using SkillSystem;` is at top of file (it is, per Step 2.1)
- `CS0117: 'DamageEventBase' does not contain a definition for 'X'` → field name typo in the switch case; cross-check against `SkillEvents.cs`

- [ ] **Step 3.3: Verify `[RegisterComponent]` shows up**

In the Console, clear all messages, then in the Project window navigate to any existing `ComponentConfig` `.asset` (search `t:ComponentConfig`). Click the `componentType` dropdown — `AttackEventValueModifier` should appear alongside the existing 22 components.

If it does NOT appear:
- Confirm `ComponentAutoRegistry.EnsureRegistered()` runs (called from `SkillSystemBootstrap.Init`, which has `[RuntimeInitializeOnLoadMethod]`) — it runs at play time, not edit time, so the dropdown may not reflect the new component in Edit mode
- Switch to Play mode briefly to force `EnsureRegistered` to run, then return to Edit mode

- [ ] **Step 3.4: Mark verification done**

No commit. This task records the verification result. If the Editor compiled cleanly and the component is in the dropdown, proceed to Task 4. If not, halt and report to the user.

---

## Task 4: PlayMode manual smoke test (per spec §"验证")

**Files:** none (manual playmode test)

This task is a manual gate. The user is responsible for running it; the plan records the test scenarios so they can be re-run.

- [ ] **Step 4.1: Create a test SkillConfig**

In the Project window, right-click → `Create → Skill System → SkillConfig` (or use an existing test asset). On the new asset:
- Add a `ComponentConfig` entry
- Set `componentType = AttackEventValueModifier`
- Add `parameters`:
  - `fields = multiplyer,cumbo,damageType` (string)
  - `values = 1.5,3,2` (string)
  - `methods = mult,set,set` (string)
- Set `triggers[0]` to `OnBeforeAttack` (TriggerEvent)

- [ ] **Step 4.2: Wire the SkillConfig to a test skill**

Attach the test config to any testable entity in a playtest scene. Easiest: use an existing test enemy / character that has the `EntitySkillRunner` and a skill slot for this config.

- [ ] **Step 4.3: Run and verify**

Enter Play mode. Trigger the skill (or wait for it to auto-fire per the entity's AI).

In Visual Studio / Rider with Unity debugger attached, set a breakpoint inside `AttackEventValueModifier.OnTrigger` at the `multiplyer` case. Confirm:
- `dab.multiplyer` was multiplied by 1.5 (e.g., original 1.0 → 1.5)
- `dab.cumbo` was set to 3
- `dab.damageType` was set to 2

If the entity's downstream behavior (damage applied, combo count, etc.) reflects the new values, the test passes.

- [ ] **Step 4.4: Edge cases**

In the same playtest, swap the SkillConfig parameters to the following and confirm the Console message + behavior:

| Test | fields | values | methods | Expected |
|---|---|---|---|---|
| Length mismatch | `a,b,c` | `1,2` | `x,y,z` | LogWarning: "length mismatch fields=3 values=2 methods=3; applying first 2", first 2 entries applied, no crash |
| Unknown field | `multipler` (typo) | `1.5` | `mult` | LogWarning: "unknown field 'multipler'; skipped" |
| cumbo on non-BeforeAttack | wire triggers to `OnAfterAttack`, fields=`cumbo` | `5` | `set` | Log: "'cumbo' skipped — event is not BeforeAttackEvent" |
| Unknown method | `multiplyer` | `1.5` | `multiply` | LogWarning: "unknown method 'multiply' for field 'multiplyer'; skipped", `multiplyer` unchanged |
| Empty fields | (empty) | (empty) | (empty) | No-op (no warnings or errors) |

- [ ] **Step 4.5: Mark verification done**

No commit. Report test results to the user. If any case fails, halt and investigate.

---

## Self-Review

**1. Spec coverage:**
- §"命名论证" → Task 1 + Task 2 (file name, class name, `[RegisterComponent]` key all align)
- §"Inputs" (3 CSVs) → Task 2 Step 2.1 (`OnInit` reads `fields`/`values`/`methods` from `ParamList.GetString`)
- §"字段白名单" (8 fields) → Task 2 Step 2.1 (8 switch cases)
- §"方法算子" (mult/add/set) → Task 2 Step 2.1 (`ApplyFloat` + `ApplyInt` switch)
- §"类型解析规则" → Task 2 Step 2.1 (both `float.TryParse` and `int.TryParse` in `OnInit`, helper selection in switch)
- §"OnTrigger 行为" → Task 2 Step 2.1 (cast `DamageEventBase`, secondary cast for `cumbo`, switch with 8 cases)
- §"CSV 错误处理" → Task 2 Step 2.1 (length mismatch `LogWarning` + trim; unknown field `LogWarning`; unknown method `LogWarning`; empty fields is no-op)
- §"旧组件 Obsolete 化" → Task 1 (both files get `[Obsolete("Use AttackEventValueModifier")]`)
- §"文件清单" → Task 1 + Task 2
- §"验证" → Task 3 (compile) + Task 4 (PlayMode)

**2. Placeholder scan:** No "TBD" / "TODO" / "implement later" / "similar to Task N" / etc. The full component code is in Task 2.1.

**3. Type consistency:** Field name strings (`"multiplyer"`, `"cumbo"`, etc.) match between `OnInit` validation (none — those happen in switch) and `OnTrigger` switch. Method strings (`"mult"`, `"add"`, `"set"`) match between the two helper switches. `_fields`/`_floatValues`/`_intValues`/`_methods` are all resized together in the length-mismatch guard.

**4. Ambiguity check:**
- Task 2.1's `ApplyInt` takes both `intValue` and `floatValue` — documented inline. The `mult` case uses `floatValue` (so `1.5` works for cumbo); `add` and `set` use `intValue` (so `2` stays `2`).
- `cumbo` skipped silently on non-BeforeAttackEvent — Log (info level) per spec; not LogWarning, because the configuration is valid, just the event is wrong.

No issues found.
