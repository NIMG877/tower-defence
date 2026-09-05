# Skill Trigger Bucket Dispatch — Implementation Plan

> 文档状态：历史实施计划存档，非当前有效文档。当前实现以代码与 docs/ 现行文档为准。


> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire `ConditionConfig.triggerEvent` into `EntitySkillRunner` dispatch via a per-`SkillRuntime` trigger→components dictionary; drop dead types `DeathEvent`/`IntervalTickEvent`; remove component-side `is XxxEvent` guards.

**Architecture:** Each `SkillEvent` subclass declares its `TriggerEvent` enum value via a `virtual abstract` property. `BuildSkillRuntime` populates `SkillRuntime.componentsByTrigger` from `ConditionConfig.triggers[]`. `DispatchToSkill` becomes O(1) dictionary lookup; the `isActive` gate still applies but the four lifecycle events (`PreWarm`/`Initialize`/`SkillBegin`/`SkillEnd`) bypass it. Component bodies stop self-filtering by event type.

**Tech Stack:** Unity 2022.x · C# 9 · `dotnet build` for compile gates.

**Spec:** `docs/superpowers/specs/2026-06-10-skill-trigger-bucket-dispatch-design.md`

**Testing posture:** This project has no project-level test suite. Each task uses `dotnet build TD.sln` as the compile gate, plus a final manual smoke test in the Unity Editor. The base class change in Task 3 uses `abstract` to make missing overrides a compile error — that *is* the safety net for the bridge table.

---

## File Map

**Modified:**
- `Assets/PublicScripts/GameData/SkillSystem/ComponentConfig.cs` — drop two enum values
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/ISkillComponent.cs` — make `SkillEvent.TriggerEvent` abstract
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillEvents.cs` — delete two classes; add `override TriggerEvent` on 14 subclasses
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs` — add `componentsByTrigger` field
- `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs` — `Tick` null-event, `BuildSkillRuntime` bucket population, `DispatchToSkill` bucket lookup
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/AttackMultiplierBoost.cs` — drop guard
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/SetAttackCombo.cs` — drop guard
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/CampDamageModifierComponent.cs` — drop guard
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/LockHpShieldComponent.cs` — drop guard
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/DeathSpawnComponent.cs` — drop guard
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/SelfDamageOnEventComponent.cs` — drop guard, keep `isDeadly` check
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/ApplyBuffComponent.cs` — switch-expression target extraction
- `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/EntitySelectorRadiusEffectComponent.cs` — XML-doc clarifying the nested sub-component contract

**Created:** none.

**Deleted:** `DeathEvent` and `IntervalTickEvent` C# types (file `SkillEvents.cs` stays, just smaller).

---

## Compile Gate

After every code change in this plan, run:

```bash
cd e:/Unity/projects/TD && dotnet build TD.sln -nologo --no-restore 2>&1 | tail -10
```

Expected: `Build succeeded.` with 0 errors. Warnings about pre-existing fields (`_tempOccupy`, `_effectSource`, etc.) are ignorable — they're not introduced by this work.

If a build fails because of `project.assets.json not found`, run `dotnet restore TD.sln` once and retry.

---

## Task 1: Remove `IntervalTickEvent`

**Why first:** Deleting the class requires its sole construction site (`EntitySkillRunner.Tick`) to be removed in the same commit. Removing the enum value `OnIntervalTick` also belongs here.

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs:117`
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillEvents.cs:83-86` (delete class)
- Modify: `Assets/PublicScripts/GameData/SkillSystem/ComponentConfig.cs:83` (remove `OnIntervalTick`)

- [ ] **Step 1.1: Change Tick to pass null SkillEvent**

  In `EntitySkillRunner.cs`, replace the `new IntervalTickEvent { dt = dt }` argument with `null` and add `ctx.entity = _entity;` for parity with `DispatchToSkill`:

  ```csharp
  // OLD (lines 115-120):
              for (int c = 0; c < s.tickingComponents.Count; c++)
              {
                  var ctx = s.MakeContext(s.tickingComponents[c], new IntervalTickEvent { dt = dt });
                  ctx.sharedBlackboard = sharedBlackboard;
                  s.tickingComponents[c].OnTick(ctx, dt);
              }

  // NEW:
              for (int c = 0; c < s.tickingComponents.Count; c++)
              {
                  // IntervalTickEvent removed: OnTick has dt as an explicit parameter,
                  // and ctx.currentEvent is null inside OnTick by design.
                  var ctx = s.MakeContext(s.tickingComponents[c], null);
                  ctx.sharedBlackboard = sharedBlackboard;
                  ctx.entity = _entity;
                  s.tickingComponents[c].OnTick(ctx, dt);
              }
  ```

- [ ] **Step 1.2: Delete the `IntervalTickEvent` class**

  In `SkillEvents.cs`, delete lines 83-86:

  ```csharp
  // DELETE:
      public class IntervalTickEvent : SkillEvent
      {
          public float dt;
      }
  ```

- [ ] **Step 1.3: Remove `OnIntervalTick` from the enum**

  In `ComponentConfig.cs`, remove `OnIntervalTick` from the `TriggerEvent` enum. After edit, lines 74-85 should read:

  ```csharp
      public enum TriggerEvent
      {
          OnPreWarm, OnInitialize,
          OnBeforeAttack, OnAfterAttack,
          OnBeforeTakeDamage, OnAfterTakeDamage,
          OnAttackSuccessfully, OnAttackInterrupt,
          OnBeforeHurt, OnAfterHurt,
          OnAttackAnimBegin,
          OnBeforeDieAnimation, OnDeath,
          OnSkillBegin, OnSkillEnd,
      }
  ```

  (`OnDeath` stays — Task 2 removes it.)

- [ ] **Step 1.4: Compile gate**

  ```bash
  cd e:/Unity/projects/TD && dotnet build TD.sln -nologo --no-restore 2>&1 | tail -10
  ```

  Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 1.5: Commit**

  ```bash
  cd e:/Unity/projects/TD && git add Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillEvents.cs Assets/PublicScripts/GameData/SkillSystem/ComponentConfig.cs && git commit -m "refactor(skill): drop IntervalTickEvent class and OnIntervalTick enum

  OnTick has dt as an explicit parameter; the event wrapper added no
  information. ctx.currentEvent is null inside OnTick now; audit confirms
  no component reads it from OnTick.

  Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
  ```

---

## Task 2: Remove `DeathEvent`

**Why:** Class is defined but no production code constructs it. Spec §6.1.

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillEvents.cs:82` (delete class)
- Modify: `Assets/PublicScripts/GameData/SkillSystem/ComponentConfig.cs` (remove `OnDeath`)

- [ ] **Step 2.1: Delete the `DeathEvent` class**

  In `SkillEvents.cs`, delete the single line:

  ```csharp
  // DELETE:
      public class DeathEvent : SkillEvent { }
  ```

- [ ] **Step 2.2: Remove `OnDeath` from the enum**

  In `ComponentConfig.cs`, remove `OnDeath`. After edit:

  ```csharp
      public enum TriggerEvent
      {
          OnPreWarm, OnInitialize,
          OnBeforeAttack, OnAfterAttack,
          OnBeforeTakeDamage, OnAfterTakeDamage,
          OnAttackSuccessfully, OnAttackInterrupt,
          OnBeforeHurt, OnAfterHurt,
          OnAttackAnimBegin,
          OnBeforeDieAnimation,
          OnSkillBegin, OnSkillEnd,
      }
  ```

- [ ] **Step 2.3: Compile gate**

  ```bash
  cd e:/Unity/projects/TD && dotnet build TD.sln -nologo --no-restore 2>&1 | tail -10
  ```

  Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 2.4: Commit**

  ```bash
  cd e:/Unity/projects/TD && git add Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillEvents.cs Assets/PublicScripts/GameData/SkillSystem/ComponentConfig.cs && git commit -m "refactor(skill): drop DeathEvent class and OnDeath enum

  Class existed but no callsite ever new'd it; the BeforeDieAnimationEvent
  is the only death-adjacent signal currently dispatched. Removing both to
  prevent designer confusion in Inspector dropdowns.

  Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
  ```

---

## Task 3: Add `abstract TriggerEvent` to `SkillEvent` + overrides on all 14 subclasses

**Why third:** Must be a single atomic commit — the `abstract` keyword turns missing overrides into compile errors. Spec §4.1, §4.2.

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/ISkillComponent.cs:5`
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillEvents.cs` (every class)

- [ ] **Step 3.1: Make `SkillEvent.TriggerEvent` abstract**

  In `ISkillComponent.cs` line 5, replace the empty `SkillEvent` base with the abstract-property version:

  ```csharp
  // OLD (line 5):
      public abstract class SkillEvent { }

  // NEW:
      public abstract class SkillEvent
      {
          // Every concrete SkillEvent must declare which TriggerEvent enum value
          // it routes to. Abstract (not virtual) so the compiler catches missing
          // overrides — that's the safety net for the dispatch bridge table.
          public abstract TriggerEvent TriggerEvent { get; }
      }
  ```

- [ ] **Step 3.2: Add `override TriggerEvent` to every subclass in `SkillEvents.cs`**

  Rewrite `SkillEvents.cs` so each of the 14 classes carries the override as its first member. The final file should read:

  ```csharp
  namespace SkillSystem
  {
      public class PreWarmEvent : SkillEvent
      {
          public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnPreWarm;
      }
      public class InitializeEvent : SkillEvent
      {
          public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnInitialize;
      }
      public class BeforeAttackEvent : SkillEvent
      {
          public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnBeforeAttack;
          public Entity target;
          public float multiplyer = 1f;
          public float defPenetrate;
          public float mgrPenetrate;
          public float defPenetrate_value;
          public float mgrPenetrate_value;
          public int cumbo = 1;
          public int damageType;
          public int applyType;
      }
      public class AfterAttackEvent : SkillEvent
      {
          public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAfterAttack;
          public Entity target;
          public float multiplyer;
          public float defPenetrate;
          public float mgrPenetrate;
          public float defPenetrate_value;
          public float mgrPenetrate_value;
          public int damageType;
          public int applyType;
          public bool isDeadly;
      }
      public class BeforeTakeDamageEvent : SkillEvent
      {
          public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnBeforeTakeDamage;
          public Entity target;
          public float multiplyer = 1f;
          public float defPenetrate;
          public float mgrPenetrate;
          public float defPenetrate_value;
          public float mgrPenetrate_value;
          public int damageType;
          public int applyType;
      }
      public class AfterTakeDamageEvent : SkillEvent
      {
          public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAfterTakeDamage;
          public Entity target;
          public float multiplyer;
          public float defPenetrate;
          public float mgrPenetrate;
          public float defPenetrate_value;
          public float mgrPenetrate_value;
          public int damageType;
          public int applyType;
          public bool isDeadly;
      }
      public class AttackSuccessfullyEvent : SkillEvent
      {
          public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAttackSuccessfully;
      }
      public class AttackInterruptEvent : SkillEvent
      {
          public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAttackInterrupt;
      }
      public class BeforeHurtEvent : SkillEvent
      {
          public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnBeforeHurt;
          public Entity origin;
          public float damage;
          public float multiplyer;
          public float defPenetrate;
          public float mgrPenetrate;
          public float defPenetrate_value;
          public float mgrPenetrate_value;
          public int damageType;
          public int applyType;
          public bool isDeadly;
      }
      public class AfterHurtEvent : SkillEvent
      {
          public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAfterHurt;
          public Entity origin;
          public float damage;
          public float multiplyer;
          public float defPenetrate;
          public float mgrPenetrate;
          public float defPenetrate_value;
          public float mgrPenetrate_value;
          public int damageType;
          public int applyType;
          public bool isDeadly;
      }
      public class AttackAnimBeginEvent : SkillEvent
      {
          public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnAttackAnimBegin;
      }
      public class BeforeDieAnimationEvent : SkillEvent
      {
          public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnBeforeDieAnimation;
      }
      public class SkillBeginEvent : SkillEvent
      {
          public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnSkillBegin;
          public SkillRuntime skill;
      }
      public class SkillEndEvent : SkillEvent
      {
          public override TriggerEvent TriggerEvent => SkillSystem.TriggerEvent.OnSkillEnd;
          public SkillRuntime skill;
      }
  }
  ```

  Note the use of `SkillSystem.TriggerEvent.OnXxx` — the fully-qualified enum name disambiguates from the property name `TriggerEvent`.

- [ ] **Step 3.3: Compile gate**

  ```bash
  cd e:/Unity/projects/TD && dotnet build TD.sln -nologo --no-restore 2>&1 | tail -10
  ```

  Expected: `Build succeeded. 0 Error(s)`. If any error of the form `'XxxEvent' does not implement inherited abstract member 'SkillEvent.TriggerEvent.get'` appears, a subclass was missed in Step 3.2.

- [ ] **Step 3.4: Commit**

  ```bash
  cd e:/Unity/projects/TD && git add Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/ISkillComponent.cs Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillEvents.cs && git commit -m "refactor(skill): SkillEvent declares its TriggerEvent enum

  Abstract property on the base, override on every subclass. The bridge
  between configuration enum and runtime class — no reflection, no
  attribute machinery. Compiler enforces every subclass declares its
  routing.

  Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
  ```

---

## Task 4: Add `componentsByTrigger` field to `SkillRuntime`

**Why:** Pure additive — adds storage that Task 5 will populate and Task 6 will read. No behavior change after this commit.

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs:11`

- [ ] **Step 4.1: Add the field**

  Final state of `SkillRuntime.cs`:

  ```csharp
  using System.Collections.Generic;

  namespace SkillSystem
  {
      public class SkillRuntime
      {
          public SkillConfig config;
          public SPEngine spEngine;
          public Blackboard blackboard = new Blackboard();
          public List<ISkillComponent> components = new List<ISkillComponent>();
          public List<ITickingComponent> tickingComponents = new List<ITickingComponent>();
          // 与 components 并行：保存每个组件的初始参数，供 OnInitialize 时 re-OnInit。
          public List<ParamList> componentParams = new List<ParamList>();
          // Trigger 分桶：BuildSkillRuntime 一次性填充，OnInitialize/OnTeardown 不重建。
          // key 是 ConditionConfig.triggerEvent 的 enum，value 是按 config 声明顺序排好的组件列表。
          public Dictionary<TriggerEvent, List<ISkillComponent>> componentsByTrigger
              = new Dictionary<TriggerEvent, List<ISkillComponent>>();
          public bool isInitialized;
          public bool isActive; // true while skill is firing (SPEngine.IsActive)

          public void OpenActiveWindow()  { isActive = true;  }
          public void CloseActiveWindow() { isActive = false; }

          public SkillContext MakeContext(ISkillComponent component, SkillEvent evt = null)
          {
              return new SkillContext
              {
                  skill = this,
                  component = component,
                  currentEvent = evt,
                  blackboard = blackboard,
              };
          }
      }
  }
  ```

- [ ] **Step 4.2: Compile gate**

  ```bash
  cd e:/Unity/projects/TD && dotnet build TD.sln -nologo --no-restore 2>&1 | tail -10
  ```

  Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4.3: Commit**

  ```bash
  cd e:/Unity/projects/TD && git add Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRuntime.cs && git commit -m "feat(skill): add componentsByTrigger bucket field to SkillRuntime

  Storage for the per-skill trigger→components dictionary. Populated in
  the next commit; reader switches over in the commit after.

  Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
  ```

---

## Task 5: Populate the bucket in `BuildSkillRuntime` + warning logic

**Why:** Fill `componentsByTrigger` from `ConditionConfig.triggers[]` and warn on zero-trigger non-ticking components. Spec §5.1.

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs:124-154`

- [ ] **Step 5.1: Rewrite `BuildSkillRuntime` to populate the bucket**

  Replace the entire `BuildSkillRuntime` method with:

  ```csharp
      private void BuildSkillRuntime(SkillConfig cfg)
      {
          var runtime = new SkillRuntime { config = cfg };
          if (cfg.sp != null && cfg.sp.totalSp > 0)
          {
              runtime.spEngine = new SPEngine(cfg.sp);
              runtime.spEngine.OnBegin += () => OnSkillBeginWindow(runtime);
              runtime.spEngine.OnEnd   += () => OnSkillEndWindow(runtime);
          }
          if (cfg.components != null)
          {
              for (int i = 0; i < cfg.components.Length; i++)
              {
                  var ccfg = cfg.components[i];
                  if (ccfg == null || string.IsNullOrEmpty(ccfg.componentType)) continue;
                  var inst = ComponentFactory.Create(ccfg.componentType);
                  if (inst == null)
                  {
                      Debug.LogError($"[EntitySkillRunner] Unknown component type: {ccfg.componentType} in skill {cfg.skillId}");
                      continue;
                  }
                  var ctx = runtime.MakeContext(inst, null);
                  inst.OnInit(ctx, ccfg.parameters);
                  runtime.components.Add(inst);
                  runtime.componentParams.Add(ccfg.parameters);
                  if (inst is ITickingComponent t) runtime.tickingComponents.Add(t);

                  // Bucket by trigger. Each ConditionConfig contributes one entry;
                  // the same component instance can land in multiple buckets when
                  // its config declares multiple triggers — that's expected.
                  int triggerCount = 0;
                  if (ccfg.triggers != null)
                  {
                      for (int tIdx = 0; tIdx < ccfg.triggers.Length; tIdx++)
                      {
                          var trig = ccfg.triggers[tIdx];
                          if (trig == null) continue;
                          var te = trig.triggerEvent;
                          if (!runtime.componentsByTrigger.TryGetValue(te, out var list))
                          {
                              list = new List<ISkillComponent>();
                              runtime.componentsByTrigger[te] = list;
                          }
                          list.Add(inst);
                          triggerCount++;
                      }
                  }

                  // Warn when a non-ticking component declared no triggers — it
                  // will never receive OnTrigger. ITickingComponent gets an
                  // implicit pass because OnTick is its primary channel.
                  if (triggerCount == 0 && !(inst is ITickingComponent))
                  {
                      Debug.LogWarning(
                          $"[SkillRuntime] Component {ccfg.componentType} in skill {cfg.skillId} "
                          + "declares no triggers — it will never receive OnTrigger. "
                          + "Add ConditionConfig entries to triggers[] if this is unintended.");
                  }
              }
          }
          runtime.isInitialized = true;
          _skills.Add(runtime);
      }
  ```

- [ ] **Step 5.2: Compile gate**

  ```bash
  cd e:/Unity/projects/TD && dotnet build TD.sln -nologo --no-restore 2>&1 | tail -10
  ```

  Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5.3: Commit**

  ```bash
  cd e:/Unity/projects/TD && git add Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs && git commit -m "feat(skill): populate componentsByTrigger in BuildSkillRuntime

  Walks ConditionConfig.triggers[] for each component, drops the
  (TriggerEvent, component) pair into the bucket. Logs a warning when a
  non-ticking component declares zero triggers — it would otherwise be
  silently dead. ITickingComponent is exempted (OnTick is its channel).

  Dispatcher still iterates all components in this commit; switch in the
  next one.

  Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
  ```

---

## Task 6: Switch `DispatchToSkill` to bucket lookup

**Why:** This is the behavior-flip commit. After this, only components whose `ConditionConfig.triggers[].triggerEvent` matches the dispatched event receive `OnTrigger`. Spec §5.2.

**Behavior change warning:** Existing SkillConfig `.asset` resources whose `triggers[]` are empty (most of them) will go silent. This is accepted per spec §6.5. The warnings from Task 5 surface this in the console.

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs:272-292`

- [ ] **Step 6.1: Rewrite `DispatchToSkill`**

  ```csharp
      private void DispatchToSkill(SkillRuntime s, SkillEvent evt)
      {
          // Active-window gate: a skill's components only see events while the
          // skill is firing, EXCEPT for these four lifecycle events which always
          // bypass the gate. They still need to be declared in config.triggers[]
          // to be received — bypass is gate-only, not bucket-only.
          bool bypassActiveGate = evt is PreWarmEvent
                               || evt is InitializeEvent
                               || evt is SkillBeginEvent
                               || evt is SkillEndEvent;
          if (!s.isActive && !bypassActiveGate) return;

          if (!s.componentsByTrigger.TryGetValue(evt.TriggerEvent, out var list)) return;
          for (int i = 0; i < list.Count; i++)
          {
              var comp = list[i];
              var ctx = s.MakeContext(comp, evt);
              ctx.sharedBlackboard = sharedBlackboard;
              ctx.entity = _entity;
              comp.OnTrigger(ctx);
          }
      }
  ```

- [ ] **Step 6.2: Compile gate**

  ```bash
  cd e:/Unity/projects/TD && dotnet build TD.sln -nologo --no-restore 2>&1 | tail -10
  ```

  Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 6.3: Commit**

  ```bash
  cd e:/Unity/projects/TD && git add Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntitySkillRunner.cs && git commit -m "feat(skill): DispatchToSkill routes via componentsByTrigger bucket

  ConditionConfig.triggerEvent becomes the single source of truth for
  which components see which events. Components still carry their
  is-XxxEvent guards in this commit (defense-in-depth); guards are
  removed across the following commits.

  Existing SkillConfig assets with empty triggers[] will receive no
  OnTrigger after this commit — by design, per spec §6.5. The warning
  added in the previous commit makes the gap visible.

  Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
  ```

---

## Task 7: Remove guard in `AttackMultiplierBoost`

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/AttackMultiplierBoost.cs:13-17`

- [ ] **Step 7.1: Replace guard with direct cast**

  ```csharp
  // OLD:
          public void OnTrigger(SkillContext ctx)
          {
              if (!(ctx.currentEvent is BeforeAttackEvent bae)) return;
              bae.multiplyer *= _multiplier;
          }

  // NEW:
          public void OnTrigger(SkillContext ctx)
          {
              var bae = (BeforeAttackEvent)ctx.currentEvent;
              bae.multiplyer *= _multiplier;
          }
  ```

- [ ] **Step 7.2: Compile gate**

  ```bash
  cd e:/Unity/projects/TD && dotnet build TD.sln -nologo --no-restore 2>&1 | tail -10
  ```

  Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 7.3: Commit**

  ```bash
  cd e:/Unity/projects/TD && git add Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/AttackMultiplierBoost.cs && git commit -m "refactor(skill): drop is-BeforeAttackEvent guard in AttackMultiplierBoost

  Dispatcher now guarantees the event type matches the configured trigger.

  Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
  ```

---

## Task 8: Remove guard in `SetAttackCombo`

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/SetAttackCombo.cs:15`

- [ ] **Step 8.1: Read current file to confirm structure**

  ```bash
  cd e:/Unity/projects/TD && cat Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/SetAttackCombo.cs
  ```

  Confirm the `OnTrigger` body opens with `if (!(ctx.currentEvent is BeforeAttackEvent bae)) return;`.

- [ ] **Step 8.2: Replace guard with direct cast**

  The replacement pattern is the same as Task 7:

  ```csharp
  // OLD opening of OnTrigger:
              if (!(ctx.currentEvent is BeforeAttackEvent bae)) return;

  // NEW opening of OnTrigger:
              var bae = (BeforeAttackEvent)ctx.currentEvent;
  ```

  The rest of `OnTrigger` (the body that uses `bae`) stays.

- [ ] **Step 8.3: Compile gate**

  ```bash
  cd e:/Unity/projects/TD && dotnet build TD.sln -nologo --no-restore 2>&1 | tail -10
  ```

  Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 8.4: Commit**

  ```bash
  cd e:/Unity/projects/TD && git add Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/SetAttackCombo.cs && git commit -m "refactor(skill): drop is-BeforeAttackEvent guard in SetAttackCombo

  Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
  ```

---

## Task 9: Remove guard in `CampDamageModifierComponent`

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/CampDamageModifierComponent.cs:17`

- [ ] **Step 9.1: Replace guard with direct cast**

  ```csharp
  // OLD opening of OnTrigger:
              if (!(ctx.currentEvent is BeforeTakeDamageEvent btd)) return;

  // NEW opening of OnTrigger:
              var btd = (BeforeTakeDamageEvent)ctx.currentEvent;
  ```

- [ ] **Step 9.2: Compile gate**

  ```bash
  cd e:/Unity/projects/TD && dotnet build TD.sln -nologo --no-restore 2>&1 | tail -10
  ```

  Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 9.3: Commit**

  ```bash
  cd e:/Unity/projects/TD && git add Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/CampDamageModifierComponent.cs && git commit -m "refactor(skill): drop is-BeforeTakeDamageEvent guard in CampDamageModifier

  Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
  ```

---

## Task 10: Remove guard in `LockHpShieldComponent`

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/LockHpShieldComponent.cs:20`

- [ ] **Step 10.1: Replace guard with direct cast**

  ```csharp
  // OLD opening of OnTrigger:
              if (!(ctx.currentEvent is BeforeHurtEvent bhe)) return;

  // NEW opening of OnTrigger:
              var bhe = (BeforeHurtEvent)ctx.currentEvent;
  ```

- [ ] **Step 10.2: Compile gate**

  ```bash
  cd e:/Unity/projects/TD && dotnet build TD.sln -nologo --no-restore 2>&1 | tail -10
  ```

  Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 10.3: Commit**

  ```bash
  cd e:/Unity/projects/TD && git add Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/LockHpShieldComponent.cs && git commit -m "refactor(skill): drop is-BeforeHurtEvent guard in LockHpShield

  Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
  ```

---

## Task 11: Remove guard in `DeathSpawnComponent`

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/DeathSpawnComponent.cs:29`

- [ ] **Step 11.1: Delete the guard line**

  `DeathSpawnComponent` doesn't extract any payload from the event — the guard just returns. The new version simply deletes the line:

  ```csharp
  // OLD opening of OnTrigger (line 29):
              if (!(ctx.currentEvent is BeforeDieAnimationEvent)) return;

  // NEW: line deleted entirely.
  ```

- [ ] **Step 11.2: Compile gate**

  ```bash
  cd e:/Unity/projects/TD && dotnet build TD.sln -nologo --no-restore 2>&1 | tail -10
  ```

  Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 11.3: Commit**

  ```bash
  cd e:/Unity/projects/TD && git add Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/DeathSpawnComponent.cs && git commit -m "refactor(skill): drop is-BeforeDieAnimationEvent guard in DeathSpawn

  Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
  ```

---

## Task 12: Remove guard in `SelfDamageOnEventComponent` (keep `isDeadly` check)

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/SelfDamageOnEventComponent.cs:17`

- [ ] **Step 12.1: Replace guard with direct cast, keep `isDeadly` check as a separate line**

  The current line `if (!(ctx.currentEvent is AfterTakeDamageEvent atd) || !atd.isDeadly) return;` is two checks fused: type guard + `isDeadly` early-exit. Split them:

  ```csharp
  // OLD opening of OnTrigger (line 17):
              if (!(ctx.currentEvent is AfterTakeDamageEvent atd) || !atd.isDeadly) return;

  // NEW opening of OnTrigger:
              var atd = (AfterTakeDamageEvent)ctx.currentEvent;
              if (!atd.isDeadly) return;
  ```

- [ ] **Step 12.2: Compile gate**

  ```bash
  cd e:/Unity/projects/TD && dotnet build TD.sln -nologo --no-restore 2>&1 | tail -10
  ```

  Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 12.3: Commit**

  ```bash
  cd e:/Unity/projects/TD && git add Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/SelfDamageOnEventComponent.cs && git commit -m "refactor(skill): drop is-AfterTakeDamageEvent guard in SelfDamageOnEvent

  Keeps the !isDeadly early-exit as a separate statement so the
  business rule (fire only on lethal hits) stays explicit.

  Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
  ```

---

## Task 13: Migrate `ApplyBuffComponent.ResolveTargets` to switch-expression target extraction

**Why:** `ApplyBuff` is the one component used opportunistically across many trigger types; it reads the event's `target` field when present. Spec §6.3.

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/ApplyBuffComponent.cs:58-71`

- [ ] **Step 13.1: Replace `ResolveTargets` and add `ExtractTargetFromEvent` helper**

  The final state of the bottom half of the file (from `ResolveTargets` down):

  ```csharp
          // Blackboard-read path: when blackboardKey is set, the target list is read
          // from the key. If the key is missing/empty, the component skips silently —
          // this means an upstream writer hasn't run yet, which is normal in some
          // dispatch orders.
          private List<Entity> ResolveTargets(SkillContext ctx)
          {
              if (!string.IsNullOrEmpty(_inputKey))
              {
                  return ctx.blackboard.Get<List<Entity>>(_inputKey, null);
              }

              // Original single-target behavior preserved for backward compatibility.
              // The dispatcher routes this component by config.triggers[]; the event
              // type that arrives depends on the config, so we extract `target` from
              // whichever event payload carries one. Falls back to ctx.entity when
              // the event doesn't carry a target field (or _toSelf is true).
              Entity t = _toSelf
                  ? ctx.entity
                  : (ExtractTargetFromEvent(ctx.currentEvent) ?? ctx.entity);
              if (t == null || t.buffController == null) return null;
              return new List<Entity> { t };
          }

          private static Entity ExtractTargetFromEvent(SkillEvent evt)
          {
              return evt switch
              {
                  BeforeTakeDamageEvent btd => btd.target,
                  AfterTakeDamageEvent  atd => atd.target,
                  BeforeAttackEvent     bae => bae.target,
                  AfterAttackEvent      aae => aae.target,
                  _ => null,
              };
          }
      }
  }
  ```

  Note the new fallback: when `_toSelf == false` AND the event carries no target, we now fall back to `ctx.entity` (the old code returned null in that case). This preserves "apply to someone" semantics under the wider event surface this component now faces.

- [ ] **Step 13.2: Compile gate**

  ```bash
  cd e:/Unity/projects/TD && dotnet build TD.sln -nologo --no-restore 2>&1 | tail -10
  ```

  Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 13.3: Commit**

  ```bash
  cd e:/Unity/projects/TD && git add Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/ApplyBuffComponent.cs && git commit -m "refactor(skill): ApplyBuff extracts event target via switch expression

  Replaces the single-event 'is BeforeTakeDamageEvent btd ? btd.target'
  with a switch over events that carry an Entity target field. Falls
  back to ctx.entity when the dispatched event has no target (instead
  of null), preserving 'apply to someone' semantics now that this
  component sees a wider event surface.

  Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
  ```

---

## Task 14: Document the nested-sub-component contract in `EntitySelectorRadiusEffectComponent`

**Why:** Spec §6.4 — this component bypasses the dispatcher's type guarantee by forwarding `currentEvent` into ad-hoc sub-components. Out of scope to fix; in scope to document.

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/EntitySelectorRadiusEffectComponent.cs:22`

- [ ] **Step 14.1: Add XML doc comment above `OnTrigger`**

  Insert the following block immediately before `public void OnTrigger(SkillContext ctx)` (currently at line 22):

  ```csharp
          /// <summary>
          /// IMPORTANT: This component constructs sub-components ad-hoc and forwards
          /// <c>ctx.currentEvent</c> into them. That bypasses the dispatcher's
          /// trigger-bucket guarantee — the sub-component sees whatever event type
          /// the parent received.
          ///
          /// When configuring this component, the sub-component declared in
          /// <c>subComponentType</c> MUST be able to handle every event type
          /// declared in this parent's <c>triggers[]</c>. Otherwise the sub-
          /// component's direct cast on <c>ctx.currentEvent</c> will NRE.
          ///
          /// Tracked for a later refactor; see spec
          /// docs/superpowers/specs/2026-06-10-skill-trigger-bucket-dispatch-design.md §6.4.
          /// </summary>
          public void OnTrigger(SkillContext ctx)
  ```

  The body of `OnTrigger` itself is unchanged.

- [ ] **Step 14.2: Compile gate**

  ```bash
  cd e:/Unity/projects/TD && dotnet build TD.sln -nologo --no-restore 2>&1 | tail -10
  ```

  Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 14.3: Commit**

  ```bash
  cd e:/Unity/projects/TD && git add Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/EntitySelectorRadiusEffectComponent.cs && git commit -m "docs(skill): document nested-sub-component dispatch bypass

  This component forwards ctx.currentEvent into ad-hoc sub-components,
  which skips the dispatcher's trigger-bucket type guarantee. Out of
  scope for the trigger-bucket refactor; documenting the implicit
  contract so configuration mistakes are catchable in code review.

  Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
  ```

---

## Task 15: Manual verification in Unity Editor

**Why:** This project has no automated runtime tests. The compile gate has already verified syntactic correctness; this step verifies behavior in the editor.

**This task is for the human reviewer to perform after subagent tasks finish.** Subagent should NOT attempt to launch Unity.

- [ ] **Step 15.1: Open the project in Unity Editor**

  Open the TD project in Unity. Wait for the script compile to finish (the IDE-driven dotnet builds during Tasks 1-14 do not invalidate Unity's own compile state, so a re-import may run).

  Expected: Console reports `0 errors`. Warnings about pre-existing fields are pre-existing — note their count before and after.

- [ ] **Step 15.2: Open a level scene that runs at least one skill-bearing entity**

  Pick a level scene that has a deployable character with a configured skill component. (For example, any scene that loads characters under `Assets/Resources/Prefabs/Characters/`.)

- [ ] **Step 15.3: Press Play, observe console for trigger warnings**

  After PreWarm of the entity, expect a flurry of `[SkillRuntime] Component … declares no triggers …` warnings. This is expected — most existing SkillConfig assets have empty `triggers[]`. The warnings are the gap-discovery mechanism per spec §6.5.

  Confirm: no NRE, no other unrelated errors.

- [ ] **Step 15.4: Trigger an attack with a skill that uses `AttackMultiplierBoost`**

  Find a character whose skill includes `AttackMultiplierBoost` in its config. Edit that SkillConfig asset in the Inspector to add a `triggers[]` entry with `triggerEvent = OnBeforeAttack`. Save.

  Re-enter Play mode. Trigger an attack. Confirm the multiplier applies to damage (compare to pre-edit damage values if available).

- [ ] **Step 15.5: Sanity check — remove the trigger entry, verify the multiplier no longer applies**

  Negative test: clear the `triggers[]` entry on the same skill config. Re-enter Play, attack, confirm:
  - The warning fires for `AttackMultiplierBoost` in the console.
  - Damage values are baseline (multiplier no longer applied).

  Restore the trigger entry afterward.

- [ ] **Step 15.6: Report results**

  Write the smoke test outcome to a comment on the implementation PR / commit, or to the user directly:
  - `[PASS]` or `[FAIL]` per step
  - Console output snippets for any unexpected errors

  No code commit needed for this task — it's verification only.

---

## Out-of-Scope (not covered by this plan)

- Wiring `ConditionConfig.op` / `leftKey` / `rightValue` (condition evaluation) — separate iteration.
- Editor-side validation of `triggers[].triggerEvent` against component-declared event metadata.
- Bulk migration of existing `*.asset` SkillConfig resources to add explicit `triggers[]` entries — done by content authors on demand, surfaced by the Task 5 warnings.
- A real fix for `EntitySelectorRadiusEffectComponent`'s nested-sub-component pattern (Task 14 is documentation only).
- Replacing the per-skill bucket with a per-entity flat dispatcher (premature optimization).
