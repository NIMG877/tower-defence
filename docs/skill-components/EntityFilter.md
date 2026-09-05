# EntityFilter

Filters the entity list at a Blackboard key in place with OR groups containing
AND conditions, matching the shape of `ConditionConfig.groups`. The component
is attack-system-agnostic: any `List<Entity>` key works. Two typical sources:

- a list written by `select_targets`' `outputEntitiesKey` (chained filtering);
- the `attackCandidates` working copy during `BeforeTargetSelectEvent`
  dispatch (attack preference — see "Attack preference wiring" below).

**Canonical op:** `filter_targets`
**Component registration:** `EntityFilter`
**Class:** `AbilitySystem.Components.EntityFilter`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/EntityFilter.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `blackboardKey` | String | `""` | The `List<Entity>` key to filter in place. For attack preference use the conventional key `attackCandidates`. |
| `fields` | StringCsv | *(empty)* | Entity fields, one per condition. |
| `ops` | StringCsv | *(empty)* | Comparison operations, one per condition. |
| `values` | StringCsv | *(empty)* | Comparison values, one per condition. Numeric fields parse their value as float (invariant culture); string fields compare trimmed and case-insensitively. |
| `groups` | IntCsv | *(empty)* | Group id per condition. Conditions sharing an id are ANDed; distinct ids are ORed. |

All four condition arrays are parallel. Only entries up to the shortest array
length are evaluated. With no complete conditions, the list is left
unchanged. Null entries in the list are removed.

## Attack preference wiring

Attack preference is a three-step pipeline. Timing comes from the event
system, data from the blackboard, and the only mutation of the live candidate
list happens in the final commit step:

```text
triggers = OnBeforeTargetSelect
steps    = write_blackboard      { key = attackCandidates, source = event, path = targets }  # snapshot copy
           filter_targets        { blackboardKey = attackCandidates, fields = ..., ... }     # filter the copy
           attack_candidate_override { blackboardKey = attackCandidates }                   # commit back to live
```

`write_blackboard` extracts a **copy** of the event's candidate list, so the
blackboard never aliases the attack system's live list; `filter_targets`
removes non-matching entities from that copy; `attack_candidate_override`
clears and refills the live list from the result. Omitting the first step
leaves `attackCandidates` holding a stale snapshot from an earlier selection —
skipping either end of the pipeline warns once and skips, exposing the wiring
error. Skill scoping comes free from the dispatch-side `isActive` gate: an
inactive skill's rules never receive the event, so a temporary skill needs a
single trigger (no subscribe/unsubscribe pair). Pair with
`force_reset_attack` after engaging when the filtered target set must take
effect immediately.

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
culture) fails the same way. A missing key or a non-list value at the key
warns once and skips.

## Example

Accept elite or leader monsters:

```text
blackboardKey = attackCandidates
fields = MonsterStatus,MonsterStatus
ops    = GreaterOrEqual,LessOrEqual
values = 1,2
groups = 0,0
```

Keep only `t/1` entities (e.g. LavaBubbles) in a selected list:

```text
blackboardKey = my_bubbles
fields = IdC,IdN
ops    = Equal,Equal
values = t,1
groups = 0,0
```
