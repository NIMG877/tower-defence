# EntitySelector

Selects entities relative to one or more subjects, merges and deduplicates the
results, then overwrites optional Blackboard entity-list and count outputs.

**Registered as:** `EntitySelector`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `subjectMode` | String | `self` | Subject source: `self`, `eventTarget`, or `blackboard`. |
| `subjectBlackboardKey` | String | `""` | Input `List<Entity>` key used by `subjectMode=blackboard`. |
| `selectionMode` | String | `radius` | Selection method: `subject`, `eventTarget`, `vision`, `radius`, or `range`. |
| `campRelation` | String | `opposing` | Select entities of the `same`, `opposing`, or `both` camp relative to each subject. Used by `vision`, `radius`, and `range`. |
| `radius` | Float | `1` | Radius passed to `EntitySelector_Radius`. |
| `squareLength` | Float | `1` | Square half-length passed to `EntitySelector_Range`. |
| `force` | Bool | `False` | Passed through to the EntityManager selector methods. |
| `excludeSubjects` | Bool | `False` | Remove every resolved subject from the merged result. |
| `outputEntitiesKey` | String | `""` | Optional output key overwritten with `List<Entity>`. |
| `outputCountKey` | String | `""` | Optional output key overwritten with the deduplicated result count as a numeric string. |

`eventTarget` is available only on events derived from `DamageEventBase`.
`range` uses each subject's calculated `Vision.Range`; a subject with a null
range contributes no entities. `vision` reads the subject's current cached
vision lists.

## Blackboard behavior

Every trigger creates a fresh result. Existing values at configured output
keys are removed and replaced, including when the new result is empty. This
makes the component suitable for per-event condition checks.

The count is stored as a numeric string because `ConditionEvaluator` reads
Blackboard operands as strings and parses numeric comparisons from them.

## Recommended triggers

Use any event appropriate to the selection. For per-hit checks, use
`OnAfterTakeDamage` or `OnBeforeTakeDamage` with `subjectMode=eventTarget`.
