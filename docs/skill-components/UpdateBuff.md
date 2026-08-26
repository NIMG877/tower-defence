# UpdateBuff

Refreshes the modifiers of existing buffs (`BuffController.SetBuffValues`).
Completes the buff-management trio: `ApplyBuff` creates, `DestroyBuff`
removes, `UpdateBuff` re-values in place. Reads the parallel
`(target, buff)` pair that `ApplyBuff` wrote to the Blackboard.

**Canonical op:** `update_buff`
**Component registration:** `UpdateBuff`
**Class:** `AbilitySystem.Components.UpdateBuff`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/UpdateBuff.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `inputTarget` | String | `""` | Blackboard key of the `List<Entity>` half of the pair. Both keys must be set, else no-op. |
| `inputBuff` | String | `""` | Blackboard key of the `List<Buff>` half of the pair. Both keys must be set, else no-op. |
| `attributes` | StringCsv | `""` | Attribute names, comma-separated (e.g. `Attack`). |
| `ops` | StringCsv | `""` | `ModifierOp` enum names, comma-separated (e.g. `AddPercent`). |
| `magnitudes` | FloatCsv | `""` | Per-modifier magnitudes. Supports `fromBlackboard=true`: the key may hold a `float`/`float[]`/CSV string. |

## Differences from ApplyBuff

- The input pair is a **persistent handle** — the Blackboard keys are not
  consumed/cleared after the update, so the same pair can be refreshed
  repeatedly (the typical pattern: a counter changes, the derived magnitude
  is recomputed, `update_buff` pushes it into the standing buff).
- Modifiers are **rebuilt on every trigger**, never cached — `magnitudes`
  with `fromBlackboard=true` is the point of this component (ApplyBuff
  caches because aura mode re-runs every tick; this one is event-driven).

## CSV alignment

The three CSVs are index-aligned into `Modifier[]`. Mismatched lengths take
the shortest and warn once (`update-buff-csv-length`), mirroring
`ApplyBuff`'s tolerance. A `magnitudes` Blackboard key holding a single
`float` is read as a one-element array.

## Typical wiring

```
# once, at setup:
- apply_buff (toSelf, Attack AddPercent 0, outputTarget=t_targets, outputBuff=t_buffs)

# on each count change:
- write_blackboard (key=t_count, method=add, value=1)
- write_blackboard (key=t_pct,   method=mult, value=0.2)   # or set+mult
- update_buff (inputTarget=t_targets, inputBuff=t_buffs,
               attributes=Attack, ops=AddPercent,
               magnitudes=t_pct [fromBlackboard])
```
