# Skill / Talent System Refactor — Design Spec

**Date:** 2026-06-07
**Status:** Draft (post-brainstorming, pending user review)
**Scope:** Replace per-skill/per-talent MonoBehaviour scripts with a data-driven, component-composed framework; migrate all 28+ existing implementations.

---

## 1. Motivation

### 1.1 Current state

Skills and talents are implemented as **one C# script per skill/talent** subclassing `Skill` / `Talent` MonoBehaviour. There are **28+ existing subclasses** under `Assets/Resources/Prefabs/.../Scripts/`. Examples:

- `Kroos/Skill1.cs` — one-shot damage multiplier on attack
- `Witch/WitchSkill.cs` — HP-threshold conditional skill with animation swap and self-effect
- `Witch/WitchTalent.cs` — multi-hook attack modification with poison/damage color switching
- `Creeper/CreeperTalent.cs` — multi-stage async behavior (charge → backout → explode → die)
- `Zombie/ZombieTalent.cs` — periodic aura buff with self-destruct timer
- `Skeleton/SkeletonTalent1.cs` — inter-entity communication with coroutine loop
- `Slime/SlimeTalent1.cs` — death-spawn N entities
- `Wither/WitherTalent1.cs` — damage modifier + self-damage-on-kill shield

The base classes embed hard-coded behavior in `switch` statements with **magic-number modes** (e.g. `_spRecoverMode = 1`), making it hard to:

- Add new skills/talents (requires a new C# file + new prefab wiring)
- Add new trigger types (requires editing the base class)
- Balance in-editor (most behavior is buried in code)
- Reuse code across skills (each implementation is bespoke)

### 1.2 Goals

- **G1.** Configure 100% of a skill or talent from `EntityData` — name, description, SP, behavior — **with zero new code**.
- **G2.** Reuse behavior code: complex multi-stage behaviors (Creeper charge-backout-explode) are written **once** as a single component, parameterized and composed into multiple skills.
- **G3.** New skills = data. New abilities = one new C# class registered to a factory.
- **G4.** Fully replace the `Skill.cs` and `Talent.cs` MonoBehaviour base classes and all 28+ subclass scripts.
- **G5.** Preserve all existing in-game behavior; no functional regression.

### 1.3 Non-goals (YAGNI)

- Networked / multi-client skill state synchronization (game is single-player).
- Visual scripting / behavior-tree editor UI (configuration is via Unity Inspector with custom drawers).
- Hot-reload of skill data at runtime (EntityData is read at PreWarm).
- Distinguishing "AI behaviors" from "skills" — they share the same component infrastructure if needed, but the migration scope is the 28+ existing scripts only.

---

## 2. Architecture

### 2.1 Two-layer model

```
Skill    = configuration in EntityData
            = one or more Components, ordered, plus optional SP/conditions
Component = one C# class implementing one well-defined behavior
            = reusable, parameterized, factory-registered
```

A "skill" is **data**; an "ability" is **code**. The unit of composition is the skill; the unit of code is the component.

### 2.2 Component diagram

```
┌────────────────────────────────────────────────────────┐
│  EntityData (ScriptableObject)                          │
│  ├─ existing fields (VisionRange, Attack, MaxHp, ...)   │
│  └─ Skills: List<SkillConfig>      ← NEW                │
│       └─ Components: List<ComponentConfig>             │
└─────────────┬──────────────────────────────────────────┘
              │ loaded at runtime via EntityDataRepository
              ▼
┌────────────────────────────────────────────────────────┐
│  Entity (runtime)                                       │
│  └─ SkillRunner (POCO MonoBehaviour, single per Entity) │
│       ├─ subscribes to Entity / AttackBase / AM events │
│       ├─ holds SkillRuntime[]                           │
│       ├─ holds Blackboard (shared + per-skill keys)     │
│       └─ ticks time-based components in FixedUpdate     │
└─────────────┬──────────────────────────────────────────┘
              │ event-driven dispatch
              ▼
┌────────────────────────────────────────────────────────┐
│  Component Library (pure C# classes, not MonoBehaviours)│
│  ├─ ISkillComponent interface                           │
│  ├─ ComponentFactory (string type → instance)           │
│  ├─ ~30-50 components covering all 28+ existing scripts │
│  └─ Each component is single-responsibility, ~30-200 LOC│
└────────────────────────────────────────────────────────┘
```

### 2.3 Lifecycle

| Phase | What happens |
|---|---|
| **PreWarm** | `SkillRunner` reads `EntityData.Skills`, builds `SkillRuntime[]`, subscribes to all relevant `Entity` / `AttackBase` / `AnimationMachine` events. Initializes each component. |
| **Initialize** | Per-component `OnInit(ctx)` (allocate blackboard keys, cache references). |
| **Tick** | `FixedUpdate` calls `OnTick(ctx, dt)` on every component that requests ticking. |
| **OnEvent** | `Entity`/`AttackBase`/`AnimationMachine` events fire → `SkillRunner` dispatches a `SkillEvent` to all `SkillRuntime`s whose `Triggers` match → matching components receive `OnTrigger(ctx)`. |
| **Dormancy** | `SkillRunner.OnTeardown` cancels all subscriptions, calls `OnTeardown` on components, returns to pool. `SkillRuntime` instances are reusable. |
| **Death** | Dedicated `OnDeath` event is dispatched before teardown for any "OnDeath" triggers. |

---

## 3. Data Model

All types are `[Serializable]` for Unity Inspector and live in a new `SkillSystem` namespace under `Assets/PublicScripts/SkillSystem/`.

### 3.1 `SkillConfig` — the unit configured per-entity

```csharp
namespace SkillSystem
{
    [Serializable]
    public class SkillConfig
    {
        public string skillId;        // unique within EntityData; used for blackboard key prefix
        public string skillName;      // UI display
        [TextArea(2,5)] public string description;
        public SkillKind kind;        // ActiveSkill | Passive | Aura | OnDeath

        public SPConfig sp;            // optional; null for passive skills with no SP rules
        public ConditionConfig[] globalConditions;  // optional; ALL components gated by these
        public ComponentConfig[] components;  // composed behaviors
    }

    public enum SkillKind
    {
        Passive,        // always active, no SP
        ActiveSkill,    // SP-gated, manual or auto-triggered
        Aura,           // periodic tick-driven, no manual trigger
        OnDeath,        // triggered once on death
    }
}
```

### 3.2 `SPConfig` — replaces 6 magic ints in `Skill.cs`

```csharp
namespace SkillSystem
{
    [Serializable]
    public class SPConfig
    {
        [Tooltip("Total SP. Skill fires when current SP >= totalSp.")]
        public int totalSp;
        public int initialSp;
        [Tooltip("Max charges. >1 enables multi-charge (multi-bar) behavior.")]
        public int chargeNum = 1;
        [Tooltip(">0 = active for that long after fire; <=0 = instant fire.")]
        public float skillDuration;
        public SpRecoverMode recoverMode = SpRecoverMode.Natural;
        public SpConsumeMode consumeMode = SpConsumeMode.Duration;
        public SkillOpenMode openMode = SkillOpenMode.Natural;
        public bool recoverForbidDuringSkill;
        public bool canManualClose;
        public Vector2Int[] skillAttackRange;  // optional override during skill
    }

    public enum SpRecoverMode { Natural, OnAttackHit, OnAfterHurt }
    public enum SpConsumeMode { Duration, OnAttackHit, OnAfterHurt, Instant }
    public enum SkillOpenMode { Natural, OnAttackAnimBegin, OnBeforeHurt, Manual, OnAttackHit }
}
```

These enums **fully replace** `Skill._spRecoverMode`, `_spConsumeMode`, `_skillOpenMode`, `_skillAmount`, `_recoverForbidDuringSkill`, `_canCloseSkill`, `_chargeNum`, `_totalSp`, `_initialSp` from `Skill.cs`.

### 3.3 `ComponentConfig` — the unit of composition

```csharp
namespace SkillSystem
{
    [Serializable]
    public class ComponentConfig
    {
        [Tooltip("Factory key, e.g. 'AttackBoost', 'ChargeBackoutExplode'.")]
        public string componentType;
        public ConditionConfig[] triggers;   // event + condition(s) that activate this component
        public ParamList parameters;         // parameters passed to the component
    }
}
```

### 3.4 `ConditionConfig` — trigger gating

```csharp
namespace SkillSystem
{
    [Serializable]
    public class ConditionConfig
    {
        public TriggerEvent triggerEvent;     // which event activates this
        public ConditionOp op = ConditionOp.None;
        public string leftKey;                // e.g. "hpRate", "targetCamp", "isDeadly"
        public string rightValue;             // e.g. "0.3", "2", "true"
    }

    public enum TriggerEvent
    {
        OnPreWarm,           // once, on SkillRunner.PreWarm
        OnInitialize,        // once, on Entity.Initialize
        OnBeforeAttack,      // AttackBase.OnBeforeAttack
        OnAfterAttack,       // AttackBase.OnAfterAttack
        OnBeforeTakeDamage,  // AttackBase.OnBeforeTakeDamage
        OnAfterTakeDamage,   // AttackBase.OnAfterTakeDamage
        OnAttackSuccessfully,// AttackBase.OnAttackSuccessfully
        OnAttackInterrupt,   // AttackBase.OnAttackInterrupt
        OnBeforeHurt,        // Entity.OnBeforeHurt
        OnAfterHurt,         // Entity.OnAfterHurt
        OnAttackAnimBegin,   // AnimationMachine.OnAttackAnimationBegin
        OnBeforeDieAnimation,// Entity.OnBeforeDieAnimation
        OnDeath,             // once, after death
        OnIntervalTick,      // periodic (TickRate set in ParamList)
        OnSkillBegin,        // this skill's SPEngine fired
        OnSkillEnd,          // this skill's SPEngine ended
    }

    public enum ConditionOp
    {
        None,                // always true
        Equal, Greater, Less, GreaterOrEqual, LessOrEqual,
        HasBuff, NotHasBuff,
        IsInAbnormalState, NotInAbnormalState,
        HasBlackboardKey, NotHasBlackboardKey,
    }
}
```

### 3.5 `ParamList` — typed parameter passing

```csharp
namespace SkillSystem
{
    [Serializable]
    public class ParamList
    {
        public ParamEntry[] entries;
    }

    [Serializable]
    public class ParamEntry
    {
        public string key;
        public ParamValueType type;
        public string value;     // string-encoded; parsed at PreWarm
    }

    public enum ParamValueType { Int, Float, Bool, String, Vector2Int, AnimationRef, Prefab, EntityId, Color }
}
```

Components declare required parameters in a static schema; the framework validates at PreWarm and surfaces Inspector errors for missing/wrong-type entries.

### 3.6 `SkillContext` — passed to every component call

```csharp
namespace SkillSystem
{
    public class SkillContext
    {
        public Entity entity;          // owner
        public SkillRuntime skill;     // owning skill
        public ISkillComponent component;
        public SkillEvent currentEvent;
        public Blackboard blackboard;  // per-skill keys
        public Blackboard sharedBlackboard;  // cross-skill keys (on SkillRunner)
    }
}
```

---

## 4. Component Library — `ISkillComponent` and Factory

### 4.1 Interface

```csharp
namespace SkillSystem
{
    public interface ISkillComponent
    {
        // Called once when SkillRuntime is created
        void OnInit(SkillContext ctx, ParamList parameters);
        // Called by SkillRunner when a matching event fires
        void OnTrigger(SkillContext ctx);
        // Called every FixedUpdate (only if component requests tick in OnInit)
        void OnTick(SkillContext ctx, float dt);
        // Called once when SkillRuntime is torn down
        void OnTeardown(SkillContext ctx);
    }

    // Marker for components that need per-frame ticking
    public interface ITickingComponent : ISkillComponent { }
}
```

### 4.2 Factory & registry

```csharp
namespace SkillSystem
{
    public static class ComponentFactory
    {
        private static readonly Dictionary<string, Func<ISkillComponent>> _registry = new();

        public static void Register(string typeName, Func<ISkillComponent> ctor) { ... }
        public static ISkillComponent Create(string typeName);

        // Auto-registration via [RegisterComponent] attribute
        [AttributeUsage(AttributeTargets.Class)]
        public class RegisterComponentAttribute : Attribute
        {
            public string TypeName;
            public RegisterComponentAttribute(string typeName) { TypeName = typeName; }
        }
    }

    // Static initializer: scans the assembly for [RegisterComponent] and registers
    public static class ComponentAutoRegistry
    {
        public static void RegisterAll();
    }
}
```

### 4.3 Initial component library (covers all 28+ existing scripts)

The 28+ existing scripts map to the following **~40 components**. Each component is registered with a `RegisterComponent("TypeName")` attribute.

#### 4.3.1 Damage / attack modification

| Component | TypeName | Replaces | Notes |
|---|---|---|---|
| `AttackBoostComponent` | `AttackBoost` | Kroos `Skill1` | Sets `multiplyer *= f`, `cumbo += n` on `OnBeforeAttack` |
| `CampDamageModifierComponent` | `CampDamageModifier` | Wither `Talent1` | If target.camp matches, set `multiplyer = X` on `OnBeforeTakeDamage` |
| `AttackRangeOverrideComponent` | `AttackRangeOverride` | Skill.cs base | Sets `Entity.Vision.Range = skillAttackRange` while skill active |
| `TargetSelectionModifierComponent` | `TargetSelectionModifier` | TBD uses | Modifies `OnBeforeTargetSelect` / `OnAfterTargetSelect` lists |

#### 4.3.2 Buff / state application

| Component | TypeName | Replaces | Notes |
|---|---|---|---|
| `ApplyBuffComponent` | `ApplyBuff` | ZombieTalent, CreeperTalent | Creates buff on self/targets with type/values/duration |
| `PeriodicAuraBuffComponent` | `PeriodicAuraBuff` | ZombieTalent aura | Per-tick: add/remove buff to entities in radius |
| `SetAbnormalStateComponent` | `SetAbnormalState` | CreeperTalent, HeadSeter | Adds/removes abnormal state on self |
| `HealOnEventComponent` | `HealOnEvent` | TBD uses | Self-heal on event with `target.hpDelta` formula |

#### 4.3.3 Animation

| Component | TypeName | Replaces | Notes |
|---|---|---|---|
| `SwapAnimationComponent` | `SwapAnimation` | Zombie, Creeper, Witch | Replaces AnimationReferenceAsset fields on self.animation machine |
| `PlayAnimationComponent` | `PlayAnimation` | Creeper, HeadSeter | Triggers `entityAM.TrySetState(...)` with optional blocking |
| `ResetAnimationComponent` | `ResetAnimation` | Creeper, Witch | Calls `entityAM.ResetAnimation(...)` with int array |

#### 4.3.4 Movement / position

| Component | TypeName | Replaces | Notes |
|---|---|---|---|
| `FlashMoveComponent` | `FlashMove` | HeadSeter `Skill1` | Custom `FlashMove` logic, parameterized by `moveDis` |
| `AddImpulseComponent` | `AddImpulse` | Creeper explosion knockback | On event, applies impulse to entities in radius with falloff |

#### 4.3.5 Spawning / entity interaction

| Component | TypeName | Replaces | Notes |
|---|---|---|---|
| `DeathSpawnComponent` | `DeathSpawn` | SlimeTalent1 | On `OnBeforeDieAnimation`, spawn N entities with gap |
| `SpawnBulletComponent` | `SpawnBullet` | SkeletonTalent1 | Spawns bullet with custom callback |
| `EntitySelectorRadiusEffectComponent` | `EntitySelectorRadiusEffect` | Creeper, Zombie, Witch | Queries `EntityManager.EntitySelector_Radius` and applies a sub-effect to each |
| `DamageRadiusFalloffComponent` | `DamageRadiusFalloff` | Creeper explosion | Damage with distance falloff (4 tiers) |

#### 4.3.6 Lifecycle / state machine

| Component | TypeName | Replaces | Notes |
|---|---|---|---|
| `StageStateMachineComponent` | `StageStateMachine` | CreeperTalent | Multi-stage async: `Idle → Charging → BackingOut → Exploding → Die` with stage transition conditions; sub-effects per stage |
| `CoroutineLoopComponent` | `CoroutineLoop` | SkeletonTalent1 | `while (cond) { ... await ... }` body executes per tick; blackboard-controlled termination |
| `SelfDestructComponent` | `SelfDestruct` | ZombieTalent | Timer-based self-death after skill begins |

#### 4.3.7 Self-damage / shield

| Component | TypeName | Replaces | Notes |
|---|---|---|---|
| `SelfDamageOnEventComponent` | `SelfDamageOnEvent` | WitherTalent1 | On `OnAfterTakeDamage(isDeadly)`, apply self damage |
| `LockHpShieldComponent` | `LockHpShield` | WitherTalent1 | HP-floor shield; while active, prevents HP from going below threshold |
| `DamageThresholdComponent` | `DamageThreshold` | TBD uses | Conditionally apply effect only when incoming damage exceeds threshold |

#### 4.3.8 Visual / VFX

| Component | TypeName | Replaces | Notes |
|---|---|---|---|
| `PlayParticleComponent` | `PlayParticle` | Witch | Play/stop ParticleSystem with color override |
| `InstantiatePrefabComponent` | `InstantiatePrefab` | Witch | Instantiate prefab at entity position; destroy after duration |
| `SetAttackEffectDataComponent` | `SetAttackEffectData` | Witch | Swap `AttackBase._attackEffectData` to alternative version |

#### 4.3.9 Effect selection / composition helpers

| Component | TypeName | Replaces | Notes |
|---|---|---|---|
| `RandomSelectorComponent` | `RandomSelector` | TBD uses | Among N sub-effects, pick one randomly per trigger |
| `ConditionalBranchComponent` | `ConditionalBranch` | Witch (poison vs damage) | Evaluate blackboard cond, dispatch to one of two sub-effects |
| `DelayedEffectComponent` | `DelayedEffect` | TBD uses | Schedule sub-effect after N seconds |

### 4.4 StageStateMachineComponent — the critical complex-behavior case

CreeperTalent's charge-backout-explode is the hardest case. It is implemented as **one** component with internal stage state:

```csharp
[RegisterComponent("StageStateMachine")]
public class StageStateMachineComponent : ITickingComponent
{
    private enum Stage { Idle, Charging, BackingOut, Exploding }
    private Stage _stage = Stage.Idle;
    private float _stageTimer;
    // parameters: stages (StageConfig[]), where StageConfig { name, duration, triggerOnEnter, triggerOnTick, transitionCond }
    // Per-stage: enter effects, tick effects, transition condition (event-based or timer-based)
    public void OnTrigger(SkillContext ctx) { /* dispatch to current stage's triggerOnEnter */ }
    public void OnTick(SkillContext ctx, float dt) { /* advance stage timer; evaluate transition */ }
}
```

The `StageConfig[]` is itself a list of `ComponentConfig` — so a stage is just **a list of effects**. This keeps the model uniform: **a complex behavior is one component whose parameters contain more components**.

### 4.5 CoroutineLoopComponent — for SkeletonTalent1's loop

```csharp
[RegisterComponent("CoroutineLoop")]
public class CoroutineLoopComponent : ITickingComponent
{
    // parameters: conditionExpression, tickBodyComponent
    // Each tick: evaluate condition (blackboard expr); if true and not running, start a sub-coroutine via UniTask
    // When condition false: stop loop, call OnTeardown on body
}
```

---

## 5. Skill Runtime

### 5.1 `SkillRuntime` — per-skill execution

```csharp
namespace SkillSystem
{
    public class SkillRuntime
    {
        public SkillConfig config;
        public SPEngine spEngine;
        public Blackboard blackboard = new();
        public List<ISkillComponent> components = new();
        public List<ITickingComponent> tickingComponents = new();

        public bool MatchesTrigger(SkillEvent evt) { /* match triggerEvent + ConditionOp */ }
    }
}
```

### 5.2 `SkillRunner` — single MonoBehaviour per Entity

```csharp
namespace SkillSystem
{
    public class SkillRunner : MonoBehaviour
    {
        private Entity _entity;
        private List<SkillRuntime> _skills = new();
        public Blackboard sharedBlackboard = new();

        public void PreWarm()
        {
            _entity = GetComponent<Entity>();
            // Subscribe to all events
            _entity.AttackBase.OnBeforeAttack += OnBeforeAttack;
            _entity.AttackBase.OnAfterAttack += OnAfterAttack;
            _entity.AttackBase.OnBeforeTakeDamage += OnBeforeTakeDamage;
            _entity.AttackBase.OnAfterTakeDamage += OnAfterTakeDamage;
            _entity.AttackBase.OnAttackSuccessfully += OnAttackSuccessfully;
            _entity.AttackBase.OnAttackInterrupt += OnAttackInterrupt;
            _entity.OnBeforeHurt += OnBeforeHurt;
            _entity.OnAfterHurt += OnAfterHurt;
            _entity.OnBeforeDieAnimation += OnBeforeDie;
            _entity.entityAM.OnAttackAnimationBegin += OnAttackAnimBegin;

            // Build skills from EntityData
            foreach (var cfg in _entity.EntityData.Skills)
            {
                var runtime = BuildSkillRuntime(cfg);
                _skills.Add(runtime);
            }
        }

        public void OnInitialize() { /* dispatch OnInitialize to all components */ }
        public void OnDeath() { /* dispatch OnDeath to all components, then teardown */ }
        public void OnTeardown() { /* unsubscribe, teardown components */ }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            foreach (var skill in _skills)
            {
                skill.spEngine?.OnTick(dt, _entity);
                foreach (var c in skill.tickingComponents)
                    c.OnTick(MakeCtx(skill), dt);
            }
        }

        // Event handlers dispatch to all matching skills
        private void OnBeforeAttack(Entity t, ref float m, ref float dP, ref float mP,
            ref float dPv, ref float mPv, ref int cumbo, ref int dt2, int applyType)
        {
            var evt = new BeforeAttackEvent { ... };
            DispatchEvent(evt);
            m = ((BeforeAttackEvent)evt).multiplyer; /* copy back */
        }
        // ... (one per event, copies ref params back)
    }
}
```

---

## 6. SP Engine

```csharp
namespace SkillSystem
{
    public class SPEngine
    {
        private SPConfig _cfg;
        private float _currentSp;
        private int _currentCharge;
        private float _currentDuration;
        private bool _isActive;
        private Entity _entity;

        public void OnTick(float dt, Entity entity)
        {
            if (_cfg.recoverMode == SpRecoverMode.Natural && !IsRecoverForbidden())
                _currentSp = Math.Min(_currentSp + dt, _cfg.totalSp);
            if (_isActive && _cfg.consumeMode == SpConsumeMode.Duration)
                TickDuration(dt);
            if (_currentSp >= _cfg.totalSp && _cfg.openMode == SkillOpenMode.Natural && !_isActive)
                FireSkill();
        }

        public void OnAttackSuccessfully() {
            if (_cfg.recoverMode == SpRecoverMode.OnAttackHit) AddSp(1);
            if (_cfg.consumeMode == SpConsumeMode.OnAttackHit) ConsumeCharge(1);
        }
        public void OnAfterHurt(int applyType, bool isDeadly) {
            if (applyType == 0 || applyType == 1) {
                if (_cfg.recoverMode == SpRecoverMode.OnAfterHurt) AddSp(1);
                if (_cfg.consumeMode == SpConsumeMode.OnAfterHurt) ConsumeCharge(1);
            }
        }
        public void OnAttackAnimBegin() {
            if (_cfg.openMode == SkillOpenMode.OnAttackAnimBegin && CanBegin()) FireSkill();
        }
        public void OnBeforeHurt(int applyType) {
            if ((applyType == 0 || applyType == 1) && _cfg.openMode == SkillOpenMode.OnBeforeHurt && CanBegin())
                FireSkill();
        }
        public void OnAttackHit() {
            if (_cfg.openMode == SkillOpenMode.OnAttackHit && CanBegin()) FireSkill();
        }

        public bool CanBegin() => _currentSp + _cfg.totalSp * _currentCharge >= _cfg.totalSp && !_isActive;
        public void FireSkill() { ... }
        public void EndSkill() { ... }
    }
}
```

`SPEngine` **fully replaces** all of `Skill.cs`'s SP-related logic, in a **structured, enum-typed** way.

---

## 7. Trigger Engine — Event Dispatch

```csharp
namespace SkillSystem
{
    public abstract class SkillEvent { }
    public class BeforeAttackEvent : SkillEvent { public Entity target; public float multiplyer; public float defPenetrate; public float mgrPenetrate; public float defPenetrate_value; public float mgrPenetrate_value; public int cumbo; public int damageType; public int applyType; }
    public class AfterAttackEvent : SkillEvent { ... }
    public class BeforeTakeDamageEvent : SkillEvent { ... }
    public class AfterTakeDamageEvent : SkillEvent { public bool isDeadly; ... }
    public class BeforeHurtEvent : SkillEvent { public bool isDeadly; ... }
    public class AfterHurtEvent : SkillEvent { public bool isDeadly; ... }
    public class AttackAnimBeginEvent : SkillEvent { }
    public class BeforeDieAnimationEvent : SkillEvent { }
    public class DeathEvent : SkillEvent { }
    public class IntervalTickEvent : SkillEvent { public float dt; }
    public class SkillBeginEvent : SkillEvent { public SkillRuntime skill; }
    public class SkillEndEvent : SkillEvent { public SkillRuntime skill; }
    public class AttackSuccessfullyEvent : SkillEvent { }
    public class AttackInterruptEvent : SkillEvent { }
}
```

`SkillRunner` converts each `Entity`/`AttackBase`/`AnimationMachine` event into a `SkillEvent` and dispatches.

---

## 8. Editor Tooling

### 8.1 PropertyDrawer for `ComponentConfig[]`

Renders a ReorderableList of components with:
- A dropdown for `componentType` populated from `ComponentAutoRegistry` reflection
- After selection, dynamically render the component's parameter schema
- Validation highlights for missing required parameters

### 8.2 PropertyDrawer for `SPConfig`

Renders all 3 modes as labeled enums (no more magic ints). Skill duration slider.

### 8.3 PropertyDrawer for `ParamList`

Renders key-value pairs with type selector and string-encoded value field, with conversion to actual value preview.

### 8.4 Migration Tool (Editor menu)

`MenuItem("Tools/Skill System/Migrate Old Skill Scripts")` — runs once to:
- Scan the project for all `MonoBehaviour` instances of `Skill` / `Talent` subclasses
- For each instance, parse `[SerializeField]` fields and emit a `SkillConfig` populated into the prefab's `EntityData`
- Strip the old MonoBehaviour from the prefab

---

## 9. Migration of 28+ Existing Scripts

The 28+ existing scripts map 1-to-1 to **a small set of `SkillConfig` data entries** in their owning `EntityData`. No new component code is required for most — they are combinations of existing components.

| Existing script | New `SkillConfig` components |
|---|---|
| `Kroos/Skill1.cs` | `AttackBoost` (mult=1.4, cumbo=2) |
| `Witch/WitchSkill.cs` | `ConditionalBranch` (hpRate < 0.7) → `SwapAnimation` (drink anims) + `PlayParticle` (medicalLight) + `SelfHeal` (TakeEffect_Single) + `PlayAnimation` (Trigger attack on self) |
| `Witch/WitchTalent.cs` | `OnBeforeTargetSelect` → compute poison/damage → `ConditionalBranch` (poison>damage) → `SetAttackEffectData` (color=poison) + `OnBeforeTakeDamage` → `EntitySelectorRadiusEffect` (TakeEffect_Radius) |
| `Creeper/CreeperTalent.cs` | `StageStateMachine` with 3 stages: Charging (animation swap + buff) → BackingOut (animation swap + buff remove) OR Exploding (animation swap + radius damage with falloff + Die) |
| `Zombie/ZombieTalent.cs` | `SelfDestruct` (timer) + `OnDeadlyDamage` trigger → `SwapAnimation` (idle2/move2/attack2) + `ApplyBuff` (self) + `PeriodicAuraBuff` (in radius) |
| `HeadSeter/HeadSeterSkill1.cs` | `ConditionalBranch` (no targets) → `SwapAnimation` (begin/begin_d) + `SetAbnormalState` (0, 3) + `FlashMove` (moveDis) + `SwapAnimation` (end) + `SetAbnormalState` (remove) |
| `Wither/WitherTalent1.cs` | `CampDamageModifier` (camp==2, mult=10) + `SelfDamageOnEvent` (isDeadly → 5000) + `LockHpShield` (active flag, threshold) |
| `Skeleton/SkeletonTalent1.cs` | `OnBeforeAttack` → `SpawnBullet` (with callback) + `CoroutineLoop` (queue targets, trigger extra attack) |
| `Slime/SlimeTalent1.cs` | `OnBeforeDieAnimation` → `DeathSpawn` (spawnEntityID, spawnNum, spawnGap) |
| `Beef/BeefSkill.cs`, `BeefTalent.cs` | TBD per existing behavior |
| 19+ other skill/talent files | TBD per existing behavior |

**Each migration entry is verified by the test suite (see §10) running the entity through the same event sequence and comparing observable state.**

---

## 10. Testing Strategy

### 10.1 Unit tests for components

Each `ISkillComponent` is unit-testable in isolation:
- Construct with mock `SkillContext`
- Call `OnTrigger` / `OnTick` with constructed events
- Assert on `Entity` / `BuffController` / `Blackboard` state

### 10.2 Snapshot tests for skill execution

For each migrated skill:
- Load its `SkillConfig` from a test fixture
- Construct a mock Entity
- Drive a scripted sequence of events
- Assert final state matches the **old script's behavior** (golden test)

### 10.3 Migration test runner

`MenuItem("Tools/Skill System/Run All Migration Tests")` iterates all 28+ skill prefabs and runs snapshot tests.

---

## 11. File & Folder Layout

```
Assets/PublicScripts/SkillSystem/
  SkillConfig.cs           # data model: SkillConfig, SkillKind
  SPConfig.cs              # data model: SPConfig + enums
  ComponentConfig.cs       # data model: ComponentConfig, ConditionConfig, ParamList
  ISkillComponent.cs       # interface + SkillContext
  ComponentFactory.cs      # registry + auto-register attribute
  ComponentAutoRegistry.cs # scans assembly, registers [RegisterComponent]
  SkillRuntime.cs          # per-skill runtime
  SkillRunner.cs           # MonoBehaviour, subscribes to events, dispatches
  SPEngine.cs              # SP state machine
  Blackboard.cs            # shared state
  SkillEvents.cs           # SkillEvent subclasses
  ConditionEvaluator.cs    # ConditionOp evaluator

Assets/PublicScripts/SkillSystem/Components/   # ~40 component files
  AttackBoostComponent.cs
  CampDamageModifierComponent.cs
  ApplyBuffComponent.cs
  PeriodicAuraBuffComponent.cs
  SwapAnimationComponent.cs
  PlayAnimationComponent.cs
  ResetAnimationComponent.cs
  FlashMoveComponent.cs
  AddImpulseComponent.cs
  DeathSpawnComponent.cs
  SpawnBulletComponent.cs
  EntitySelectorRadiusEffectComponent.cs
  DamageRadiusFalloffComponent.cs
  StageStateMachineComponent.cs
  CoroutineLoopComponent.cs
  SelfDestructComponent.cs
  SelfDamageOnEventComponent.cs
  LockHpShieldComponent.cs
  PlayParticleComponent.cs
  InstantiatePrefabComponent.cs
  SetAttackEffectDataComponent.cs
  RandomSelectorComponent.cs
  ConditionalBranchComponent.cs
  DelayedEffectComponent.cs
  AttackRangeOverrideComponent.cs
  TargetSelectionModifierComponent.cs
  SetAbnormalStateComponent.cs
  HealOnEventComponent.cs
  DamageThresholdComponent.cs
  # + edge-case components discovered during migration

Assets/PublicScripts/SkillSystem/Editor/
  ComponentConfigDrawer.cs
  SPConfigDrawer.cs
  ParamListDrawer.cs
  MigrationTool.cs
  MigrationTestRunner.cs
```

### 11.1 Files to delete after migration

- `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Skill.cs`
- `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Talent.cs`
- All 28+ subclass files under `Assets/Resources/Prefabs/.../Scripts/`

---

## 12. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Behavior drift during migration | Snapshot tests with golden behavior; manual playtest of each entity after migration |
| Component count grows unboundedly | Strict "one responsibility per component" rule; periodic component de-duplication review |
| `ParamList` string-typed values are error-prone | Component declares required keys + types; PreWarm validates and surfaces Inspector errors |
| Skill scripts on prefabs break when Skill.cs deleted | Migration tool must run **before** Skill.cs is deleted; CI enforces zero `Skill`/`Talent` MonoBehaviour references before merge |
| Performance: per-event dispatch iterates all skills | Component-level pre-filter by `triggerEvent` so the dispatch loop only visits components whose declared trigger matches; benchmark target <1µs per event |
| Complex behavior (Creeper) doesn't fit stage model cleanly | `StageStateMachine` is generic; per-stage effects are themselves `ComponentConfig[]`; verified against CreeperTalent migration test |

---

## 13. Out of Scope / Future Work

- **Visual scripting editor** for skills (could be added later via NodeCanvas plugin)
- **Network sync** of skill state (single-player only for now)
- **Skill dependency graph** (e.g. "Skill A modifies Skill B's parameters") — can be expressed via shared `Blackboard` keys, no explicit graph needed
- **AI behavior reuse** — same `ISkillComponent` infrastructure could power AI in the future, but no AI migration in this spec

---

## 14. Open Questions

None at this stage. All key decisions resolved during brainstorming:

1. ✅ Framework: self-built lightweight (data-driven + Effect composition)
2. ✅ Scope: one-shot refactor + migrate all 28+ scripts
3. ✅ Configuration model: `EntityData.Skills: List<SkillConfig>`, components as `List<ComponentConfig>` with type+params
4. ✅ Complex behavior: `StageStateMachineComponent` (one component with internal stages, parameterized)
5. ✅ Old base classes: completely delete `Skill.cs` and `Talent.cs` MonoBehaviour classes

---

## 15. Definition of Done

- [ ] All 40+ components implemented, registered, and unit-tested
- [ ] All 28+ existing skills/talents migrated to `SkillConfig` data entries in their owning `EntityData`
- [ ] `Skill.cs` and `Talent.cs` deleted; all 28+ subclass scripts deleted
- [ ] No `Skill` / `Talent` MonoBehaviour references in any prefab
- [ ] Migration test suite passes 100% (snapshot tests for each migrated skill)
- [ ] Manual playtest of every entity type passes (no functional regression)
- [ ] PropertyDrawers implemented for `ComponentConfig`, `SPConfig`, `ParamList`
- [ ] Design spec + implementation plan merged to `main`
