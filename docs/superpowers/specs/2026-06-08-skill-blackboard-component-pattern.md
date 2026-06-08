# Skill Blackboard Component Pattern — Spec

**Date**: 2026-06-08
**Status**: Approved
**Parent design**: [2026-06-07-skill-talent-refactor-design.md](2026-06-07-skill-talent-refactor-design.md)
**Scope**: Conventions for skill components that share data through `Blackboard`. The mechanism (`Blackboard` class + `SkillContext.blackboard`) already exists; this spec defines **how to use it cleanly** without naming components after their plumbing.

## Problem

In a data-driven skill system, a single skill often needs multiple components to collaborate. Example: a "fireball" skill has a selector that picks targets in a radius, and a buff applier that applies "burn" to those targets. The selector and the applier run in separate components and need to pass the target list between them.

There are three ways to pass that list:

| Channel | Capacity | When to use |
| --- | --- | --- |
| `ParamList` entry | Static, single value, designer-typed | Simple scalars (`float value = "10"`, `string buff = "burn"`) |
| `SkillEvent` field | Read-only snapshot of one event | Data that the event already carries (e.g. `BeforeTakeDamageEvent.target`) |
| `Blackboard` | Arbitrary runtime-generated data, mutable, scoped | Lists of entities, computed values, cross-component pipelines |

For the fireball case, the target list is a **`List<Entity>`** generated at runtime — it has no place in `ParamList` and does not exist on a single event. The only practical channel is the blackboard.

The mechanism is already in place. The question is **how to expose it on components without polluting component names** with suffixes like `ToBlackboard` / `FromBlackboard`.

## Goal

A component is named for **what it does** (e.g. `EntitySelector`, `ApplyBuff`), not for **how it shares data**. Blackboard interaction is an optional, parameterised capability that any component can opt into by:

1. Declaring a string parameter named `blackboardKey`.
2. Following the read/write conventions below.
3. Providing a sensible default when the key is empty.

## Design

### Parameter convention

A component that wants to read or write a blackboard key exposes a single string parameter:

| Parameter name | Type | Required | Meaning |
| --- | --- | --- | --- |
| `blackboardKey` | `string` (designer-filled) | **No**, default `""` | The blackboard slot this component reads from / writes to. Empty = no blackboard interaction. |

The string is **the same name** for both readers and writers. Designers match them by string equality in the Inspector.

**Type information is not in the key.** A `blackboardKey = "selectedEnemies"` value is a `List<Entity>` because the **component** that writes it is `EntitySelector` (which always writes `List<Entity>`). A different component that writes `"selectedEnemies"` would be a type error — the spec forbids it. See "Type contract" below.

### Type contract

Each component that touches the blackboard **commits to a single type** for the data on that key:

| Component | Key reads/writes | Type committed |
| --- | --- | --- |
| `EntitySelector` | writes | `List<Entity>` |
| `ApplyBuff` | reads | `List<Entity>` |
| `DealDamageToBlackboardTargets` (hypothetical) | reads | `List<Entity>` |
| `WriteChargeProgress` (hypothetical) | writes | `float` |

A component's type contract is encoded in its **C# class signature** (the `Get<T>` call), not in any string field. Two components with different types **must not** share a `blackboardKey` value — Unity Editor will not catch this, but a `Get<T>` cast on a stored value of the wrong type will throw `InvalidCastException` at runtime. The spec relies on naming discipline and component-name review.

### Read/write conventions

**Writers (e.g. `EntitySelector`):**

```csharp
public void OnTrigger(SkillContext ctx)
{
    var selected = DoSelect(ctx);
    if (string.IsNullOrEmpty(_outputKey)) return;   // 1) empty key = opt out, no-op
    ctx.blackboard.Remove(_outputKey);              // 2) clear first to avoid stale data
    ctx.blackboard.Set(_outputKey, selected);       // 3) write
}
```

**Readers (e.g. `ApplyBuff`):**

```csharp
public void OnTrigger(SkillContext ctx)
{
    List<Entity> targets;
    if (!string.IsNullOrEmpty(_inputKey))
    {
        targets = ctx.blackboard.Get<List<Entity>>(_inputKey, null);
        if (targets == null) return;                // 1) key not set = upstream didn't write, skip silently
    }
    else
    {
        targets = new List<Entity> { ctx.entity };  // 2) empty key = sensible default (e.g. self)
    }
    foreach (var t in targets) DoStuff(t);
}
```

### Default behavior when key is empty

Every component that participates in this pattern **must** have a sensible non-blackboard default. This guarantees that:

- A component placed in a skill without `blackboardKey` still does something useful.
- A skill can be authored without first understanding the blackboard system.
- Components are independently testable in isolation.

Examples:

| Component | Default (no `blackboardKey`) | With `blackboardKey` |
| --- | --- | --- |
| `EntitySelector` | No-op (the result is discarded) | Writes `List<Entity>` to the key |
| `ApplyBuff` | Buff `ctx.entity` (self) | Buffs every entity in the key's `List<Entity>` |
| `DamageToBlackboardTargets` (future) | Damage `ctx.entity` (self) | Damages every entity in the key's `List<Entity>` |
| `WriteFloatToBlackboard` (future) | No-op | Writes the configured float to the key |

### Scope rules

| Blackboard | Scope | When to use |
| --- | --- | --- |
| `ctx.blackboard` (per-skill) | Lives as long as the `SkillRuntime` | Default. Cross-component pipelines inside one skill. |
| `ctx.sharedBlackboard` (per-Entity) | Lives as long as the `SkillRunner` (one per Entity) | System metadata only (`__attackerCamp`, `__damageType`). **Business components do not use this.** |

Conventions:

- All business components (selector, buff, damage) write to `ctx.blackboard` only.
- Reserved keys (set by dispatch layer): `__attackerCamp`, `__targetEntity`, `__damageType`, `__applyType`. Components may read these but must not write them.
- Reserved keys (per spec): `__stage_*` (stages, see [stages spec](2026-06-08-skill-stages-and-subcomponents-design.md)), `__sub_*` (sub-components, same spec).

### Cleanup

`SkillRuntime.blackboard` is **not** automatically cleared on `OnSkillBegin` / `OnSkillEnd`. Two consequences:

1. A component that runs multiple times in one activation will see its previous writes. This is fine when the writer uses `Remove` before `Set` (the convention above). If a writer does not, stale data leaks.
2. If a skill's `SkillRuntime` is reused across activations (same `skillId`, same `SkillRunner`), blackboard state persists. Writers should `Remove` in `OnInit` to reset, or rely on the `Remove`-before-`Set` pattern in `OnTrigger`.

**The spec does not require** auto-cleanup at the `SkillRuntime` level. The convention is: writers either `Remove`-before-`Set` (per-trigger reset) or call `Remove` in `OnInit` (per-activation reset).

## Reference implementations

### `EntitySelector` (new, registered as `"EntitySelector"`)

```csharp
[RegisterComponent("EntitySelector")]
public class EntitySelector : ISkillComponent
{
    private string _outputKey;            // blackboard key
    private float _radius = 1f;
    private int _camp = 0;                // 0 = ctx.entity.Camp, else explicit
    private bool _sameCamp = true;        // only used when _camp == 0
    private bool _force = false;          // pass-through to EntitySelector_Radius
    private bool _selectSelf = false;     // include ctx.entity in result

    public void OnInit(SkillContext ctx, ParamList p)
    {
        _outputKey   = p.GetString("blackboardKey", "");
        _radius      = p.GetFloat("radius", 1f);
        _camp        = p.GetInt("camp", 0);
        _sameCamp    = p.GetBool("sameCamp", true);
        _force       = p.GetBool("force", false);
        _selectSelf  = p.GetBool("selectSelf", false);
    }

    public void OnTrigger(SkillContext ctx)
    {
        if (string.IsNullOrEmpty(_outputKey) || ctx.entity == null) return;
        int camp = _camp == 0 ? ctx.entity.Camp : _camp;
        var pos = ctx.entity.Movement.Position;
        var ents = EntityManager.Manager.EntitySelector_Radius(
            (pos.x, pos.y), camp, _sameCamp, _radius, _force);
        if (_selectSelf && !ents.Contains(ctx.entity)) ents.Add(ctx.entity);
        ctx.blackboard.Remove(_outputKey);
        ctx.blackboard.Set(_outputKey, ents);
    }

    public void OnTick(SkillContext ctx, float dt) { }
    public void OnTeardown(SkillContext ctx) { }
}
```

### `ApplyBuff` (extends existing `ApplyBuffComponent`)

The existing `ApplyBuffComponent` ([ApplyBuffComponent.cs](../../Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/ApplyBuffComponent.cs)) is extended with a `blackboardKey` parameter. When the key is empty, the original self-target behavior is preserved. When set, the component reads a `List<Entity>` from the key and applies the buff to each.

See commit message on 2026-06-08 for the diff. Pattern:

```csharp
public void OnTrigger(SkillContext ctx)
{
    if (ctx.entity == null) return;
    var types = ParseEnums(_buffTypesRaw);
    var values = ParseFloats(_buffValuesRaw);

    List<Entity> targets;
    if (!string.IsNullOrEmpty(_inputKey))
    {
        targets = ctx.blackboard.Get<List<Entity>>(_inputKey, null);
        if (targets == null) return;
    }
    else
    {
        targets = new List<Entity>();
        var t = _toSelf ? ctx.entity : (ctx.currentEvent is BeforeTakeDamageEvent btd ? btd.target : null);
        if (t == null || t.buffController == null) return;
        targets.Add(t);
    }
    foreach (var tgt in targets)
    {
        if (tgt.buffController == null) continue;
        tgt.buffController.CreateBuff(types, null, _buffId, values, _priority, true);
    }
}
```

### Example skill: "AoE burn on enemies, heal on allies"

```yaml
components:
  - componentType: "EntitySelector"
    parameters:
      blackboardKey: "enemiesInRange"
      radius: "5"
      camp: "2"           # 2 = monster camp
      sameCamp: "false"
  - componentType: "ApplyBuff"
    parameters:
      blackboardKey: "enemiesInRange"
      buffTypes: "burn"
      buffValues: "0.05"
      buffId: "fireball_burn"
  - componentType: "EntitySelector"
    parameters:
      blackboardKey: "alliesInRange"
      radius: "5"
      camp: "1"           # 1 = player camp
      sameCamp: "true"
  - componentType: "ApplyBuff"
    parameters:
      blackboardKey: "alliesInRange"
      buffTypes: "heal"
      buffValues: "0.1"
      buffId: "fireball_heal"
```

Two selector instances, same component type, different `blackboardKey` values feeding two different `ApplyBuff` instances. The pattern is symmetric and pipeline-friendly.

## Non-Goals

- **Not** a generic typed key-value bus. Each component commits to a single type per key. Components that need multiple data shapes use multiple keys, multiple components, or `Tuple<>`.
- **Not** a replacement for `Entity.buffController`, `Entity.Movement`, etc. Blackboard is for **cross-component data flow inside a skill**, not for storing entity state.
- **Not** an inter-Entity communication channel. `ctx.sharedBlackboard` exists for that but is reserved for system metadata; business components do not use it.
- **Not** auto-cleaned. Writers handle cleanup by convention.

## Existing component migration

The three components that read blackboard today have orphan reads (no writers). They should be migrated as follows:

| Component | Today | After this spec |
| --- | --- | --- |
| `CampDamageModifierComponent` | Reads `attackerCamp` from blackboard | Read directly from `BeforeTakeDamageEvent.target.Movement.Camp` or similar. Drop the blackboard read. |
| `PlayParticleComponent` | Reads `__particleSystem` from blackboard | Read prefab via `parameters.Get<GameObject>("prefab")` or similar. Drop the blackboard read. |
| `CoroutineLoopComponent` | Reads `Has(stopWhenBlackboardKeyMissing)` from blackboard | **Keep as-is**, but rename parameter to `stopWhenKeyMissing` (drop the `Blackboard` substring) and document that this is the public API for external control. |
| `EntitySelectorRadiusEffectComponent` | Selects + immediately applies a sub-component | **Keep as-is**. It is the "immediate" mode. The new `EntitySelector` is the "piped" mode. Both are valid; choose per use case. |

## References

- Blackboard class: [Blackboard.cs](../../Assets/PublicScripts/GameData/SkillSystem/Blackboard.cs)
- `SkillContext` (per-skill vs shared): [ISkillComponent.cs](../../Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/ISkillComponent.cs)
- Condition evaluator (reads blackboard): [ConditionEvaluator.cs](../../Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/ConditionEvaluator.cs)
- Dispatch layer (sets system keys): [SkillRunner.cs](../../Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/SkillRunner.cs)
- Sister spec (stage / sub-component reserved keys): [2026-06-08-skill-stages-and-subcomponents-design.md](2026-06-08-skill-stages-and-subcomponents-design.md)
