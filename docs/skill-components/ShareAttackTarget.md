# ShareAttackTarget

Relays the current `BeforeAttackEvent` target to selected allied entities,
optionally using a communication projectile before enqueueing the request.

**Canonical op:** `share_attack_target`
**Component registration:** `ShareAttackTarget`
**Class:** `AbilitySystem.Components.ShareAttackTarget`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/ShareAttackTarget.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `blackboardKey` | String | `""` | Sender Blackboard `List<Entity>` containing recipients. |
| `receiverAbilityId` | String | `""` | When set, only entities currently running this ability receive requests. |
| `queueKey` | String | `shared_attack_requests` | Recipient Blackboard key storing `List<SharedAttackRequest>`. |
| `activeSourceKey` | String | `shared_attack_source` | Sender Blackboard key written by `SharedTargetExtraAttack`; that source is skipped to prevent echo. |
| `bulletPrefabResource` | String | `""` | `Resources.Load<GameObject>` path for the communication projectile. |
| `bulletTrailResource` | String | `""` | `Resources.Load<GameObject>` path for its trail. |
| `bulletSpeed` | Float | `0` | Communication projectile speed. Invalid resources/speed fall back to immediate delivery. |

## Behavior

Use an `OnBeforeAttack` rule with `select_targets` before
`share_attack_target`.
Recipients are deduplicated; self, the active shared-request sender, and
entities without `receiverAbilityId` are skipped. Delivery requires the
recipient to still be active (with a runner and `Stats`) — inactive
recipients are dropped. When the projectile arrives,
the `(sender, attack target)` request is appended only if the recipient does
not currently see that enemy; a recipient that went inactive by arrival time
is dropped as well.

The communication projectile deals zero final damage (`damage=1`,
`multiplier=0`) before delivering its callback.
