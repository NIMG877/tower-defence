# EntitySelector

Selects entities relative to one or more subjects, merges and deduplicates the
results, then overwrites optional Blackboard entity-list and count outputs.

**Canonical op:** `select_targets`
**Component registration:** `EntitySelector`
**Class:** `AbilitySystem.Components.EntitySelector`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/EntitySelector.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `subjectMode` | String | `self` | Subject source: `self`, `eventTarget`, or `blackboard` (alias `blackboardEntities`). Any other value logs an error and selects nothing. |
| `subjectBlackboardKey` | String | `""` | Input `List<Entity>` key used by `subjectMode=blackboard`. |
| `selectionMode` | String | `radius` | Selection method: `vision`, `radius`, `ring`, `range`, or `all`. Any other value logs an error and selects nothing. |
| `campRelation` | String | `opposing` | Select entities of the `same`, `opposing`, or `both` camp relative to each subject. Used by `vision`, `radius`, `ring`, `range`, and `all`. |
| `radius` | Float | `1` | Radius passed to `EntitySelector_Radius`. |
| `minRadius` | Float | `0` | Inner radius used only by `selectionMode=ring`; negative values are treated as `0`. |
| `squareLength` | Float | `1` | Square half-length passed to `EntitySelector_Range`. |
| `force` | Bool | `False` | Passed through to the EntityManager selector methods. |
| `excludeSubjects` | Bool | `False` | Remove every resolved subject from the merged result. |
| `outputEntitiesKey` | String | `""` | Optional output key overwritten with `List<Entity>`. |
| `outputCountKey` | String | `""` | Optional output key overwritten with the deduplicated result count as a numeric string. |

`eventTarget` is available only on events derived from `DamageEventBase`.
`range` uses each subject's calculated `Vision.Range`; a subject with a null
range contributes no entities. `vision` reads the subject's current cached
vision lists.

`ring` first performs the same outer-radius selection as `radius`, then keeps
entities whose distance from the subject is strictly greater than
`minRadius`. Its effective interval is `minRadius < distance <= radius`.
A non-positive `minRadius` (the default `0` included) skips the inner cut
entirely, so the selection degenerates to the plain `radius` result and an
entity at the subject's center is included.
When `minRadius` is greater than a non-negative `radius`, the result is empty.

`all` returns every on-field entity of the `campRelation` camp (selectability
rules apply unless `force=true`). It routes through `EntitySelector_Radius`
with `radius<0` (the existing whole-field convention — no distance test).
The subject serves only as the camp anchor, so `all` also works in detached
executions via the snapshot camp.
Typical use: "re-collect every entity of a kind currently on the field" at
OnInitialize, paired with a `filter_targets` (mode `list`) step that narrows
the result by identity.

## Blackboard behavior

Every trigger creates a fresh result. Existing values at configured output
keys are removed and replaced, including when the new result is empty. This
makes the component suitable for per-event condition checks.

The count is stored as a numeric string because `ConditionEvaluator` reads
Blackboard operands as strings and parses numeric comparisons from them.

## Detached executions (ctx.entity == null)

`subjectMode=self` mirrors `spawn_entity`'s `positionMode=self` convention:
with no live entity, the subject degrades to the fork snapshot's
`(position, camp)` captured at trigger time. Selections that need only the
snapshot (`radius`, `ring`, `all`) are usable; `vision` and `range` need a
live entity and log an error in detached executions. Null entries inside a
`subjectMode=blackboard` list are skipped.

Typical shape: a summon's posthumous rule (`OnBeforeDieAnimation`, detached)
selects around its death position after a `delay` — the entity may be
pool-recycled by then, which is exactly why the anchor comes from the
snapshot.

## Rule triggers

Use any event appropriate to the selection. For per-hit checks, use
`OnAfterTakeDamage` or `OnBeforeTakeDamage` with `subjectMode=eventTarget`.
