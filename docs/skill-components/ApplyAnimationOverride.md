# ApplyAnimationOverride

Replaces animation slots using names from the target entity's
`AnimationResources` Named Resources.

**Canonical op:** `apply_animation_override`
**Component registration:** `ApplyAnimationOverride`
**Pair with:** `RemoveAnimationOverride` for persistent overrides.

## Modes

- `once`: applies the replacement only to the state transition triggered by
  this component.
- `override`: adds a persistent override that affects future state transitions.
  Set `outputKey` so it can later be removed.

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `toSelf` | Bool | `True` | Target `ctx.entity`. If false, read `List<Entity>` from `blackboardKey`. |
| `blackboardKey` | String | `""` | Blackboard key containing target entities when `toSelf=false`. |
| `mode` | String | `once` | `once` or `override`. |
| `slots` | StringCsv | `""` | Parallel list of `AnimationSlot` names. |
| `resources` | StringCsv | `""` | Parallel list of Named Resource names. |
| `outputKey` | String | `""` | In `override` mode, stores `List<AnimationOverrideRecord>` for later removal. |
| `priority` | Int | `0` | Persistent override priority. Higher priority is applied later. |
| `state` | String | `Idle` | State immediately played in `once` mode. |
| `forceChange` | Bool | `True` | Whether the one-time state transition is forced. |
| `moveBranch` | String | `Normal` | `MoveAnimationBranch` used when `state=Move`. |
| `attackBranch` | String | `Normal` | `AttackAnimationBranch` used when `state=Attack`. |

`AttackRemote`, `AttackClose`, and `Charge` require Named Animation Groups.
Other slots require Named Animations.

## Example

```yaml
slots: AttackRemote,AttackClose
resources: Skill,Skill
mode: override
outputKey: skill_animation_overrides
```

Persistent overrides affect future animation resolution; they do not restart
the animation currently playing. A one-time `state=Attack` transition passes
no attack action, so it plays an attack animation but does not itself deal
damage.
