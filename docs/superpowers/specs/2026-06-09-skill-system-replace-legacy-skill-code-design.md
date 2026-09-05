# Skill System Replace Legacy Skill Code — Design Spec
> 状态：已实施

**Date:** 2026-06-09
**Status:** Draft (post-brainstorming, pending user review)
**Scope:** Replace all UI / Entity code that reads the legacy `Skill` MonoBehaviour with the new `SkillSystem` data API. Keep `Skill.cs` file and prefab-mounted `Skill` components on disk; do **not** delete them.
**Supersedes:** No prior spec; complements [2026-06-07-skill-talent-refactor-design.md](2026-06-07-skill-talent-refactor-design.md) (which aimed to fully delete `Skill.cs` and all subclass scripts — out of scope for this spec).

---

## 1. Motivation

### 1.1 Current state

The new `SkillSystem` (under `Assets/PublicScripts/SkillSystem/` and `Assets/PublicScripts/GameData/SkillSystem/`) is fully built and drives combat: `SkillRunner` subscribes to `Entity` / `AttackBase` / `AnimationMachine` events; `SPEngine` is the SP state machine; `ISkillComponent` + factory are the effect library. Phase 2 migration (per prior `SkillRunner` commit `db3c8e5`) is complete.

`SpSliderController` has been migrated (per [2026-06-08-spslider-new-skill-system-adaptation-design.md](2026-06-08-spslider-new-skill-system-adaptation-design.md)).

**However, four UI files still read the legacy `Skill` MonoBehaviour** (now an orphaned type with no runtime writers):

| File | What it reads |
|---|---|
| `MyUI/Components/Cards.cs` | `SkillCard.UpdateSkillCardMessage(Skill skill)` — `SkillImg`, `SkillName`, `SkillDescription`, `SkillAmount`, `SpRecoverMode`, `SkillOpenMode`, `SpComsumeMode`, `TotalSp`, `InitialSp` |
| `MyUI/Panels/LevelMessagePanel.cs` | `_selectSkill : Skill`, `skill[0]`, `SkillCanBegin` / `SkillBegin` / `SkillEnd` / `SkillMessage` / `SPFull` / `SpComsumeMode` / `SkillAttackRange` |
| `MyUI/CharacterCardManager.cs` | `Prefab.GetComponents<Skill>()[0].SkillImg` |
| `MyUI/Panels/CharacterSelectPanel.cs` | `entityData.Prefab.GetComponents<Skill>()` (iterated) |
| `MyUI/Panels/MonsterHandbookPanel.cs` | `monsterData.Prefab.GetComponents<Skill>()` (iterated) |

`Entity.cs` carries a transitional bridge — `[HideInInspector] public Skill[] skill;` field, populated in `PreWarm()` by `this.skill = GetComponents<Skill>();` (comment-marked as Phase 2 transition patch). All UI readers are downstream of this field.

### 1.2 The legacy data is dead

- `Skill` instances on the prefab get `IPoolOperation.PreWarm` / `Initialize` / `Dormancy` from the `EntityPool` cascade.
- `Skill.Initialize` (line 210 of [Skill.cs](../../Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Skill.cs)) subscribes the legacy `Skill` to `Entity.AttackBase.OnAttackSuccessfully` / `Entity.OnAfterHurt` / `Entity.OnBeforeHurt` / `entityAM.OnAttackAnimationBegin` — but these subscriptions are **in parallel** with `SkillRunner` subscriptions; both receive every event.
- The legacy `Skill`'s SP state (`_currentSp`, `_currentChargeNum`, `_currentSkillAmount`) **is** updated, but the values are now duplicated / shadowed by `SPEngine`, and the UI's `_selectSkill` reference points at a `Skill` instance whose state machine semantics differ from the live one driving the combat effects.

In short: the legacy `Skill` array is **a no-op read replica of state that's already in `SkillRunner`**. Every UI site reading it either shows stale values, or in the worst case (`LevelMessagePanel` click-to-release) toggles a no-op state machine that has no effect on combat.

### 1.3 Goals

- **G1.** Zero references to the legacy `Skill` type or `Entity.skill[]` field in any C# code.
- **G2.** All UI skill display / interaction reads from `SkillSystem.SkillConfig` + `SkillSystem.SkillRuntime` + `SPEngine`.
- **G3.** `Skill.cs` file and prefab-mounted `Skill` components remain on disk (intentional — they are vestigial legacy data; deleting them is a separate task and would require prefab-level surgery).
- **G4.** No regression in observable behavior: skill icon, description, SP-state UI, manual release, manual close, range preview all continue to work for `EntityData.Skills` populated configurations.

### 1.4 Non-goals (YAGNI)

- Deleting `Skill.cs` or removing `Skill` components from prefabs.
- Migrating the `Talent[]` field, `Talent.cs`, or the talent half of `SwitchShowSkillTalent`.
- Filling in `SkillConfig.icon` / `SPConfig.skillAttackRange` values in existing `EntityData` assets (data fill is a follow-up; UI gracefully degrades to no-icon / no-range-preview when these are null).
- Multi-skill UI (selection card, multi-slider) — out of scope; we follow the old `skill[0]` / `Skills[0]` pattern.
- Behavioural refactor of `SwitchShowSkillTalent`'s talent path (talent remains dead code as before).

---

## 2. Architecture

### 2.1 Data flow

```
                                  ┌──────────────────────────────────────┐
                                  │ EntityData (ScriptableObject)        │
                                  │  Skills: List<SkillConfig>           │
                                  │     skillId, skillName, description  │
                                  │     icon ← NEW                       │
                                  │     sp: SPConfig                     │
                                  │       totalSp, initialSp, ...        │
                                  │       skillAttackRange ← NEW        │
                                  │     components: ComponentConfig[]    │
                                  └────────────┬─────────────────────────┘
                                               │ (PreWarm reads, builds)
                                               ▼
┌────────────────────────────┐    ┌──────────────────────────────────────┐
│ UI: character card /       │    │ Entity (MonoBehaviour)                │
│     select / handbook      │    │  SkillRunner (MonoBehaviour)          │
│                            │    │   _skills[i] : SkillRuntime           │
│   reads directly from      │    │     spEngine : SPEngine               │
│   entityData.Skills[i]     │    │     config : SkillConfig              │
│                            │    │     blackboard                        │
└────────────────────────────┘    └────────────┬─────────────────────────┘
                                               │ (TryGetComponent<SkillRunner>)
                                               ▼
                              ┌──────────────────────────────────────┐
                              │ UI: LevelMessagePanel                 │
                              │  deployed-operator skill display     │
                              │   _selectSkillRuntime = runner.Skills[0]
                              │   _selectSkillConfig = runtime.config │
                              │   spEngine.CanBegin/FireSkill/EndSkill│
                              └──────────────────────────────────────┘
```

### 2.2 Data-model additions

#### 2.2.1 `SkillConfig.icon` ([SkillConfig.cs](../../Assets/PublicScripts/GameData/SkillSystem/SkillConfig.cs))

```csharp
[Tooltip("技能图标，UI 上技能卡 / 按钮 / 范围预览使用。")]
public Sprite icon;
```

#### 2.2.2 `SPConfig.skillAttackRange` ([SPConfig.cs](../../Assets/PublicScripts/GameData/SkillSystem/SPConfig.cs))

```csharp
[Tooltip("技能激活时改写的攻击范围（相对当前 Vision.Range 的覆盖）。" +
         "空表示不覆盖。")]
public Vector2Int[] skillAttackRange;
```

> This field was planned in [2026-06-07-skill-talent-refactor-design.md §3.2](2026-06-07-skill-talent-refactor-design.md) but never landed in code. This spec is the natural place to add it. The field is **read-only by UI for range preview**. The actual "fire skill → rewrite `Entity.Vision.Range`" behaviour in combat is owned by the existing `AttackRangeOverrideComponent` (parameters live in `ParamList`, not on `SPConfig`) — the two coexist without conflict.

### 2.3 Entity layer

`Entity.skill[]` field and its `GetComponents<Skill>()` bridge in `PreWarm` are removed. `Entity.Talents[]` stays (out of scope). `SkillRunner` lifecycle hooks at lines 208-210 / 232-234 / 255-257 stay.

### 2.4 UI layer

| File | New read path |
|---|---|
| `Cards.cs` | `SkillCard.UpdateSkillCardMessage(SkillConfig config, SkillRuntime runtime = null)` |
| `LevelMessagePanel.cs` | `_selectSkillConfig : SkillConfig` + `_selectSkillRuntime : SkillRuntime`; clicks call `spEngine.CanBegin / FireSkill / EndSkill`; live UI reads from `spEngine.CurrentSp / IsActive / CurrentDuration` + `config.sp.*` |
| `CharacterCardManager.cs` | `entityData.Skills[0].icon` (with null-guard) |
| `CharacterSelectPanel.cs` | `entityData.Skills` (List iteration) |
| `MonsterHandbookPanel.cs` | `monsterData.Skills` (List iteration) |

---

## 3. Component-Level Design

### 3.1 `Entity.cs` — remove field and bridge

Remove:

- Line 24: `[HideInInspector] public Skill[] skill;`
- Lines 205-206:
  ```csharp
  // === 旧 Skill[] 填充（与 SkillSystem 并存期，过渡给 LevelMessagePanel 的旧 UI 读 _selectSkill） ===
  this.skill = GetComponents<Skill>();
  ```

Keep:

- `Talent[]` field (out of scope).
- `SkillSystem.SkillRunner` lifecycle hook lines (208-210, 232-234, 255-257).
- All other Entity refactoring from prior work.

### 3.2 `Cards.cs` — `SkillCard` rewires to `SkillConfig`

New signature:

```csharp
public void UpdateSkillCardMessage(SkillConfig config, SkillRuntime runtime = null)
```

`runtime` is reserved for future live-state display (e.g. SP progress bar) and is **unused in this spec**.

Field mapping:

| Old (`Skill`) | New (`SkillConfig`) |
|---|---|
| `skill.SkillImg` | `config.icon` (null → hide `skillImage`) |
| `skill.SkillName` | `config.skillName` |
| `skill.SkillDescription` | `config.description` |
| `skill.SkillAmount.ToString()` | `config.sp.skillDuration.ToString("0.#")` (hide row if `skillDuration <= 0`) |
| `skill.SpRecoverMode` (int 0/1/2) | `config.sp.recoverMode` (enum) |
| `skill.SkillOpenMode` (int 0/1/2/3/4) | `config.sp.openMode` (enum) |
| `skill.SpComsumeMode` (int) | `config.sp.consumeMode` (enum) |
| `skill.TotalSp` | `config.sp.totalSp` (hide row if `totalSp <= 0`) |
| `skill.InitialSp` | `config.sp.initialSp` (hide row if `initialSp <= 0`) |

Chinese label map (replaces current garbled switch labels):

| enum | label |
|---|---|
| `SpRecoverMode.Natural` | `自动回复` |
| `SpRecoverMode.OnAttackHit` | `攻击回复` |
| `SpRecoverMode.OnAfterHurt` | `受击回复` |
| `SpConsumeMode.Natural` | `持续消耗` |
| `SpConsumeMode.OnAttackHit` | `命中消耗` |
| `SpConsumeMode.OnAfterHurt` | `受击消耗` |
| `SpConsumeMode.Instant` | `瞬发` |
| `SkillOpenMode.Auto` | `自动触发` |
| `SkillOpenMode.OnAttackAnimBegin` | `攻击时触发` |
| `SkillOpenMode.OnBeforeHurt` | `受击时触发` |
| `SkillOpenMode.Manual` | `手动触发` |
| `SkillOpenMode.OnAttackHit` | `命中触发` |

`Image` null-guard: when `config.icon == null`, set `skillImage.enabled = false` (or assign a placeholder sprite via a public field if the design later wants one; for this spec, hide the image).

### 3.3 `LevelMessagePanel.cs` — full migration

#### 3.3.1 Field split ([L305](../../Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs#L305))

```csharp
// 旧
private Skill _selectSkill;
// 新
private SkillSystem.SkillConfig _selectSkillConfig;
private SkillSystem.SkillRuntime _selectSkillRuntime;
```

#### 3.3.2 `UIStates_ShowClose_Operator(true)` — load skill on operator-panel open

Locate the block at L1162-1175 (the `if (_selectedEntity.skill != null && _selectedEntity.skill.Length > 0)` branch). Replace with:

```csharp
SkillSystem.SkillRunner runner;
SkillSystem.SkillConfig cfg = null;
SkillSystem.SkillRuntime rt = null;
if (_selectedEntity != null
    && _selectedEntity.TryGetComponent<SkillSystem.SkillRunner>(out runner)
    && runner.Skills != null
    && runner.Skills.Count > 0)
{
    rt = runner.Skills[0];
    cfg = rt.config;
    _selectSkillRuntime = rt;
    _selectSkillConfig = cfg;
    _skillOpen.gameObject.SetActive(true);
    _skillOpen.sprite = cfg.icon;
    var range = cfg.sp != null ? cfg.sp.skillAttackRange : null;
    _skillRange.gameObject.SetActive(range != null && range.Length > 0);
}
else
{
    _selectSkillRuntime = null;
    _selectSkillConfig = null;
    _skillOpen.gameObject.SetActive(false);
    _skillRange.gameObject.SetActive(false);
}
```

#### 3.3.3 `InitOperatorPanel` — release / close click handlers

L522-545. The release button:

```csharp
// 旧
if (_selectSkill.SkillCanBegin()) { _selectSkill.SkillBegin(); UIStates_SwitchTo_Normal(); }
// 新
var sp = _selectSkillRuntime?.spEngine;
if (sp != null && sp.CanBegin()) { sp.FireSkill(); UIStates_SwitchTo_Normal(); }
```

The close button:

```csharp
// 旧
_selectSkill.SkillEnd();
// 新
_selectSkillRuntime?.spEngine?.EndSkill();
```

#### 3.3.4 `UIStates_Update_Operator` — live state reads

L1189-1283. Field substitution (UI display logic unchanged):

| Old | New |
|---|---|
| `_selectSkill.SkillMessage.currentSpRate` | `runtime.spEngine.CurrentSp / runtime.config.sp.totalSp` |
| `_selectSkill.SkillMessage.currentChargeNum` | `runtime.spEngine.CurrentCharge` |
| `_selectSkill.SkillMessage.isSkill` | `runtime.spEngine.IsActive` |
| `_selectSkill.SPFull()` | `runtime.spEngine.CurrentSp >= runtime.config.sp.totalSp \|\| runtime.spEngine.CurrentCharge >= runtime.config.sp.chargeNum` |
| `_selectSkill.SpComsumeMode` | `runtime.config.sp.consumeMode` |
| `_selectSkill.SkillAmount` | `runtime.config.sp.skillDuration` |

Top of the method: guard

```csharp
if (_selectSkillRuntime == null || _selectSkillRuntime.spEngine == null || _selectSkillConfig == null)
    return;
```

#### 3.3.5 `SwitchShowSkillTalent` — revive skill branch with new API

L1650-1684. The commented `// _skillCard.UpdateSkillCardMessage(entity.skill[0]);` becomes:

```csharp
if (entityData.Skills != null && entityData.Skills.Count > 0)
{
    _skillCard.UpdateSkillCardMessage(entityData.Skills[0]);
}
```

Talent branch (`TalentCard` etc.) remains dead / commented.

#### 3.3.6 `UIStates_ShowClose_Operator(false)` — clear

When the panel closes, set `_selectSkillConfig = null; _selectSkillRuntime = null;` so a subsequent open starts from a clean slate.

### 3.4 `CharacterCardManager.cs` — character card icon

L118. Replace:

```csharp
// 旧
Skill[] skills = characterData.Prefab.GetComponents<Skill>();
if (skills.Length > 0) { skillImg.sprite = skills[0].SkillImg; }
else { skillImg.sprite = _noneSkill; }
// 新
var skills = characterData.Skills;
if (skills != null && skills.Count > 0 && skills[0].icon != null) skillImg.sprite = skills[0].icon;
else skillImg.sprite = _noneSkill;
```

The commented skill-icon logic in `InstantiateCardForbidNull` (L146-150) and `ResetCardForbidNullSkill` (L171-175) stays commented — no-op.

### 3.5 `CharacterSelectPanel.cs` — operator detail skill list

L333. Replace `Skill[] skills = entityData.Prefab.GetComponents<Skill>();` with `var skills = entityData.Skills;`. Iterate `skills.Count` (was `skills.Length`). Field reads in the iteration body: `skills[i].SkillImg` → `skills[i].icon`, `skills[i].SkillDescription` → `skills[i].description`.

### 3.6 `MonsterHandbookPanel.cs` — handbook description

L231. Replace `Skill[] skills = monsterData.Prefab.GetComponents<Skill>();` with `var skills = monsterData.Skills;`. `skills.Length` → `skills.Count`. Field reads: `skills[i].SkillDescription` → `skills[i].description`.

---

## 4. File-Level Change List

| File | Action |
|---|---|
| `Assets/PublicScripts/GameData/SkillSystem/SkillConfig.cs` | +1 field `Sprite icon` |
| `Assets/PublicScripts/GameData/SkillSystem/SPConfig.cs` | +1 field `Vector2Int[] skillAttackRange` |
| `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs` | Remove `Skill[] skill;` field; remove 2-line bridge in `PreWarm` |
| `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Components/Cards.cs` | `SkillCard.UpdateSkillCardMessage` signature change; field map; label map |
| `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs` | Field split, ~5 method blocks updated, dead code revived with new API |
| `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/CharacterCardManager.cs` | 1 line + null-guard |
| `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/CharacterSelectPanel.cs` | 1 line + iteration field reads |
| `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/MonsterHandbookPanel.cs` | 1 line + iteration field reads |

**Unchanged:** `Skill.cs` (kept), prefab-mounted `Skill` components (kept), `Entity.Talents[]` (out of scope), `SkillRunner` / `SkillRuntime` / `SPEngine` / `ISkillComponent` library, `SpSliderController` (already migrated in 2026-06-08 plan).

---

## 5. Error Handling / Edge Cases

| Situation | Behaviour |
|---|---|
| `Entity` has no `SkillRunner` (prefab without one) | `_selectSkillRuntime` stays null; skill open/close button hidden; `UIStates_Update_Operator` early-returns |
| `SkillRunner.Skills` empty | Same as above |
| `SkillConfig.sp` is null | Skill button hidden; range preview hidden; `UIStates_Update_Operator` early-returns on `sp == null` |
| `SkillConfig.icon` is null | `SkillCard` hides the icon image; `LevelMessagePanel` `_skillOpen` Image is null-coloured (already what it is for `null` Sprite — Unity renders nothing) |
| `SPConfig.skillAttackRange` is null or empty | `_skillRange` hidden |
| `EntityData.Skills` null or empty (character card / select / handbook) | "No skill" branch fires: `_noneSkill` placeholder or hidden skill cards |
| `EntityData.Skills` count > 1 (future multi-skill) | Out of scope — same as old `skill[0]` behaviour, only the first is displayed |
| Legacy `Skill` components on prefab still get `IPoolOperation.PreWarm / Initialize / Dormancy` from `EntityPool` cascade | Unchanged — no NRE since `Skill` is still a valid `IPoolOperation`. Memory cost: one legacy state machine per entity instance, never read. (Acceptable per G3; deletion is a separate prefab-level task.) |
| Garbled `switch` labels in `SkillCard` | Replaced with the clean Chinese labels in §3.2 |
| `UIStates_Update_Operator` runs after entity death | `Stats.IsActive` guard in `UIStateMachine` (added in 2026-06-07 plan) already prevents re-entry; `_selectSkillRuntime` may still hold a reference, but `spEngine` is on a `SkillRuntime` field on the entity — the runtime is still alive while the entity is in dormancy. We accept stale-frame render of the last good state (slider cleans up via its own `IsActive` poll). |

---

## 6. Testing Strategy

Project has no Unity Test Framework harness. Per project convention, verify by PlayMode run.

### 6.1 PlayMode checklist

1. **No NRE on scene load.** Deploy any operator. Verify no console errors from `Entity.PreWarm` or `EntityPoolManager.CreateNewEntity`. (The removed `GetComponents<Skill>()` line eliminates one source; `Skill` itself is still on the prefab so its `PreWarm` still runs and does `_thisEntity = this.GetComponent<Entity>()` — fine.)
2. **Character select (selecting an operator in the deploy screen).**
   - Operator with `Skills.Count > 0` and `Skills[0].icon != null`: skill card on detail panel shows the configured icon, name, description, mode labels in Chinese.
   - Operator with `Skills.Count == 0` or `icon == null`: skill cards hidden / no icon.
3. **Character card (roster sidebar).**
   - Same icon-presence behaviour.
4. **Monster handbook (browser / sandbox).**
   - Monster with `Skills` populated: descriptions concatenated with `\n` separators.
   - Monster with no skills: skill description block hidden.
5. **Deployed operator click → operator panel open.**
   - With `SkillRunner` and `Skills[0]`: skill open button visible; range preview visible if `skillAttackRange` non-empty.
   - Without `SkillRunner` (debug entity): skill open / range preview hidden; no NRE.
6. **Manual release.** Click skill open with full SP → `spEngine.FireSkill()` is called → combat effects fire (e.g. `AttackBoost` mult kicks in if configured). `UIStates_SwitchTo_Normal()` returns the panel to base state.
7. **Manual close.** Click close button while skill active → `spEngine.EndSkill()` called. Verify combat effect cleared (e.g. attack boost dropped if any `EndSkill` cleanup runs).
8. **Live SP state in operator panel.** Place operator, let natural SP recover; verify the SP bar / charge counter / skill-active tint updates each frame per `spEngine`.
9. **Death path.** Kill the operator mid-skill: `UIStateMachine` death-detection guard (added 2026-06-07) returns to normal; `Entity` returns to pool; slider self-cleans. No NRE from the now-orphaned `_selectSkillRuntime` reference.
10. **Compile check.** Project compiles with no `Skill` / `Entity.skill` references remaining (grep `Entity.skill` / `: Skill\b` / `GetComponents<Skill>` returns zero matches in non-comment code).

### 6.2 Data-followup (out of scope for this spec)

After this code change lands, the user (or a follow-up task) must populate `SkillConfig.icon` and `SPConfig.skillAttackRange` on existing `SkillConfig` ScriptableObject assets for the UI to show icons / range previews. Until then, UI degrades to icon-less / no-range-preview.

---

## 7. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Behaviour drift: clicking the skill button in the operator panel used to call the legacy `Skill.SkillBegin()`; now calls `SPEngine.FireSkill()`. If a prefab has an `AttackRangeOverrideComponent` whose `OnSkillBegin` trigger rewrites `Vision.Range`, the **range is rewritten by the component**, not by `SPEngine.FireSkill()` itself. UI side just needs `spEngine.IsActive` to flip. | Verified: `SPEngine.FireSkill` sets `_isActive = true` and fires `OnBegin`; `AttackRangeOverrideComponent` is already wired to `OnSkillBegin` in the data (per prior migration work). Range rewrite is correct. |
| `Entity.skill` field removal breaks a third party that wasn't grep'd | Re-grep after the change: `grep -rn "entity\.skill\b" Assets/PublicScripts` and `grep -rn "\.skill\b" Assets/PublicScripts` should return zero non-comment hits. If anything surfaces, migrate it in this spec. |
| `Skill` components on prefab still execute their `IPoolOperation` cascade and accumulate event subscriptions across `EntityPool.Return` cycles (existing `Skill.Dormancy` is empty) | Out of scope — pre-existing bug, not introduced by this spec. Note in follow-up backlog. |
| `SkillConfig.icon` not set on any existing asset → silent icon-loss for the user | The PlayMode checklist (§6.1 items 2-3) catches this. Documented in §6.2 as a follow-up data-fill task. |
| Multi-skill entity with `Skills.Count > 1` → only `[0]` is read | Mirrors old `skill[0]` behaviour; out of scope. |

---

## 8. Out of Scope

- Deletion of `Skill.cs` file and `Skill` components on prefabs (per G3).
- Migration of `Talent[]` and the talent half of `SwitchShowSkillTalent` (per §1.4).
- Filling `SkillConfig.icon` / `SPConfig.skillAttackRange` on existing data assets (§6.2).
- Multi-skill UI (skill selection, multi-bar SP slider).
- `Skill` component subscription leak fix in `Skill.Dormancy` (pre-existing bug).

---

## 9. Open Questions

None. All design decisions resolved during brainstorming:

1. ✅ Missing fields (`icon`, `skillAttackRange`) → add to new data model.
2. ✅ `Entity.skill[]` field and bridge → remove.
3. ✅ Talent scope → out of scope.
4. ✅ Dead `SwitchShowSkillTalent` skill branch → revive with new API.
5. ✅ `SkillCard` signature → `(SkillConfig, SkillRuntime = null)`.
6. ✅ `Skill.cs` file → keep on disk.
