# ApplyBuff

Applies one or more buffs to one or more target entities. Supports ordinary
one-shot application and an aura mode that synchronizes buffs against a
changing Blackboard target list.

**Canonical op:** `apply_buff`
**Component registration:** `ApplyBuff`
**Class:** `AbilitySystem.Components.ApplyBuff`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/ApplyBuff.cs`

## Target selection

- **`blackboardKey` empty** → single-target. With `toSelf=true` the target is
  `ctx.entity`; with `toSelf=false` the target is the entity carried by the
  current event payload (or `ctx.entity` if the event carries none).
- **`blackboardKey` set** → multi-target. The operation reads
  `List<Entity>` from the configured blackboard key and applies the buff to
  each entry. If the key is missing/empty on read, the trigger is silently
  skipped (an upstream writer hasn't run yet).

## BlackBoard output (optional)

When **both** `outputTarget` and `outputBuff` are set, the operation appends
this round's `targets` (those with a `buffController`) and the corresponding
created `Buff` objects to the per-Entity shared blackboard at those keys.
Lists are accumulated across executions within the same ability active window.
A null `Buff` slot is padded for targets where `CreateBuff` returned null —
the index alignment between target list and buff list is preserved so
downstream consumers (e.g. `DestroyBuff`) can walk them in parallel.

The writer/reader pair is symmetric: `ApplyBuff` writes only when both
output keys are set; `DestroyBuff` reads only when both input keys are set.

## Modes

- `normal` (default): each execution creates buffs and appends optional output
  records.
- `aura`: requires `blackboardKey`, `outputTarget`, and `outputBuff`. Each
  trigger treats the input entity list as the complete desired set. Existing
  tracked buffs have their values and duration refreshed, new targets receive
  a buff, and targets absent from the new list lose the tracked buff. Output
  records are overwritten with the current active set rather than appended.

Aura mode deduplicates the current target list. If a recorded buff expired or
was removed externally, it is recreated on the next trigger. `OnTeardown`
removes all remaining tracked aura buffs and clears both output keys.

## Parameters

All parameters are read via the `ParamList` lazy API (see README → Storage);
each `Func<T>` re-evaluates the source on every call.

| Key | Type | Default | Description |
|---|---|---|---|
| `mode` | String | `normal` | `normal` for one-shot application; `aura` for target-list synchronization. |
| `attributes` | StringCsv | `""` | Attribute names, comma-separated (e.g. `Attack`). Empty = trigger is a no-op. |
| `ops` | StringCsv | `""` | `ModifierOp` enum names, same order as `attributes` (e.g. `AddPercent`). |
| `magnitudes` | FloatCsv | `""` | Per-modifier magnitudes, same order as `attributes`. |
| `buffId` | String | `skill_buff` | Id passed to `BuffController.CreateBuff`. In normal mode, repeated triggers may create additional records with the same id; aura mode updates its tracked record instead. |
| `buffTime` | Float | `-10` | Duration in seconds. **Negative = permanent** (project convention: `-10` = permanent). |
| `toSelf` | Bool | `True` | `true` = apply to `ctx.entity`; `false` = apply to the target carried by the current event. Ignored when `blackboardKey` is set. |
| `isWhiteList` | *removed* | — | Replaced by `buffScope`. |
| `buffScope` | String | `normal` | Which `BuffController` list owns the buff: `normal` or `whiteList` (both cleared on `Dormancy`, i.e. each recall), or `level` — survives `Dormancy` (redeploy), cleared only when the entity leaves the entity pool (level exit, via `BuffController.ClearLevelBuffs`). Unknown values are a config error (warn once, trigger skipped). | |
| `blackboardKey` | String | `""` | If set, read `List<Entity>` from this blackboard key and apply to each. Empty = single-target mode. |
| `outputTarget` | String | `""` | If set (with `outputBuff`), append this round's target list to the blackboard at this key. |
| `outputBuff` | String | `""` | If set (with `outputTarget`), append this round's buff list to the blackboard at this key. |

The three CSVs are index-aligned into `Modifier[]`; mismatched lengths take
the shortest and warn once. The built modifiers are **cached after first
build** (aura mode re-runs every tick) — `magnitudes` with
`fromBlackboard=true` will therefore freeze at the first-read value here.
For dynamic re-valuing of a standing buff use `update_buff`, which rebuilds
modifiers on every trigger.

In `aura` mode the two output keys are required and represent the current
tracked pairs. In `normal` mode they append one set of records per execution.

## Known limitations

- Normal mode does not destroy created buffs on ability end. To destroy them
  at a specific point, pair `apply_buff` with `destroy_buff` and execute the
  latter from the desired `OnAbilityEnd` rule.

The limitation above applies to `normal` mode. Aura records are cleaned during
component teardown, and may also be consumed explicitly by `destroy_buff` on
`OnAbilityEnd`.
