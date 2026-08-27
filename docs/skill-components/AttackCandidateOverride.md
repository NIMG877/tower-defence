# AttackCandidateOverride

Replaces the **entire** live attack-candidate list with a Blackboard entity
list: the event's candidate list is cleared, then refilled from the source
(`Clear` + `AddRange` — `AttackBase` keeps using the same list object, so no
writeback is needed). Overriding with an empty list is legitimate semantics
(e.g. "only elites while the skill is on" with no elites on the field → this
selection yields no targets). Per-unit by construction: each entity's
`OnBeforeTargetSelect` is bridged to its own runner.

**Canonical op:** `attack_candidate_override`
**Component registration:** `AttackCandidateOverride`
**Class:** `AbilitySystem.Components.AttackCandidateOverride`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/AttackCandidateOverride.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `blackboardKey` | String | `""` | Blackboard key of the `List<Entity>` to override with. |

## Usage

Wire the rule's trigger to `OnBeforeTargetSelect`. This component is the
**commit step** of the attack-preference pipeline — the only step that
touches the live candidate list:

```text
triggers = OnBeforeTargetSelect
steps    = write_blackboard      { key = attackCandidates, source = event, path = targets }  # snapshot copy
           filter_targets        { blackboardKey = attackCandidates, fields = ..., ... }     # filter the copy
           attack_candidate_override { blackboardKey = attackCandidates }                   # commit to live
```

`write_blackboard`'s `event.targets` path writes a copy, so the pipeline is
read → transform → commit with a single mutation point. A source key holding
no list (pipeline missing its `write_blackboard` step) or a rule not
triggering on `OnBeforeTargetSelect` warns once and skips — the wiring error
is exposed, not masked.

For preference semantics stricter than filtering (e.g. "always attack the
bubbles first"), feed a list built by `select_targets`/`filter_targets` into
`blackboardKey` instead of the snapshot: override then means "only these
targets".
