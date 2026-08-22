# DestroyBuff

Destroys buffs that a prior step (typically `apply_buff`) wrote to the
per-Entity shared blackboard. Reads two parallel lists from
`ctx.sharedBlackboard` at the configured keys:

- `inputTarget` → `List<Entity>` (one entry per buff to destroy)
- `inputBuff` → `List<Buff>` (the actual buff to destroy)

The two lists are walked in parallel — index `i` in each list is the pair.

**Canonical op:** `destroy_buff`
**Component registration:** `DestroyBuff`
**Class:** `AbilitySystem.Components.DestroyBuff`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/DestroyBuff.cs`

## Gating

Both input keys MUST be set; if either is empty the operation is a no-op.
`apply_buff` writes pairs only when both output keys are set;
`destroy_buff` reads pairs only when both input keys are set.

After consuming, both blackboard keys are removed. This matches `ApplyBuff`'s
"this round" semantics: the lists are a per-round handoff, and leaving them
in place would risk double-destroy on a re-trigger.

## Skips

Null/empty list entries are skipped (entity without `buffController`, or
a null `Buff` slot from a failed `CreateBuff`). The null-pad contract from
`ApplyBuff`'s `outputTarget` / `outputBuff` writers is preserved through
the read.

## Parameters

All parameters are read via the `ParamList` lazy API; each `Func<string>`
re-evaluates the source on every call.

| Key | Type | Default | Description |
|---|---|---|---|
| `inputTarget` | String | `""` | BlackBoard key to read `List<Entity>` from. Empty = operation is a no-op. |
| `inputBuff` | String | `""` | BlackBoard key to read `List<Buff>` from. Empty = operation is a no-op. |
