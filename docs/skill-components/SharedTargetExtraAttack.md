# SharedTargetExtraAttack

Consumes shared attack requests from the owning entity's Blackboard and
performs interruptible extra attacks against their exact targets.

**Canonical op:** `shared_target_extra_attack`
**Component registration:** `SharedTargetExtraAttack`
**Class:** `AbilitySystem.Components.SharedTargetExtraAttack`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/SharedTargetExtraAttack.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `queueKey` | String | `shared_attack_requests` | Input `List<SharedAttackRequest>` Blackboard key. |
| `activeSourceKey` | String | `shared_attack_source` | Writes the active request sender so `ShareAttackTarget` can prevent echo. |
| `attackAnimation` | String | `""` | Named Animation Group used for both `AttackClose` and `AttackRemote`. |
| `abnormalType` | Int | `0` | Abnormal state held while the queue is nonempty. Supported range `0..3`. |
| `abnormalTime` | Float | `-10` | Abnormal-state duration; the default is permanent until explicitly removed. |

## Triggers and lifecycle

Use one rule with `OnTick`, `OnAttackSuccessfully`, and `OnAttackInterrupt`
trigger entries and one `shared_target_extra_attack` step. The rule binds one
component instance for queue and active-request state.

- `OnTick`: removes invalid queue-front targets, applies the abnormal state,
  and attempts the first exact-target attack unless the entity is in `Start`
  or another shared extra attack is active. A host without an `AttackBase`
  bails out entirely; without an animation machine the abnormal state is
  still applied but no attack starts.
- `OnAttackSuccessfully`: consumes the active request.
- `OnAttackInterrupt`: aborts the shared sequence — clears the active marker,
  **removes the whole queue**, and releases the abnormal state. Interrupted
  requests are not retried.
- Teardown clears the queue/source keys and removes its abnormal state.

The underlying abnormal-state API has no source handle, so removing the state
can also remove the same type applied by another system.

## Animation

Each extra attack applies `attackAnimation` as a machine one-shot override
(`AnimationMachine.AddOneShotOverride` covering `AttackClose`/`AttackRemote`)
registered immediately before `TryToAttack`; the attack's own state
resolution consumes it. If the attack fails to start, the entry is revoked on
the spot so the next natural attack does not play the shared animation.
