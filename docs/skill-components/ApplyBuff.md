# ApplyBuff

Applies buffs to one or more entities. When `blackboardKey` is empty,
behaves as single-target (self or event target). When `blackboardKey`
is set, reads a `List<Entity>` from that key and applies the buff to
each entry.

When `endOnSkillEnd` is `true`, every buff this component creates is
tracked and destroyed on `SkillEndEvent` (or on `OnTeardown` if the
skill never ends cleanly, e.g. pool dormancy mid-skill). The config
**must** also declare `OnSkillEnd` in its `triggers[]`, because the
dispatcher only routes events that have a matching trigger bucket.

**Registered as:** `ApplyBuff`
**Class:** `SkillSystem.Components.ApplyBuff`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/ApplyBuff.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `buffTypes` | BuffTypeCsv | `""` | Buff types to apply. One entry per chip in the Inspector. Comma-separated in the raw form (e.g. `AtkSpeed,Bleed`). |
| `buffValues` | FloatCsv | `""` | Per-BuffType value, same order as `buffTypes` (e.g. `0.5,1.0`). |
| `buffId` | String | `skill_buff` | Unique id passed to `BuffController.CreateBuff`. Re-using an id replaces the prior buff. |
| `buffTime` | Float | `-10` | Duration in seconds. **Negative = permanent** (project convention: `-10` = permanent). |
| `toSelf` | Bool | `True` | `true` = apply to `ctx.entity`; `false` = apply to the target carried by the current event. |
| `blackboardKey` | BlackboardKey | `""` | If set, read `List<Entity>` from this blackboard key and apply to each. Empty = single-target. |
| `endOnSkillEnd` | Bool | `True` | Destroy created buffs on `SkillEndEvent` or `OnTeardown`. Config must declare `OnSkillEnd` in `triggers[]`. |
| `isWhiteList` | Bool | `False` | Whitelist (`true`) vs blacklist (`false`) semantics in `BuffController.CreateBuff`. |
