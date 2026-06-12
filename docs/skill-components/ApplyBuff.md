# ApplyBuff

Applies one or more buffs to one or more target entities. Two target-selection
modes, three parameter CSV sets, and optional blackboard output for chained
consumption (typically by `DestroyBuff`).

**Registered as:** `ApplyBuff`
**Class:** `AbilitySystem.Components.ApplyBuff`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/ApplyBuff.cs`

## Target selection

- **`blackboardKey` empty** → single-target. With `toSelf=true` the target is
  `ctx.entity`; with `toSelf=false` the target is the entity carried by the
  current event payload (or `ctx.entity` if the event carries none).
- **`blackboardKey` set** → multi-target. The component reads
  `List<Entity>` from the configured blackboard key and applies the buff to
  each entry. If the key is missing/empty on read, the trigger is silently
  skipped (an upstream writer hasn't run yet).

## BlackBoard output (optional)

When **both** `outputTarget` and `outputBuff` are set, the component appends
this round's `targets` (those with a `buffController`) and the corresponding
created `Buff` objects to the per-Entity shared blackboard at those keys.
Lists are accumulated across `OnTrigger` calls within the same skill window.
A null `Buff` slot is padded for targets where `CreateBuff` returned null —
the index alignment between target list and buff list is preserved so
downstream consumers (e.g. `DestroyBuff`) can walk them in parallel.

The writer/reader pair is symmetric: `ApplyBuff` writes only when both
output keys are set; `DestroyBuff` reads only when both input keys are set.

## Parameters

All parameters are read via the `ParamList` lazy API (see README → Storage);
each `Func<T>` re-evaluates the source on every call.

| Key | Type | Default | Description |
|---|---|---|---|
| `buffTypes` | BuffTypeCsv | `""` | Buff types to apply, comma-separated `BuffType` enum names (e.g. `AtkSpeed,Bleed`). Empty = trigger is a no-op. |
| `buffValues` | FloatCsv | `""` | Per-BuffType value, same order as `buffTypes` (e.g. `0.5,1.0`). |
| `buffId` | String | `skill_buff` | Unique id passed to `BuffController.CreateBuff`. Re-using an id replaces the prior buff. |
| `buffTime` | Float | `-10` | Duration in seconds. **Negative = permanent** (project convention: `-10` = permanent). |
| `toSelf` | Bool | `True` | `true` = apply to `ctx.entity`; `false` = apply to the target carried by the current event. Ignored when `blackboardKey` is set. |
| `isWhiteList` | Bool | `False` | Whitelist (`true`) vs blacklist (`false`) semantics in `BuffController.CreateBuff`. |
| `blackboardKey` | String | `""` | If set, read `List<Entity>` from this blackboard key and apply to each. Empty = single-target mode. |
| `outputTarget` | String | `""` | If set (with `outputBuff`), append this round's target list to the blackboard at this key. |
| `outputBuff` | String | `""` | If set (with `outputTarget`), append this round's buff list to the blackboard at this key. |

## Known limitations

- No auto-destroy on ability end. The historical `endOnSkillEnd` flag is
  documented in the class comment but not implemented in `OnTrigger`/`OnTeardown`.
  To destroy buffs at a specific point, pair with `DestroyBuff` and configure
  the latter on the desired `OnAbilityEnd` trigger.

## Changelog

- 2026-06-12: migrated to `ParamList.GetXxxLazy` (per-call blackboard reads);
  added `outputTarget` / `outputBuff` keys; removed `endOnSkillEnd` (not wired).
