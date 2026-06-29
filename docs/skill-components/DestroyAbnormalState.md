# DestroyAbnormalState

Removes abnormal states recorded by `ApplyAbnormalState`.

**Registered as:** `DestroyAbnormalState`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `inputTarget` | String | `""` | Blackboard `List<Entity>` key. |
| `inputState` | String | `""` | Parallel Blackboard `List<int>` abnormal-state key. |

Both keys are required. The component walks the lists in parallel, calls
`TryRemoveAbnormalState` for valid types `0..3`, then removes both Blackboard
keys. It is normally triggered on `OnAbilityEnd`.

The underlying abnormal-state system has no per-source handle, so overlapping
applications of the same type cannot be restored independently.
