# Skill System Replace Legacy Skill Code — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all UI / Entity code that reads the legacy `Skill` MonoBehaviour with the new `SkillSystem` (`SkillConfig` / `SkillRuntime` / `SPEngine`) data API. `Skill.cs` and prefab-mounted `Skill` components remain on disk.

**Architecture:** Two-step data model additions (`SkillConfig.icon`, `SPConfig.skillAttackRange`); bottom-up code migration from `Entity` → `SkillCard` → `LevelMessagePanel` → static-display UIs (`CharacterCardManager` / `CharacterSelectPanel` / `MonsterHandbookPanel`); final grep-and-compile verification.

**Tech Stack:** Unity 2022+ / C# 7.3+ / no Unity Test Framework (project uses PlayMode manual verification, per project convention)

**Spec:** [2026-06-09-skill-system-replace-legacy-skill-code-design.md](../specs/2026-06-09-skill-system-replace-legacy-skill-code-design.md)

---

## File Structure

### Files to modify

| File | Purpose of change |
|---|---|
| `Assets/PublicScripts/GameData/SkillSystem/SkillConfig.cs` | +1 field `Sprite icon` |
| `Assets/PublicScripts/GameData/SkillSystem/SPConfig.cs` | +1 field `Vector2Int[] skillAttackRange` |
| `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs` | Remove `Skill[] skill;` field (L24) + `GetComponents<Skill>()` bridge (L212) |
| `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Components/Cards.cs` | `SkillCard.UpdateSkillCardMessage` signature change (L38) + field map + label map |
| `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs` | Field split (L305), 5 method blocks (L526, L1162-1175, L1189-1292, L1681-1689) |
| `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/CharacterCardManager.cs` | 1 line at L118 + null-guard |
| `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/CharacterSelectPanel.cs` | L333 + L358 + L362 + inner-loop field reads |
| `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/MonsterHandbookPanel.cs` | L231 + inner-loop field reads |

### Files to NOT modify

- `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Skill.cs` — kept on disk
- Any prefab asset under `Assets/Resources/Prefabs/...` — kept as-is (Skill components remain on prefabs)
- `Entity.Talents[]` field — out of scope
- `SkillRunner` / `SkillRuntime` / `SPEngine` / `ISkillComponent` library — new system core, untouched
- `SpSliderController` — already migrated in [2026-06-08 plan](2026-06-08-spslider-new-skill-system-adaptation.md)

---

## Task 1: Add `icon` and `skillAttackRange` fields to data model

**Files:**
- Modify: `Assets/PublicScripts/GameData/SkillSystem/SkillConfig.cs`
- Modify: `Assets/PublicScripts/GameData/SkillSystem/SPConfig.cs`

- [ ] **Step 1: Add `icon` field to SkillConfig**

Open `Assets/PublicScripts/GameData/SkillSystem/SkillConfig.cs`. The current file content is:

```csharp
using System;
using UnityEngine;

namespace SkillSystem
{
    [CreateAssetMenu(fileName = "SkillConfig", menuName = "SkillSystem/Skill Config", order = 0)]
    public class SkillConfig : ScriptableObject
    {
        public string skillId;
        public string skillName;
        [TextArea(2, 5)] public string description;

        public SPConfig sp;
        public ComponentConfig[] components = Array.Empty<ComponentConfig>();
    }
}
```

Add the `icon` field after `description`:

```csharp
using System;
using UnityEngine;

namespace SkillSystem
{
    [CreateAssetMenu(fileName = "SkillConfig", menuName = "SkillSystem/Skill Config", order = 0)]
    public class SkillConfig : ScriptableObject
    {
        public string skillId;
        public string skillName;
        [TextArea(2, 5)] public string description;

        [Tooltip("技能图标，UI 上技能卡 / 按钮使用。")]
        public Sprite icon;

        public SPConfig sp;
        public ComponentConfig[] components = Array.Empty<ComponentConfig>();
    }
}
```

- [ ] **Step 2: Add `skillAttackRange` field to SPConfig**

Open `Assets/PublicScripts/GameData/SkillSystem/SPConfig.cs`. The current file content is:

```csharp
using System;
using UnityEngine;

namespace SkillSystem
{
    public enum SpRecoverMode { Natural, OnAttackHit, OnAfterHurt }
    public enum SpConsumeMode { Natural, OnAttackHit, OnAfterHurt, Instant }
    public enum SkillOpenMode { Auto, OnAttackAnimBegin, OnBeforeHurt, Manual, OnAttackHit }

    [Serializable]
    public class SPConfig
    {
        [Tooltip("Total SP. Skill fires when current SP >= totalSp.")]
        public int totalSp;
        public int initialSp;
        [Tooltip("Max charges. >1 enables multi-charge behavior.")]
        public int chargeNum = 1;
        [Tooltip(">0 = active for that long after fire; <=0 = instant fire.")]
        public float skillDuration;
        public SpRecoverMode recoverMode = SpRecoverMode.Natural;
        public SpConsumeMode consumeMode = SpConsumeMode.Natural;
        public SkillOpenMode openMode = SkillOpenMode.Auto;
        public bool recoverForbidDuringSkill;
        public bool canManualClose;
    }
}
```

Add the `skillAttackRange` field at the end of the class (before the closing brace):

```csharp
using System;
using UnityEngine;

namespace SkillSystem
{
    public enum SpRecoverMode { Natural, OnAttackHit, OnAfterHurt }
    public enum SpConsumeMode { Natural, OnAttackHit, OnAfterHurt, Instant }
    public enum SkillOpenMode { Auto, OnAttackAnimBegin, OnBeforeHurt, Manual, OnAttackHit }

    [Serializable]
    public class SPConfig
    {
        [Tooltip("Total SP. Skill fires when current SP >= totalSp.")]
        public int totalSp;
        public int initialSp;
        [Tooltip("Max charges. >1 enables multi-charge behavior.")]
        public int chargeNum = 1;
        [Tooltip(">0 = active for that long after fire; <=0 = instant fire.")]
        public float skillDuration;
        public SpRecoverMode recoverMode = SpRecoverMode.Natural;
        public SpConsumeMode consumeMode = SpConsumeMode.Natural;
        public SkillOpenMode openMode = SkillOpenMode.Auto;
        public bool recoverForbidDuringSkill;
        public bool canManualClose;
        [Tooltip("技能激活时的攻击范围覆盖（相对 Vision.Range）。空表示不覆盖。")]
        public Vector2Int[] skillAttackRange;
    }
}
```

- [ ] **Step 3: Compile-check**

Open the project in Unity Editor. Wait for the asset import + compile to finish.

Expected: Console shows no compile errors. The two new fields are visible in the Inspector when selecting a `SkillConfig` ScriptableObject asset (their values default to `null` / `null` until you populate them — this is the documented follow-up data-fill task).

- [ ] **Step 4: Commit**

```bash
git add Assets/PublicScripts/GameData/SkillSystem/SkillConfig.cs \
        Assets/PublicScripts/GameData/SkillSystem/SPConfig.cs
git commit -m "feat(skill-config): add icon sprite and skillAttackRange fields

Add two UI-display fields to the new SkillSystem data model:
- SkillConfig.icon (Sprite) — used by SkillCard / character card /
  deployed-operator skill button.
- SPConfig.skillAttackRange (Vector2Int[]) — used by the operator panel
  for range preview. The actual combat range-rewrite remains owned by
  the existing AttackRangeOverrideComponent (its parameters live in
  ComponentConfig.ParamList, not on SPConfig).

No behaviour change at runtime. Existing SkillConfig assets deserialize
with both fields as null; UI gracefully degrades to no-icon / no-range-
preview until the data is populated (follow-up task).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: Remove `Entity.skill[]` field and `GetComponents<Skill>()` bridge

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs`

- [ ] **Step 1: Remove the `Skill[] skill;` field**

Open `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs`. The field block at L20-25 currently reads:

```csharp
[HideInInspector] public Skill[] skill;
[HideInInspector] public Talent[] Talents;
```

Remove the `skill` line only. Result:

```csharp
[HideInInspector] public Talent[] Talents;
```

(`Talent` stays — out of scope per spec §1.4.)

- [ ] **Step 2: Remove the `GetComponents<Skill>()` bridge in `PreWarm`**

In the same file, the `PreWarm()` method contains this block (currently at L211-212):

```csharp
        // === 旧 Skill[] 填充（与 SkillSystem 并存期，过渡给 LevelMessagePanel 的旧 UI 读 _selectSkill） ===
        this.skill = GetComponents<Skill>();

        // === SkillRunner 生命周期接入（Phase 2 迁移期，与旧 Skill[]/Talent[] 共存） ===
        if (TryGetComponent<SkillSystem.SkillRunner>(out var skillRunner))
            skillRunner.PreWarm();
```

Delete the comment line + the assignment line. Result:

```csharp
        // === SkillRunner 生命周期接入（Phase 2 迁移期，与 Talent[] 共存） ===
        if (TryGetComponent<SkillSystem.SkillRunner>(out var skillRunner))
            skillRunner.PreWarm();
```

- [ ] **Step 3: Compile-check**

Open in Unity Editor. Wait for compile.

Expected: compile errors **about `_selectSkill` / `Entity.skill`** are expected at this point — the rest of the codebase still references them. The next 5 tasks remove those references. The compile will be clean after Task 7.

For *this* task, the only validation is: the file itself has no syntax errors. (You should see the file in the Solution view with no red squiggles specific to `Entity.cs`.)

- [ ] **Step 4: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs
git commit -m "refactor(entity): drop Skill[] field and GetComponents<Skill>() bridge

Entity.skill[] was a transitional patch from the Phase 2 SkillSystem
migration, populated by PreWarm via GetComponents<Skill>() and read by
the now-being-migrated UI layer. With all UI readers moving to the new
SkillConfig/SkillRuntime API in subsequent tasks, the field and the
bridge have no consumers.

Skill.cs file and prefab-mounted Skill components remain on disk per
spec G3; prefab-level Skill state continues to be IPoolOperation-
initialised by the EntityPool cascade, but is now orphaned.

Talent[] field stays (out of scope per spec §1.4).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Migrate `SkillCard.UpdateSkillCardMessage` to `SkillConfig`

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Components/Cards.cs`

- [ ] **Step 1: Replace the `UpdateSkillCardMessage` method body**

Open `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Components/Cards.cs`. Find the `SkillCard` class (starts at L10). The method `UpdateSkillCardMessage(Skill skill)` is at L38, with body from L38 to L82 (the closing `}` of the method).

**Replace the entire method** (the line `public void UpdateSkillCardMessage(Skill skill) {` through the matching `}`). The replacement:

```csharp
        public void UpdateSkillCardMessage(SkillConfig config, SkillRuntime runtime = null)
        {
            if (config == null) return;
            if (config.icon != null) skillImage.sprite = config.icon;
            skillName.text = config.skillName;
            description.text = config.description;
            var sp = config.sp;
            float skillDuration = sp != null ? sp.skillDuration : 0f;
            if (skillDuration > 0f)
            {
                skillAmountText.text = skillDuration.ToString("0.#");
            }
            else
            {
                skillAmountText.transform.parent.gameObject.SetActive(false);
            }
            if (sp != null)
            {
                switch (sp.recoverMode)
                {
                    case SpRecoverMode.Natural: spRecoverModeText.text = "自动回复"; break;
                    case SpRecoverMode.OnAttackHit: spRecoverModeText.text = "攻击回复"; break;
                    case SpRecoverMode.OnAfterHurt: spRecoverModeText.text = "受击回复"; break;
                }
                switch (sp.openMode)
                {
                    case SkillOpenMode.Auto: skillOpenModeText.text = "自动触发"; break;
                    case SkillOpenMode.OnAttackAnimBegin: skillOpenModeText.text = "攻击时触发"; break;
                    case SkillOpenMode.OnBeforeHurt: skillOpenModeText.text = "受击时触发"; break;
                    case SkillOpenMode.Manual: skillOpenModeText.text = "手动触发"; break;
                    case SkillOpenMode.OnAttackHit: skillOpenModeText.text = "命中触发"; break;
                }
                if (sp.totalSp > 0)
                {
                    totalSpText.transform.parent.gameObject.SetActive(true);
                    totalSpText.text = sp.totalSp.ToString();
                }
                else
                {
                    totalSpText.transform.parent.gameObject.SetActive(false);
                }
                if (sp.initialSp > 0)
                {
                    sp0Text.transform.parent.gameObject.SetActive(true);
                    sp0Text.text = sp.initialSp.ToString();
                }
                else
                {
                    sp0Text.transform.parent.gameObject.SetActive(false);
                }
            }
        }
```

The `runtime` parameter is intentionally unused in this spec — reserved for future live-state display (e.g. SP progress bar overlay on the card).

- [ ] **Step 2: Verify the file's `using` directives**

`Cards.cs` should already have:

```csharp
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
```

Add `using SkillSystem;` if not present (needed for `SkillConfig` / `SkillRuntime` / `SpRecoverMode` / `SkillOpenMode` references). Result:

```csharp
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using SkillSystem;
```

- [ ] **Step 3: Compile-check**

Open in Unity Editor.

Expected: compile errors about `UpdateSkillCardMessage(Skill)` argument mismatch at the call sites in `LevelMessagePanel.cs:1684` (commented) and `CharacterSelectPanel.cs:358` (live). The L358 error is the only one blocking the build. (We fix it in Task 6.)

For this task, validate only `Cards.cs` itself has no syntax errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Components/Cards.cs
git commit -m "refactor(skillcard): switch UpdateSkillCardMessage to SkillConfig

The legacy Skill MonoBehaviour is no longer the source of truth for
skill display data. SkillCard now reads from SkillConfig (and an
optional SkillRuntime, reserved for future live-state display).

Field map:
  SkillImg           -> config.icon
  SkillName          -> config.skillName
  SkillDescription   -> config.description
  SkillAmount        -> config.sp.skillDuration
  SpRecoverMode (int)-> config.sp.recoverMode (enum)
  SkillOpenMode (int)-> config.sp.openMode (enum)
  SpComsumeMode (int)-> (no card display, was no-op)
  TotalSp            -> config.sp.totalSp
  InitialSp          -> config.sp.initialSp

Replaces garbled Chinese labels with proper text in the enum switches.

Call sites still on the old signature (CharacterSelectPanel L358) will
be updated in a subsequent task.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Migrate `LevelMessagePanel` — field split, click handlers, skill load, live state, dead-code revival

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs`

This is the largest task because `LevelMessagePanel` is the only file that uses *both* static `SkillConfig` and live `SkillRuntime` state. Five substeps, one commit.

- [ ] **Step 1: Split `_selectSkill` field (L305)**

Find L305:

```csharp
        private Skill _selectSkill;
```

Replace with:

```csharp
        private SkillSystem.SkillConfig _selectSkillConfig;
        private SkillSystem.SkillRuntime _selectSkillRuntime;
```

- [ ] **Step 2: Update skill open / close click handlers (L522-545)**

Find the two click handlers around L522-545. The release handler (L524-531) and the close handler (L537-543) both reference `_selectSkill`. Replace the entire `InitOperatorPanel` body inside the click-handler setup with the following. The rest of `InitOperatorPanel` (the `_callBackClick` etc. above and the `_skillRangeClick` initialization) is unchanged.

Replace from line 522 (`EventTrigger.Entry skillOpenClick = new EventTrigger.Entry();`) through line 544 (the `});` that closes `skillstop` and the next blank line). The full replacement block:

```csharp
            EventTrigger.Entry skillOpenClick = new EventTrigger.Entry();
            skillOpenClick.eventID = EventTriggerType.PointerClick;
            skillOpenClick.callback.AddListener((data) =>
            {
                var sp = _selectSkillRuntime != null ? _selectSkillRuntime.spEngine : null;
                if (sp != null && sp.CanBegin())
                {
                    sp.FireSkill();
                    UIStates_SwitchTo_Normal();
                }
            });
            _skillOpen.GetComponent<EventTrigger>().triggers.Add(skillOpenClick);
            _skillRangeClick = new EventTrigger.Entry();
            _skillRangeClick.eventID = EventTriggerType.PointerClick;
            _skillRange.GetComponent<EventTrigger>().triggers.Add(_skillRangeClick);
            EventTrigger.Entry skillstop = new EventTrigger.Entry();
            skillstop.eventID = EventTriggerType.PointerClick;
            skillstop.callback.AddListener((data) =>
            {
                if (_selectSkillRuntime != null && _selectSkillRuntime.spEngine != null)
                    _selectSkillRuntime.spEngine.EndSkill();
                UIStates_SwitchTo_Normal();
                AudioManager.Manager.PlayAudio("skill_boostclose", 1, false, false);
            });
            _stop.GetComponent<EventTrigger>().triggers.Add(skillstop);
```

> The original release handler used `if (_selectSkill.SkillCanBegin())` then `_selectSkill.SkillBegin()`. The new version uses `sp.CanBegin()` and `sp.FireSkill()`. Behaviour: `SPEngine.FireSkill()` flips `_isActive=true`, fires `OnBegin` (which `SkillRuntime.OpenActiveWindow()` consumes), dispatches `SkillBeginEvent` to all components. That replaces `Skill.SkillBegin()`'s side effect of setting `_currentSkillAmount = _skillAmount` and rewriting `Vision.Range` — both are now done by the corresponding `ISkillComponent` plugins on `OnSkillBegin` (already in place per prior migration work).

- [ ] **Step 3: Update skill load in `UIStates_ShowClose_Operator(true)` (L1162-1175)**

Find the block at L1162-1175. The current text is:

```csharp
                // 技能按钮与技能范围预览（旧 Skill MonoBehaviour 数据源，与 SkillSystem 并存期）
                if (_selectedEntity.skill != null && _selectedEntity.skill.Length > 0)
                {
                    _skillOpen.gameObject.SetActive(true);
                    _selectSkill = _selectedEntity.skill[0];
                    _skillOpen.sprite = _selectSkill.SkillImg;
                    _skillRange.gameObject.SetActive(_selectSkill.SkillAttackRange != null);
                }
                else
                {
                    _skillOpen.gameObject.SetActive(false);
                    _selectSkill = null;
                    _skillRange.gameObject.SetActive(false);
                }
```

Replace with:

```csharp
                // 技能按钮与技能范围预览（新 SkillSystem 数据源：SkillRunner / SkillConfig / SPConfig）
                SkillSystem.SkillRunner runner;
                if (_selectedEntity.TryGetComponent<SkillSystem.SkillRunner>(out runner)
                    && runner.Skills != null && runner.Skills.Count > 0)
                {
                    _selectSkillRuntime = runner.Skills[0];
                    _selectSkillConfig = _selectSkillRuntime.config;
                    _skillOpen.gameObject.SetActive(true);
                    _skillOpen.sprite = _selectSkillConfig.icon;
                    var range = _selectSkillConfig.sp != null ? _selectSkillConfig.sp.skillAttackRange : null;
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

- [ ] **Step 4: Update `UIStates_Update_Operator` to read from new system (L1189-1292)**

Find `private void UIStates_Update_Operator()` at L1189. The body runs from L1190 to L1292.

**Replace the entire method body** (L1190's `{` through L1292's `}`) with:

```csharp
        {
            if (_selectSkillRuntime == null
                || _selectSkillRuntime.spEngine == null
                || _selectSkillConfig == null
                || _selectSkillConfig.sp == null)
                return;
            var sp = _selectSkillRuntime.spEngine;
            var cfg = _selectSkillConfig.sp;

            bool canBegin = sp.CanBegin();
            if (!canBegin)
            {
                _skillOpen.raycastTarget = false;
                _skillOpen.color = Color.gray;
            }
            else
            {
                if (cfg.openMode == SkillSystem.SkillOpenMode.Manual)
                {
                    _skillOpen.raycastTarget = true;
                }
                else
                {
                    _skillOpen.raycastTarget = false;
                }
                _skillOpen.color = Color.white;
            }

            float currentSpRate = cfg.totalSp > 0 ? Mathf.Clamp01(sp.CurrentSp / cfg.totalSp) : 0f;
            int currentChargeNum = sp.CurrentCharge;
            bool isSkill = sp.IsActive;
            _spMask.fillAmount = currentSpRate;
            if (currentChargeNum < 1)
            {
                _skillChargeNum.gameObject.SetActive(false);
            }
            else
            {
                _skillChargeNum.gameObject.SetActive(true);
                _skillChargeNumText.text = currentChargeNum.ToString();
            }
            if (!isSkill)
            {
                float tsp = cfg.totalSp;
                _spState.sprite = _spMessageAtlas[0];
                _stop.enabled = false;
                if (!(sp.CurrentSp >= cfg.totalSp || (cfg.chargeNum > 1 && sp.CurrentCharge >= cfg.chargeNum)))
                {
                    _spMask.enabled = true;
                    _spMask.color = _lightGreen_half;
                    _spText.text = $"{(int)(tsp * currentSpRate)}/{(int)tsp}";
                    if (currentChargeNum == 0 && currentSpRate < 1)
                    {
                        _spBk.color = _gray;
                        _spState.color = _lightGreen;
                        _spText.color = Color.white;
                    }
                    else
                    {
                        _spBk.color = _lightGreen;
                        _spState.color = Color.white;
                        _spText.color = Color.black;
                    }
                }
                else
                {
                    _spMask.enabled = false;
                    _spText.text = "READY";
                    _spBk.color = _lightGreen;
                    _spState.color = Color.white;
                    _spText.color = Color.black;
                }
            }
            else
            {
                int consumeType = (int)cfg.consumeMode;
                _spBk.color = _orange;
                _spState.color = Color.white;
                if (consumeType < 3)
                {
                    float tsa = cfg.skillDuration;
                    _spMask.enabled = true;
                    _spMask.color = _orange_half;
                    _spState.sprite = _spMessageAtlas[consumeType + 1];
                    _spText.color = Color.white;
                    if (consumeType == 0)
                    {
                        _spText.text = (tsa * currentSpRate).ToString("0.0") + 's';
                    }
                    else
                    {
                        _spText.text = (tsa * currentSpRate).ToString() + '/' + tsa.ToString();
                    }
                }
                else
                {
                    _spMask.enabled = false;
                    _spState.sprite = _spMessageAtlas[4];
                    _spText.text = null;
                }
                if (cfg.canManualClose)
                {
                    _stop.enabled = true;
                }
                else
                {
                    _stop.enabled = false;
                }
            }
        }
```

> Behavioural notes:
> - `if (_selectSkill)` truthy-check on the legacy `Skill` MonoBehaviour becomes an explicit null-guard on the new fields.
> - `_selectSkill.SkillCanBegin()` → `sp.CanBegin()`.
> - `_selectSkill.SkillOpenMode == 3` (manual int) → `cfg.openMode == SkillSystem.SkillOpenMode.Manual` (typed enum).
> - `_selectSkill.SkillMessage` tuple unpack → direct reads of `sp.CurrentSp` / `sp.CurrentCharge` / `sp.IsActive`.
> - `_selectSkill.SPFull()` → `sp.CurrentSp >= cfg.totalSp || (cfg.chargeNum > 1 && sp.CurrentCharge >= cfg.chargeNum)`. Mirrors `SPEngine.SPFull()` semantics for `chargeNum <= 1` (SP-only) and `> 1` (charge-only).
> - `_selectSkill.SpComsumeMode` (int) → `(int)cfg.consumeMode` for the existing `_spMessageAtlas[consumeType + 1]` index calculation. Casts to int; enum members have the same order (`Natural=0, OnAttackHit=1, OnAfterHurt=2, Instant=3`).
> - `_selectSkill.SkillAmount` (float, the old active duration) → `cfg.skillDuration` (new active duration). Same semantic, same unit.
> - `_selectSkill.CanCloseSkill` (bool) → `cfg.canManualClose` (bool).
> - The `Mathf.Clamp01` on `currentSpRate` defends against `cfg.totalSp == 0` in addition to the early-return guard.

- [ ] **Step 5: Revive `SwitchShowSkillTalent` skill branch (L1681-1689)**

Find the commented-out block at L1678-1689:

```csharp
            // switch (_currentShow)
            // {
            //     case 0:
            //         if (entityData.skill != null && entity.skill.Length > 0)
            //         {
            //             _skillCard.SkillRT.gameObject.SetActive(true);
            //             _skillCard.UpdateSkillCardMessage(entity.skill[0]);
            //         }
            //         else
            //         {
            //             _skillCard.SkillRT.gameObject.SetActive(false);
            //         }
```

Replace with:

```csharp
            // switch (_currentShow)
            // {
            //     case 0:
            //         if (entityData.Skills != null && entityData.Skills.Count > 0)
            //         {
            //             _skillCard.SkillRT.gameObject.SetActive(true);
            //             _skillCard.UpdateSkillCardMessage(entityData.Skills[0]);
            //         }
            //         else
            //         {
            //             _skillCard.SkillRT.gameObject.SetActive(false);
            //         }
```

(Talent branch below stays commented — out of scope.)

- [ ] **Step 6: Clear the new fields when the operator panel closes**

Find the `else` branch of `UIStates_ShowClose_Operator` (at L1178+ — the `else` that hides the panel). It currently does:

```csharp
            else
            {
                if (_operaterOpen)
                {
                    _operaterOpen = false;
                    _operateArea.SetActive(false);
                    MoveCamera(_cameraOriginalPos, 0.1f);
                }
            }
```

Add a clear of the new fields at the top of the `if (_operaterOpen)` block so a subsequent open starts from a clean slate:

```csharp
            else
            {
                if (_operaterOpen)
                {
                    _operaterOpen = false;
                    _operateArea.SetActive(false);
                    MoveCamera(_cameraOriginalPos, 0.1f);
                    _selectSkillConfig = null;
                    _selectSkillRuntime = null;
                }
            }
```

- [ ] **Step 7: Compile-check**

Open in Unity Editor.

Expected: no compile errors remain **for `LevelMessagePanel.cs`**. The remaining errors (if any) should be only in `CharacterCardManager.cs`, `CharacterSelectPanel.cs`, `MonsterHandbookPanel.cs` (Tasks 5-7 fix those).

- [ ] **Step 8: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/LevelMessagePanel.cs
git commit -m "refactor(level-message-panel): migrate deployed-operator skill UI to SkillSystem

Replaces the legacy _selectSkill : Skill field with the new pair
_selectSkillConfig : SkillConfig + _selectSkillRuntime : SkillRuntime.

Touched methods:
  - InitOperatorPanel click handlers: release calls spEngine.FireSkill
    (gated on spEngine.CanBegin); close calls spEngine.EndSkill.
  - UIStates_ShowClose_Operator(true): skill load via TryGetComponent
    <SkillSystem.SkillRunner> + runner.Skills[0]; range preview gated
    on cfg.sp.skillAttackRange.
  - UIStates_Update_Operator: live state reads from SPEngine
    (CurrentSp / CurrentCharge / IsActive / CanBegin) and SkillConfig.sp
    (openMode / totalSp / skillDuration / consumeMode / canManualClose).
  - UIStates_ShowClose_Operator(false): clear the two new fields.
  - SwitchShowSkillTalent: revive the dead skill branch with the new
    SkillConfig-based SkillCard API.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: Migrate `CharacterCardManager` (character card icon)

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/CharacterCardManager.cs`

- [ ] **Step 1: Replace the skill-icon block at L118**

Find the block at L117-126:

```csharp
            Skill[] skills = characterData.Prefab.GetComponents<Skill>();
            if (skills.Length > 0)
            {
                skillImg.sprite = skills[0].SkillImg;
            }
            else
            {
                skillImg.sprite = _noneSkill;
            }
```

Replace with:

```csharp
            var skills = characterData.Skills;
            if (skills != null && skills.Count > 0 && skills[0].icon != null)
            {
                skillImg.sprite = skills[0].icon;
            }
            else
            {
                skillImg.sprite = _noneSkill;
            }
```

- [ ] **Step 2: Compile-check**

Open in Unity Editor.

Expected: no new errors. `CharacterCardManager.cs` now compiles. (The other two UI files still error, fixed in Tasks 6-7.)

- [ ] **Step 3: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/CharacterCardManager.cs
git commit -m "refactor(character-card): use SkillConfig.icon instead of legacy Skill

Replace Prefab.GetComponents<Skill>()[0].SkillImg with
characterData.Skills[0].icon. Null guard added for both the list
itself and the icon field (UI gracefully degrades to _noneSkill
placeholder).

Commented-out skill-icon logic in InstantiateCardForbidNull /
ResetCardForbidNullSkill stays commented (no-op).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: Migrate `CharacterSelectPanel` (operator detail skill list)

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/CharacterSelectPanel.cs`

- [ ] **Step 1: Replace the skill source at L333**

Find L333:

```csharp
            Skill[] skills = entityData.Prefab.GetComponents<Skill>();
```

Replace with:

```csharp
            var skills = entityData.Skills;
```

- [ ] **Step 2: Update the iteration bounds**

In the same method, find the loop bound at L346:

```csharp
                for (int i = 0; i < skills.Length; i++)
```

Replace with:

```csharp
                for (int i = 0; i < skills.Count; i++)
```

And the trailing cleanup loop at L362:

```csharp
                for (int i = skills.Length; i < 3; i++)
```

Replace with:

```csharp
                for (int i = skills.Count; i < 3; i++)
```

- [ ] **Step 3: Update the inner-loop call to SkillCard (L358)**

Find L358:

```csharp
                    _skillSelectorCards[i].UpdateSkillCardMessage(skills[i]);
```

The new `SkillCard.UpdateSkillCardMessage` signature is `(SkillConfig, SkillRuntime = null)`. The `SkillConfig` is `skills[i]`; pass `null` for `SkillRuntime` (this branch is static display only — no live state).

Replace with:

```csharp
                    _skillSelectorCards[i].UpdateSkillCardMessage(skills[i]);
```

(Line unchanged — `skills[i]` is now `SkillConfig` instead of `Skill`, and the new overload accepts that directly. The `= null` for `SkillRuntime` uses the default parameter.)

- [ ] **Step 4: Compile-check**

Open in Unity Editor.

Expected: no new errors in `CharacterSelectPanel.cs`.

- [ ] **Step 5: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/CharacterSelectPanel.cs
git commit -m "refactor(character-select): use EntityData.Skills for skill list

Replace Prefab.GetComponents<Skill>() with entityData.Skills (the
new data-driven list). Iteration bounds updated from .Length to .Count.
The call to SkillCard.UpdateSkillCardMessage uses the new
(SkillConfig, SkillRuntime=null) overload.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 7: Migrate `MonsterHandbookPanel` (handbook description)

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/MonsterHandbookPanel.cs`

- [ ] **Step 1: Replace the skill source at L231**

Find L231:

```csharp
                Skill[] skills = monsterData.Prefab.GetComponents<Skill>();
```

Replace with:

```csharp
                var skills = monsterData.Skills;
```

- [ ] **Step 2: Update the iteration bounds and field reads**

In the same method, find the iteration:

```csharp
                if (skills.Length > 0)
                {
                    _skillDescriptionText.text = "�� " + skills[0].SkillDescription;
                    for (int i = 1; i < skills.Length; i++)
                    {
                        _skillDescriptionText.text += "\n�� " + skills[i].SkillDescription;
                    }
```

Replace with:

```csharp
                if (skills != null && skills.Count > 0)
                {
                    _skillDescriptionText.text = "● " + skills[0].description;
                    for (int i = 1; i < skills.Count; i++)
                    {
                        _skillDescriptionText.text += "\n● " + skills[i].description;
                    }
```

(The `"��"` was a corrupted bullet; replaced with `"●"` for visual clarity. Preserves the original concatenation pattern.)

- [ ] **Step 3: Compile-check**

Open in Unity Editor.

Expected: no new errors. The whole project should now compile cleanly.

- [ ] **Step 4: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/MyUI/Panels/MonsterHandbookPanel.cs
git commit -m "refactor(monster-handbook): use EntityData.Skills for skill description

Replace Prefab.GetComponents<Skill>() with monsterData.Skills. Field
reads switch from Skill.SkillDescription to SkillConfig.description.
Iteration bounds switch from .Length to .Count.

Replaces the corrupted '��' bullet prefix with a clean '●'.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 8: Final verification — grep, compile, PlayMode smoke

**Files:** none modified in this task.

- [ ] **Step 1: Grep for residual legacy references**

Run from the project root:

```bash
grep -rn --include='*.cs' '\.skill\b' Assets/PublicScripts
```

Expected: zero non-comment matches. (The only acceptable hits are inside `//` comments, which grep may still print; if you see comment hits, manually confirm they are inside `// ...` and not live code.)

```bash
grep -rn --include='*.cs' 'GetComponents<Skill>' Assets/PublicScripts
```

Expected: zero matches.

```bash
grep -rn --include='*.cs' ': Skill\b' Assets/PublicScripts
```

Expected: zero matches. (Field declarations, parameter types, return types — none should still use `Skill` as a type. The `Skill.cs` file itself is allowed to have its own class definition.)

```bash
grep -rn --include='*.cs' 'public Skill\[\] skill' Assets/PublicScripts
```

Expected: zero matches.

- [ ] **Step 2: Project compile**

Open in Unity Editor. Wait for compile.

Expected: zero compile errors in the console.

- [ ] **Step 3: PlayMode smoke test (per spec §6.1)**

Run through each item in spec §6.1:

1. **No NRE on scene load.** Deploy any operator. Verify no console errors.
2. **Character select (deploy screen).** Operator with `Skills` populated: skill card shows configured icon/name/description/mode labels in Chinese. Operator with empty/null `Skills` or null `icon`: skill cards hidden / no icon.
3. **Character card (roster).** Same icon-presence behaviour.
4. **Monster handbook.** Monster with `Skills` populated: descriptions concatenated. Monster with no skills: skill description block hidden.
5. **Deployed operator click → operator panel open.** With `SkillRunner` and `Skills[0]`: skill open button visible; range preview visible if `skillAttackRange` non-empty. Without `SkillRunner`: skill open / range preview hidden; no NRE.
6. **Manual release.** Click skill open with full SP → `spEngine.FireSkill()` is called → combat effects fire (e.g. `AttackBoost` mult kicks in if configured). `UIStates_SwitchTo_Normal()` returns panel to base state.
7. **Manual close.** Click close button while skill active → `spEngine.EndSkill()` called. Verify combat effect cleared.
8. **Live SP state in operator panel.** Place operator, let natural SP recover; verify the SP bar / charge counter / skill-active tint updates each frame per `spEngine`.
9. **Death path.** Kill the operator mid-skill: `UIStateMachine` death-detection guard returns to normal; `Entity` returns to pool; slider self-cleans. No NRE from the now-orphaned `_selectSkillRuntime` reference.
10. **Compile check (already done in Step 2).** Zero `Skill` / `Entity.skill` references in code.

If any item fails, fix the regression in the file responsible, run Steps 1-2 again, then re-run this step.

- [ ] **Step 4: No commit (verification task)**

This task makes no source changes; nothing to commit. The plan ends here.

---

## Self-Review Checklist (Run after writing the plan)

**Spec coverage:**
- [x] §1.1 G1 (zero `Skill` / `Entity.skill[]` references) — Tasks 2, 3, 4, 5, 6, 7, 8 step 1
- [x] §1.1 G2 (UI reads from new system) — Tasks 3, 4, 5, 6, 7
- [x] §1.1 G3 (Skill.cs kept on disk) — Plan explicitly does not delete it
- [x] §1.1 G4 (no behaviour regression) — Task 4 step 4 enumerates the per-field mapping
- [x] §2.2.1 `SkillConfig.icon` — Task 1 step 1
- [x] §2.2.2 `SPConfig.skillAttackRange` — Task 1 step 2
- [x] §3.1 `Entity.skill[]` removal — Task 2
- [x] §3.2 `SkillCard` signature — Task 3
- [x] §3.3.1 field split — Task 4 step 1
- [x] §3.3.2 skill load — Task 4 step 3
- [x] §3.3.3 click handlers — Task 4 step 2
- [x] §3.3.4 live state — Task 4 step 4
- [x] §3.3.5 `SwitchShowSkillTalent` revival — Task 4 step 5
- [x] §3.3.6 field clear on close — Task 4 step 6
- [x] §3.4 `CharacterCardManager` — Task 5
- [x] §3.5 `CharacterSelectPanel` — Task 6
- [x] §3.6 `MonsterHandbookPanel` — Task 7
- [x] §6.1 PlayMode checklist — Task 8 step 3

**Placeholders:** None. Every step has either concrete code, exact commands, or a clear directive ("Replace X with Y" with the new content shown).

**Type consistency:**
- `SkillConfig` (PascalCase, no `SkillSystem.` prefix needed inside `SkillSystem` namespace) — used identically in Tasks 1, 3, 4, 5, 6, 7.
- `SPEngine` / `spEngine` / `CurrentSp` / `CurrentCharge` / `IsActive` / `CanBegin` / `FireSkill` / `EndSkill` — used consistently in Task 4.
- `SkillRuntime` — used in Task 3 (as parameter type) and Task 4 (as field type). The optional `= null` default is set in Task 3 and relied upon in Task 6 step 3.
- Field renames: `_selectSkill` → `_selectSkillConfig` + `_selectSkillRuntime` (consistent in Task 4 steps 1, 2, 3, 4, 6).
- `Vector2Int[]` (Unity type) for `skillAttackRange` (Task 1 step 2); read in Task 4 step 3 with `range != null && range.Length > 0`.
- `(float, int, bool)` tuple from old `SkillMessage` → split reads of `sp.CurrentSp`, `sp.CurrentCharge`, `sp.IsActive` (Task 4 step 4). The `currentSpRate` is recomputed in `UIStates_Update_Operator` as `sp.CurrentSp / cfg.totalSp` (clamped) — same as the old `(_currentSp / _totalSp)` value, modulo precision.
- `_selectSkill.SkillOpenMode == 3` (int) → `cfg.openMode == SkillSystem.SkillOpenMode.Manual` (enum). The integer value 3 corresponds to `Manual` in both enums (`{Auto=0, OnAttackAnimBegin=1, OnBeforeHurt=2, Manual=3, OnAttackHit=4}`) — safe enum-typed replacement.
- `_selectSkill.SpComsumeMode` (int) → `(int)cfg.consumeMode` (cast). The integer values align: `{Natural=0, OnAttackHit=1, OnAfterHurt=2, Instant=3}` — the same indexing is used to pick `_spMessageAtlas[consumeType + 1]`.
- `_selectSkill.SkillAmount` (float) → `cfg.skillDuration` (float). Both represent the active skill duration in seconds; same unit, same range.
- `_selectSkill.CanCloseSkill` (bool) → `cfg.canManualClose` (bool). Same semantic.
- `_selectSkill.SPFull()` → `sp.CurrentSp >= cfg.totalSp || (cfg.chargeNum > 1 && sp.CurrentCharge >= cfg.chargeNum)`. Mirrors `SPEngine.SPFull()` semantics.
