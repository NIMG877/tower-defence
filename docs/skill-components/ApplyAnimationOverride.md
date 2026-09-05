# ApplyAnimationOverride

Replaces animation slots using names from the target entity's
`AnimationResources` Named Resources.

**Canonical op:** `apply_animation_override`
**Component registration:** `ApplyAnimationOverride`
**Class:** `AbilitySystem.Components.ApplyAnimationOverride`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/ApplyAnimationOverride.cs`
**Pair with:** `RemoveAnimationOverride` for early revocation.

## Modes

- `once`: registers a **one-shot** override. No state transition is performed —
  the entry takes effect the next time the machine naturally plays a covered
  slot (e.g. the attack system entering the attack state), and the whole entry
  removes itself at that first actual play.
- `override`: adds a persistent override that affects all future animation
  resolution until removed.

Both modes stack into `AnimationMachine`'s priority-ordered override layers
(`(priority, registration id)`, later registration wins ties) and record
revocable handles to `outputKey`.

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `toSelf` | Bool | `True` | Target `ctx.entity`. If false, read `List<Entity>` from `blackboardKey`. |
| `blackboardKey` | String | `""` | Blackboard key containing target entities when `toSelf=false`. |
| `mode` | String | `once` | `once` or `override`. |
| `slots` | StringCsv | `""` | Parallel list of `AnimationSlot` names. |
| `resources` | StringCsv | `""` | Parallel list of Named Resource names. |
| `outputKey` | String | `""` | Stores `List<AnimationOverrideRecord>` for later removal (both modes). |
| `priority` | Int | `0` | Override priority. Higher priority is applied later (wins). |

`AttackRemote`, `AttackClose`, and `Charge` require Named Animation Groups.
Other slots require Named Animations.

## One-shot consumption semantics

- Covered slots = slots with a non-empty resource (plus cleared slots).
- The entry is consumed **whole the first time any covered slot is actually
  played** (not when a state transition merely re-resolves it). Covering slots
  that may never play (e.g. `AttackClose` on a ranged attacker) must not make
  the entry immortal — covering both attack variants to mean "whatever attack
  plays" is a supported pattern.
- Playback works from a snapshot resolved at the state transition, so a
  multi-slot set (e.g. `AttackBegin/AttackRemote/AttackEnd`) stays coherent
  through one full attack cycle; removal only affects the next resolution.
- If an attack is interrupted, the entry has already been consumed (or is
  consumed by the next play of a covered slot); revoke early via
  `RemoveAnimationOverride`.

## Example

```yaml
slots: AttackBegin,AttackRemote,AttackClose,AttackEnd
resources: Talent_Begin,Talent_Attack,Talent_Attack,Talent_End
mode: once
```

Registers the three-segment talent attack; the attack system's own
`TryToAttack` transition resolves and plays it, consuming the entry whole.
