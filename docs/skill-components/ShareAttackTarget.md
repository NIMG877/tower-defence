# ShareAttackTarget

Relays the current `BeforeAttackEvent` target to selected allied entities,
optionally using a communication projectile before enqueueing the request.

**Registered as:** `ShareAttackTarget`

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

Configure an `EntitySelector` before this component on `OnBeforeAttack`.
Recipients are deduplicated; self, the active shared-request sender, and
entities without `receiverAbilityId` are skipped. When the projectile arrives,
the `(sender, attack target)` request is appended only if the recipient does
not currently see that enemy.

The communication projectile mirrors the legacy behavior and deals zero final
damage (`damage=1`, `multiplier=0`) before delivering its callback.
