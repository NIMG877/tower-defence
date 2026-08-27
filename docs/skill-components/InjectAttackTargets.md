# InjectAttackTargets

Injects a Blackboard entity list to the **front** of this unit's attack
target candidates: list members are removed from the candidate list first,
then inserted at index 0 — injected entities unconditionally get the highest
targeting priority, including entities outside vision range. The candidate
list is per-unit (each entity's `OnBeforeTargetSelect` is bridged to its own
runner), so the priority never leaks into other units' targeting.
Complements `EntityFilter`, which can only remove candidates, never add
them.

**Canonical op:** `inject_attack_targets`
**Component registration:** `InjectAttackTargets`
**Class:** `AbilitySystem.Components.InjectAttackTargets`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/InjectAttackTargets.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `blackboardKey` | String | `""` | Blackboard key of the `List<Entity>` to inject. Empty list = no-op injection. |

## Usage

Wire the rule's trigger to `OnBeforeTargetSelect`. The destination is the
reserved key `BlackboardKeys.AttackCandidates` (hardcoded — this component's
purpose is attack-candidate injection), which exists only during the
synchronous dispatch window of `BeforeTargetSelectEvent`. If the key is
missing (wrong trigger wired), the step warns once and skips — the wiring
error is exposed, not masked.

The list contents are read at every target selection, so roster changes
apply to the very next attack. Pair with `force_reset_attack` right after
engaging, so the unit re-targets immediately instead of finishing its
current attack cycle first.
