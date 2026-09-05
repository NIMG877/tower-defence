# ForceAttack

Immediately performs a forced **empty-target attack** on selected entities
(`TryToAttack(∅, forceChange, not-interruptible)`): it plays
the attack animation and fires the `OnAttackSuccessfully` beat (SP consume,
event bridges included) but selects no targets and deals no direct damage.

**Canonical op:** `force_attack`
**Component registration:** `ForceAttack`
**Class:** `AbilitySystem.Components.ForceAttack`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/ForceAttack.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `toSelf` | Bool | `True` | Target `ctx.entity`. If false, read `List<Entity>` from `blackboardKey`. |
| `blackboardKey` | String | `""` | Blackboard key containing target entities when `toSelf=false`. |

## Pairing

Use after `apply_animation_override` in `once` mode: the pending one-shot
registers first, and the same-frame `TrySetAttackState` inside this
component resolves and consumes it — the skill-specific animation plays for
this forced attack.

## Versus `force_reset_attack`

`ForceResetAttack` resets the cooldown and lets the main loop attack with
**real selected targets** (normal damage). `ForceAttack` performs the
attack immediately with **no targets at all** (animation + event beats
only) — for skills whose damage comes from elsewhere (e.g. bullets or
spawned entities).

## Semantics

- The "no targets, no damage" contract is guaranteed on the normal attack
  path. Attack behaviors that preprocess the candidate list differ:
  `ChargeAttack` treats the empty call as a charge-store attack, and
  `WitherAttack` re-selects real targets and deals real damage. Check the
  entity's attack behavior before relying on `force_attack` for those.
- The main attack loop keeps ticking during the forced animation (the
  forced attack does not reset the attack timer) — its per-tick `TryToAttack`
  fails against the already-active attack state.
