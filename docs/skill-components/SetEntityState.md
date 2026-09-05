# SetEntityState

Switches the logical `EntityStateMachine` state of the host entity or a
Blackboard-provided entity list. `AnimationMachine` observes the resulting
`StateChanged` event and plays the resolved animation slot.

**Canonical op:** `set_entity_state`  
**Component registration:** `SetEntityState`
**Class:** `AbilitySystem.Components.SetEntityState`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/SetEntityState.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `state` | String, BB-capable | `""` | Target `EntityState`: `Default`, `Idle`, `Move`, `Attack`, `Start`, `Cast`, or `Die`. |
| `castMode` | String, BB-capable | `OneShot` | Used only when `state=Cast`. Accepts `OneShot` or `Sustained` case-insensitively. |
| `force` | Bool, BB-capable | `False` | `False` uses normal priority/ban rules. `True` permits backward and same-state transitions unless the current state is `Die`; destination bans still apply. |
| `toSelf` | Bool, BB-capable | `True` | Targets `ctx.entity` when `blackboardKey` is empty. |
| `blackboardKey` | String, BB-capable | `""` | Blackboard key containing `List<Entity>`. A non-empty key takes precedence over `toSelf`. |

There are no Blackboard outputs. Unknown `state` or Cast-only `castMode`
values produce a one-shot warning and skip the whole operation. `castMode` is
ignored for every non-Cast target state. A missing target list, null target, or
rejected state transition is a silent no-op; targets in a list compete
independently.

## Cast modes

### `OneShot`

The Cast animation is played once. Its matching Spine `Complete` callback
returns the entity to `Idle`. Omitting `castMode`, including in existing
assets, selects this mode.

### `Sustained`

The Cast animation loops and Spine `Complete` callbacks do not exit Cast. A
later rule/step must explicitly select another state. Returning to lower
priority `Idle` normally requires `force=True`; `Die` can still interrupt Cast
through the usual higher-priority transition.

```yaml
# Start a sustained Cast.
- op: set_entity_state
  args:
    state: Cast
    castMode: Sustained

# End it from the appropriate ability-end/cancel rule.
- op: set_entity_state
  args:
    state: Idle
    force: True
```

A non-forced `Cast -> Cast` request is rejected by the normal same-state rule.
Use `force=True` only when intentionally restarting Cast or changing its mode.
`Attack -> Attack` is the one same-state exception: the normal priority rule
admits it while the machine's live attack phase is `ComboWindow` (chaining the
next combo hit needs no force).

## Lifecycle and reentry

`OnInit` binds lazy argument readers. Each `OnTrigger` resolves the current
arguments and performs an immediate state-transition attempt; the component
has no tick or teardown state. It is therefore safe for `Parallel` rule
reentry, although all runs still compete for the same target state machine.

The component does not own or restore the state on rule cancellation,
ability removal, or teardown. A sequence that starts `Sustained` must arrange
an explicit exit on every intended completion/cancellation path.

## Limitations

- `state=Attack` does not register the one-shot attack-frame callback supplied
  by `TrySetAttackState`; use the attack operations for a complete attack.
- `state=Die` does not execute `Entity.Die()` side effects such as
  `Stats.BeginDie()` and death audio; use the entity-destruction operation for
  the complete death lifecycle.
- The Cast animation slot must resolve to an animation. A missing animation
  cannot produce the `Complete` event required for `OneShot` to return to
  `Idle`.
