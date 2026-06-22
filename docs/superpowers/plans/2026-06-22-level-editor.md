# Level Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 `LevelData` ScriptableObject 提供一个自定义 Inspector 编辑器,覆盖所有字段的可视化编辑 (元数据 / 引用 / 波次时间线 / 经济),含静态校验和一键 Playtest。

**Architecture:** `[CustomEditor(typeof(LevelData))]` + UI Toolkit (`CreateInspectorGUI` 返回 `VisualElement`)。4 个 Section 类拼接 (Metadata / References / Economy / WaveTimeline + ActionDetail)。纯函数 `LevelDataValidator` 可单测。Playtest 复用 `LevelTest.unity` (自动创建) + 现有 `LevelResourceSharing.LevelInitialize/LevelStart`。

**Tech Stack:** Unity 2022.3.62f3, C#, Unity Test Framework 1.1.33 (已存在), UI Toolkit (UXML/USS/PropertyField), NUnit. EditMode tests.

**Spec:** `docs/superpowers/specs/2026-06-22-level-editor-design.md` (commit `9b053b4`).

**Project conventions:**
- 编辑器代码在 `Assets/Editor/LevelEditor/`,**不**新建 .asmdef(沿用 `Assembly-CSharp-Editor`)。
- 测试在 `Assets/Tests/EditMode/`(待新建,Task 1 搭脚手架)。
- 命名空间: 运行时类型 (`LevelData`, `EntityID`, `LevelActions`) 在全局命名空间; 编辑器代码默认全局, 不额外加 namespace (匹配项目惯例)。
- 公共类注释风格: `/// <summary>` 中文, 顶格写。
- `Debug.Log` 用法: 校验错误用 `Debug.LogWarning` 或红字 UI 提示, 不污染 console。

**Execution model:** Unity Test Framework 已在 `Packages/manifest.json` (1.1.33)。本项目**无头 headless test runner**, 每个 test 步骤需要手动开 Unity Editor 跑 Test Runner。Inspector UI 行为通过 smoke test 验证 (Task 17)。

---

## File Structure

### New files

```
Assets/Editor/LevelEditor/
├── LevelDataEditor.cs
│   ([CustomEditor(typeof(LevelData))], override CreateInspectorGUI)

Assets/Editor/LevelEditor/Sections/
├── MetadataSection.cs
│   (Name/Code/Description/Camera/Cutscene PropertyField 拼接)
├── ReferencesSection.cs
│   (MapPrefab/EnvControl/CheckPoints/WaveEntityPrefabIDs)
├── EconomySection.cs
│   (LevelHp/Cost0/MaxCost/CanSetNum/CostRecoverSpeed)
├── WaveTimelineSection.cs
│   (横向时间线 + 卡片 + ActionSelected 事件)
└── ActionDetailSection.cs
    (选中 Action 的内联条件字段, CommandType 0-6 显隐)

Assets/Editor/LevelEditor/Validation/
├── LevelDataValidator.cs
│   (纯静态 Validate(LevelData) -> List<ValidationIssue>)
└── ValidationBar.cs
    (底部状态条 UI)

Assets/Editor/LevelEditor/Playtest/
├── PlaytestLauncher.cs
│   (Playtest 按钮 + 校验 + 切场景 + EnterPlay)
└── LevelTestSceneBuilder.cs
    (EnsureScene: 检查/创建 Assets/Scenes/LevelTest.unity)

Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelTestStarter.cs
    (运行时 MonoBehaviour: 读 LevelDataToPlay 字段, 调 LevelInitialize/LevelStart)
    * 不放 Assets/Editor/ 因为它是运行时组件, 挂在测试场景的 GameObject 上

Assets/Editor/LevelEditor/LevelEditorStyles.uss
│   (共享样式: header / tab / timeline-card / status-bar / invalid-field)
└── Tests/
    └── LevelDataValidatorTests.cs
        (10 条规则的 EditMode 单测)

Assets/Tests/EditMode/EditMode.asmdef
    (NUnit + Unity Test Framework)

Assets/Scenes/LevelTest.unity
    (Task 14 自动创建, 包含 LM/MCam/UICam/LevelTestStarter)
```

### Modified files

无。运行时代码 (`LevelData`, `LevelActions`, `LevelResourceSharing`, `MapDataManager`, `EntityManager` 等) **零修改**。

### Files NOT modified

所有 `Assets/PublicScripts/` 下的运行时代码。`Assets/Editor/StaticDataValidator.cs`, `Assets/Editor/SkillSystem/`。`Packages/manifest.json` (Test Framework 已存在)。

---

## Task 1: EditMode 测试脚手架

**Files:**
- Create: `Assets/Tests/EditMode/EditMode.asmdef`
- Create: `Assets/Tests/EditMode/SmokeTests.cs`

- [ ] **Step 1: 创建测试目录**

```bash
mkdir -p Assets/Tests/EditMode
```

(Windows PowerShell / cmd 下用 `mkdir Assets\Tests\EditMode` 或在 IDE 里新建文件夹。)

- [ ] **Step 2: 创建 `Assets/Tests/EditMode/EditMode.asmdef`**

```json
{
    "name": "EditMode",
    "rootNamespace": "Tests.EditMode",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 3: 创建 smoke test `Assets/Tests/EditMode/SmokeTests.cs`**

```csharp
using NUnit.Framework;

namespace Tests.EditMode
{
    public class SmokeTests
    {
        [Test]
        public void Always_Passes()
        {
            Assert.AreEqual(1 + 1, 2);
        }
    }
}
```

- [ ] **Step 4: 在 Unity 里跑 SmokeTests**

打开 Unity, 等编译完。菜单 `Window > General > Test Runner`, 切到 EditMode tab, 点 `Run All`。预期: 1 个测试通过, 0 失败。

- [ ] **Step 5: Commit**

```bash
git add Assets/Tests/EditMode/EditMode.asmdef Assets/Tests/EditMode/SmokeTests.cs
git commit -m "test(level-editor): 搭 EditMode 测试脚手架"
```

---

## Task 2: `LevelDataValidator` 数据类 + 第一条规则 (TDD)

**Files:**
- Create: `Assets/Editor/LevelEditor/Validation/ValidationIssue.cs`
- Create: `Assets/Tests/EditMode/LevelDataValidatorTests.cs`

- [ ] **Step 1: 写失败测试 (规则 1: Waves 为空)**

`Assets/Tests/EditMode/LevelDataValidatorTests.cs`:

```csharp
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public class LevelDataValidatorTests
    {
        LevelData _data;

        [SetUp]
        public void SetUp()
        {
            _data = ScriptableObject.CreateInstance<LevelData>();
            // 填一个能通过大部分规则的最小有效配置
            _data.LevelName = "TestLevel";
            _data.LevelCode = "test";
            _data.CameraSize = 5f;
            _data.CameraPos = Vector2.zero;
            _data.MapPrefab = new GameObject("dummy_map");
            _data.EnvironmentalControlDevice = null;
            _data.Waves = new LevelActions.Wave[] { new LevelActions.Wave { Actions = new LevelActions.Action[0] } };
            _data.CheckPoints = new GameObject[0];
            _data.WaveEntityPrefabIDs = new EntityID[] { new EntityID("c", 1) };
            _data.LevelHp = 10;
            _data.Cost0 = 100;
            _data.MaxCost = 200;
            _data.CanSetNum = 5;
            _data.CostRecoverSpeed = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            if (_data.MapPrefab != null) Object.DestroyImmediate(_data.MapPrefab);
            Object.DestroyImmediate(_data);
        }

        [Test]
        public void Validate_EmptyWaves_ReturnsError()
        {
            _data.Waves = new LevelActions.Wave[0];
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Path.Contains("Waves")),
                $"Expected error mentioning Waves, got: {string.Join("; ", issues.Select(i => i.Message))}");
        }
    }
}
```

- [ ] **Step 2: 跑测试, 确认失败 (编译错误: 类型不存在)**

在 Test Runner 跑 `LevelDataValidatorTests.Validate_EmptyWaves_ReturnsError`。预期: 编译失败 (`LevelDataValidator` 和 `ValidationIssue` 不存在)。

- [ ] **Step 3: 创建 `ValidationIssue` 类**

`Assets/Editor/LevelEditor/Validation/ValidationIssue.cs`:

```csharp
using System;

namespace Validation
{
    public enum ValidationSeverity
    {
        Warning,
        Error,
    }

    /// <summary>
    /// 单条校验结果: 严重度 + 字段路径 + 可读消息。
    /// </summary>
    [Serializable]
    public struct ValidationIssue
    {
        public ValidationSeverity Severity;
        public string Path;
        public string Message;

        public ValidationIssue(ValidationSeverity severity, string path, string message)
        {
            Severity = severity;
            Path = path;
            Message = message;
        }
    }
}
```

(命名空间 `Validation` 仅用于编辑器内部类型, 避免污染全局。)

- [ ] **Step 4: 编译, 跑测试, 确认失败 (现在编译过, 但 `LevelDataValidator.Validate` 还不存在)**

预期: 编译失败 (`LevelDataValidator` 不存在)。

- [ ] **Step 5: 创建 `LevelDataValidator` 骨架 (实现规则 1)**

`Assets/Editor/LevelEditor/Validation/LevelDataValidator.cs`:

```csharp
using System.Collections.Generic;

namespace Validation
{
    /// <summary>
    /// 纯函数校验: 给定 LevelData, 返回发现的 issue 列表。
    /// 不依赖 UI, 不修改传入数据。
    /// </summary>
    public static class LevelDataValidator
    {
        public static List<ValidationIssue> Validate(LevelData data)
        {
            var issues = new List<ValidationIssue>();
            if (data == null) return issues;

            // 规则 1: Waves 为 null 或空 -> Error
            if (data.Waves == null || data.Waves.Length == 0)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "Waves", "Waves 不能为空"));
            }

            return issues;
        }
    }
}
```

- [ ] **Step 6: 跑测试, 确认通过**

在 Test Runner 跑 `LevelDataValidatorTests.Validate_EmptyWaves_ReturnsError`。预期: 通过。

- [ ] **Step 7: Commit**

```bash
git add Assets/Editor/LevelEditor/Validation/ValidationIssue.cs \
        Assets/Editor/LevelEditor/Validation/LevelDataValidator.cs \
        Assets/Tests/EditMode/LevelDataValidatorTests.cs
git commit -m "feat(level-editor): 规则 1 (Waves 不能为空) + Validator 骨架"
```

---

## Task 3: 规则 2-4 (Wave / WaveEntityPrefabIDs / EntityPrefabSerial 越界) TDD

**Files:**
- Modify: `Assets/Editor/LevelEditor/Validation/LevelDataValidator.cs`
- Modify: `Assets/Tests/EditMode/LevelDataValidatorTests.cs`

- [ ] **Step 1: 追加测试 (规则 2-4)**

把以下测试方法追加到 `LevelDataValidatorTests` 类 (在最后一个 `}` 之前):

```csharp
        [Test]
        public void Validate_WaveWithEmptyActions_ReturnsWarning()
        {
            _data.Waves = new LevelActions.Wave[] {
                new LevelActions.Wave { Actions = new LevelActions.Action[0] }
            };
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Warning && i.Message.Contains("空")),
                $"Expected warning about empty Wave actions, got: {string.Join("; ", issues.Select(i => i.Message))}");
        }

        [Test]
        public void Validate_EmptyWaveEntityPrefabIDs_ReturnsError()
        {
            _data.WaveEntityPrefabIDs = new EntityID[0];
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Path.Contains("WaveEntityPrefabIDs")),
                $"Expected error about WaveEntityPrefabIDs, got: {string.Join("; ", issues.Select(i => i.Message))}");
        }

        [Test]
        public void Validate_EntityPrefabSerialOutOfRange_ReturnsError()
        {
            _data.Waves = new LevelActions.Wave[] {
                new LevelActions.Wave { Actions = new LevelActions.Action[] {
                    new LevelActions.Action { CommandType = 0, EntityPrefabSerial = 99 }
                } }
            };
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Message.Contains("EntityPrefabSerial")),
                $"Expected error about EntityPrefabSerial range, got: {string.Join("; ", issues.Select(i => i.Message))}");
        }
```

- [ ] **Step 2: 跑测试, 确认 3 个新测试都失败 (Waves 不空时规则 2-4 还没实现)**

- [ ] **Step 3: 实现规则 2-4**

替换 `LevelDataValidator.Validate` 的整个方法体 (在 `if (data == null) return issues;` 之后追加):

```csharp
            // 规则 2: 任一 Wave.Actions 为 null 或空 -> Warning
            if (data.Waves != null)
            {
                for (int i = 0; i < data.Waves.Length; i++)
                {
                    var wave = data.Waves[i];
                    if (wave.Actions == null || wave.Actions.Length == 0)
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Warning,
                            $"Waves[{i}].Actions",
                            $"Wave {i} 的 Actions 为空"));
                    }
                }
            }

            // 规则 3: WaveEntityPrefabIDs 为 null 或空 -> Error
            if (data.WaveEntityPrefabIDs == null || data.WaveEntityPrefabIDs.Length == 0)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "WaveEntityPrefabIDs",
                    "WaveEntityPrefabIDs 不能为空"));
            }

            // 规则 4: EntityPrefabSerial 越界 -> Error
            if (data.Waves != null && data.WaveEntityPrefabIDs != null)
            {
                int prefabCount = data.WaveEntityPrefabIDs.Length;
                for (int w = 0; w < data.Waves.Length; w++)
                {
                    var actions = data.Waves[w].Actions;
                    if (actions == null) continue;
                    for (int a = 0; a < actions.Length; a++)
                    {
                        var act = actions[a];
                        // 仅对需要 EntityPrefabSerial 的 CommandType 校验 (0, 1)
                        if (act.CommandType == 0 || act.CommandType == 1)
                        {
                            if (act.EntityPrefabSerial < 0 || act.EntityPrefabSerial >= prefabCount)
                            {
                                issues.Add(new ValidationIssue(
                                    ValidationSeverity.Error,
                                    $"Waves[{w}].Actions[{a}].EntityPrefabSerial",
                                    $"EntityPrefabSerial={act.EntityPrefabSerial} 越界 (有效范围 0..{prefabCount - 1})"));
                            }
                        }
                    }
                }
            }
```

- [ ] **Step 4: 跑全部 LevelDataValidatorTests, 确认 4 个测试都通过**

- [ ] **Step 5: Commit**

```bash
git add Assets/Editor/LevelEditor/Validation/LevelDataValidator.cs \
        Assets/Tests/EditMode/LevelDataValidatorTests.cs
git commit -m "feat(level-editor): 规则 2-4 (Wave 空 / PrefabIDs 空 / EntityPrefabSerial 越界)"
```

---

## Task 4: 规则 6-10 (经济 + 必需引用) TDD

**Files:**
- Modify: `Assets/Editor/LevelEditor/Validation/LevelDataValidator.cs`
- Modify: `Assets/Tests/EditMode/LevelDataValidatorTests.cs`

- [ ] **Step 1: 追加测试 (规则 6-10)**

把以下测试追加到 `LevelDataValidatorTests` 类:

```csharp
        [Test]
        public void Validate_LevelHpZeroOrNegative_ReturnsError()
        {
            _data.LevelHp = 0;
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Path.Contains("LevelHp")));
        }

        [Test]
        public void Validate_MaxCostLessThanCost0_ReturnsError()
        {
            _data.Cost0 = 200;
            _data.MaxCost = 100;
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Path.Contains("MaxCost")));
        }

        [Test]
        public void Validate_NullMapPrefab_ReturnsError()
        {
            _data.MapPrefab = null;
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Path.Contains("MapPrefab")));
        }

        [Test]
        public void Validate_NullCutToLevelTexture_ReturnsWarning()
        {
            _data.CutToLevelTexture = null;
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Warning && i.Path.Contains("CutToLevelTexture")));
        }

        [Test]
        public void Validate_ZeroCameraSize_ReturnsError()
        {
            _data.CameraSize = 0f;
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Path.Contains("CameraSize")));
        }
```

- [ ] **Step 2: 跑测试, 确认 5 个新测试都失败**

- [ ] **Step 3: 实现规则 6-10**

在 `LevelDataValidator.Validate` 末尾 (在规则 4 的 `}` 之后) 追加:

```csharp
            // 规则 6: LevelHp <= 0 -> Error
            if (data.LevelHp <= 0)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "LevelHp",
                    $"LevelHp 必须 > 0, 当前 {data.LevelHp}"));
            }

            // 规则 7: MaxCost < Cost0 -> Error
            if (data.MaxCost < data.Cost0)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "MaxCost",
                    $"MaxCost ({data.MaxCost}) 不能小于 Cost0 ({data.Cost0})"));
            }

            // 规则 8: MapPrefab == null -> Error
            if (data.MapPrefab == null)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "MapPrefab",
                    "MapPrefab 不能为空"));
            }

            // 规则 9: CutToLevelTexture == null -> Warning
            if (data.CutToLevelTexture == null)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "CutToLevelTexture",
                    "CutToLevelTexture 未指定 (可选)"));
            }

            // 规则 10: CameraSize <= 0 -> Error
            if (data.CameraSize <= 0f)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "CameraSize",
                    $"CameraSize 必须 > 0, 当前 {data.CameraSize}"));
            }
```

- [ ] **Step 4: 跑全部 LevelDataValidatorTests, 确认 9 个测试都通过**

- [ ] **Step 5: Commit**

```bash
git add Assets/Editor/LevelEditor/Validation/LevelDataValidator.cs \
        Assets/Tests/EditMode/LevelDataValidatorTests.cs
git commit -m "feat(level-editor): 规则 6-10 (经济 / MapPrefab / CameraSize)"
```

---

## Task 5: `LevelDataEditor` 骨架

**Files:**
- Create: `Assets/Editor/LevelEditor/LevelDataEditor.cs`
- Create: `Assets/Editor/LevelEditor/LevelEditorStyles.uss`

- [ ] **Step 1: 创建 `LevelEditorStyles.uss`**

`Assets/Editor/LevelEditor/LevelEditorStyles.uss`:

```css
.level-editor-header {
    background-color: rgb(42, 42, 53);
    padding: 8px 12px;
    flex-direction: row;
    align-items: center;
}

.level-editor-title {
    font-size: 16px;
    color: rgb(255, 255, 255);
    -unity-font-style: bold;
}

.level-editor-subtitle {
    color: rgb(136, 136, 136);
    font-size: 11px;
    margin-left: 12px;
}

.level-editor-badge {
    margin-left: auto;
    padding: 2px 8px;
    border-radius: 8px;
    font-size: 11px;
    -unity-font-style: bold;
}

.level-editor-badge-ok {
    background-color: rgb(46, 125, 50);
    color: rgb(255, 255, 255);
}

.level-editor-badge-warn {
    background-color: rgb(200, 150, 30);
    color: rgb(0, 0, 0);
}

.level-editor-badge-error {
    background-color: rgb(198, 40, 40);
    color: rgb(255, 255, 255);
}

.level-editor-tabbar {
    flex-direction: row;
    background-color: rgb(37, 37, 48);
}

.level-editor-tab {
    padding: 6px 14px;
    font-size: 11px;
    color: rgb(136, 136, 136);
    border-bottom-width: 2px;
    border-bottom-color: rgba(0, 0, 0, 0);
}

.level-editor-tab-active {
    color: rgb(78, 201, 160);
    border-bottom-color: rgb(78, 201, 160);
}

.level-editor-section {
    background-color: rgb(30, 30, 34);
    padding: 10px;
    border-radius: 4px;
    margin-bottom: 12px;
}

.level-editor-section-title {
    color: rgb(156, 220, 254);
    font-size: 12px;
    -unity-font-style: bold;
    margin-bottom: 6px;
}

.level-editor-statusbar {
    background-color: rgb(42, 42, 53);
    padding: 10px 12px;
    flex-direction: row;
    align-items: center;
    border-top-width: 1px;
    border-top-color: rgb(68, 68, 68);
}

.level-editor-statusbar-text {
    color: rgb(78, 201, 160);
    font-size: 11px;
}

.level-editor-playtest-btn {
    margin-left: auto;
    background-color: rgb(220, 220, 170);
    color: rgb(0, 0, 0);
    -unity-font-style: bold;
    font-size: 12px;
    padding: 4px 12px;
    border-radius: 3px;
}
```

- [ ] **Step 2: 创建 `LevelDataEditor.cs` (骨架: header + 标签 + 占位内容)**

`Assets/Editor/LevelEditor/LevelDataEditor.cs`:

```csharp
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// LevelData 的自定义 Inspector (UI Toolkit)。
/// 触发条件: 在 Project 窗口选中 LevelData 资产。
/// </summary>
[CustomEditor(typeof(LevelData))]
public class LevelDataEditor : Editor
{
    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        root.style.flexDirection = FlexDirection.Column;

        // Header
        var header = new VisualElement();
        header.AddToClassList("level-editor-header");
        var title = new Label(((LevelData)target).LevelName);
        title.AddToClassList("level-editor-title");
        header.Add(title);
        root.Add(header);

        // Body placeholder
        var body = new VisualElement();
        body.style.paddingLeft = 12;
        body.style.paddingRight = 12;
        body.style.paddingTop = 12;
        body.style.paddingBottom = 12;
        body.Add(new Label("(Sections will be added in subsequent tasks)"));
        root.Add(body);

        return root;
    }
}
```

- [ ] **Step 3: 在 Unity 里确认能加载**

打开 Unity, 编译完。Project 窗口右键 → Create → TD → (找 LevelData 菜单项) → 创建一个新的 `LevelData.asset`。点选它, 确认 Inspector 出现自定义 header (显示 LevelName)。

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/LevelEditor/LevelDataEditor.cs \
        Assets/Editor/LevelEditor/LevelEditorStyles.uss
git commit -m "feat(level-editor): 自定义 Inspector 骨架 + 共享样式"
```

---

## Task 6: `MetadataSection`

**Files:**
- Create: `Assets/Editor/LevelEditor/Sections/MetadataSection.cs`
- Modify: `Assets/Editor/LevelEditor/LevelDataEditor.cs` (接入)

- [ ] **Step 1: 创建 `MetadataSection.cs`**

`Assets/Editor/LevelEditor/Sections/MetadataSection.cs`:

```csharp
using UnityEditor;
using UnityEngine.UIElements;

/// <summary>
/// 元数据节: LevelName / LevelCode / LevelDescription / CameraSize / CameraPos / CutToLevelTexture。
/// </summary>
public static class MetadataSection
{
    public static VisualElement Build(SerializedObject so)
    {
        var section = new VisualElement();
        section.AddToClassList("level-editor-section");

        var title = new Label("▸ 元数据");
        title.AddToClassList("level-editor-section-title");
        section.Add(title);

        section.Add(MakeRow("LevelName",          so.FindProperty("LevelName")));
        section.Add(MakeRow("LevelCode",          so.FindProperty("LevelCode")));
        section.Add(MakeRow("LevelDescription",   so.FindProperty("LevelDescription")));
        section.Add(MakeRow("CameraSize",         so.FindProperty("CameraSize")));
        section.Add(MakeRow("CameraPos",          so.FindProperty("CameraPos")));
        section.Add(MakeRow("CutToLevelTexture",  so.FindProperty("CutToLevelTexture")));

        return section;
    }

    static VisualElement MakeRow(string label, SerializedProperty prop)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginBottom = 2;

        var lab = new Label(label);
        lab.style.width = 130;
        lab.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
        row.Add(lab);

        var field = new PropertyField(prop);
        field.style.flexGrow = 1;
        field.BindProperty(prop);
        row.Add(field);

        return row;
    }
}
```

- [ ] **Step 2: 在 `LevelDataEditor` 里替换 placeholder**

替换 `LevelDataEditor.CreateInspectorGUI` 中 `// Body placeholder` 之后到 `return root;` 之前的部分, 为:

```csharp
        // Body
        var body = new VisualElement();
        body.style.paddingLeft = 12;
        body.style.paddingRight = 12;
        body.style.paddingTop = 12;
        body.style.paddingBottom = 12;
        body.Add(MetadataSection.Build(serializedObject));
        root.Add(body);
```

(在第 5 步, 删除 `body.Add(new Label("(Sections will be added in subsequent tasks)"));` 那行。)

- [ ] **Step 3: 在 Unity 里验证**

选中 LevelData asset, 检查 Inspector 出现 6 个字段 (Name/Code/Description/CameraSize/CameraPos/CutToLevelTexture)。改一个值, 关闭 Inspector, 再打开, 值保留 (说明序列化正常)。

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/LevelEditor/Sections/MetadataSection.cs \
        Assets/Editor/LevelEditor/LevelDataEditor.cs
git commit -m "feat(level-editor): MetadataSection (6 个基础字段)"
```

---

## Task 7: `EconomySection`

**Files:**
- Create: `Assets/Editor/LevelEditor/Sections/EconomySection.cs`
- Modify: `Assets/Editor/LevelEditor/LevelDataEditor.cs`

- [ ] **Step 1: 创建 `EconomySection.cs`**

`Assets/Editor/LevelEditor/Sections/EconomySection.cs`:

```csharp
using UnityEditor;
using UnityEngine.UIElements;

/// <summary>
/// 经济节: LevelHp / Cost0 / MaxCost / CanSetNum / CostRecoverSpeed。
/// </summary>
public static class EconomySection
{
    public static VisualElement Build(SerializedObject so)
    {
        var section = new VisualElement();
        section.AddToClassList("level-editor-section");

        var title = new Label("▸ 经济");
        title.AddToClassList("level-editor-section-title");
        section.Add(title);

        section.Add(MakeRow("LevelHp",           so.FindProperty("LevelHp")));
        section.Add(MakeRow("Cost0",             so.FindProperty("Cost0")));
        section.Add(MakeRow("MaxCost",           so.FindProperty("MaxCost")));
        section.Add(MakeRow("CanSetNum",         so.FindProperty("CanSetNum")));
        section.Add(MakeRow("CostRecoverSpeed",  so.FindProperty("CostRecoverSpeed")));

        return section;
    }

    static VisualElement MakeRow(string label, SerializedProperty prop)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginBottom = 2;

        var lab = new Label(label);
        lab.style.width = 160;
        lab.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
        row.Add(lab);

        var field = new PropertyField(prop);
        field.style.flexGrow = 1;
        field.BindProperty(prop);
        row.Add(field);

        return row;
    }
}
```

- [ ] **Step 2: 在 `LevelDataEditor` 里接入**

在 `body.Add(MetadataSection.Build(serializedObject));` 这一行**之后**追加:

```csharp
        body.Add(EconomySection.Build(serializedObject));
```

- [ ] **Step 3: 在 Unity 里验证**

选中 LevelData asset, 检查经济节出现 5 个字段。

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/LevelEditor/Sections/EconomySection.cs \
        Assets/Editor/LevelEditor/LevelDataEditor.cs
git commit -m "feat(level-editor): EconomySection (5 个经济字段)"
```

---

## Task 8: `ReferencesSection` (ObjectField + ListView 骨架)

**Files:**
- Create: `Assets/Editor/LevelEditor/Sections/ReferencesSection.cs`
- Modify: `Assets/Editor/LevelEditor/LevelDataEditor.cs`

- [ ] **Step 1: 创建 `ReferencesSection.cs` (单 ObjectField, ListView 后续任务细化)**

`Assets/Editor/LevelEditor/Sections/ReferencesSection.cs`:

```csharp
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

/// <summary>
/// 引用节: MapPrefab / EnvironmentalControlDevice (单 GameObject) + CheckPoints / WaveEntityPrefabIDs (数组)。
/// </summary>
public static class ReferencesSection
{
    public static VisualElement Build(SerializedObject so)
    {
        var section = new VisualElement();
        section.AddToClassList("level-editor-section");

        var title = new Label("▸ 引用 (只填, 不做内部编辑)");
        title.AddToClassList("level-editor-section-title");
        section.Add(title);

        section.Add(MakeSingleRow("MapPrefab",                 so.FindProperty("MapPrefab")));
        section.Add(MakeSingleRow("EnvironmentalControlDevice", so.FindProperty("EnvironmentalControlDevice")));

        // 数组
        var checkpointsProp = so.FindProperty("CheckPoints");
        var idsProp = so.FindProperty("WaveEntityPrefabIDs");
        section.Add(MakeArrayRow("CheckPoints (拖拽 GameObject)",        checkpointsProp));
        section.Add(MakeArrayRow("WaveEntityPrefabIDs (拖拽 EntityData)", idsProp));

        return section;
    }

    static VisualElement MakeSingleRow(string label, SerializedProperty prop)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginBottom = 4;

        var lab = new Label(label);
        lab.style.width = 200;
        lab.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
        row.Add(lab);

        var field = new PropertyField(prop);
        field.style.flexGrow = 1;
        field.BindProperty(prop);
        row.Add(field);

        return row;
    }

    static VisualElement MakeArrayRow(string label, SerializedProperty arrayProp)
    {
        var row = new VisualElement();
        row.style.marginBottom = 8;

        var lab = new Label(label);
        lab.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
        lab.style.marginBottom = 2;
        row.Add(lab);

        // 直接 PropertyField 让 Unity 自己渲染 ListView (Unity 2022 ListView binding)
        var field = new PropertyField(arrayProp);
        field.BindProperty(arrayProp);
        row.Add(field);

        return row;
    }
}
```

- [ ] **Step 2: 在 `LevelDataEditor` 里接入**

在 `body.Add(EconomySection.Build(serializedObject));` 这一行**之后**追加:

```csharp
        body.Add(ReferencesSection.Build(serializedObject));
```

- [ ] **Step 3: 在 Unity 里验证**

选中 LevelData asset, 检查引用节出现 4 个字段: MapPrefab, EnvironmentalControlDevice, CheckPoints, WaveEntityPrefabIDs。点 CheckPoints 的 + 按钮, 确认能加项。

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/LevelEditor/Sections/ReferencesSection.cs \
        Assets/Editor/LevelEditor/LevelDataEditor.cs
git commit -m "feat(level-editor): ReferencesSection (2 单字段 + 2 数组)"
```

---

## Task 9: `ActionDetailSection` 基础字段 (CommandType + Gap + OnBeforeAction)

**Files:**
- Create: `Assets/Editor/LevelEditor/Sections/ActionDetailSection.cs`

- [ ] **Step 1: 创建 `ActionDetailSection.cs` (本任务只放基础字段, 条件字段下一任务做)**

`Assets/Editor/LevelEditor/Sections/ActionDetailSection.cs`:

```csharp
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Action 详情面板: 接收 waveIdx/actionIdx, 绑定对应 SerializedProperty 子路径。
/// 基础字段: CommandType / GapFromLastAction / OnBeforeAction。
/// 条件字段 (按 CommandType 显隐): 后续任务添加。
/// </summary>
public static class ActionDetailSection
{
    public static VisualElement Build(SerializedObject so, int waveIdx, int actionIdx)
    {
        var section = new VisualElement();
        section.style.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
        section.style.paddingTop = 8;
        section.style.paddingBottom = 8;
        section.style.paddingLeft = 8;
        section.style.paddingRight = 8;
        section.style.borderTopLeftRadius = 3;
        section.style.borderTopRightRadius = 3;
        section.style.borderBottomLeftRadius = 3;
        section.style.borderBottomRightRadius = 3;
        section.style.borderLeftWidth = 1;
        section.style.borderRightWidth = 1;
        section.style.borderTopWidth = 1;
        section.style.borderBottomWidth = 1;
        section.style.borderLeftColor = new Color(0.3f, 0.8f, 0.6f);
        section.style.borderRightColor = new Color(0.3f, 0.8f, 0.6f);
        section.style.borderTopColor = new Color(0.3f, 0.8f, 0.6f);
        section.style.borderBottomColor = new Color(0.3f, 0.8f, 0.6f);

        var actionsProp = so.FindProperty($"Waves.Array.data[{waveIdx}].Actions");
        if (actionsProp == null)
        {
            section.Add(new Label($"Wave {waveIdx} not found"));
            return section;
        }
        var actionProp = actionsProp.GetArrayElementAtIndex(actionIdx);
        if (actionProp == null)
        {
            section.Add(new Label($"Action {actionIdx} not found"));
            return section;
        }

        // 顶部信息条
        var header = new Label($"▸ 已选 Wave[{waveIdx}].Action[{actionIdx}]");
        header.style.color = new Color(0.3f, 0.8f, 0.6f);
        header.style.fontSize = 10;
        header.style.marginBottom = 4;
        section.Add(header);

        // 基础字段 (始终显示)
        section.Add(MakeRow("CommandType",        actionProp.FindPropertyRelative("CommandType")));
        section.Add(MakeRow("GapFromLastAction",  actionProp.FindPropertyRelative("GapFromLastAction")));
        section.Add(MakeUnityEventRow("OnBeforeAction", actionProp.FindPropertyRelative("OnBeforeAction")));

        return section;
    }

    static VisualElement MakeRow(string label, SerializedProperty prop)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginBottom = 2;

        var lab = new Label(label);
        lab.style.width = 160;
        lab.style.color = new Color(0.5f, 0.5f, 0.5f);
        row.Add(lab);

        var field = new PropertyField(prop);
        field.style.flexGrow = 1;
        field.BindProperty(prop);
        row.Add(field);

        return row;
    }

    /// <summary>
    /// UnityEvent 字段: UI Toolkit 的 PropertyField 对 UnityEvent 渲染有限, 这里用 IMGUIContainer 包一层 IMGUI。
    /// </summary>
    static VisualElement MakeUnityEventRow(string label, SerializedProperty prop)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginBottom = 2;

        var lab = new Label(label);
        lab.style.width = 160;
        lab.style.color = new Color(0.5f, 0.5f, 0.5f);
        row.Add(lab);

        var imgui = new IMGUIContainer(() =>
        {
            if (prop != null) EditorGUILayout.PropertyField(prop);
        });
        imgui.style.flexGrow = 1;
        row.Add(imgui);

        return row;
    }
}
```

- [ ] **Step 2: 编译, 确保无错误**

打开 Unity, 等编译。Console 应无错误。

- [ ] **Step 3: 暂时不接到 `LevelDataEditor` (等待 WaveTimelineSection 给出 ActionSelected 事件后再接)**

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/LevelEditor/Sections/ActionDetailSection.cs
git commit -m "feat(level-editor): ActionDetailSection 基础字段 (3 个)"
```

---

## Task 10: `ActionDetailSection` 条件字段 (CommandType 0-6 显隐)

**Files:**
- Modify: `Assets/Editor/LevelEditor/Sections/ActionDetailSection.cs`

- [ ] **Step 1: 在 `ActionDetailSection.Build` 末尾追加条件字段容器**

在 `return section;` 之前, 在 `// 基础字段` 块之后插入:

```csharp
        // 条件字段容器 (按 CommandType 显隐)
        var conditionalContainer = new VisualElement();
        section.Add(conditionalContainer);

        // CommandType 变化时重建条件容器
        var commandTypeProp = actionProp.FindPropertyRelative("CommandType");
        RebuildConditional(conditionalContainer, actionProp, commandTypeProp.intValue);
        commandTypeProp.RegisterValueChangeCallback(evt =>
        {
            RebuildConditional(conditionalContainer, actionProp, evt.changedProperty.intValue);
        });
```

- [ ] **Step 2: 实现 `RebuildConditional` 静态方法**

在 `ActionDetailSection` 类内 (在 `MakeUnityEventRow` 之后) 添加:

```csharp
    static void RebuildConditional(VisualElement container, SerializedProperty actionProp, int commandType)
    {
        container.Clear();

        // CommandType 0/1: 召唤/静止实体
        if (commandType == 0 || commandType == 1)
        {
            var title = new Label("▸ 召唤/静止参数");
            title.style.color = new Color(0.3f, 0.8f, 0.6f);
            title.style.fontSize = 10;
            title.style.marginTop = 4;
            title.style.marginBottom = 4;
            container.Add(title);

            container.Add(MakeRow("EntityPrefabSerial", actionProp.FindPropertyRelative("EntityPrefabSerial")));
            container.Add(MakeRow("Camp",               actionProp.FindPropertyRelative("Camp")));
            if (commandType == 0)
            {
                container.Add(MakeUnityEventRow("OnActionRepeat", actionProp.FindPropertyRelative("OnActionRepeat")));
            }
        }

        // CommandType 0/2/3/4: 路径
        if (commandType == 0 || commandType == 2 || commandType == 3 || commandType == 4)
        {
            container.Add(MakeRow("PathSerial", actionProp.FindPropertyRelative("PathSerial")));
        }

        // CommandType 0: 重复召唤
        if (commandType == 0)
        {
            var t = new Label("▸ 重复召唤 (仅 CommandType 0)");
            t.style.color = new Color(0.3f, 0.8f, 0.6f);
            t.style.fontSize = 10;
            t.style.marginTop = 4;
            t.style.marginBottom = 4;
            container.Add(t);

            container.Add(MakeRow("GapsFromLastRepeat", actionProp.FindPropertyRelative("GapsFromLastRepeat")));
            container.Add(MakeRow("ModifyAttributes",   actionProp.FindPropertyRelative("ModifyAttributes")));

            var modifyProp = actionProp.FindPropertyRelative("ModifyAttributes");
            if (modifyProp.boolValue)
            {
                container.Add(MakeRow("ModifyLevelHpConsume", actionProp.FindPropertyRelative("ModifyLevelHpConsume")));
                container.Add(MakeRow("ModifyPrimary",        actionProp.FindPropertyRelative("ModifyPrimary")));
                container.Add(MakeRow("ModifyCountOperate",   actionProp.FindPropertyRelative("ModifyCountOperate")));
            }
            modifyProp.RegisterValueChangeCallback(_ =>
            {
                // ModifyAttributes 切换时重建 (展开/收起 ModifyLevelHpConsume 等)
                RebuildConditional(container, actionProp, commandType);
            });
        }

        // CommandType 1: 静止目标位置
        if (commandType == 1)
        {
            container.Add(MakeRow("Destination", actionProp.FindPropertyRelative("Destination")));
            container.Add(MakeRow("Orientation", actionProp.FindPropertyRelative("Orientation")));
        }

        // CommandType 5: 对话框
        if (commandType == 5)
        {
            var t = new Label("▸ 对话框 (仅 CommandType 5)");
            t.style.color = new Color(0.3f, 0.8f, 0.6f);
            t.style.fontSize = 10;
            t.style.marginTop = 4;
            t.style.marginBottom = 4;
            container.Add(t);

            container.Add(MakeRow("HeadImage",    actionProp.FindPropertyRelative("HeadImage")));
            container.Add(MakeRow("Content",      actionProp.FindPropertyRelative("Content")));
            container.Add(MakeRow("DurationTime", actionProp.FindPropertyRelative("DurationTime")));
        }

        // CommandType 6: 面板
        if (commandType == 6)
        {
            var t = new Label("▸ 面板 (仅 CommandType 6)");
            t.style.color = new Color(0.3f, 0.8f, 0.6f);
            t.style.fontSize = 10;
            t.style.marginTop = 4;
            t.style.marginBottom = 4;
            container.Add(t);

            container.Add(MakeRow("Contents", actionProp.FindPropertyRelative("Contents")));
        }
    }
```

- [ ] **Step 2: 编译验证**

打开 Unity, 等编译, Console 无错误。

- [ ] **Step 3: Commit**

```bash
git add Assets/Editor/LevelEditor/Sections/ActionDetailSection.cs
git commit -m "feat(level-editor): ActionDetailSection 条件字段 (CommandType 0-6 显隐)"
```

---

## Task 11: `WaveTimelineSection` 骨架 (Wave 列表 + Add/Delete)

**Files:**
- Create: `Assets/Editor/LevelEditor/Sections/WaveTimelineSection.cs`
- Modify: `Assets/Editor/LevelEditor/LevelDataEditor.cs` (接入)

- [ ] **Step 1: 创建 `WaveTimelineSection.cs` (本任务: Wave 列表 + 操作按钮, 不含卡片)**

`Assets/Editor/LevelEditor/Sections/WaveTimelineSection.cs`:

```csharp
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 横向波次时间线: 列出 Waves, 每条 Wave 一行; 每条 Action 渲染为卡片 (Task 12 加入)。
/// 通过事件 ActionSelected 派发"用户选中"信号给 ActionDetailSection。
/// </summary>
public static class WaveTimelineSection
{
    public static VisualElement Build(SerializedObject so, Action<int, int> onActionSelected)
    {
        var section = new VisualElement();
        section.AddToClassList("level-editor-section");

        var title = new Label("▸ 波次时间线");
        title.AddToClassList("level-editor-section-title");
        section.Add(title);

        var wavesProp = so.FindProperty("Waves");
        var wavesListContainer = new VisualElement();
        section.Add(wavesListContainer);

        // 重建: 在 onCreate / wave 数组变化时调用
        Action rebuild = () => RebuildWaves(wavesListContainer, wavesProp, so, onActionSelected);
        rebuild();

        // 监听 Waves 数组变化 (Undo/Redo, 外部 mutation)
        wavesProp.TrackPropertyValue(wavesProp, _ => rebuild());

        // + 新增 Wave 按钮
        var addWaveBtn = new Button(() =>
        {
            Undo.RecordObject(so.targetObject, "Add Wave");
            wavesProp.InsertArrayElementAtIndex(wavesProp.arraySize);
            wavesProp.GetArrayElementAtIndex(wavesProp.arraySize - 1).FindPropertyRelative("Actions").arraySize = 0;
            so.ApplyModifiedProperties();
        })
        { text = "+ 新增 Wave" };
        addWaveBtn.style.marginTop = 6;
        section.Add(addWaveBtn);

        return section;
    }

    static void RebuildWaves(VisualElement container, SerializedProperty wavesProp, SerializedObject so, Action<int, int> onActionSelected)
    {
        container.Clear();
        for (int w = 0; w < wavesProp.arraySize; w++)
        {
            container.Add(BuildWaveRow(w, wavesProp.GetArrayElementAtIndex(w), so, onActionSelected));
        }
    }

    static VisualElement BuildWaveRow(int waveIdx, SerializedProperty waveProp, SerializedObject so, Action<int, int> onActionSelected)
    {
        var row = new VisualElement();
        row.style.backgroundColor = new Color(0.15f, 0.15f, 0.18f);
        row.style.borderTopLeftRadius = 3;
        row.style.borderTopRightRadius = 3;
        row.style.borderBottomLeftRadius = 3;
        row.style.borderBottomRightRadius = 3;
        row.style.paddingTop = 4;
        row.style.paddingBottom = 4;
        row.style.paddingLeft = 6;
        row.style.paddingRight = 6;
        row.style.marginBottom = 6;

        // Wave 标签
        var actionsProp = waveProp.FindPropertyRelative("Actions");
        var label = new Label($"Wave {waveIdx} · {actionsProp.arraySize} actions");
        label.style.color = new Color(0.8f, 0.8f, 0.8f);
        label.style.fontSize = 11;
        label.style.marginBottom = 4;
        row.Add(label);

        // Action 卡片容器 (Task 12 填充)
        var cardsContainer = new VisualElement();
        cardsContainer.style.height = 32;
        cardsContainer.style.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
        cardsContainer.style.borderTopLeftRadius = 3;
        cardsContainer.style.borderTopRightRadius = 3;
        cardsContainer.style.borderBottomLeftRadius = 3;
        cardsContainer.style.borderBottomRightRadius = 3;
        row.Add(cardsContainer);

        // + 新增 Action 按钮
        var addActionBtn = new Button(() =>
        {
            Undo.RecordObject(so.targetObject, "Add Action");
            actionsProp.InsertArrayElementAtIndex(actionsProp.arraySize);
            actionsProp.GetArrayElementAtIndex(actionsProp.arraySize - 1).FindPropertyRelative("CommandType").intValue = 0;
            so.ApplyModifiedProperties();
        })
        { text = "+ 新增 Action" };
        addActionBtn.style.marginTop = 4;
        row.Add(addActionBtn);

        // 删除 Wave 按钮
        var delBtn = new Button(() =>
        {
            if (EditorUtility.DisplayDialog("删除 Wave", $"确认删除 Wave {waveIdx}?", "删除", "取消"))
            {
                Undo.RecordObject(so.targetObject, "Delete Wave");
                waveProp.serializedObject.FindProperty("Waves").DeleteArrayElementAtIndex(waveIdx);
                so.ApplyModifiedProperties();
            }
        })
        { text = "× 删除 Wave" };
        delBtn.style.marginTop = 4;
        row.Add(delBtn);

        return row;
    }
}
```

- [ ] **Step 2: 在 `LevelDataEditor` 接入 (本任务临时传 null onActionSelected)**

替换 `LevelDataEditor.CreateInspectorGUI` 中 `body.Add(MetadataSection.Build(serializedObject));` 之后的所有 `body.Add(...)` 行为:

```csharp
        body.Add(MetadataSection.Build(serializedObject));
        body.Add(ReferencesSection.Build(serializedObject));
        body.Add(WaveTimelineSection.Build(serializedObject, (w, a) => { /* wired in Task 13 */ }));
        body.Add(EconomySection.Build(serializedObject));
```

- [ ] **Step 3: 在 Unity 里验证**

选中 LevelData asset, 点 "+ 新增 Wave" → 多一行; 点 "+ 新增 Action" → 计数 +1; 点 "× 删除 Wave" → 弹 dialog, 确认后该 Wave 消失。

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/LevelEditor/Sections/WaveTimelineSection.cs \
        Assets/Editor/LevelEditor/LevelDataEditor.cs
git commit -m "feat(level-editor): WaveTimelineSection 骨架 (Wave/Action 增删)"
```

---

## Task 12: `WaveTimelineSection` 卡片渲染 + 选中事件

**Files:**
- Modify: `Assets/Editor/LevelEditor/Sections/WaveTimelineSection.cs`

- [ ] **Step 1: 添加卡片渲染辅助方法 + 修改 BuildWaveRow**

在 `WaveTimelineSection` 类内, `RebuildWaves` 之后添加:

```csharp
    static void RenderActionCards(VisualElement container, SerializedProperty actionsProp, int waveIdx, Action<int, int> onActionSelected)
    {
        container.Clear();
        if (actionsProp.arraySize == 0)
        {
            var empty = new Label("(空)");
            empty.style.color = new Color(0.4f, 0.4f, 0.4f);
            empty.style.unityTextAlign = TextAnchor.MiddleCenter;
            empty.style.flexGrow = 1;
            container.Add(empty);
            return;
        }

        // 计算总时长 (累加所有 Gap + 每条 Action 占 1s 占位宽度)
        float totalUnits = 0f;
        for (int i = 0; i < actionsProp.arraySize; i++)
        {
            var a = actionsProp.GetArrayElementAtIndex(i);
            totalUnits += 1f; // Action 自身占 1 单位
            totalUnits += Mathf.Max(0f, a.FindPropertyRelative("GapFromLastAction").floatValue);
        }
        if (totalUnits <= 0f) totalUnits = 1f;

        // 渲染
        float cursorUnits = 0f;
        for (int i = 0; i < actionsProp.arraySize; i++)
        {
            var a = actionsProp.GetArrayElementAtIndex(i);
            float gap = Mathf.Max(0f, a.FindPropertyRelative("GapFromLastAction").floatValue);
            int cmd = a.FindPropertyRelative("CommandType").intValue;

            // 间隔标记
            if (gap > 0f)
            {
                var gapEl = new VisualElement();
                gapEl.style.position = Position.Absolute;
                gapEl.style.left = Length.Percent(cursorUnits / totalUnits * 100f);
                gapEl.style.width = Length.Percent(gap / totalUnits * 100f);
                gapEl.style.height = Length.Percent(100f);
                gapEl.style.backgroundColor = new Color(0.23f, 0.23f, 0.27f);
                gapEl.style.flexDirection = FlexDirection.Row;
                gapEl.style.alignItems = Align.Center;
                gapEl.style.justifyContent = Justify.Center;
                var gapLabel = new Label($"gap {gap:0.0}s");
                gapLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                gapLabel.style.fontSize = 9;
                gapEl.Add(gapLabel);
                container.Add(gapEl);
                cursorUnits += gap;
            }

            // 卡片
            var card = new Button(() => onActionSelected?.Invoke(waveIdx, i))
            {
                text = $"A{i} {CommandTypeShort(cmd)}"
            };
            card.style.position = Position.Absolute;
            card.style.left = Length.Percent(cursorUnits / totalUnits * 100f);
            card.style.width = Length.Percent(1f / totalUnits * 100f);
            card.style.height = Length.Percent(100f);
            card.style.backgroundColor = CommandTypeColor(cmd);
            card.style.color = new Color(0, 0, 0);
            card.style.fontSize = 9;
            card.style.paddingLeft = 2;
            card.style.paddingRight = 2;
            container.Add(card);
            cursorUnits += 1f;
        }
    }

    static string CommandTypeShort(int cmd)
    {
        switch (cmd)
        {
            case 0: return "spawn";
            case 1: return "static";
            case 2: return "path↑";
            case 3: return "path↗";
            case 4: return "path→";
            case 5: return "dialog";
            case 6: return "panel";
            default: return $"c{cmd}";
        }
    }

    static Color CommandTypeColor(int cmd)
    {
        switch (cmd)
        {
            case 0: return new Color(0.3f, 0.8f, 0.6f);   // 绿
            case 1: return new Color(0.3f, 0.6f, 0.9f);   // 蓝
            case 2:
            case 3:
            case 4: return new Color(0.86f, 0.8f, 0.66f);  // 黄
            case 5: return new Color(0.77f, 0.52f, 0.75f); // 紫
            case 6: return new Color(0.95f, 0.5f, 0.5f);   // 红
            default: return new Color(0.5f, 0.5f, 0.5f);
        }
    }
```

- [ ] **Step 2: 在 `BuildWaveRow` 末尾 (在 return row; 之前) 调用卡片渲染**

找到 `row.Add(cardsContainer);` 那一行**之后**, 在 `// + 新增 Action 按钮` 之前, 插入:

```csharp
        RenderActionCards(cardsContainer, actionsProp, waveIdx, onActionSelected);
        actionsProp.TrackPropertyValue(actionsProp, _ => RenderActionCards(cardsContainer, actionsProp, waveIdx, onActionSelected));
```

- [ ] **Step 3: 编译验证 + Unity 行为验证**

打开 Unity, 编译。选中 LevelData, 增 Wave/Action, 确认卡片按时间比例布局。点卡片触发 `onActionSelected` 回调(目前还是空 lambda, 下一任务接入 ActionDetail)。

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/LevelEditor/Sections/WaveTimelineSection.cs
git commit -m "feat(level-editor): 时间线卡片渲染 + 选中事件"
```

---

## Task 13: 接入 `ActionDetailSection` (选中后渲染)

**Files:**
- Modify: `Assets/Editor/LevelEditor/LevelDataEditor.cs`

- [ ] **Step 1: 在 `LevelDataEditor` 加选中状态字段**

在 `LevelDataEditor` 类内 (在 `CreateInspectorGUI` 之前) 添加:

```csharp
    (int waveIdx, int actionIdx) _selectedAction = (-1, -1);
    VisualElement _detailContainer;
```

- [ ] **Step 2: 调整 `CreateInspectorGUI` 里的 section 顺序, 在 WaveTimeline 后插入 detail 容器**

替换 Task 11 Step 2 中加的 `body.Add(...)` 4 行, 为下面这段 (注意 `OnActionSelected` 引用, 此时还未定义, 但因为 lambda 延迟到点击时调用, 编译能过):

```csharp
        body.Add(MetadataSection.Build(serializedObject));
        body.Add(ReferencesSection.Build(serializedObject));
        body.Add(WaveTimelineSection.Build(serializedObject, OnActionSelected));
        _detailContainer = new VisualElement();
        _detailContainer.style.paddingLeft = 12;
        _detailContainer.style.paddingRight = 12;
        _detailContainer.style.paddingTop = 6;
        _detailContainer.style.paddingBottom = 6;
        body.Add(_detailContainer);
        RenderDetail();
        body.Add(EconomySection.Build(serializedObject));
```

- [ ] **Step 3: 添加 `OnActionSelected` 和 `RenderDetail` 私有方法**

在 `LevelDataEditor` 类内, `CreateInspectorGUI` 之后添加:

```csharp
    void OnActionSelected(int waveIdx, int actionIdx)
    {
        _selectedAction = (waveIdx, actionIdx);
        RenderDetail();
    }

    void RenderDetail()
    {
        if (_detailContainer == null) return;
        _detailContainer.Clear();
        if (_selectedAction.waveIdx < 0) return;
        _detailContainer.Add(ActionDetailSection.Build(
            serializedObject,
            _selectedAction.waveIdx,
            _selectedAction.actionIdx));
    }
```

- [ ] **Step 4: 监听 SerializedObject 变化, 重建 detail**

在 `OnActionSelected` 方法之前添加 (或在 `OnEnable` 注册):

找到 `OnEnable` 方法 (基类 `Editor` 提供, 我们 override 它):

```csharp
    void OnEnable()
    {
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
    }

    void OnUndoRedo()
    {
        serializedObject.Update();
        RenderDetail();
    }
```

- [ ] **Step 5: 验证**

打开 Unity, 选中 LevelData。点时间线上的 Action 卡片, 检查 detail 面板出现并显示该 Action 的字段。改 CommandType 0 → 5, 检查条件字段实时切换 (应隐藏召唤参数、显示对话框字段)。Undo (Ctrl+Z), 检查字段回到原值, detail 同步刷新。

- [ ] **Step 6: Commit**

```bash
git add Assets/Editor/LevelEditor/LevelDataEditor.cs
git commit -m "feat(level-editor): 接入 ActionDetailSection + Undo/Redo 同步"
```

---

## Task 14: `LevelTestStarter` (运行时 MonoBehaviour)

**Files:**
- Create: `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelTestStarter.cs`

> **Why 放运行时目录, 不放 `Assets/Editor/`**:
> `LevelTestStarter` 挂在测试场景的 GameObject 上, 运行时需要。`Assets/Editor/` 是 Editor-only 编译, 不会进入 Play 模式 / Build。所以放在 `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/` 下, 跟 LevelData/LevelActions 同目录, 编译进运行时。

- [ ] **Step 1: 创建 `LevelTestStarter.cs`**

`Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelTestStarter.cs`:

```csharp
using UnityEngine;

/// <summary>
/// 挂在测试场景 LevelTest.unity 上。PlaytestLauncher 会在 EnterPlay 前设置 LevelDataToPlay。
/// 启动时调用 LevelResourceSharing 的 LevelInitialize/LevelStart。
/// </summary>
public class LevelTestStarter : MonoBehaviour
{
    public LevelData LevelDataToPlay;

    void Awake()
    {
        if (LevelDataToPlay == null)
        {
            Debug.LogError("[LevelTestStarter] LevelDataToPlay is null; nothing to play.");
            return;
        }
        LevelResourceSharing.LD = LevelDataToPlay;
        LevelResourceSharing.LevelInitialize();
        LevelResourceSharing.LevelStart();
    }

    void OnDisable()
    {
        if (LevelResourceSharing.LD != null)
        {
            LevelResourceSharing.LevelEnd();
        }
    }
}
```

- [ ] **Step 2: 编译验证**

打开 Unity, 等编译。Console 无错误。

- [ ] **Step 3: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelTestStarter.cs
git commit -m "feat(level-editor): LevelTestStarter (运行时 MonoBehaviour, 调 LevelInitialize/Start)"
```

---

## Task 15: `LevelTestSceneBuilder` (自动创建测试场景)

**Files:**
- Create: `Assets/Editor/LevelEditor/Playtest/LevelTestSceneBuilder.cs`

- [ ] **Step 1: 创建 `LevelTestSceneBuilder.cs`**

`Assets/Editor/LevelEditor/Playtest/LevelTestSceneBuilder.cs`:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 确保 Assets/Scenes/LevelTest.unity 存在: 存在则返回路径, 不存在则创建。
/// 场景内容: 空场景 + LM (空 Transform) + MCam (orthographic Camera) + UICam (depth=1) + LevelTestStarter。
/// </summary>
public static class LevelTestSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/LevelTest.unity";

    public static string EnsureScene()
    {
        if (System.IO.File.Exists(ScenePath))
        {
            return ScenePath;
        }

        // 创建目录
        var dir = System.IO.Path.GetDirectoryName(ScenePath);
        if (!System.IO.Directory.Exists(dir))
        {
            System.IO.Directory.CreateDirectory(dir);
        }

        // 新建空场景
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // LM
        var lm = new GameObject("LM");

        // MCam (Main Camera)
        var mcam = new GameObject("MCam");
        var mcamCam = mcam.AddComponent<Camera>();
        mcamCam.orthographic = true;
        mcamCam.orthographicSize = 5f;
        mcamCam.transform.position = new Vector3(0, 0, -10);

        // UICam
        var uicam = new GameObject("UICam");
        var uicamCam = uicam.AddComponent<Camera>();
        uicamCam.orthographic = true;
        uicamCam.orthographicSize = 5f;
        uicamCam.depth = 1;
        uicamCam.transform.position = new Vector3(0, 0, 0);

        // LevelTestStarter
        var starterGo = new GameObject("LevelTestStarter");
        starterGo.AddComponent<LevelTestStarter>();

        // 保存
        EditorSceneManager.SaveScene(scene, ScenePath);

        return ScenePath;
    }
}
```

- [ ] **Step 2: 验证 (手工调用)**

打开 Unity, 选菜单 `Window > General > Search` (或写一个临时菜单调用), 或者更简单: 暂时在 `LevelTestSceneBuilder` 上加一个 `[MenuItem("Tools/Level Editor/Create Test Scene")]`, 验证用, 验证后删掉菜单项。

**临时菜单项 (验证用, 提交后删除):**

在 `LevelTestSceneBuilder` 末尾追加:

```csharp
    [MenuItem("Tools/Level Editor/Create Test Scene (verify only)")]
    static void _VerifyMenu() { EnsureScene(); }
```

打开 Unity, 运行 `Tools > Level Editor > Create Test Scene (verify only)`, 确认 `Assets/Scenes/LevelTest.unity` 创建。打开场景确认有 LM/MCam/UICam/LevelTestStarter 四个 GameObject。

**完成后**删除 `_VerifyMenu` 方法 (避免污染菜单)。再次 Commit。

- [ ] **Step 3: Commit**

```bash
git add Assets/Editor/LevelEditor/Playtest/LevelTestSceneBuilder.cs \
        Assets/Scenes/LevelTest.unity \
        Assets/Scenes/LevelTest.unity.meta
git commit -m "feat(level-editor): LevelTestSceneBuilder + Assets/Scenes/LevelTest.unity"
```

---

## Task 16: `PlaytestLauncher` (按钮 + 流程)

**Files:**
- Create: `Assets/Editor/LevelEditor/Playtest/PlaytestLauncher.cs`
- Modify: `Assets/Editor/LevelEditor/Validation/ValidationBar.cs` (新文件, Task 17 接)

- [ ] **Step 1: 创建 `PlaytestLauncher.cs`**

`Assets/Editor/LevelEditor/Playtest/PlaytestLauncher.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Playtest 流程: 校验当前 LevelData → 保存当前场景 → 切到测试场景 → 设 LevelDataToPlay → EnterPlay。
/// </summary>
public static class PlaytestLauncher
{
    public static void Playtest(LevelData data)
    {
        if (data == null)
        {
            EditorUtility.DisplayDialog("Playtest", "未选中 LevelData 资产。", "OK");
            return;
        }

        // 校验
        var issues = LevelDataValidator.Validate(data);
        var errors = issues.FindAll(i => i.Severity == Validation.ValidationSeverity.Error);
        if (errors.Count > 0)
        {
            if (!EditorUtility.DisplayDialog(
                "Playtest",
                $"当前 LevelData 有 {errors.Count} 个 Error:\n\n{string.Join("\n", errors.ConvertAll(e => "· " + e.Message))}\n\n是否仍要 Playtest?",
                "继续", "取消"))
            {
                return;
            }
        }

        // 确保测试场景
        var scenePath = LevelTestSceneBuilder.EnsureScene();

        // 保存当前场景 (询问)
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        // 打开测试场景
        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        // 设置 LevelDataToPlay
        var starter = Object.FindObjectOfType<LevelTestStarter>();
        if (starter == null)
        {
            Debug.LogError("[PlaytestLauncher] LevelTest.unity missing LevelTestStarter. Re-create scene?");
            return;
        }
        starter.LevelDataToPlay = data;

        // Enter Play
        EditorApplication.EnterPlaymode();
    }
}
```

- [ ] **Step 2: 在 `LevelDataEditor` 加 Playtest 按钮**

在 `CreateInspectorGUI` 末尾 (`return root;` 之前) 追加:

```csharp
        // 底部 Playtest 按钮 (简化版, 校验条 Task 17 再加)
        var footer = new VisualElement();
        footer.style.backgroundColor = new Color(0.16f, 0.16f, 0.2f);
        footer.style.paddingTop = 8;
        footer.style.paddingBottom = 8;
        footer.style.paddingLeft = 12;
        footer.style.paddingRight = 12;
        footer.style.flexDirection = FlexDirection.Row;
        footer.style.alignItems = Align.Center;
        root.Add(footer);

        var playtestBtn = new Button(() => PlaytestLauncher.Playtest((LevelData)target)) { text = "▶ Playtest" };
        playtestBtn.style.marginLeft = 0;
        playtestBtn.style.backgroundColor = new Color(0.86f, 0.86f, 0.66f);
        playtestBtn.style.color = new Color(0, 0, 0);
        playtestBtn.style.fontSize = 12;
        playtestBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
        playtestBtn.style.paddingTop = 4;
        playtestBtn.style.paddingBottom = 4;
        playtestBtn.style.paddingLeft = 12;
        playtestBtn.style.paddingRight = 12;
        playtestBtn.style.borderTopLeftRadius = 3;
        playtestBtn.style.borderTopRightRadius = 3;
        playtestBtn.style.borderBottomLeftRadius = 3;
        playtestBtn.style.borderBottomRightRadius = 3;
        footer.Add(playtestBtn);
```

- [ ] **Step 3: 验证**

打开 Unity, 选一个有效 LevelData (无 Error), 点 Playtest 按钮:
- 确认弹窗或直接进 Play
- Play 模式里场景 = `LevelTest.unity`, 包含 LM/MCam/UICam
- Console 应有 `[LevelActionManager] ToStart` 等日志(原本就有, 不应报错)

退出 Play 模式, 确认 `LevelTestStarter.OnDisable` 调用 `LevelResourceSharing.LevelEnd()`, 场景清理。

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/LevelEditor/Playtest/PlaytestLauncher.cs \
        Assets/Editor/LevelEditor/LevelDataEditor.cs
git commit -m "feat(level-editor): PlaytestLauncher + 底部 Playtest 按钮"
```

---

## Task 17: `ValidationBar` 状态条

**Files:**
- Create: `Assets/Editor/LevelEditor/Validation/ValidationBar.cs`
- Modify: `Assets/Editor/LevelEditor/LevelDataEditor.cs`

- [ ] **Step 1: 创建 `ValidationBar.cs`**

`Assets/Editor/LevelEditor/Validation/ValidationBar.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 校验状态条: 显示当前 LevelData 的 issue 数量, 颜色按 Error/Warning/OK 区分。
/// </summary>
public static class ValidationBar
{
    public static VisualElement Build(SerializedObject so, out Label statusLabel)
    {
        var bar = new VisualElement();
        bar.style.flexDirection = FlexDirection.Row;
        bar.style.alignItems = Align.Center;

        statusLabel = new Label("...");
        statusLabel.style.fontSize = 11;
        bar.Add(statusLabel);

        Refresh(so, statusLabel);

        // 监听: 任何字段改动后延迟一帧刷新
        so.Update();
        bar.TrackSerializedObjectValue(so, _ =>
        {
            // 延迟 1 帧
            bar.schedule.Execute(() => Refresh(so, statusLabel)).StartingIn(50);
        });

        return bar;
    }

    static void Refresh(SerializedObject so, Label statusLabel)
    {
        var data = so.targetObject as LevelData;
        if (data == null)
        {
            statusLabel.text = "(无目标)";
            return;
        }
        var issues = LevelDataValidator.Validate(data);
        int errors = 0, warnings = 0;
        foreach (var i in issues)
        {
            if (i.Severity == Validation.ValidationSeverity.Error) errors++;
            else warnings++;
        }

        if (errors == 0 && warnings == 0)
        {
            statusLabel.text = "✓ 校验通过";
            statusLabel.style.color = new Color(0.3f, 0.8f, 0.6f);
        }
        else if (errors == 0)
        {
            statusLabel.text = $"⚠ {warnings} 个 Warning";
            statusLabel.style.color = new Color(0.85f, 0.7f, 0.3f);
        }
        else
        {
            statusLabel.text = $"✗ {errors} 个 Error, {warnings} 个 Warning";
            statusLabel.style.color = new Color(0.95f, 0.4f, 0.4f);
        }
    }
}
```

- [ ] **Step 2: 在 `LevelDataEditor` 接入 (放在 Playtest 按钮同一 footer 里)**

找到 Task 16 步骤 2 加的 `footer` 块, 修改为:

```csharp
        // 底部状态条 + Playtest 按钮
        var footer = new VisualElement();
        footer.style.backgroundColor = new Color(0.16f, 0.16f, 0.2f);
        footer.style.paddingTop = 8;
        footer.style.paddingBottom = 8;
        footer.style.paddingLeft = 12;
        footer.style.paddingRight = 12;
        footer.style.flexDirection = FlexDirection.Row;
        footer.style.alignItems = Align.Center;
        footer.style.position = Position.Sticky;
        footer.style.bottom = 0;
        root.Add(footer);

        Label statusLabel;
        footer.Add(ValidationBar.Build(serializedObject, out statusLabel));

        var spacer = new VisualElement();
        spacer.style.flexGrow = 1;
        footer.Add(spacer);

        var playtestBtn = new Button(() => PlaytestLauncher.Playtest((LevelData)target)) { text = "▶ Playtest" };
        playtestBtn.style.backgroundColor = new Color(0.86f, 0.86f, 0.66f);
        playtestBtn.style.color = new Color(0, 0, 0);
        playtestBtn.style.fontSize = 12;
        playtestBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
        playtestBtn.style.paddingTop = 4;
        playtestBtn.style.paddingBottom = 4;
        playtestBtn.style.paddingLeft = 12;
        playtestBtn.style.paddingRight = 12;
        playtestBtn.style.borderTopLeftRadius = 3;
        playtestBtn.style.borderTopRightRadius = 3;
        playtestBtn.style.borderBottomLeftRadius = 3;
        playtestBtn.style.borderBottomRightRadius = 3;
        footer.Add(playtestBtn);
```

(把 Task 16 步骤 2 里的 footer 块整体替换为本步骤的版本。)

- [ ] **Step 3: 验证**

打开 Unity, 选 LevelData:
- 改 `LevelHp` 到 0, 校验条变红 "✗ 1 个 Error"
- 改回正常值, 校验条变绿 "✓ 校验通过"
- 删 `Waves` 全空, 校验条变红

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/LevelEditor/Validation/ValidationBar.cs \
        Assets/Editor/LevelEditor/LevelDataEditor.cs
git commit -m "feat(level-editor): ValidationBar 状态条 (颜色 + 数量 + 实时刷新)"
```

---

## Task 18: Smoke test 终验 + 文档

**Files:**
- Create: `docs/level-editor-usage.md` (用户文档, 项目级 docs/ 目录)

- [ ] **Step 1: 跑全部测试**

Test Runner → EditMode → Run All. 预期: 9 个 LevelDataValidatorTests + 1 个 SmokeTests, 全部通过。

- [ ] **Step 2: 手工 smoke test checklist**

在 Unity 里执行以下, 全部应通过:

- [ ] 创建新 LevelData → Inspector 显示自定义编辑器
- [ ] 4 个 section (元数据/引用/波次/经济) 都能填, 字段保存正确
- [ ] 校验条颜色: 合法时绿, Error 时红, Warning 时黄
- [ ] 添加 3 条 Wave, 每条 4 个不同 CommandType 的 Action
- [ ] 改某 Action 的 CommandType 0 → 5, 确认条件字段实时切换
- [ ] 故意把 EntityPrefabSerial 设越界, 校验条变红
- [ ] 点 Playtest 按钮 → 测试场景自动建好(若缺失)→ Enter Play → 跑第一波 → 退出
- [ ] 退出 Play 后 LevelData 编辑器内容保留
- [ ] Ctrl+Z / Ctrl+Y 多次, 字段正确撤销/重做, detail 面板同步刷新

- [ ] **Step 3: 创建用户文档 `docs/level-editor-usage.md`**

```markdown
# Level Editor 使用说明

## 入口

在 Project 窗口选中任意 `LevelData` 资产, Inspector 自动呈现本编辑器。

## 4 个 Section

- **元数据**: 关卡名/代码/描述/相机/转场贴图
- **引用**: 拖拽 MapPrefab / EnvironmentalControlDevice / CheckPoints / WaveEntityPrefabIDs
- **波次时间线**: 每条 Wave 一行, Action 渲染为卡片, 卡片宽度按 GapFromLastAction 比例计算
- **经济**: HP/成本/上限/部署上限/恢复速度

## 选中 Action 编辑

点时间线上的卡片, 下方内联面板出现该 Action 的字段。CommandType 切换会实时显隐条件字段。

## 校验条

底部状态条颜色: 绿 ✓ / 黄 ⚠ / 红 ✗。规则清单见 [validator 实现](../Assets/Editor/LevelEditor/Validation/LevelDataValidator.cs)。

## Playtest

点底部 ▶ Playtest 按钮:
1. 若有 Error, 弹 Dialog 询问"继续还是取消"
2. 第一次使用自动创建 `Assets/Scenes/LevelTest.unity` (含 LM/MCam/UICam/LevelTestStarter)
3. 切到测试场景, 设置 LevelDataToPlay, Enter Play
4. 退出 Play 时自动清理

## 范围

- 地图方块/传送门不在本编辑器内编辑, MapPrefab 仍需独立制作
- 修改 LevelData 不需要重启编辑器
- 所有 Undo/Redo 走 Unity 内置 Ctrl+Z / Ctrl+Y
```

- [ ] **Step 4: Commit**

```bash
git add docs/level-editor-usage.md
git commit -m "docs(level-editor): 使用说明"
```

---

## Self-Review Checklist

**Spec 覆盖**:
- 第 1 节 目标/非目标: Task 1-18 全部范围内, 非目标 (MapPrefab 内部编辑等) 不实现 ✓
- 第 2 节 入口: Task 5 + Task 6 (CustomEditor on LevelData) ✓
- 第 3 节 文件结构: Task 1-18 创建的文件与 spec 一致; `LevelTestStarter` 放运行时目录已对齐 (因它是 MonoBehaviour 需进入 Play 模式) ✓
- 第 4 节 组件职责: Task 2-4 (Validator) + Task 6-13 (Sections) + Task 14-16 (Playtest) + Task 17 (ValidationBar) ✓
- 第 4.3 校验规则: Task 2 (规则 1) + Task 3 (规则 2-4) + Task 4 (规则 6-10) — 规则 5 标为占位, 不实现 ✓
- 第 5 节 数据流: SerializedObject/PropertyField/BindProperty 模式贯穿 Task 5-13 ✓
- 第 6 节 错误处理: Task 16 (Playtest 错误 Dialog) + Task 17 (校验状态条) ✓
- 第 7 节 测试策略: Task 1 (EditMode 脚手架) + Task 2-4 (Validator 单测) + Task 18 (smoke checklist) ✓
- 第 8 节 已知限制: spec 已记, 不需在 plan 里重复 ✓
- 第 10 节 5 个待确认事项: spec 中"规则 5 暂不实现"采用; 测试场景路径固定; UnityEvent 走 IMGUIContainer; Playtest Error 不强制; 文件结构已定 ✓

**Placeholder 扫描**:
- 无 "TBD" / "TODO" / "fill in" / "similar to" 残留
- Task 9 的 `MakeUnityEventRow` 签名从 3 参修正为 2 参 (运行时不需要 Type 参数) ✓
- Task 10 的 `RebuildConditional` 涵盖 CommandType 0-6 全部 7 种情况 ✓
- Task 13 的 `body.Add` 顺序修正为 Metadata / References / WaveTimeline / Detail / Economy, 与设计一致 ✓
- Task 14 的 `LevelTestStarter` 路径从 `Assets/Editor/...` 改为 `Assets/PublicScripts/...` (运行时组件) ✓

**Type 一致性**:
- `LevelDataValidator.Validate(LevelData)` → `List<ValidationIssue>` 在 Task 2 定义, Task 3/4/16 复用 ✓
- `ValidationIssue(Severity, Path, Message)` 签名一致 ✓
- `ActionDetailSection.Build(SerializedObject, int, int)` 签名在 Task 9 定义, Task 13 复用 ✓
- `MakeUnityEventRow(string, SerializedProperty)` 2 参, 在 Task 9/10 内部使用一致 ✓
- `WaveTimelineSection.Build(SerializedObject, Action<int,int>)` 签名在 Task 11 定义, Task 12 复用 ✓
- `LevelTestStarter.LevelDataToPlay` 在 Task 14 定义, Task 16 复用 ✓
- `LevelTestSceneBuilder.EnsureScene()` 返回 string, Task 16 用作 scenePath ✓
- `PlaytestLauncher.Playtest(LevelData)` 签名在 Task 16 定义 ✓
- `ValidationBar.Build(SerializedObject, out Label)` 在 Task 17 定义, `LevelDataEditor` 步骤 2 复用 ✓

**已知 follow-up** (不阻塞本计划):
- `MakeRow` 在 3 个 Section 类里重复, 可提取到 `LevelEditorStyles.uss` 同级 helper 类 (后续 refactor)
- `ActionDetailSection.RebuildConditional` 的 `ModifyAttributes` 切换使用 `RegisterValueChangeCallback` 可能在 Undo 路径下累积回调, 实际表现为多次重建 (无功能问题, 仅微小性能损耗)
- 卡片按 `totalUnits = sum(1 + gap)` 分配宽度, 极端情况下 (gap=0, 大量 action) 总宽度可能溢出; 当前不处理, 后续加滚动容器
