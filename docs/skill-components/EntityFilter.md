# EntityFilter

Filters entity lists with OR groups containing AND conditions, matching the
shape of `ConditionConfig.groups`. Two operating modes: the default
`attackCandidates` subscribes to entities' `AttackBase.OnBeforeTargetSelect`
and filters the candidate list produced by `AttackBase.AttackTargetSelect`;
`list` filters the Blackboard `List<Entity>` at `blackboardKey` in place.

**Canonical op:** `filter_targets`
**Component registration:** `EntityFilter`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `mode` | String | `attackCandidates` | `attackCandidates` (subscribe to target selection) or `list` (filter a Blackboard list in place). |
| `toSelf` | Bool | `True` | `attackCandidates` mode: filter target selection for `ctx.entity`. If false, read entities from `blackboardKey`. |
| `blackboardKey` | String | `""` | `attackCandidates` + `toSelf=false`: input `List<Entity>` whose entities get the subscription. `list` mode: the `List<Entity>` key to filter in place. |
| `fields` | StringCsv | `""` | Entity fields, one per condition. |
| `ops` | StringCsv | `""` | Comparison operations, one per condition. |
| `values` | StringCsv | `""` | Comparison values, one per condition. Numeric fields parse their value as float (invariant culture); string fields compare literally. |
| `groups` | IntCsv | `""` | Group id per condition. Conditions sharing an id are ANDed; distinct ids are ORed. |

All four condition arrays are parallel. Only entries up to the shortest array
length are evaluated. With no complete conditions, the list is left
unchanged.

## Supported fields

- `MonsterStatus`
- `Camp`
- `CurrentHp`
- `CurrentHpRate`
- `MaxHp`
- `IdN` — `EntityData.ID.ID_N` (numeric)
- `IdC` — `EntityData.ID.ID_C` (string; `Equal`/`NotEqual` only — ordering ops
  warn once and fail the condition)

`IdC` + `IdN` together identify an entity kind, e.g. `t,1` = the `t/1`
entity (LavaBubble).

## Supported operations

- `None`
- `Equal` / `Eq`
- `NotEqual` / `Ne`
- `Greater` / `Gt`
- `GreaterOrEqual` / `Ge`
- `Less` / `Lt`
- `LessOrEqual` / `Le`

Unknown fields or operations make that condition fail and emit a one-shot
warning. A numeric field whose value does not parse as a number (invariant
culture) fails the same way.

## Lifecycle

`attackCandidates` mode:

- On a non-`OnAbilityEnd` trigger, subscribe to selected entities'
  `AttackBase.OnBeforeTargetSelect`.
- On `OnAbilityEnd`, remove all subscriptions created by this component.
- On teardown, remove subscriptions defensively.

For a temporary skill, use one rule with `OnAbilityBegin` and `OnAbilityEnd`
trigger entries and one `filter_targets` step. Put `force_reset_attack` after
it when the filtered target set must take effect immediately on both events.

`list` mode is stateless: each trigger filters the Blackboard list at
`blackboardKey` synchronously (typical chain: `select_targets`
`outputEntitiesKey` → `filter_targets` mode `list`). A missing key or a
non-list value at the key warns once and skips.

## Example

Accept elite or leader monsters:

```text
fields = MonsterStatus,MonsterStatus
ops    = GreaterOrEqual,LessOrEqual
values = 1,2
groups = 0,0
```

Keep only `t/1` entities (e.g. LavaBubbles) in a selected list:

```text
mode         = list
blackboardKey = my_bubbles
fields = IdC,IdN
ops    = Equal,Equal
values = t,1
groups = 0,0
```
