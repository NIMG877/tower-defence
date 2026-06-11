# Condition Trigger AND/OR + Blackboard Unification

**Date:** 2026-06-11
**Status:** Draft
**Author:** brainstorming session

## Problem

Two related gaps in the skill-system trigger pipeline:

1. **`triggers[]` is documented as a conditional gate but is not enforced.**
   `EntitySkillRunner.DispatchToSkill` calls `OnTrigger` on every component
   in the bucket without consulting `ConditionConfig.op / leftKey / rightValue`.
   `ConditionEvaluator.Evaluate` is implemented but has zero callers in the
   codebase (the only references are in stale plan documents).
   `docs/skill-components/README.md:114-135` advertises the conditional
   behaviour; the runtime does not match the documentation.

2. **The `triggers[]` bucket has a duplication bug.**
   `EntitySkillRunner.BuildSkillRuntime:169` does
   `list.Add(inst)` without dedup. A designer who configures two
   `ConditionConfig` entries with the same `(component, triggerEvent)` —
   e.g. to express "hp > 0.3 OR has_debuff" via repetition — gets
   `OnTrigger` called twice for the same component on the same event.
   This is a real footgun, not theoretical: the `AttackMultiplierBoost` /
   `SetAttackCombo` rows in `SK_Kroos_1.asset` were historical examples
   (now removed in commit `a822214`).

Two blackboards coexist (`SkillRuntime.blackboard` per-skill,
`EntitySkillRunner.sharedBlackboard` per-Entity) and 5 components read
the wrong one. Designer intent is "per-Entity shared blackboard" (you
explicitly stated this during brainstorming: "技能系统所有对于blackboard的操作都应该是per-entity的,这样不同技能之间方便联动,不应该出现其它黑板").

## Goal

Make `ConditionConfig` an actual conditional gate at dispatch time,
express AND/OR composition in the data model (eliminating the bucket
duplication footgun by replacing the repetition pattern with proper
nesting), and unify on a single per-Entity blackboard.

## Non-Goals

- Restoring the `HasBlackboardKey` / `NotHasBlackboardKey` operators
  trimmed in commit `54c3465`. They were intentionally removed; this
  spec does not bring them back.
- Implementing the deferred `HasBuff` / `NotHasBuff` / `IsInAbnormalState`
  / `NotInAbnormalState` semantics. `ConditionEvalContext.entity` is
  reserved for a future PR to do so; this spec just exposes the hook.
- Adding a unit-test infrastructure. The project has no EditMode/PlayMode
  test runner today; introducing one is out of scope.
- Migrating every existing `ConditionConfig` field in `.asset` files.
  Unity silently drops unknown fields; the existing data shape is
  compatible (see §1.4 Compatibility).

---

## §1 — Data Model: Nested `ConditionConfig`

### 1.1 New Shape

Three new `[Serializable]` classes; `ConditionConfig` loses its flat
condition fields.

```csharp
[Serializable]
public class ConditionUnit
{
    public ConditionOp op = ConditionOp.None;
    public string leftKey;
    public string rightValue;
}

[Serializable]
public class ConditionGroup
{
    public List<ConditionUnit> units = new List<ConditionUnit>();
}

[Serializable]
public class ConditionConfig
{
    public TriggerEvent triggerEvent;
    public List<ConditionGroup> groups = new List<ConditionGroup>();
}
```

The old fields (`op`, `leftKey`, `rightValue`) on `ConditionConfig` are
**removed** (not `[Obsolete]`-bridged; the only deserialized instance is
`SK_Kroos_1.asset` and the change is a no-op for it — see §1.4).

### 1.2 Why Three Classes, Not `List<List<ConditionUnit>>`

Unity's `SerializeField` does not serialize `List<List<T>>` (the inner
list is not a recognised container). A wrapper class for the inner
list is the standard pattern; it also produces clean Inspector
visualisation (`groups → Group 0 → units → Unit 0`).

### 1.3 Semantics

- `Evaluate(ConditionConfig, ConditionEvalContext)`:
  - Empty / null `groups` → `true` (no condition, always passes).
  - Otherwise: iterate `groups`; return `true` on the first group that
    evaluates `true` (short-circuit OR).
  - If no group passes, return `false`.
- `Evaluate(ConditionGroup, …)`:
  - Empty / null `units` → `true`.
  - Otherwise: iterate `units`; return `false` on the first unit that
    evaluates `false` (short-circuit AND).
- `Evaluate(ConditionUnit, …)`:
  - `op == None` → `true`.
  - Otherwise: dispatch to the existing operator switch (Equal,
    NotEqual, Greater, GreaterOrEqual, Less, LessOrEqual, plus the
    safe default for unknown ops — see §4).

### 1.4 Compatibility with Existing `.asset` Files

The only `ConditionConfig` data in the project lives in
`Assets/Resources/Prefabs/Characters/3/Kroos/skills/SK_Kroos_1.asset`
(3 entries, all with `op: 0` and empty `leftKey` / `rightValue`).

After the field-shape change:
- The old `op` / `leftKey` / `rightValue` keys are unknown to Unity's
  serializer and are silently dropped.
- The new `groups` field defaults to an empty list.
- Empty `groups` evaluates to `true` — behaviour identical to the
  previous `op: 0` (None) short-circuit.
- Two of the three entries are attached to components that were removed
  in commit `a822214` (`AttackMultiplierBoost`, `SetAttackCombo`).
  Those components are never instantiated, so the dangling trigger
  data is unreachable code. **No `.asset` migration is required.**
  A designer who opens the asset will see the orphan entries and can
  delete them at their leisure.

---

## §2 — Dispatch Pipeline

### 2.1 Bucket Data Structure

`SkillRuntime.componentsByTrigger` value type changes from
`List<ISkillComponent>` to `List<(ISkillComponent comp, ConditionConfig cond)>`:

```csharp
public Dictionary<TriggerEvent, List<(ISkillComponent comp, ConditionConfig cond)>>
    componentsByTrigger
    = new Dictionary<TriggerEvent, List<(ISkillComponent, ConditionConfig)>>();
```

Keyed by `TriggerEvent` (unchanged); value is now a list of
`(component, condition)` pairs so each `ConditionConfig` is independently
evaluable.

### 2.2 Build Phase

In `EntitySkillRunner.BuildSkillRuntime` (around line 157-172), each
`ConditionConfig` in `ccfg.triggers` produces exactly one entry in the
appropriate bucket. **No deduplication at build time** — see §2.4 for
why the old duplication bug disappears by construction.

### 2.3 Dispatch Phase (the actual gate)

`EntitySkillRunner.DispatchToSkill` is the single insertion point for
the new gate. The implementation must avoid a subtle bug: if a
component has multiple `ConditionConfig` entries for the same
`triggerEvent` and the *first* one fails, the *second* one (which
might pass) must still be allowed to trigger `OnTrigger`. The naive
"evaluate-as-you-dedupe" order makes the first-fail-wins; the correct
order is **evaluate all, then fire once per component if any passed**.

```csharp
private void DispatchToSkill(SkillRuntime s, SkillEvent evt)
{
    bool bypassActiveGate = evt is PreWarmEvent
                         || evt is InitializeEvent
                         || evt is SkillBeginEvent
                         || evt is SkillEndEvent;
    if (!s.isActive && !bypassActiveGate) return;
    if (!s.componentsByTrigger.TryGetValue(evt.TriggerEvent, out var list)) return;

    // All (comp, cond) for this event share the same eval context.
    var evalCtx = new ConditionEvalContext
    {
        sharedBlackboard = sharedBlackboard,
        entity = _entity,
        currentEvent = evt,
    };

    // Two-pass over the bucket:
    //   pass 1: evaluate every (comp, cond); mark comp "should-fire" if any of its conds passes
    //   pass 2: for each "should-fire" comp, call OnTrigger exactly once
    // The set is keyed by comp, not by (comp, cond), so two entries
    // pointing at the same comp collapse into one OnTrigger.
    var shouldFire = new HashSet<ISkillComponent>();
    for (int i = 0; i < list.Count; i++)
    {
        var (comp, cond) = list[i];
        if (shouldFire.Contains(comp)) continue;     // already known to fire
        if (ConditionEvaluator.Evaluate(cond, evalCtx))
            shouldFire.Add(comp);
    }
    foreach (var comp in shouldFire)
    {
        var ctx = PrepareContext(s.MakeContext(comp, evt));
        comp.OnTrigger(ctx);
    }
}
```

Note the **order**: evaluate first, dedup second. Reversing the order
would mean a failing condition "consumes" the component for this
event, even if a later condition on the same component would have
passed.

### 2.4 Why the Duplication Bug Is Gone

Old (broken) behaviour: a designer who wanted "hp > 0.3 OR has_debuff"
expressed it by repeating the same `triggerEvent` twice in `triggers[]`.
The builder appended `(inst, c1)` and `(inst, c2)` as two entries;
dispatch called `OnTrigger` twice. This was the documented footgun
behind this spec.

New behaviour: the same intent is expressed via **one** `ConditionConfig`
with `groups = [[{Greater, "hpRate", "0.3"}], [{Equal, "has_debuff", "1"}]]`.
The builder appends one entry. Dispatch evaluates the AND/OR tree and
calls `OnTrigger` once if any group passes.

The dedup in §2.3 is a safety net for **misconfigured** triggers
(designer accidentally repeats the same `triggerEvent` despite the
new nesting option). With the two-pass order above, misconfiguration
is silently downgraded to "any one of the repeated conditions passes
→ fire once", which is the closest semantically-sensible behaviour
short of rejecting the configuration outright. It is not
load-bearing for correct configurations and is not an officially
supported expression form.

### 2.5 Hot-Path Performance

- `HashSet<ISkillComponent>` is allocated per `DispatchToSkill` call.
  In the worst case (a hot event like `OnBeforeAttack` firing per
  attack), this is one allocation per event. If profiling shows this
  is a problem, the set can be reused across calls (field on
  `EntitySkillRunner` cleared at the start of each dispatch). Defer
  until profiling evidence exists.

---

## §3 — Blackboard Unification

### 3.1 The One Blackboard

**The only allowed blackboard is the per-Entity shared blackboard.**
`SkillRuntime.blackboard` and `SkillContext.blackboard` (the per-skill
fields) are removed.

### 3.2 Changes

`Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs`:
- Delete `public Blackboard blackboard = new Blackboard();` (line 9).
- `MakeContext` no longer sets `blackboard` on the context.

`Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/ISkillComponent.cs`:
- `SkillContext.blackboard` field deleted (line 19).
- `SkillContext.sharedBlackboard` field retained; comment updated to
  reflect that it is the only blackboard.

Five component files change `ctx.blackboard` → `ctx.sharedBlackboard`:

| File | Lines |
|---|---|
| `Components/ApplyBuff.cs` | 103 |
| `Components/PlayParticleComponent.cs` | 20 |
| `Components/EntitySelector.cs` | 46, 47 |
| `Components/EntitySelectorRadiusEffectComponent.cs` | 51 |
| `Components/CampDamageModifierComponent.cs` | 20 |
| `Components/CoroutineLoopComponent.cs` | 37, 38 |

All seven touch-points are mechanical renames; no logic change.

### 3.3 Behavioral Consequence

A component that previously wrote to `ctx.blackboard` (per-skill) will
now write to `ctx.sharedBlackboard` (per-Entity). Data that was
isolated to a single `SkillRuntime` becomes visible to all skills on
the same entity. This matches the brainstorming decision: cross-skill
collaboration is the intended use case, and "isolated per-skill state"
was never a documented property.

The 7 touch-points are all reads (no writes that would have
cross-skill side effects). The behavioral change is therefore limited
to "read may now see data set by a different skill on the same entity"
— which is the new intent, not a regression.

---

## §4 — `ConditionEvaluator` API

### 4.1 `ConditionEvalContext` Reshape

```csharp
public class ConditionEvalContext
{
    // Renamed from Blackboard → sharedBlackboard to align with SkillContext.
    public Blackboard sharedBlackboard = new Blackboard();

    // Owner entity. Reserved for future HasBuff/IsInAbnormalState
    // operators; this spec does not read it.
    public Entity entity;

    // The dispatching event. Currently unread by the evaluator; typed
    // (not object) to align with SkillContext.currentEvent.
    public SkillEvent currentEvent;
}
```

Field rename + type tightening: `Blackboard` → `sharedBlackboard`;
`object Event` → `SkillEvent currentEvent`. No code in the repo
references the old field names (verified by grep — the only references
were in stale plan docs).

### 4.2 `Evaluate` Signature

```csharp
public static bool Evaluate(ConditionConfig cond, ConditionEvalContext ctx)
{
    if (cond == null) return true;
    if (cond.groups == null || cond.groups.Count == 0) return true;

    for (int g = 0; g < cond.groups.Count; g++)
        if (EvaluateGroup(cond.groups[g], ctx)) return true;
    return false;
}

private static bool EvaluateGroup(ConditionGroup group, ConditionEvalContext ctx)
{
    if (group == null) return true;
    if (group.units == null || group.units.Count == 0) return true;

    for (int u = 0; u < group.units.Count; u++)
        if (!EvaluateUnit(group.units[u], ctx)) return false;
    return true;
}

private static bool EvaluateUnit(ConditionUnit unit, ConditionEvalContext ctx)
{
    if (unit == null) return true;
    if (unit.op == ConditionOp.None) return true;

    return unit.op switch
    {
        ConditionOp.Equal          => ctx.sharedBlackboard.Get<string>(unit.leftKey) == unit.rightValue,
        ConditionOp.NotEqual       => ctx.sharedBlackboard.Get<string>(unit.leftKey) != unit.rightValue,
        ConditionOp.Greater        => CompareNumeric(ctx, unit.leftKey, unit.rightValue) >  0,
        ConditionOp.GreaterOrEqual => CompareNumeric(ctx, unit.leftKey, unit.rightValue) >= 0,
        ConditionOp.Less           => CompareNumeric(ctx, unit.leftKey, unit.rightValue) <  0,
        ConditionOp.LessOrEqual    => CompareNumeric(ctx, unit.leftKey, unit.rightValue) <= 0,
        _                          => WarnUnknownOpAndPass(unit.op),
    };
}

private static int CompareNumeric(ConditionEvalContext ctx, string leftKey, string rightValueStr)
{
    var left = ctx.sharedBlackboard.Get<string>(leftKey, "");
    if (float.TryParse(left, out var l) && float.TryParse(rightValueStr, out var r))
        return l.CompareTo(r);
    return string.Compare(left, rightValueStr, StringComparison.Ordinal);
}

private static readonly HashSet<ConditionOp> _warnedOps = new HashSet<ConditionOp>();
private static bool WarnUnknownOpAndPass(ConditionOp op)
{
    if (_warnedOps.Add(op))
        UnityEngine.Debug.LogWarning(
            $"ConditionEvaluator: unknown ConditionOp {op} treated as 'pass'. " +
            "If this fires, a new op was added without a case in Evaluate.");
    return true;   // backwards-compatible: unknown op → pass
}
```

### 4.3 Unknown-Op Behaviour

**Pass with a one-shot warning.** This is the same end behaviour as the
old `default: return true` (which is what the stale `HasBuff` /
`IsInAbnormalState` paths currently rely on), but the warning surfaces
the case to designers when a new `ConditionOp` is added without
extending the evaluator.

`_warnedOps` is process-wide; the warning fires once per unknown op
across the application lifetime. This bounds log spam if the
configuration is reused.

---

## §5 — Verification

The project has no EditMode/PlayMode test infrastructure. Verification
uses three layers:

### 5.1 Layer 1: Unity Editor Compile

Open the project in Unity. The console must show zero errors and zero
warnings. The `ConditionEvaluator` unknown-op warning is **expected to
never fire** in the current codebase because the `ConditionOp` enum is
the trimmed whitelist from commit `54c3465` (only `None` /
`Equal` / `NotEqual` / `Greater` / `GreaterOrEqual` / `Less` /
`LessOrEqual` remain).

### 5.2 Layer 2: Subagent Spec + Code-Quality Review

- **Spec reviewer**: confirms the implementation matches §1-§4 of this
  document; flags any deviation.
- **Code-quality reviewer**: checks the dispatch hot path (`HashSet`
  allocation, evaluator nesting depth, null safety on `groups` /
  `units`).

### 5.3 Layer 3: PlayMode Smoke (designer in Editor)

1. Load `SK_Kroos_1.asset`'s entity; trigger one `OnBeforeAttack`.
   Assert `ApplyBuff.OnTrigger` is called once (its `triggers[]` had
   `op: 0` in the old shape; new `groups = []` is behaviourally
   equivalent).
2. Edit `SK_Kroos_1.asset`: change `ApplyBuff`'s trigger to
   `groups = [[{Greater, "hpRate", "0.3"}]]`. With entity HP above
   30%, trigger; assert `OnTrigger` fires. Drop HP below 30%; trigger;
   assert `OnTrigger` does not fire.
3. Add a second unit to the same group:
   `groups = [[{Greater, "hpRate", "0.3"}, {Equal, "targetCamp", "1"}]]`.
   Vary HP and target camp; assert the AND semantics.
4. Add a second group:
   `groups = [[{Greater, "hpRate", "0.3"}], [{Equal, "has_debuff", "1"}]]`.
   Vary HP and `has_debuff`; assert the OR semantics.

### 5.4 Hard Pass Criteria

1. ✅ Unity Editor console: zero errors, zero warnings.
2. ✅ Both subagent reviews: approved.
3. ✅ All 4 PlayMode smoke cases pass.
4. ✅ Grep for `ctx.blackboard` (excluding `ConditionEvalContext`
   field definition): 0 hits.
5. ✅ Grep for `\.blackboard` in `SkillRuntime.cs`: 0 hits (field
   removed).
6. ✅ `SK_Kroos_1.asset` opens in Unity without errors after the field
   shape change (silent field drop is a no-op).

### 5.5 Future Work (not in this spec)

- Unit-test infrastructure: `ConditionEvaluator.Evaluate` is designed
  to be unit-testable as soon as an EditMode test asmdef is added
  (`new ConditionEvalContext()`, set blackboard keys, call `Evaluate`).
- `HasBuff` / `IsInAbnormalState` operators: `ConditionEvalContext.entity`
  is the hook for a future PR to read `BuffController` state.

---

## File Inventory

### Modify

- `Assets/PublicScripts/GameData/SkillSystem/ComponentConfig.cs` —
  new `ConditionUnit` / `ConditionGroup` classes; `ConditionConfig`
  field shape change.
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/ConditionEvaluator.cs` —
  nested evaluator + `ConditionEvalContext` reshape + unknown-op warning.
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs` —
  delete `blackboard` field; new `componentsByTrigger` value type;
  `MakeContext` no longer sets `blackboard`.
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/ISkillComponent.cs` —
  delete `SkillContext.blackboard` field; update `sharedBlackboard` comment.
- `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs` —
  `DispatchToSkill` inserts the Evaluate gate; `BuildSkillRuntime`
  emits `(comp, cond)` tuples.
- 5 component files (7 touch-points total): `ctx.blackboard` →
  `ctx.sharedBlackboard`.
- `docs/skill-components/README.md` — "Conditional triggers" section
  (§114-135) rewritten to document the nested AND/OR shape.

### Do Not Modify

- `SK_Kroos_1.asset` — Unity silently drops the old `op` / `leftKey` /
  `rightValue` fields; the new `groups` field defaults to empty; the
  three existing entries behave identically. Designer can clean up the
  two orphan entries at their leisure.

---

## Decisions Captured During Brainstorming

1. **Blackboard scope** — per-Entity shared only; no per-skill
   blackboard.
2. **`SkillRuntime.blackboard` lifecycle** — deleted, not aliased.
3. **`ConditionEvalContext` interface** — reshape (rename + add fields);
   not a clean-slate replacement.
4. **Trigger semantics** — the AND/OR shape in `ConditionConfig.groups`
   is the **only** supported way to express multi-condition triggers.
   The "repeat the same `triggerEvent` to express OR" pattern is a
   pre-fix misconfiguration (root cause of the duplication bug
   described in the Problem section); designers who currently rely on
   it must migrate to nested `groups` before the field shape lands.
   The `HashSet<ISkillComponent>` dedup in §2.3 is a runtime safety
   net that fires `OnTrigger` at most once per component per event
   regardless of how many `ConditionConfig` entries point at the same
   component for the same `triggerEvent` — it does not make the
   misconfiguration express the same intent as a nested OR (the
   conditions on the repeated entries are not merged, each is
   evaluated independently and the first one to pass wins, but only
   one `OnTrigger` call is produced).
5. **AND/OR shape** — three classes (`ConditionConfig` / `ConditionGroup` /
   `ConditionUnit`) rather than `List<List<ConditionUnit>>` (Unity
   serialiser doesn't accept nested `List<T>`).
6. **Old `triggers[]` flat shape** — no compatibility bridge; the
   `op` / `leftKey` / `rightValue` fields on `ConditionConfig` are
   removed. Unity's silent drop is sufficient because the only
   deserialised data has `op: 0` (behavioural no-op).
7. **Unknown-op behaviour** — pass + one-shot warning (mirrors old
   `default: return true` end behaviour, adds visibility).
