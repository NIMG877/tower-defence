# SelectLandingPoints

Picks `count` landing points for carrier bullets with a random pipeline
(2026-08-29 revision; replaced the first-version coverage-optimizing search):

1. **Enemies first** — all enemies in attack range (`Vision.NearbyMonsters`,
   the same per-frame list targeting uses), minus an optional exclusion
   roster (`excludeKey` — Eyjafjalla passes her bubble roster). Randomly
   take up to `count` distinct enemies at their exact positions (no
   offset).
2. **Fill from cells** — if fewer than `count`: pick distinct cells from
   `Vision.Range`. Ground tier is consumed before highland tier
   (`Tile.highland`); within a tier the order is random. Each chosen cell
   becomes cell center + ±`offset` random offset on both axes.
3. **Cycle with repetition** — once both distinct pools are exhausted,
   start from enemies again (enemy ↔ cell alternating draws, repetition
   allowed, empty pools skipped).

**Canonical op:** `select_landing_points`
**Component registration:** `SelectLandingPoints`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `count` | Int | `4` | Number of points to select. |
| `offset` | Float | `0.24` | Per-axis random offset added to cell-sourced points (enemy-sourced points keep the enemy's exact position). |
| `excludeKey` | String | `""` | Blackboard `List<Entity>` removed from the enemy pool (e.g. the lava-bubble roster — bubbles are camp-opposing and would otherwise count as "enemies"). |
| `outputKey` | String | `""` | Writes `List<Vector2>` world positions. |

## Semantics

- Points are **not** constrained to land inside the attack range as a
  design rule: enemy positions are taken where the enemy stands (in-range
  at trigger time), and cell points drift up to `offset` off the cell
  center. The sources are in-range by construction; no extra filtering.
- Enemy positions are snapshotted at trigger time — movement during
  bullet flight is not tracked.
- Randomness is injected as `Func<float>` ([0,1]); runtime uses
  `RandomHelper.Helper.RandomF()` (shared cycling array), so multiple
  draws in one cast are independent.
- Config errors (radius-based vision with no cell range, missing map
  manager, no cells and no enemies at all) log an error and skip.

## Tests

`Assets/Tests/Editor/AbilitySystem/SelectLandingPointsTests.cs` covers the
pure pick function (enemies-first exact positions, ground-before-platform
fill, signed offset mapping, exhaustion cycling with repetition, config
errors) plus the `ExtractEnemyPositions` exclusion glue.
