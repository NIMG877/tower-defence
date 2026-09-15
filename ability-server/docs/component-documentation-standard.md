# Component Documentation Standard

## Purpose

Every component exposes four authoring documents:

1. `summary`
2. `behavior`
3. `wiring`
4. `params[].desc`

Together they must let an AI Agent choose a component, predict its runtime result, compose it with other steps, and fill every parameter without reading source code.

Write for the ability author, not the component maintainer. State current, observable authoring semantics. Be complete, but make every sentence earn its place.

## The Four-Layer Contract

| Field | Agent question | Content | Budget |
|---|---|---|---|
| `summary` | Should I inspect this component? | Primary action and one selection-critical distinction. | One sentence; normally 12-30 words. |
| `behavior` | What happens when it runs? | Runtime effect, meaningful modes, lifetime, and material limits. | 2-5 short sentences or bullets. |
| `wiring` | How do I place it in a rule flow? | Required trigger, order, handoff, pairing, or cleanup. | 0-3 short bullets; use an arrow flow when useful. |
| `params[].desc` | How do I fill this parameter? | Meaning, activation condition, value interpretation, and material fallback. | One short sentence; two clauses only when needed. |

The reading order is deliberate: **select -> predict -> compose -> configure**. Each layer must add information that the previous layer does not contain.

## Shared Rules

- Use direct present-tense English and concrete verbs: selects, writes, removes, restores, spawns, consumes etc.
- Prefer game and authoring terms: target, event, rule, sequence, Blackboard, output, handle, teardown, restore etc.
- State an internal detail only when it changes what an author must do.
- Mark a constraint as **required** only when violating it makes the design wrong; otherwise say **optional** or omit it.
- Name an event, key, component, or token only when it is needed to author correctly.
- Put one fact in one primary field. Link to another field instead of restating it.
- Omit history, migration notes, implementation names, routine null checks, logs, and vague claims such as "works correctly."
- Keep literals in backticks. Use the canonical operation name and exact parameter token.

Structured metadata is authoritative for parameter name, type, default, allowed values, shape, and Blackboard role. Do not repeat it unless the meaning cannot be inferred from the structure - for example, a numeric enum, a unit, precedence, or a runtime fallback.

All examples below describe the same representative component.

## `summary`

### Include

- The primary action.
- The affected target, state, event, or data.
- At most one distinction that changes component selection: one-shot versus persistent, current-event-only, asynchronous, detached, or paired cleanup.

### Exclude

- Parameter names, parameter lists, defaults, and formats.
- A recipe, Blackboard handoff, or full lifecycle explanation.
- Implementation details and ordinary error handling.

### Template

```text
[Verb] [target or data] to [observable result]; [one selection-critical consequence].
```

Example:

```text
Selects and deduplicates entities by subject, spatial mode, and camp relation, then writes optional entity-list and count outputs.
```

## `behavior`

### Include

- The normal successful effect.
- Only modes or branches that materially change the effect.
- Timing, lifetime, accumulation, replacement, consumption, teardown, or restoration when relevant.
- Event-context or detached-execution limits that change where the component is valid.
- Material failure behavior only when an author must design around it.

### Exclude

- A parameter-by-parameter reference.
- The concrete predecessor/consumer sequence; put that in `wiring`.
- A list of implementation classes, parser behavior, logs, or defensive branches with no authoring consequence.

### Template

```text
[Normal effect]. [Meaningful mode or lifetime difference]. [Material limit or retained state, if any].
```

Example:

```text
Resolves subjects from the owner, a damage-event target, or the same entity's Blackboard, then unions and deduplicates matches and can exclude subjects. `radius`, `ring`, and `all` support detached-self snapshots, while `vision` and `range` require live subjects. Configured outputs replace prior values even when empty.
```

## `wiring`

### Include

- A required event phase or rule placement.
- A prerequisite step or event context.
- A producer -> consumer Blackboard handoff.
- For each Blackboard handoff, the producer, consumer, payload role, owner or scope, and required order.
- A required paired restore, cleanup, or completion step.
- One minimal canonical flow when more than one dependency is non-obvious.
- Write `None.` only when the component has no cross-step, cross-rule, or cleanup contract.

### Exclude

- A second explanation of the component's own effect.
- Optional variations, a parameter catalogue, or generic sequencing advice.
- Exact runtime types; describe the payload by role here and put its concrete type in the relevant parameter description.

### Template

```text
[producer.output] -> [consumer.input]; run the producer first.
```

Example:

```text
With `subjectMode=eventtarget`, use a damage-event trigger that exposes a target.
An earlier instance's `outputEntitiesKey` -> `subjectBlackboardKey`; pass the target list on the same entity Blackboard before this step.
`outputEntitiesKey` -> `apply_damage.blackboardKey`; pass the selected target list on the same entity Blackboard, then run damage.
```

If a component creates state that is not automatically removed, `wiring` must name the cleanup path and the event or lifecycle boundary that runs it.

## `params[].desc`

Every parameter must have a concise description. Its job is to make one `ParamEntry` authorable without guessing.

### Include when applicable

- What the value represents, including a unit, scale, or numeric-enum meaning that metadata does not express.
- When the parameter is read, ignored, overridden, or required.
- Its relationship to another parameter when that relationship changes valid configuration.
- The Blackboard value kind, ownership, or lifetime when a key alone is ambiguous.
- A missing or invalid-value result only when it changes the ability design.

### Exclude

- The parameter name, type, default, allowed-value list, CSV encoding, or `bbRole` when the structured metadata already says it.
- The component's general action, full lifecycle, or cross-step recipe.
- Routine conversion, logging, and implementation detail.

### Template

```text
[When it applies]; [what the value means, including unit or scale]; [material fallback or dependency].
```

Example:

| `key` | `desc` |
|---|---|
| `radius` | Outer world-space radius for `radius` and `ring`; `0` selects none, while a negative value removes the distance limit. |
| `subjectBlackboardKey` | With `subjectMode=blackboard`, names the owner Blackboard's input `List<Entity>`; empty or missing input yields no subjects. |
| `force` | In `radius`, `ring`, `range`, and `all`, bypasses selectable, isolation, and dormancy gates but never includes inactive entities; ignored by `vision`. |
| `outputCountKey` | Receives the deduplicated result count as a numeric string; an empty result writes `"0"`. |

## Boundary Rules

| Fact | Primary home |
|---|---|
| What the component does and why to choose it | `summary` |
| What changes at runtime, including modes and lifetime | `behavior` |
| Which event, producer, consumer, pairing, or cleanup makes composition correct | `wiring` |
| What one configured value means and when it matters | `params[].desc` |
| Name, type, default, allowed values, shape, and Blackboard role | Structured parameter metadata |
| Internal detail with no author-visible consequence | Nowhere |

When a fact crosses layers, state the semantic consequence once in `behavior` and the concrete flow once in `wiring`. Do not copy the same sentence.

## Completeness and Conciseness Review

Accept a component document only when all answers are yes:

1. Can an Agent choose the component from `summary` alone?
2. Can it predict the observable effect, timing, and lifetime from `behavior` without source code?
3. Can it place the component correctly from `wiring`, including every required handoff and cleanup?
4. Can it fill every parameter from metadata plus that parameter's `desc` without guessing units, conditions, or dependencies?
5. Is every required field non-empty, including `wiring`?
6. Is every fact stated once, in its proper layer?
7. Can any sentence be removed without losing an authoring decision? If yes, remove it.

When implementation changes, update the smallest affected layer first, then run this review from `summary` through `params[].desc`.
