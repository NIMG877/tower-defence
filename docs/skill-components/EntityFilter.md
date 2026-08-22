# EntityFilter

Filters the candidate list produced by `AttackBase.AttackTargetSelect`.
Conditions are organized as OR groups containing AND conditions, matching the
shape of `ConditionConfig.groups`.

**Canonical op:** `filter_targets`
**Component registration:** `EntityFilter`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `toSelf` | Bool | `True` | Filter target selection for `ctx.entity`. If false, read entities from `blackboardKey`. |
| `blackboardKey` | String | `""` | Input `List<Entity>` key used when `toSelf=false`. |
| `fields` | StringCsv | `""` | Candidate entity fields, one per condition. |
| `ops` | StringCsv | `""` | Comparison operations, one per condition. |
| `values` | FloatCsv | `""` | Comparison values, one per condition. |
| `groups` | IntCsv | `""` | Group id per condition. Conditions sharing an id are ANDed; distinct ids are ORed. |

All four condition arrays are parallel. Only entries up to the shortest array
length are evaluated. With no complete conditions, the candidate list is left
unchanged.

## Supported fields

- `MonsterStatus`
- `Camp`
- `CurrentHp`
- `CurrentHpRate`
- `MaxHp`

## Supported operations

- `None`
- `Equal` / `Eq`
- `NotEqual` / `Ne`
- `Greater` / `Gt`
- `GreaterOrEqual` / `Ge`
- `Less` / `Lt`
- `LessOrEqual` / `Le`

Unknown fields or operations make that condition fail and emit a one-shot
warning.

## Lifecycle

- On a non-`OnAbilityEnd` trigger, subscribe to selected entities'
  `AttackBase.OnBeforeTargetSelect`.
- On `OnAbilityEnd`, remove all subscriptions created by this component.
- On teardown, remove subscriptions defensively.

For a temporary skill, use one rule with `OnAbilityBegin` and `OnAbilityEnd`
trigger entries and one `filter_targets` step. Put `force_reset_attack` after
it when the filtered target set must take effect immediately on both events.

## Example

Accept elite or leader monsters:

```text
fields = MonsterStatus,MonsterStatus
ops    = GreaterOrEqual,LessOrEqual
values = 1,2
groups = 0,0
```
