# PrependEntities

Prepends a Blackboard `List<Entity>` source list to the **front** of another
Blackboard entity list, in place (the destination list object is preserved —
downstream steps keep reading the same key). Deduplication is
**promote-to-front** semantics: source entities land at the front in source
order, and duplicates previously present in the destination are removed from
their old positions — not kept behind, not double-counted.

**Canonical op:** `prepend_entities`
**Component registration:** `PrependEntities`
**Class:** `AbilitySystem.Components.PrependEntities`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/PrependEntities.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `blackboardKey` | String | `""` | Destination list key (entities are inserted at its front). |
| `sourceKey` | String | `""` | Source list key (its entities, in order, become the new front). |

## Usage

Attack-system-agnostic list manipulation. Typical use inside the
attack-preference pipeline (trigger `OnBeforeTargetSelect`), between the
snapshot and the commit steps — e.g. "always attack the bubbles first":

```text
triggers = OnBeforeTargetSelect
steps    = write_blackboard       { key = attackCandidates, source = event, path = targets }  # snapshot copy
           prepend_entities       { blackboardKey = attackCandidates, sourceKey = bubbles }   # promote to front
           attack_candidate_override { blackboardKey = attackCandidates }                    # commit to live
```

Bubbles already inside the candidate list (in vision) are moved to the front
rather than duplicated; bubbles outside the candidate list are prepended
regardless — the event dispatch performs no range re-check, so the merged list
is what `AttackBase` will attack.

## Edge behavior

| Case | Behavior |
|---|---|
| Either key empty / holds no list | OneShotWarn + skip (wiring error exposed). |
| Null entries in the source | Skipped. |
| Duplicates within the source list | Only the first occurrence is inserted. |
| Null entries in the destination | Left untouched. |
| Empty source list | No-op (legitimate state). |
