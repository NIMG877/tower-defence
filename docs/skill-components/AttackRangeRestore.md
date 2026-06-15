# AttackRangeRestore

Restores the selected entity's attack range by assigning
`EntityVision.BaseRange` back to `EntityVision.Range`.

**Registered as:** `AttackRangeRestore`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `toSelf` | Bool | `True` | Target `ctx.entity`. |
| `targetEntity` | String | `""` | When `toSelf=false`, Blackboard key containing one `Entity`. |

The range setter recalculates orientation, so the restored base range follows
the entity's current direction.
