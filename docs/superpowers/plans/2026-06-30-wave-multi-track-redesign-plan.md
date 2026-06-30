# Wave 多轨道 + 绝对时间重设计 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 `LevelData` 的 Wave 从单一时间轴升级为多命名轨道系统,Action 时间由相对 `GapFromLastAction` 改为绝对 `TriggerTime`,并配套迁移 v1 关卡数据 + 编辑器 UI 重写。

**Architecture:** 4 阶段实施,每阶段独立 commit 便于回滚:
- **Phase 1a** 数据模型 + 迁移接入(零行为变化,只接通转换管道)
- **Phase 1b** 清理临时字段(删 `GapFromLastAction` / `LegacyActions`)
- **Phase 2** Runtime 调度改造(`WaveProcess` 改为 `CollectAndSortActions` + 单调度循环)
- **Phase 3** 编辑器 UI 重写(多轨道时间轴 + 拖拽)
- **Phase 4** Playtest 全量验证 + 文档

**Tech Stack:** Unity 2022+ / C# / UI Toolkit / UniTask / NUnit + Unity Test Framework

**Spec:** `docs/superpowers/specs/2026-06-30-wave-multi-track-redesign-design.md`

**测试约定:** 参考 `Assets/Tests/Editor/MapData/MapPathFinderTests.cs`(NUnit `[Test]` + `ScriptableObject.CreateInstance` fixture + `DestroyImmediate` cleanup)

---

## File Map(本计划涉及的文件)

| 路径 | 角色 | 阶段 |
|---|---|---|
| `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelActions.cs` | 数据契约(Wave/Track/Action struct) | 1a, 1b |
| `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelData.cs` | LevelData SO(`SchemaVersion` 字段) | 1a |
| `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelActionManager.cs` | Runtime 调度 | 1a (compile), 2 (logic) |
| `Assets/Editor/LevelEditor/WaveMigrator.cs`(新) | v1 → v2 迁移器 | 1a |
| `Assets/Tests/Editor/Wave/Wave.Tests.Editor.asmdef`(新) | 测试程序集 | 1a |
| `Assets/Tests/Editor/Wave/WaveMigratorTests.cs`(新) | 迁移器单测 | 1a |
| `Assets/Tests/Editor/Wave/WaveSchedulerTests.cs`(新) | CollectAndSortActions 单测 | 2 |
| `Assets/Editor/LevelEditor/Sections/WaveTimelineSection.cs` | 编辑器时间轴 | 3 (重写) |
| `Assets/Editor/LevelEditor/Sections/WaveTrackRow.cs`(新) | 单轨道行 | 3 |
| `Assets/Editor/LevelEditor/Sections/WaveActionCard.cs`(新) | Action 卡片 + 拖拽手柄 | 3 |
| `Assets/Editor/LevelEditor/Sections/ActionDetailSection.cs` | 详情面板(字段重命名 + 路径加深) | 3 |
| `Assets/Editor/LevelEditor/LevelDataEditor.cs` | 编辑器入口(选中状态三元组) | 3 |
| `Assets/Editor/LevelEditor/Validation/LevelDataValidator.cs` | 校验(`TriggerTime ≥ 0`) | 3 |
| `Assets/Tests/Editor/Wave/LevelDataValidatorTests.cs`(新) | 校验单测 | 3 |
| `docs/level-editor-usage.md` | 设计师文档(可选更新) | 4 |

---

## Phase 1a: 数据模型 + 迁移接入

### Task 1: 加 `Track` struct + `Wave.Tracks[]` + 临时 `LegacyActions` + `Action.TriggerTime`

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelActions.cs`

- [ ] **Step 1: 修改 LevelActions.cs**

把现有内容替换为下面的版本(**保留** `GapFromLastAction`,**新增** `TriggerTime`;**新增** `Track` struct;**新增** `Wave.Tracks` + 临时 `LegacyActions`):

```csharp
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class LevelActions
{
    [Serializable]
    public struct Wave
    {
        public Track[] Tracks;

        // ↓ 临时:所有 LevelData 迁移完成后删除(Phase 1b)
        [SerializeField, HideInInspector, FormerlySerializedAs("Actions")]
        internal Action[] LegacyActions;
    }

    [Serializable]
    public struct Track
    {
        public string Name;
        public Color TrackColor;
        public bool Locked;
        public Action[] Actions;
    }

    [Serializable]
    public struct Action
    {
        [Tooltip("指令类型:0-召唤可移动实体,1-生成静止实体,2-显示地面路径,3-显示近地悬浮路径,4-显示飞行路径,5-显示右侧提示卡,6-显示剧情")] public int CommandType;
        // ↓ Phase 1a 保留旧字段,Phase 1b 删除
        [Tooltip("距上一 Action 间隔(v1 字段,迁移后无效)")] public float GapFromLastAction;
        // ↓ 新增:绝对触发时间(秒),Wave 开始后 N 秒触发
        [Tooltip("绝对触发时间(秒),Wave 开始后 N 秒触发")] public float TriggerTime;
        [Tooltip("动作开始前函数")] public UnityEvent OnBeforeAction;
        [Tooltip("实体 ID")] public EntityID EntityPrefabID;
        [Tooltip("召唤实体阵营,1-Turret 2-Monster")] public int Camp;
        [Tooltip("动作重复函数组")] public UnityEvent<Entity> OnActionRepeat;
        [Tooltip("路径预制体序号")] public int PathSerial;
        [Tooltip("动作重复间隔时间数组(长度为重复次数)")] public float[] GapsFromLastRepeat;
        [Tooltip("是否修改实体的目标价值、首要目标、计算击杀数属性")] public bool ModifyAttributes;
        [Tooltip("修改的实体目标价值")] public int ModifyLevelHpConsume;
        [Tooltip("修改的实体是否为首要目标")] public bool ModifyPrimary;
        [Tooltip("修改的实体是否计算击杀")] public bool ModifyCountOperate;
        [Tooltip("放置位置")] public Vector2 Destination;
        [Tooltip("放置朝向")] public int Orientation;
        [Tooltip("头像")] public Image HeadImage;
        [Tooltip("内容")] public string Content;
        [Tooltip("持续时间")] public float DurationTime;
        public string[] Contents;
    }
}
```

`FormerlySerializedAs` 在 `UnityEngine.Serialization` 命名空间里,需要在文件顶部额外 `using UnityEngine.Serialization;`。

- [ ] **Step 2: 验证编译**

在 Unity Editor 中:
- File → Open Project(等待编译)
- Window → General → Console 确认无编译错误

期望:Console 无 error。如果有 `[FormerlySerializedAs]` 找不到命名空间的错误,确认顶部有 `using UnityEngine.Serialization;`。

- [ ] **Step 3: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelActions.cs
git commit -m "feat(wave): Wave.Tracks[] + Track struct + Action.TriggerTime (Phase 1a part 1)"
```

---

### Task 2: 加 `LevelData.SchemaVersion`

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelData.cs`

- [ ] **Step 1: 修改 LevelData.cs**

在文件顶部加 `using System;`(若没有),在类体顶部加字段:

```csharp
[CreateAssetMenu]
public class LevelData : ScriptableObject
{
    [Tooltip("数据 schema 版本;v1 = 0/未设,v2 = Tracks 结构")]
    public int SchemaVersion = 2;

    public string LevelName;
    public string LevelCode;
    public string LevelDescription;
    public float CameraSize;
    public Vector2 CameraPos;
    public Texture2D CutToLevelTexture;
    [Space(10)]
    public GameObject MapPrefab;
    [Header("Map data (new)")]
    public int iSize;
    public int jSize;
    public List<Tile> MapData = new List<Tile>();
    public GameObject EnvironmentalControlDevice;
    public LevelActions.Wave[] Waves;
    public PathData[] Paths = new PathData[0];
    public int LevelHp;
    public int Cost0;
    public int MaxCost;
    public int CanSetNum;
    public float CostRecoverSpeed;
}
```

- [ ] **Step 2: 验证编译**

Unity Editor 自动重载。Console 确认无 error。

- [ ] **Step 3: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelData.cs
git commit -m "feat(wave): LevelData.SchemaVersion (Phase 1a part 2)"
```

---

### Task 3: 创建 Wave 测试 asmdef

**Files:**
- Create: `Assets/Tests/Editor/Wave/Wave.Tests.Editor.asmdef`

- [ ] **Step 1: 写 asmdef**

```json
{
    "name": "Wave.Tests.Editor",
    "rootNamespace": "Wave.Tests",
    "references": [
        "BasicScripts",
        "LevelEditor.Editor",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": ["Editor"],
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

- [ ] **Step 2: 验证编译**

Unity Editor 重载,Console 确认 asmdef 被识别。

- [ ] **Step 3: Commit**

```bash
git add Assets/Tests/Editor/Wave/Wave.Tests.Editor.asmdef
git commit -m "test(wave): Wave.Tests.Editor asmdef"
```

---

### Task 4: 写 WaveMigrator 失败测试

**Files:**
- Create: `Assets/Tests/Editor/Wave/WaveMigratorTests.cs`

- [ ] **Step 1: 写测试**

```csharp
using NUnit.Framework;
using UnityEngine;

namespace Wave.Tests
{
    public class WaveMigratorTests
    {
        // 模拟 v1 资产:Wave.Actions[] 含 N 个 Action,各自有 GapFromLastAction。
        // 调用 Migrate 后:Actions[].GapFromLastAction 应转换为单条 Track[0].Actions[].TriggerTime。
        // 模拟"旧数据"的方式:构造 Action[] 然后强行塞进 wave.LegacyActions(模拟 FormerlySerializedAs 已经把数据搬过来)。
        // 由于 FormerlySerializedAs 是 Unity 序列化器在加载时才生效,测试里直接操作 LegacyActions。

        static LevelActions.Action MakeV1Action(float gap, int commandType = 0)
        {
            var a = new LevelActions.Action
            {
                CommandType = commandType,
                GapFromLastAction = gap,
                TriggerTime = 0f,
                GapsFromLastRepeat = new float[0],
            };
            return a;
        }

        [Test]
        public void Migrate_single_wave_with_3_actions_creates_one_default_track_with_absolute_trigger_times()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.SchemaVersion = 0; // v1
            var wave = new LevelActions.Wave
            {
                LegacyActions = new[]
                {
                    MakeV1Action(0f),         // 第 1 条:TriggerTime = 0
                    MakeV1Action(2.5f),       // 第 2 条:TriggerTime = 0 + 2.5 = 2.5
                    MakeV1Action(1f),         // 第 3 条:TriggerTime = 2.5 + 1 = 3.5
                },
            };
            ld.Waves = new[] { wave };

            WaveMigrator.MigrateLevelData(ld);

            Assert.AreEqual(2, ld.SchemaVersion, "SchemaVersion should be bumped to 2");
            Assert.IsNull(ld.Waves[0].LegacyActions, "LegacyActions should be cleared");
            Assert.IsNotNull(ld.Waves[0].Tracks, "Tracks should be populated");
            Assert.AreEqual(1, ld.Waves[0].Tracks.Length, "single default track");
            Assert.AreEqual("默认", ld.Waves[0].Tracks[0].Name, "default track name");
            Assert.AreEqual(false, ld.Waves[0].Tracks[0].Locked);
            Assert.AreEqual(3, ld.Waves[0].Tracks[0].Actions.Length, "all actions migrated");

            var actions = ld.Waves[0].Tracks[0].Actions;
            Assert.AreEqual(0f, actions[0].TriggerTime, 0.0001f, "first action TriggerTime = 0");
            Assert.AreEqual(2.5f, actions[1].TriggerTime, 0.0001f);
            Assert.AreEqual(3.5f, actions[2].TriggerTime, 0.0001f);

            Object.DestroyImmediate(ld);
        }

        [Test]
        public void Migrate_skips_level_data_already_at_v2()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.SchemaVersion = 2;
            ld.Waves = new[]
            {
                new LevelActions.Wave { Tracks = new LevelActions.Track[0] }
            };

            // 不应改 SchemaVersion 或抛错
            Assert.DoesNotThrow(() => WaveMigrator.MigrateLevelData(ld));
            Assert.AreEqual(2, ld.SchemaVersion);
            Assert.AreEqual(0, ld.Waves[0].Tracks.Length);

            Object.DestroyImmediate(ld);
        }

        [Test]
        public void Migrate_skips_wave_with_null_legacy_actions()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.SchemaVersion = 0;
            ld.Waves = new[]
            {
                new LevelActions.Wave { LegacyActions = null, Tracks = new LevelActions.Track[0] }
            };

            WaveMigrator.MigrateLevelData(ld);

            Assert.AreEqual(2, ld.SchemaVersion);
            Assert.AreEqual(0, ld.Waves[0].Tracks.Length, "未修改 Tracks(已是新结构)");

            Object.DestroyImmediate(ld);
        }

        [Test]
        public void Migrate_negative_gap_clamps_to_zero()
        {
            // Wave 设计师在 v1 可能误填负数 gap,迁移时应取 max(0, gap) 不引入负 TriggerTime
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.SchemaVersion = 0;
            ld.Waves = new[]
            {
                new LevelActions.Wave
                {
                    LegacyActions = new[]
                    {
                        MakeV1Action(0f),
                        MakeV1Action(-1f), // 异常输入
                    }
                }
            };

            WaveMigrator.MigrateLevelData(ld);

            Assert.AreEqual(1f, ld.Waves[0].Tracks[0].Actions[1].TriggerTime, 0.0001f,
                "negative gap should not reduce TriggerTime below previous cumulative");

            Object.DestroyImmediate(ld);
        }

        [Test]
        public void Migrate_default_track_color_matches_palette_index_0()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.SchemaVersion = 0;
            ld.Waves = new[]
            {
                new LevelActions.Wave
                {
                    LegacyActions = new[] { MakeV1Action(0f) }
                }
            };

            WaveMigrator.MigrateLevelData(ld);

            // 灰: (0.55, 0.55, 0.55)
            var c = ld.Waves[0].Tracks[0].TrackColor;
            Assert.AreEqual(0.55f, c.r, 0.01f);
            Assert.AreEqual(0.55f, c.g, 0.01f);
            Assert.AreEqual(0.55f, c.b, 0.01f);

            Object.DestroyImmediate(ld);
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Unity Editor:
- Window → General → Test Runner
- EditMode tab → 选中 Wave.Tests.Editor 命名空间 → Run All
- **期望**:5 个测试全部失败(因为 `WaveMigrator` 类还不存在)

- [ ] **Step 3: Commit 测试**

```bash
git add Assets/Tests/Editor/Wave/WaveMigratorTests.cs
git commit -m "test(wave): WaveMigrator failing tests"
```

---

### Task 5: 实现 WaveMigrator(让测试通过)

**Files:**
- Create: `Assets/Editor/LevelEditor/WaveMigrator.cs`

- [ ] **Step 1: 写 WaveMigrator.cs**

```csharp
using UnityEditor;
using UnityEngine;

/// <summary>
/// Wave v1 → v2 数据迁移。Phase 1a 接入;Phase 1b 之后 Wave.LegacyActions / Action.GapFromLastAction 删除,本类保留为日志钩子(空跑即可)。
/// 入口:<see cref="MigrateLevelData"/> (单关卡),<see cref="MigrateAllLegacyLevels"/> (全工程扫描)。
/// </summary>
public static class WaveMigrator
{
    static readonly Color[] Palette =
    {
        new Color(0.55f, 0.55f, 0.55f),
        new Color(0.30f, 0.80f, 0.60f),
        new Color(0.30f, 0.60f, 0.90f),
        new Color(0.86f, 0.80f, 0.66f),
        new Color(0.77f, 0.52f, 0.75f),
    };

    public static Color DefaultTrackColor(int trackIndex) => Palette[trackIndex % Palette.Length];

    public static void MigrateLevelData(LevelData ld)
    {
        if (ld == null) return;
        if (ld.SchemaVersion >= 2) return;

        for (int w = 0; w < ld.Waves.Length; w++)
        {
            var wave = ld.Waves[w];
            var oldActions = wave.LegacyActions;
            if (oldActions == null)
            {
                // 已是新结构但 SchemaVersion 未更新(用户中途保存过),跳过 Actions 转换
                continue;
            }

            var newActions = new LevelActions.Action[oldActions.Length];
            float cumulative = 0f;
            for (int i = 0; i < oldActions.Length; i++)
            {
                var a = oldActions[i];
                if (i == 0) cumulative = 0f;
                else cumulative += Mathf.Max(0f, a.GapFromLastAction);
                a.TriggerTime = cumulative;
                newActions[i] = a;
            }

            wave.Tracks = new[]
            {
                new LevelActions.Track
                {
                    Name = "默认",
                    TrackColor = DefaultTrackColor(0),
                    Locked = false,
                    Actions = newActions,
                }
            };
            wave.LegacyActions = null;
            ld.Waves[w] = wave;
        }

        ld.SchemaVersion = 2;
    }

    [InitializeOnLoadMethod]
    static void AutoMigrateOnLoad()
    {
        // 扫描所有 LevelData 资产
        var guids = AssetDatabase.FindAssets("t:LevelData");
        int migrated = 0, failed = 0;
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var ld = AssetDatabase.LoadAssetAtPath<LevelData>(path);
            if (ld == null || ld.SchemaVersion >= 2) continue;
            try
            {
                MigrateLevelData(ld);
                EditorUtility.SetDirty(ld);
                migrated++;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[WaveMigrator] 迁移 {path} 失败: {e}");
                failed++;
            }
        }
        if (migrated > 0 || failed > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"[WaveMigrator] 迁移完成: {migrated} 成功, {failed} 失败");
        }
    }
}
```

- [ ] **Step 2: 运行测试**

Unity Editor → Test Runner → EditMode → Wave.Tests.Editor → Run All
**期望**:5 个测试全部通过。

- [ ] **Step 3: 验证 Editor 启动时自动迁移**

随便打开一个 v1 形态的 LevelData 资产(已有 `.asset` 在 `Assets/Resources/GameDatas/...` 或 `Assets/Data/Levels/...` 之类目录;如果找不到就跳过此步,生产验证在 Phase 1a 完成后单独 commit 时再确认):
- 双击打开资产 → 看到 Inspector 显示 `SchemaVersion = 2`, `Waves[i].Tracks[0]` 含原 Actions
- Console 应有 `[WaveMigrator] 迁移完成: N 成功, 0 失败`

如果工程中没有现成 LevelData 资产,跳过此步视觉验证;Playtest 验证在 Phase 1a 完成 commit 后做。

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/LevelEditor/WaveMigrator.cs
git commit -m "feat(wave): WaveMigrator v1→v2 with InitializeOnLoad auto-migration (Phase 1a part 3)"
```

---

### Task 6: 让 `LevelActionManager.Initialize` 读 `Tracks`

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelActionManager.cs`

- [ ] **Step 1: 改 Initialize 方法**

把现有 `Initialize()` 方法的入口改成读 `wave.Tracks[].Actions`(而不是 `wave.Actions`),但保持其它逻辑不变。具体改 `Initialize()` 里的两层 `for` 循环:

```csharp
public void Initialize()
{
    _currentIndex = 0;
    _waveEntities = new List<Entity>();
    // 从 actions 扫描派生 ID -> 召唤次数,直接喂给 EntityPoolManager(不再走 WaveEntityPrefabIDs 索引)
    Dictionary<EntityID, int> entityNum = new Dictionary<EntityID, int>();
    for (int i = 0; i < _waves.Length; i++)
    {
        LevelActions.Wave wave = _waves[i];
        if (wave.Tracks == null) continue;
        for (int t = 0; t < wave.Tracks.Length; t++)
        {
            LevelActions.Action[] actions = wave.Tracks[t].Actions;
            if (actions == null) continue;
            for (int j = 0; j < actions.Length; j++)
            {
                LevelActions.Action action = actions[j];
                int perAction = action.CommandType switch
                {
                    0 => action.GapsFromLastRepeat.Length,
                    1 => 1,
                    _ => 0,
                };
                if (perAction > 0 && !action.EntityPrefabID.IsNull)
                {
                    if (!entityNum.ContainsKey(action.EntityPrefabID))
                        entityNum[action.EntityPrefabID] = 0;
                    entityNum[action.EntityPrefabID] += perAction;
                }
                switch (action.CommandType)
                {
                    case 0: break;
                    case 1: break;
                    case 2: actions[j] = WithGaps(action, new float[2] { 0, printerLifeTime }); break;
                    case 3: actions[j] = WithGaps(action, new float[2] { 0, printerLifeTime }); break;
                    case 4: actions[j] = WithGaps(action, new float[2] { 0, printerLifeTime }); break;
                    case 5: break;
                    case 6: break;
                }
            }
        }
    }
    EntityPoolManager.Manager.CreateOrExpandEntityPool(entityNum);
}

// 辅助:struct Action 改字段后回写
static LevelActions.Action WithGaps(LevelActions.Action a, float[] gaps)
{
    a.GapsFromLastRepeat = gaps;
    return a;
}
```

> 关键改动:`switch (action.CommandType)` 那几行必须用 `WithGaps` 回写(因为 Action 是 struct,直接 `action.GapsFromLastRepeat = ...` 改不到数组)。

- [ ] **Step 2: 验证编译**

Unity Editor 自动重载。Console 确认无 error。

- [ ] **Step 3: Playtest 验证**

打开任一 LevelData 资产 → 顶部 ▶ Playtest 按钮 → 确认 Wave 正常推进,Action 触发时刻与 Phase 1a 之前一致(此时 WaveProcess 仍按数组顺序,但 TriggerTime 已是绝对值,如果原数据中第一条 Action 的 GapFromLastAction > 0,可能出现 TriggerTime 与原触发时间不同——这是预期,会由 Phase 2 的排序修复)。

> 如果发现 Phase 1a 立即出现"明显不对劲"的偏差(比如首条 Action 完全不触发),检查 WaveProcess 是否仍在用 `GapFromLastAction`(而不是 `TriggerTime`)。Phase 1a 不应改 WaveProcess,只改 Initialize。

- [ ] **Step 4: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelActionManager.cs
git commit -m "feat(wave): LevelActionManager.Initialize reads from Tracks (Phase 1a part 4)"
```

---

### Task 7: Phase 1a 整体验证 + commit

- [ ] **Step 1: 运行所有测试**

Unity Editor → Test Runner → EditMode → 全部 → Run All
**期望**:WaveMigrator 5 测试通过,MapData 测试仍通过(没有破坏既有功能)。

- [ ] **Step 2: Playtest 1 个生产关卡**

选定 1 个 LevelData(从 `Assets/Resources/GameDatas/` 或类似目录找),记录:
- PathPrinter 显示时刻
- Boss 出现时刻
- 最后一个 Wave 完成时刻

Playtest,目测这些时间点是否在合理范围内(±0.1s 内)。**Phase 1a 不要求时间点精确,只要"差不多"就 OK**——精确验证留给 Phase 2 后做。

- [ ] **Step 3: git log 检查 commit 顺序**

```bash
git log --oneline -10
```

**期望**:5 个 commit 按顺序出现(Tasks 1-6),加上既有的 `a35d7d3` spec commit。

---

## Phase 1b: 清理临时字段

### Task 8: 删除 `Action.GapFromLastAction` 和 `Wave.LegacyActions`

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelActions.cs`

- [ ] **Step 1: 修改 LevelActions.cs**

把 `Action` struct 里的 `GapFromLastAction` 字段删除,把 `Wave` struct 里的 `LegacyActions` 字段删除。`FormerlySerializedAs` 也不再需要,顶部 `using UnityEngine.Serialization;` 如果无其他用途可一并删(若有保留即可)。

最终 `Wave`:
```csharp
[Serializable]
public struct Wave
{
    public Track[] Tracks;
}
```

最终 `Action`(移除 GapFromLastAction 那行):
```csharp
[Serializable]
public struct Action
{
    [Tooltip("指令类型:...")] public int CommandType;
    [Tooltip("绝对触发时间(秒),Wave 开始后 N 秒触发")] public float TriggerTime;
    [Tooltip("动作开始前函数")] public UnityEvent OnBeforeAction;
    [Tooltip("实体 ID")] public EntityID EntityPrefabID;
    [Tooltip("召唤实体阵营,1-Turret 2-Monster")] public int Camp;
    [Tooltip("动作重复函数组")] public UnityEvent<Entity> OnActionRepeat;
    [Tooltip("路径预制体序号")] public int PathSerial;
    [Tooltip("动作重复间隔时间数组(长度为重复次数)")] public float[] GapsFromLastRepeat;
    [Tooltip("是否修改实体的目标价值、首要目标、计算击杀数属性")] public bool ModifyAttributes;
    [Tooltip("修改的实体目标价值")] public int ModifyLevelHpConsume;
    [Tooltip("修改的实体是否为首要目标")] public bool ModifyPrimary;
    [Tooltip("修改的实体是否计算击杀")] public bool ModifyCountOperate;
    [Tooltip("放置位置")] public Vector2 Destination;
    [Tooltip("放置朝向")] public int Orientation;
    [Tooltip("头像")] public Image HeadImage;
    [Tooltip("内容")] public string Content;
    [Tooltip("持续时间")] public float DurationTime;
    public string[] Contents;
}
```

- [ ] **Step 2: 修改 LevelActionManager.Initialize 里的 WithGaps 调用**

由于 `GapFromLastAction` 字段已删除,Initialize 中如果还有任何引用需要清理(应该没有,Phase 1a 的 Initialize 改写没引用它)。grep 确认:

```bash
grep -rn "GapFromLastAction" Assets/
```

**期望**:无匹配。如果还有,移除。

- [ ] **Step 3: 验证编译 + 测试**

- Unity Editor 重载 → Console 无 error
- Test Runner → 全部通过(WaveMigrator 测试还应通过,因为它从 `LegacyActions` 读 — `LegacyActions` 已删,这意味着 MigrateLevelData 现在永远走"skip LegacyActions"分支 → Migrator 测试需要调整)

> **重要修正**: 删除 `LegacyActions` 后,`MigrateLevelData` 的 `LegacyActions == null → skip` 逻辑成了**唯一路径**(因为旧字段没了,所有 Wave 都走 skip)。这意味着:
> 1. 新建 LevelData 直接 `SchemaVersion = 2`,`Waves[].Tracks = [...]`,没有 `LegacyActions`
> 2. WaveMigrator 退化为"只检查 SchemaVersion < 2 则 log warning 不动数据"的兜底
> 3. 旧关卡如果还有 `GapFromLastAction` 数据在 YAML 里(Phase 1a 时迁移过的),它会被 Unity 忽略(因为 struct 字段没了),但 `Tracks` 已经有数据,运行正常

调整 Migrator(下面 Step 4)。

- [ ] **Step 4: 更新 WaveMigrator 应对 LegacyActions 已删除**

把 `MigrateLevelData` 简化,删除对 `LegacyActions` 的引用:

```csharp
public static void MigrateLevelData(LevelData ld)
{
    if (ld == null) return;
    if (ld.SchemaVersion >= 2) return;

    // Phase 1b+:LegacyActions 字段已删除。这里只兜底 v1 → v2 未在 Phase 1a 跑过迁移的关卡
    // (理论上不会发生,但 InitializeOnLoad 仍扫一遍以防 commit 跨分支合并时漏过)。
    Debug.LogWarning($"[WaveMigrator] 关卡 {ld.name} SchemaVersion={ld.SchemaVersion} 但 Tracks 为空,无法自动恢复 v1 数据,请手动重建或从 git 找回 Phase 1a 前的版本。");
    ld.SchemaVersion = 2;
}
```

- [ ] **Step 5: 更新 WaveMigratorTests**

由于 MigrateLevelData 现在无法从 LegacyActions 迁移,需要更新测试以反映新行为。**只保留一个测试**验证 v2 关卡不会被改:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace Wave.Tests
{
    public class WaveMigratorTests
    {
        [Test]
        public void Migrate_skips_level_data_already_at_v2()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.SchemaVersion = 2;
            ld.Waves = new[]
            {
                new LevelActions.Wave { Tracks = new LevelActions.Track[0] }
            };

            Assert.DoesNotThrow(() => WaveMigrator.MigrateLevelData(ld));
            Assert.AreEqual(2, ld.SchemaVersion);
            Assert.AreEqual(0, ld.Waves[0].Tracks.Length);

            Object.DestroyImmediate(ld);
        }

        [Test]
        public void DefaultTrackColor_palette_wraps_at_5()
        {
            Assert.AreEqual(WaveMigrator.DefaultTrackColor(0), WaveMigrator.DefaultTrackColor(5));
            Assert.AreEqual(WaveMigrator.DefaultTrackColor(1), WaveMigrator.DefaultTrackColor(6));
        }
    }
}
```

- [ ] **Step 6: 运行测试**

Unity Editor → Test Runner → EditMode → Wave.Tests.Editor → Run All
**期望**:2 个测试通过。

- [ ] **Step 7: Playtest 1 个生产关卡**

确认 Phase 1a 迁移过的关卡仍正常运行(Action 触发、Wave 推进、实体生成全部正常)。

- [ ] **Step 8: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelActions.cs
git add Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelActionManager.cs
git add Assets/Editor/LevelEditor/WaveMigrator.cs
git add Assets/Tests/Editor/Wave/WaveMigratorTests.cs
git commit -m "refactor(wave): remove GapFromLastAction and LegacyActions (Phase 1b cleanup)"
```

---

## Phase 2: Runtime 调度改造

### Task 9: 写 CollectAndSortActions 失败测试

**Files:**
- Create: `Assets/Tests/Editor/Wave/WaveSchedulerTests.cs`

- [ ] **Step 1: 写测试**

```csharp
using NUnit.Framework;
using UnityEngine;

namespace Wave.Tests
{
    public class WaveSchedulerTests
    {
        static LevelActions.Action MakeAction(float triggerTime, int trackIdx = 0, int actionIdx = 0, int commandType = 0)
        {
            return new LevelActions.Action
            {
                CommandType = commandType,
                TriggerTime = triggerTime,
                GapsFromLastRepeat = new float[0],
            };
        }

        [Test]
        public void Collect_returns_empty_when_wave_has_null_tracks()
        {
            var wave = new LevelActions.Wave { Tracks = null };
            var result = LevelActionScheduler.CollectAndSortActions(wave);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Collect_sorts_by_trigger_time_ascending()
        {
            var wave = new LevelActions.Wave
            {
                Tracks = new[]
                {
                    new LevelActions.Track
                    {
                        Actions = new[] { MakeAction(5f, 0, 0), MakeAction(2f, 0, 1), MakeAction(8f, 0, 2) }
                    }
                }
            };

            var result = LevelActionScheduler.CollectAndSortActions(wave);
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(2f, result[0].Action.TriggerTime, 0.0001f);
            Assert.AreEqual(5f, result[1].Action.TriggerTime, 0.0001f);
            Assert.AreEqual(8f, result[2].Action.TriggerTime, 0.0001f);
        }

        [Test]
        public void Collect_flattens_across_multiple_tracks()
        {
            var wave = new LevelActions.Wave
            {
                Tracks = new[]
                {
                    new LevelActions.Track { Actions = new[] { MakeAction(3f, 0, 0), MakeAction(7f, 0, 1) } },
                    new LevelActions.Track { Actions = new[] { MakeAction(1f, 1, 0), MakeAction(5f, 1, 1) } },
                    new LevelActions.Track { Actions = new[] { MakeAction(9f, 2, 0) } },
                }
            };

            var result = LevelActionScheduler.CollectAndSortActions(wave);
            Assert.AreEqual(5, result.Count);
            Assert.AreEqual(1f, result[0].Action.TriggerTime, 0.0001f);
            Assert.AreEqual(3f, result[1].Action.TriggerTime, 0.0001f);
            Assert.AreEqual(5f, result[2].Action.TriggerTime, 0.0001f);
            Assert.AreEqual(7f, result[3].Action.TriggerTime, 0.0001f);
            Assert.AreEqual(9f, result[4].Action.TriggerTime, 0.0001f);
        }

        [Test]
        public void Collect_tie_breaks_by_track_index_then_action_index()
        {
            // 3 个 Action 都 TriggerTime=5,TrackIndex 0/1/2,ActionIndex 0/0/0
            var wave = new LevelActions.Wave
            {
                Tracks = new[]
                {
                    new LevelActions.Track { Actions = new[] { MakeAction(5f, 0, 0) } },
                    new LevelActions.Track { Actions = new[] { MakeAction(5f, 1, 0) } },
                    new LevelActions.Track { Actions = new[] { MakeAction(5f, 2, 0) } },
                }
            };

            var result = LevelActionScheduler.CollectAndSortActions(wave);
            Assert.AreEqual(0, result[0].TrackIndex, "tie-break: track 0 first");
            Assert.AreEqual(1, result[1].TrackIndex);
            Assert.AreEqual(2, result[2].TrackIndex);
        }

        [Test]
        public void Collect_tie_breaks_by_action_index_within_same_track()
        {
            // 同 Track 两个 Action 同 TriggerTime
            var wave = new LevelActions.Wave
            {
                Tracks = new[]
                {
                    new LevelActions.Track { Actions = new[] { MakeAction(5f, 0, 0), MakeAction(5f, 0, 1) } }
                }
            };

            var result = LevelActionScheduler.CollectAndSortActions(wave);
            Assert.AreEqual(0, result[0].ActionIndex);
            Assert.AreEqual(1, result[1].ActionIndex);
        }

        [Test]
        public void Collect_skips_tracks_with_null_actions()
        {
            var wave = new LevelActions.Wave
            {
                Tracks = new[]
                {
                    new LevelActions.Track { Actions = null },
                    new LevelActions.Track { Actions = new[] { MakeAction(1f, 1, 0) } },
                }
            };

            var result = LevelActionScheduler.CollectAndSortActions(wave);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1f, result[0].Action.TriggerTime, 0.0001f);
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Test Runner → EditMode → Wave.Tests.Editor → Run All
**期望**:6 个测试全部失败(`LevelActionScheduler` 类不存在)。

- [ ] **Step 3: Commit 测试**

```bash
git add Assets/Tests/Editor/Wave/WaveSchedulerTests.cs
git commit -m "test(wave): CollectAndSortActions failing tests"
```

---

### Task 10: 抽 `LevelActionScheduler` 纯函数

**Files:**
- Create: `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelActionScheduler.cs`

- [ ] **Step 1: 写 LevelActionScheduler.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Wave → 排序后 ScheduledAction 列表的纯函数。从 LevelActionManager 抽出,便于单测。
/// 不依赖 Unity 时间 / 协程 / 单例,可纯逻辑测试。
/// </summary>
public static class LevelActionScheduler
{
    public struct ScheduledAction
    {
        public int TrackIndex;
        public int ActionIndex;
        public LevelActions.Action Action;
    }

    public static List<ScheduledAction> CollectAndSortActions(LevelActions.Wave wave)
    {
        var list = new List<ScheduledAction>();
        if (wave.Tracks == null) return list;

        for (int t = 0; t < wave.Tracks.Length; t++)
        {
            var actions = wave.Tracks[t].Actions;
            if (actions == null) continue;
            for (int a = 0; a < actions.Length; a++)
            {
                list.Add(new ScheduledAction
                {
                    TrackIndex = t,
                    ActionIndex = a,
                    Action = actions[a],
                });
            }
        }

        list.Sort((x, y) =>
        {
            int byTime = x.Action.TriggerTime.CompareTo(y.Action.TriggerTime);
            if (byTime != 0) return byTime;
            int byTrack = x.TrackIndex.CompareTo(y.TrackIndex);
            if (byTrack != 0) return byTrack;
            return x.ActionIndex.CompareTo(y.ActionIndex);
        });

        return list;
    }
}
```

- [ ] **Step 2: 运行测试**

Test Runner → EditMode → Wave.Tests.Editor → Run All
**期望**:6 个 Collect 测试全部通过。

- [ ] **Step 3: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelActionScheduler.cs
git commit -m "feat(wave): LevelActionScheduler pure function (Phase 2 part 1)"
```

---

### Task 11: 改造 `LevelActionManager.WaveProcess` + 抽 `TryAdvanceWave`

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelActionManager.cs`

- [ ] **Step 1: 替换 WaveProcess 为新版本**

把现有 `WaveProcess` 方法体替换为:

```csharp
private async void WaveProcess(LevelActions.Wave wave, CancellationToken cancellationToken)
{
    _holdingWaveWhileExistWaveEntities = true;

    var scheduled = LevelActionScheduler.CollectAndSortActions(wave);
    float waveStartTime = Time.time;
    int total = scheduled.Count;

    for (int i = 0; i < total; i++)
    {
        var entry = scheduled[i];
        float dueTime = waveStartTime + Mathf.Max(0f, entry.Action.TriggerTime);
        float wait = dueTime - Time.time;
        if (wait > 0f)
            await UniTask.WaitForSeconds(wait, false, PlayerLoopTiming.Update, cancellationToken);
        ActionProcess(entry.Action, LevelResourceSharing.LevelCtk);
    }
}
```

- [ ] **Step 2: 把 ActionProcess 末尾 + RemoveFromWaveEntities 末尾的"推进 Wave"判断抽成 TryAdvanceWave**

把原 `ActionProcess`:

```csharp
_actionProcessNum--;
if (!_holdingWaveWhileExistWaveEntities && _actionProcessNum == 0)
{
    if (_currentIndex < _waves.Length - 1)
    {
        _currentIndex++;
        WaveProcess(_waves[_currentIndex], LevelResourceSharing.LevelCtk);
    }
    else
    {
        MissionEnd(true);
    }
}
```

改为:

```csharp
_actionProcessNum--;
TryAdvanceWave();
```

把原 `RemoveFromWaveEntities`:

```csharp
if (_waveEntities.Remove(entity))
{
    if (_waveEntities.Count == 0 && _actionProcessNum == 0)
    {
        if (_currentIndex < _waves.Length - 1)
        {
            _currentIndex++;
            WaveProcess(_waves[_currentIndex], LevelResourceSharing.LevelCtk);
        }
        else
        {
            MissionEnd(true);
        }
    }
}
```

改为:

```csharp
if (_waveEntities.Remove(entity))
{
    TryAdvanceWave();
}
```

新增方法:

```csharp
private void TryAdvanceWave()
{
    if (_actionProcessNum != 0) return;
    bool okToAdvance = !_holdingWaveWhileExistWaveEntities || _waveEntities.Count == 0;
    if (!okToAdvance) return;

    if (_currentIndex < _waves.Length - 1)
    {
        _currentIndex++;
        WaveProcess(_waves[_currentIndex], LevelResourceSharing.LevelCtk);
    }
    else
    {
        MissionEnd(true);
    }
}
```

> 合并条件:`_actionProcessNum == 0` 且 (`!_holdingWaveWhileExistWaveEntities` 或 `_waveEntities.Count == 0`)。这是 v1 双条件的 OR 合并,见 spec §3.3。

同时把 `ReleaseCurrentWave` 末尾也改为调 `TryAdvanceWave`:

```csharp
public void ReleaseCurrentWave()
{
    _holdingWaveWhileExistWaveEntities = false;
    TryAdvanceWave();
}
```

- [ ] **Step 3: 验证编译**

Unity Editor 自动重载,Console 无 error。

- [ ] **Step 4: 运行所有测试**

Test Runner → EditMode → 全部 → Run All
**期望**:WaveMigrator (2) + WaveScheduler (6) + MapData 全部通过。

- [ ] **Step 5: Playtest 1 个生产关卡**

记录关键时间点(PathPrinter / Boss / 完成时刻),Playtest 比对:

**期望**:触发时刻与 Phase 1a 时记录的"差不多"的值一致(±0.1s 容忍,Frame skip / GC 等可能造成小偏差)。如果出现系统性偏差(比如所有 Action 提前 1s),检查 WaveProcess 的 `waveStartTime = Time.time` 是否在 `WaveProcess` 第一次调用时正确记录(应该在 `ToStart` 时记录,而不是 `WaveProcess` 进入时)。

- [ ] **Step 6: Commit**

```bash
git add Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperater/LevelActionManager.cs
git commit -m "feat(wave): WaveProcess uses scheduler + TryAdvanceWave extracted (Phase 2 complete)"
```

---

## Phase 3: 编辑器 UI 重写

### Task 12: 写 `LevelDataValidator` 失败测试

**Files:**
- Create: `Assets/Tests/Editor/Wave/LevelDataValidatorTests.cs`

- [ ] **Step 1: 写测试**

```csharp
using NUnit.Framework;
using UnityEngine;

namespace Wave.Tests
{
    public class LevelDataValidatorTests
    {
        static LevelData MakeValidLevelData()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.LevelName = "Test";
            ld.LevelCode = "T01";
            ld.iSize = 3;
            ld.jSize = 3;
            ld.MapData = new System.Collections.Generic.List<Tile>();
            ld.MapPrefab = null; // 校验器应当放过 null MapPrefab? 看现有规则
            ld.CutToLevelTexture = null;
            ld.CameraSize = 5f;
            ld.LevelHp = 10;
            ld.MaxCost = 100;
            ld.Cost0 = 50;
            ld.Waves = new[]
            {
                new LevelActions.Wave
                {
                    Tracks = new[]
                    {
                        new LevelActions.Track
                        {
                            Actions = new[]
                            {
                                new LevelActions.Action { CommandType = 0, TriggerTime = 1f, GapsFromLastRepeat = new float[0] }
                            }
                        }
                    }
                }
            };
            return ld;
        }

        [Test]
        public void Validator_flags_negative_trigger_time_as_error()
        {
            var ld = MakeValidLevelData();
            var action = ld.Waves[0].Tracks[0].Actions[0];
            action.TriggerTime = -0.5f;
            ld.Waves[0].Tracks[0].Actions[0] = action;

            var issues = LevelDataValidator.Validate(ld);
            Assert.IsTrue(issues.Exists(i => i.Severity == ValidationSeverity.Error && i.Message.Contains("TriggerTime")),
                $"expected TriggerTime error, got: {string.Join("; ", issues.ConvertAll(i => i.Message))}");

            Object.DestroyImmediate(ld);
        }

        [Test]
        public void Validator_passes_valid_level_data()
        {
            var ld = MakeValidLevelData();
            var issues = LevelDataValidator.Validate(ld);

            // 应当没有 TriggerTime 相关错误
            var trigErrors = issues.FindAll(i => i.Message.Contains("TriggerTime"));
            Assert.AreEqual(0, trigErrors.Count, $"unexpected TriggerTime errors: {string.Join("; ", trigErrors.ConvertAll(i => i.Message))}");

            Object.DestroyImmediate(ld);
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Test Runner → EditMode → LevelDataValidatorTests
**期望**:2 个测试可能部分通过(没有 TriggerTime 校验时),负值测试会失败。新增 TriggerTime ≥ 0 校验后全部通过(Task 13)。

- [ ] **Step 3: Commit 测试**

```bash
git add Assets/Tests/Editor/Wave/LevelDataValidatorTests.cs
git commit -m "test(wave): LevelDataValidator TriggerTime ≥ 0 failing test"
```

---

### Task 13: 加 `TriggerTime ≥ 0` 校验

**Files:**
- Modify: `Assets/Editor/LevelEditor/Validation/LevelDataValidator.cs`

- [ ] **Step 1: 加新规则**

在现有 `Validate` 方法体里追加新规则(放在最后,return issues 之前):

```csharp
// 规则 11:TriggerTime ≥ 0(所有 Action)
if (ld.Waves != null)
{
    foreach (var wave in ld.Waves)
    {
        if (wave.Tracks == null) continue;
        foreach (var track in wave.Tracks)
        {
            if (track.Actions == null) continue;
            foreach (var action in track.Actions)
            {
                if (action.TriggerTime < 0f)
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Message = $"Wave 的轨道上 Action 触发时间 TriggerTime = {action.TriggerTime} 为负值",
                    });
                }
            }
        }
    }
}
```

- [ ] **Step 2: 运行测试**

Test Runner → EditMode → LevelDataValidatorTests
**期望**:2 个测试全部通过。

- [ ] **Step 3: Commit**

```bash
git add Assets/Editor/LevelEditor/Validation/LevelDataValidator.cs
git commit -m "feat(wave): LevelDataValidator checks TriggerTime ≥ 0 (Phase 3 part 1)"
```

---

### Task 14: 创建 `WaveTrackRow.cs`(单轨道行)

**Files:**
- Create: `Assets/Editor/LevelEditor/Sections/WaveTrackRow.cs`

- [ ] **Step 1: 写 WaveTrackRow.cs**

```csharp
using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 单条轨道行:左侧轨道头(Name / Color / Locked / 排序手柄 / + / ×),右侧时间轴 + Action 卡片。
/// 构造时只建一次结构,字段级变更走增量更新(通过 TrackPropertyValue 触发 Rebuild)。
/// </summary>
public static class WaveTrackRow
{
    public class State
    {
        public TextField NameField;
        public VisualElement ColorSwatch;
        public Button LockButton;
        public VisualElement DragHandle;
        public VisualElement CardsContainer;  // 时间轴容器
        public Label EndLabel;
    }

    public static VisualElement Build(
        int waveIdx,
        int trackIdx,
        SerializedProperty trackProp,
        SerializedObject so,
        Action<int, int, int> onActionSelected,
        Func<(int, int, int)> getCurrentSelection,
        Action rebuild)
    {
        var row = new VisualElement();
        row.AddToClassList("level-editor-section");
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginBottom = 4;

        // === 左侧:轨道头 ===
        var header = new VisualElement();
        header.style.width = 180;
        header.style.flexShrink = 0;
        header.style.backgroundColor = new Color(0.13f, 0.13f, 0.16f);
        header.style.paddingTop = 4;
        header.style.paddingBottom = 4;
        header.style.paddingLeft = 6;
        header.style.paddingRight = 6;
        header.style.borderTopLeftRadius = 3;
        header.style.borderBottomLeftRadius = 3;
        row.Add(header);

        // 排序手柄(占位,Phase 3 内简化版只显示不动)
        var dragHandle = new Label("≡");
        dragHandle.style.fontSize = 14;
        dragHandle.style.color = new Color(0.6f, 0.6f, 0.6f);
        dragHandle.style.unityTextAlign = TextAnchor.MiddleCenter;
        dragHandle.style.width = 16;
        header.Add(dragHandle);

        // Name(可编辑)
        var nameField = new TextField { value = trackProp.FindPropertyRelative("Name").stringValue };
        nameField.style.flexGrow = 1;
        nameField.style.marginLeft = 2;
        nameField.style.marginRight = 2;
        nameField.RegisterValueChangedCallback(evt =>
        {
            Undo.RecordObject(so.targetObject, "Rename Track");
            trackProp.FindPropertyRelative("Name").stringValue = evt.newValue;
            so.ApplyModifiedProperties();
        });
        header.Add(nameField);

        // Color swatch
        var colorProp = trackProp.FindPropertyRelative("TrackColor");
        var swatch = new VisualElement();
        swatch.style.width = 18;
        swatch.style.height = 18;
        swatch.style.borderTopLeftRadius = 2;
        swatch.style.borderTopRightRadius = 2;
        swatch.style.borderBottomLeftRadius = 2;
        swatch.style.borderBottomRightRadius = 2;
        swatch.style.marginRight = 2;
        swatch.style.backgroundColor = colorProp.colorValue;
        swatch.style.borderLeftWidth = 1;
        swatch.style.borderRightWidth = 1;
        swatch.style.borderTopWidth = 1;
        swatch.style.borderBottomWidth = 1;
        swatch.style.borderLeftColor = new Color(0.3f, 0.3f, 0.3f);
        swatch.style.borderRightColor = new Color(0.3f, 0.3f, 0.3f);
        swatch.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f);
        swatch.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);
        var colorField = new ColorField { value = colorProp.colorValue };
        colorField.style.position = Position.Absolute;
        colorField.style.left = -1000; // 隐藏,仅用 swatch 点击触发
        colorField.style.width = 1;
        colorField.style.height = 1;
        colorField.RegisterValueChangedCallback(evt =>
        {
            Undo.RecordObject(so.targetObject, "Change Track Color");
            colorProp.colorValue = evt.newValue;
            so.ApplyModifiedProperties();
            swatch.style.backgroundColor = evt.newValue;
        });
        swatch.Add(colorField);
        // swatch 点击 → 显示 ColorField 拾色器
        swatch.RegisterCallback<ClickEvent>(_ => colorField.value = colorProp.colorValue);
        // 简化版:用 ColorField 直接放小按钮代替弹窗
        var colorPickerBtn = new Button(() =>
        {
            // 简化:打开系统 ColorField 弹窗
            EditorGUI.BeginChangeCheck();
            Color newColor = EditorGUILayout.ColorField("Track Color", colorProp.colorValue);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(so.targetObject, "Change Track Color");
                colorProp.colorValue = newColor;
                so.ApplyModifiedProperties();
                swatch.style.backgroundColor = newColor;
                rebuild?.Invoke();
            }
        }) { text = "🎨" };
        colorPickerBtn.style.width = 22;
        colorPickerBtn.style.height = 18;
        colorPickerBtn.style.fontSize = 9;
        header.Add(colorPickerBtn);
        header.Add(swatch);

        // Lock button
        var lockProp = trackProp.FindPropertyRelative("Locked");
        var lockBtn = new Button(() =>
        {
            Undo.RecordObject(so.targetObject, "Toggle Track Lock");
            lockProp.boolValue = !lockProp.boolValue;
            so.ApplyModifiedProperties();
            lockBtn.text = lockProp.boolValue ? "🔒" : "🔓";
        }) { text = lockProp.boolValue ? "🔒" : "🔓" };
        lockBtn.style.width = 22;
        lockBtn.style.height = 18;
        lockBtn.style.fontSize = 10;
        header.Add(lockBtn);

        // + / × 行内按钮
        var btnRow = new VisualElement();
        btnRow.style.flexDirection = FlexDirection.Row;
        btnRow.style.marginTop = 4;

        var addActionBtn = new Button(() =>
        {
            var actionsProp = trackProp.FindPropertyRelative("Actions");
            Undo.RecordObject(so.targetObject, "Add Action");
            actionsProp.InsertArrayElementAtIndex(actionsProp.arraySize);
            var newAction = actionsProp.GetArrayElementAtIndex(actionsProp.arraySize - 1);
            newAction.FindPropertyRelative("CommandType").intValue = 0;
            // TriggerTime = 同 Track 内最大值 + 1s(空 Track 则 0)
            float maxTrig = 0f;
            for (int i = 0; i < actionsProp.arraySize - 1; i++)
            {
                float t = actionsProp.GetArrayElementAtIndex(i).FindPropertyRelative("TriggerTime").floatValue;
                if (t > maxTrig) maxTrig = t;
            }
            newAction.FindPropertyRelative("TriggerTime").floatValue = maxTrig + 1f;
            newAction.FindPropertyRelative("GapsFromLastRepeat").arraySize = 0;
            so.ApplyModifiedProperties();
        }) { text = "+" };
        addActionBtn.style.flexGrow = 1;
        addActionBtn.style.marginRight = 2;
        btnRow.Add(addActionBtn);

        var delActionBtn = new Button(() =>
        {
            var actionsProp = trackProp.FindPropertyRelative("Actions");
            if (actionsProp.arraySize == 0) return;
            var (selW, selT, selA) = getCurrentSelection();
            bool willInvalidate = selW == waveIdx && selT == trackIdx && selA == actionsProp.arraySize - 1;
            if (EditorUtility.DisplayDialog("删除 Action", $"确认删除 Track {trackIdx} 的最后一个 Action?", "删除", "取消"))
            {
                Undo.RecordObject(so.targetObject, "Delete Action");
                actionsProp.DeleteArrayElementAtIndex(actionsProp.arraySize - 1);
                so.ApplyModifiedProperties();
                if (willInvalidate) onActionSelected?.Invoke(-1, -1, -1);
            }
        }) { text = "×" };
        delActionBtn.style.flexGrow = 1;
        btnRow.Add(delActionBtn);

        header.Add(btnRow);

        // === 右侧:时间轴 ===
        var timelineScroll = new ScrollView(ScrollViewMode.Horizontal);
        timelineScroll.style.flexGrow = 1;
        timelineScroll.style.height = 70;
        timelineScroll.style.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
        timelineScroll.style.borderTopRightRadius = 3;
        timelineScroll.style.borderBottomRightRadius = 3;
        timelineScroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
        timelineScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        row.Add(timelineScroll);

        var cardsContainer = new VisualElement();
        cardsContainer.style.height = 70;
        cardsContainer.style.position = Position.Relative;
        cardsContainer.style.overflow = Overflow.Visible;
        timelineScroll.Add(cardsContainer);

        // Track 状态对象(后续 Phase 3 增量更新用)
        var state = new State
        {
            NameField = nameField,
            ColorSwatch = swatch,
            LockButton = lockBtn,
            DragHandle = dragHandle,
            CardsContainer = cardsContainer,
        };
        cardsContainer.userData = state;

        // 卡片渲染(委托给 WaveActionCard)
        var actionsProp = trackProp.FindPropertyRelative("Actions");
        WaveActionCard.Render(cardsContainer, actionsProp, waveIdx, trackIdx, onActionSelected, () => lockProp.boolValue, rebuild);
        cardsContainer.TrackPropertyValue(actionsProp, _ =>
            WaveActionCard.Render(cardsContainer, actionsProp, waveIdx, trackIdx, onActionSelected, () => lockProp.boolValue, rebuild));

        return row;
    }
}
```

> 注:本 Task 的代码块较大,实现时可拆为多个子步骤。先做骨架(纯渲染),再补充拖拽(后续 Task)。

- [ ] **Step 2: 验证编译**

Unity Editor 重载,Console 无 error(此时 WaveActionCard 还不存在,会有编译错误,这是预期;后续 Task 15 创建 WaveActionCard 后才能编译)。

- [ ] **Step 3: (跳过)此 Task 不单独 commit**

与 Task 15 一起 commit(Task 15 完成后再 commit 两个文件)。

---

### Task 15: 创建 `WaveActionCard.cs`(卡片 + 拖拽)

**Files:**
- Create: `Assets/Editor/LevelEditor/Sections/WaveActionCard.cs`

- [ ] **Step 1: 写 WaveActionCard.cs**

```csharp
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Action 卡片渲染 + 拖拽手柄。
/// 中段拖拽改 TriggerTime,右沿拖拽改持续时间(spawner = 按比例缩放 GapsFromLastRepeat,dialog = 改 DurationTime,其它不可拖)。
/// 锁定轨道(pickingMode=Ignore)不接收鼠标。
/// </summary>
public static class WaveActionCard
{
    public class State
    {
        public List<Button> Cards = new();
        public int LastActionCount = -1;
    }

    static float PixelsPerSecond() => 24f * WaveTimelineSection.Zoom;  // 共享缩放
    static Color CommandTypeColor(int cmd) => WaveTimelineSection.CommandTypeColor(cmd);

    public static void Render(
        VisualElement container,
        SerializedProperty actionsProp,
        int waveIdx,
        int trackIdx,
        Action<int, int, int> onActionSelected,
        Func<bool> isLocked,
        Action rebuild)
    {
        var state = container.userData as State;
        if (state == null)
        {
            state = new State();
            container.userData = state;
        }

        // 结构性变化(add/delete)整树重建;字段级变化走下面的增量更新
        if (state.LastActionCount != actionsProp.arraySize)
        {
            container.Clear();
            state.Cards.Clear();

            if (actionsProp.arraySize == 0)
            {
                var empty = new Label("(空)");
                empty.style.color = new Color(0.4f, 0.4f, 0.4f);
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                empty.style.position = Position.Absolute;
                empty.style.left = 8;
                empty.style.top = 26;
                container.Add(empty);
            }
            else
            {
                for (int i = 0; i < actionsProp.arraySize; i++)
                {
                    int actionIdx = i;
                    var card = new Button(() => onActionSelected?.Invoke(waveIdx, trackIdx, actionIdx))
                    { text = $"A{i}" };
                    card.style.position = Position.Absolute;
                    card.style.top = 22;
                    card.style.height = 36;
                    card.style.width = 60;  // 起始宽度,Render 阶段按 Duration 调整
                    card.style.color = new Color(0, 0, 0);
                    card.style.fontSize = 9;
                    card.style.paddingLeft = 2;
                    card.style.paddingRight = 2;
                    container.Add(card);
                    state.Cards.Add(card);

                    // 拖拽手柄:中段 → TriggerTime,右沿 → Duration
                    var drag = new ActionCardDragManipulator(card, actionsProp, actionIdx, isLocked, rebuild);
                    card.AddManipulator(drag);
                }
            }

            state.LastActionCount = actionsProp.arraySize;
        }

        if (actionsProp.arraySize == 0) return;

        // 增量更新:位置 + 宽度 + 颜色
        float pxPerSec = PixelsPerSecond();
        for (int i = 0; i < actionsProp.arraySize; i++)
        {
            var a = actionsProp.GetArrayElementAtIndex(i);
            var card = state.Cards[i];

            float triggerTime = a.FindPropertyRelative("TriggerTime").floatValue;
            card.style.left = triggerTime * pxPerSec;

            int cmd = a.FindPropertyRelative("CommandType").intValue;
            float duration = ComputeEndTime(a, cmd) - triggerTime;
            float width = Mathf.Max(20f, duration * pxPerSec);
            card.style.width = width;
            card.style.backgroundColor = CommandTypeColor(cmd);

            // 锁定轨道:pickingMode Ignore(只挡拖拽,不挡点击 — Button.click 仍走 picking)
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderTopWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftColor = isLocked() ? new Color(0.8f, 0.6f, 0.2f) : new Color(0.3f, 0.3f, 0.3f);
            card.style.borderRightColor = card.style.borderLeftColor;
            card.style.borderTopColor = card.style.borderLeftColor;
            card.style.borderBottomColor = card.style.borderLeftColor;
        }
    }

    /// <summary>
    /// 计算 Action 右端时间(TriggerTime + 占用时长)。
    /// 见 spec §2.4 派生表。
    /// </summary>
    public static float ComputeEndTime(SerializedProperty actionProp, int commandType)
    {
        float triggerTime = actionProp.FindPropertyRelative("TriggerTime").floatValue;
        switch (commandType)
        {
            case 0: // spawner:TriggerTime + sum(GapsFromLastRepeat)
                var gapsProp = actionProp.FindPropertyRelative("GapsFromLastRepeat");
                float sum = 0f;
                for (int i = 0; i < gapsProp.arraySize; i++)
                    sum += Mathf.Max(0f, gapsProp.GetArrayElementAtIndex(i).floatValue);
                return triggerTime + sum;
            case 2:
            case 3:
            case 4: // path preview:GapsFromLastRepeat 硬编码 = {0, printerLifeTime}
                var gapsProp2 = actionProp.FindPropertyRelative("GapsFromLastRepeat");
                float sum2 = 0f;
                for (int i = 0; i < gapsProp2.arraySize; i++)
                    sum2 += Mathf.Max(0f, gapsProp2.GetArrayElementAtIndex(i).floatValue);
                return triggerTime + sum2;
            case 5: // dialog:TriggerTime + DurationTime
                return triggerTime + Mathf.Max(0f, actionProp.FindPropertyRelative("DurationTime").floatValue);
            case 1:
            case 6: // 静态 / 剧情:无右端,返回 triggerTime(纯点)
            default:
                return triggerTime;
        }
    }
}

/// <summary>
/// ActionCard 的拖拽 Manipulator:
/// - 中段(除右沿 6px)按下:水平拖动改 TriggerTime(吸附 0.1s,Shift 关闭)
/// - 右沿 6px:水平拖动改右端(spawner 按比例缩放 GapsFromLastRepeat,dialog 改 DurationTime,其它不变)
/// </summary>
public class ActionCardDragManipulator : MouseManipulator
{
    static float PixelsPerSecond() => 24f * WaveTimelineSection.Zoom;
    const float RightEdgeWidth = 6f;

    readonly SerializedProperty _actionsProp;
    readonly int _actionIdx;
    readonly Func<bool> _isLocked;
    readonly Action _rebuild;

    Vector2 _startMouse;
    float _startTriggerTime;
    float _startEndTime;

    public ActionCardDragManipulator(VisualElement target, SerializedProperty actionsProp, int actionIdx, Func<bool> isLocked, Action rebuild)
        : base(target)
    {
        _actionsProp = actionsProp;
        _actionIdx = actionIdx;
        _isLocked = isLocked;
        _rebuild = rebuild;
        activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
    }

    protected override void RegisterCallbacksOnTarget()
    {
        target.RegisterCallback<MouseDownEvent>(OnMouseDown);
        target.RegisterCallback<MouseMoveEvent>(OnMouseMove);
        target.RegisterCallback<MouseUpEvent>(OnMouseUp);
    }

    protected override void UnregisterCallbacksFromTarget()
    {
        target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
        target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
        target.UnregisterCallback<MouseUpEvent>(OnMouseUp);
    }

    void OnMouseDown(MouseDownEvent evt)
    {
        if (_isLocked()) return;
        var actionProp = _actionsProp.GetArrayElementAtIndex(_actionIdx);
        float endTime = WaveActionCard.ComputeEndTime(actionProp, actionProp.FindPropertyRelative("CommandType").intValue);

        _startMouse = evt.mousePosition;
        _startTriggerTime = actionProp.FindPropertyRelative("TriggerTime").floatValue;
        _startEndTime = endTime;
        target.CaptureMouse();
        evt.StopPropagation();
    }

    void OnMouseMove(MouseMoveEvent evt)
    {
        if (!target.HasMouseCapture()) return;

        var actionProp = _actionsProp.GetArrayElementAtIndex(_actionIdx);
        float pxPerSec = PixelsPerSecond();
        float dx = evt.mousePosition.x - _startMouse.x;

        // 判定是否在右沿
        float cardRight = (_startEndTime - _startTriggerTime) * pxPerSec;
        float mouseOffsetFromRight = cardRight - (dx);

        bool isRightEdge = Mathf.Abs(mouseOffsetFromRight) < RightEdgeWidth;
        // 简化:中段拖动即认为是 TriggerTime 拖动(右沿判断用初始卡宽,移动过程中失效)
        // 精确做法:在 OnMouseDown 时记录鼠标相对卡右沿的距离,这里判断
        // 这里为了简洁,统一按 TriggerTime 处理;右沿手柄在 Phase 3 后续 PR 中可加

        if (evt.shiftKey)
        {
            // 关闭吸附
        }
        else
        {
            float rawDelta = dx / pxPerSec;
            float snapped = Mathf.Round(rawDelta * 10f) / 10f;
            dx = snapped * pxPerSec;
        }

        float newTrigger = Mathf.Max(0f, _startTriggerTime + dx / pxPerSec);
        actionProp.FindPropertyRelative("TriggerTime").floatValue = newTrigger;
        _actionsProp.serializedObject.ApplyModifiedProperties();
        _rebuild?.Invoke();
        evt.StopPropagation();
    }

    void OnMouseUp(MouseUpEvent evt)
    {
        if (target.HasMouseCapture())
        {
            target.ReleaseMouse();
            evt.StopPropagation();
        }
    }
}
```

> 注:上述 Manipulator 简化了"右沿拖拽 vs 中段拖拽"的判定(目前都按 TriggerTime 处理)。Phase 3 完成后再补右沿缩放 GapsFromLastRepeat 的逻辑,作为可选 follow-up(在 spec 里属于"应做"项,但代码块太大,先 commit 最小可用版本)。

- [ ] **Step 2: 修改 WaveTimelineSection 添加 Zoom 访问器**

`WaveActionCard.PixelsPerSecond` 引用了 `WaveTimelineSection.Zoom`(public static),在 `WaveTimelineSection.cs` 顶部加:

```csharp
public static float Zoom => _zoom;
```

把 `_zoom` 改为 public static(原本是 private static),或加 `public static float Zoom => _zoom;` 属性。

- [ ] **Step 3: 修改 WaveTimelineSection 添加 CommandTypeColor 访问器**

`WaveActionCard.CommandTypeColor` 引用 `WaveTimelineSection.CommandTypeColor`,同样改 public static:

```csharp
public static Color CommandTypeColor(int cmd) => CommandTypeColorInternal(cmd);
// (原本是 private static Color CommandTypeColor(int cmd) { switch ... })
```

把现有 `CommandTypeColor` 方法的 private 改为 public(同时保留行为)。

- [ ] **Step 4: 验证编译**

Unity Editor 重载,Console 无 error。

- [ ] **Step 5: Commit Task 14 + 15 一起**

```bash
git add Assets/Editor/LevelEditor/Sections/WaveTrackRow.cs
git add Assets/Editor/LevelEditor/Sections/WaveActionCard.cs
git add Assets/Editor/LevelEditor/Sections/WaveTimelineSection.cs
git commit -m "feat(wave): WaveTrackRow + WaveActionCard (Phase 3 part 2)"
```

---

### Task 16: 重写 `WaveTimelineSection.cs` 使用 WaveTrackRow

**Files:**
- Modify: `Assets/Editor/LevelEditor/Sections/WaveTimelineSection.cs`

- [ ] **Step 1: 把 Build 方法改为渲染 N 个 Wave × M 个 Track**

现有 `Build` 方法内部已经构建了 Wave + 单行 timeline。需要改成 Wave → N 个 TrackRow。

替换 Build 方法的核心部分(保留 `Build` 签名、缩放控件、新增 Wave 按钮):

```csharp
public static VisualElement Build(
    SerializedObject so,
    Action<int, int, int> onActionSelected,
    Func<(int, int, int)> getCurrentSelection)
{
    var section = new VisualElement();
    section.AddToClassList("level-editor-section");
    section.style.width = Length.Percent(100);

    var title = new Label("▸ 波次时间线");
    title.AddToClassList("level-editor-section-title");
    section.Add(title);

    var wavesProp = so.FindProperty("Waves");
    var wavesListContainer = new VisualElement();
    section.Add(wavesListContainer);

    Action rebuild = () => RebuildWaves(wavesListContainer, wavesProp, so, onActionSelected, getCurrentSelection);
    rebuild();

    section.Add(BuildZoomControls(rebuild));

    int lastWaveCount = wavesProp.arraySize;
    section.TrackPropertyValue(wavesProp, _ =>
    {
        if (wavesProp.arraySize != lastWaveCount)
        {
            lastWaveCount = wavesProp.arraySize;
            rebuild();
        }
    });

    var addWaveBtn = new Button(() =>
    {
        Undo.RecordObject(so.targetObject, "Add Wave");
        wavesProp.InsertArrayElementAtIndex(wavesProp.arraySize);
        var newWave = wavesProp.GetArrayElementAtIndex(wavesProp.arraySize - 1);
        newWave.FindPropertyRelative("Tracks").arraySize = 0;
        so.ApplyModifiedProperties();
    })
    { text = "+ 新增 Wave" };
    addWaveBtn.style.marginTop = 6;
    section.Add(addWaveBtn);

    return section;
}

static void RebuildWaves(VisualElement container, SerializedProperty wavesProp, SerializedObject so, Action<int, int, int> onActionSelected, Func<(int, int, int)> getCurrentSelection)
{
    container.Clear();
    for (int w = 0; w < wavesProp.arraySize; w++)
    {
        container.Add(BuildWaveBlock(w, wavesProp.GetArrayElementAtIndex(w), so, onActionSelected, getCurrentSelection));
    }
}

static VisualElement BuildWaveBlock(int waveIdx, SerializedProperty waveProp, SerializedObject so, Action<int, int, int> onActionSelected, Func<(int, int, int)> getCurrentSelection)
{
    var block = new VisualElement();
    block.style.backgroundColor = new Color(0.15f, 0.15f, 0.18f);
    block.style.borderTopLeftRadius = 3;
    block.style.borderTopRightRadius = 3;
    block.style.borderBottomLeftRadius = 3;
    block.style.borderBottomRightRadius = 3;
    block.style.paddingTop = 4;
    block.style.paddingBottom = 4;
    block.style.paddingLeft = 6;
    block.style.paddingRight = 6;
    block.style.marginBottom = 8;

    // Wave 标签
    var tracksProp = waveProp.FindPropertyRelative("Tracks");
    var label = new Label($"Wave {waveIdx} · {tracksProp.arraySize} tracks");
    label.style.color = new Color(0.8f, 0.8f, 0.8f);
    label.style.fontSize = 11;
    label.style.marginBottom = 4;
    block.Add(label);

    // 重建闭包(供 TrackRow 调)
    Action rebuild = () =>
    {
        // 找到这个 wave 块在父容器中的位置,移除后重建
        var parent = block.parent;
        int idx = parent.IndexOf(block);
        parent.RemoveAt(idx);
        parent.Insert(idx, BuildWaveBlock(waveIdx, waveProp, so, onActionSelected, getCurrentSelection));
    };

    // 各 Track 行
    var tracksContainer = new VisualElement();
    block.Add(tracksContainer);

    Action rebuildTracks = () =>
    {
        tracksContainer.Clear();
        for (int t = 0; t < tracksProp.arraySize; t++)
        {
            tracksContainer.Add(WaveTrackRow.Build(
                waveIdx, t,
                tracksProp.GetArrayElementAtIndex(t),
                so, onActionSelected, getCurrentSelection,
                rebuild));
        }
    };
    rebuildTracks();

    int lastTracksCount = tracksProp.arraySize;
    tracksContainer.TrackPropertyValue(tracksProp, _ =>
    {
        if (tracksProp.arraySize != lastTracksCount)
        {
            lastTracksCount = tracksProp.arraySize;
            rebuildTracks();
        }
    });

    // 按钮行
    var btnRow = new VisualElement();
    btnRow.style.flexDirection = FlexDirection.Row;
    btnRow.style.marginTop = 4;

    var addTrackBtn = new Button(() =>
    {
        Undo.RecordObject(so.targetObject, "Add Track");
        tracksProp.InsertArrayElementAtIndex(tracksProp.arraySize);
        var newTrack = tracksProp.GetArrayElementAtIndex(tracksProp.arraySize - 1);
        newTrack.FindPropertyRelative("Name").stringValue = $"Track {tracksProp.arraySize - 1}";
        newTrack.FindPropertyRelative("TrackColor").colorValue = WaveMigrator.DefaultTrackColor(tracksProp.arraySize - 1);
        newTrack.FindPropertyRelative("Locked").boolValue = false;
        newTrack.FindPropertyRelative("Actions").arraySize = 0;
        so.ApplyModifiedProperties();
    })
    { text = "+ 新增 Track" };
    addTrackBtn.style.flexGrow = 1;
    addTrackBtn.style.marginRight = 4;
    btnRow.Add(addTrackBtn);

    var delWaveBtn = new Button(() =>
    {
        var (selW, _, _) = getCurrentSelection();
        bool willInvalidate = selW >= 0 && waveIdx <= selW;
        if (EditorUtility.DisplayDialog("删除 Wave", $"确认删除 Wave {waveIdx}?", "删除", "取消"))
        {
            Undo.RecordObject(so.targetObject, "Delete Wave");
            waveProp.serializedObject.FindProperty("Waves").DeleteArrayElementAtIndex(waveIdx);
            so.ApplyModifiedProperties();
            if (willInvalidate) onActionSelected?.Invoke(-1, -1, -1);
        }
    })
    { text = "× 删除 Wave" };
    btnRow.Add(delWaveBtn);

    block.Add(btnRow);

    return block;
}
```

保留顶部 `static float _zoom = 1f;` 和 `BuildZoomControls` 方法。

**删除** 现有 `RenderActionCards` / `WaveTimelineState` / `ComputeMaxTime` 等私有方法(已被 `WaveActionCard.Render` 替代)。

- [ ] **Step 2: 修改 `CommandTypeColor` 和 `_zoom` 为 public**

```csharp
public static float _zoom = 1f;
// 加访问器(供 WaveActionCard.PixelsPerSecond 调用)
public static float Zoom => _zoom;
public static Color CommandTypeColor(int cmd) { /* 现有 switch */ }
```

- [ ] **Step 3: 验证编译**

Unity Editor 重载,Console 无 error。

- [ ] **Step 4: 手动验证编辑器**

打开任一 LevelData 资产,确认:
- 多条 Track 行可见(默认 1 条"默认")
- Action 卡片按 TriggerTime 渲染在时间轴上
- 拖动卡片 → TriggerTime 变化(详情面板数值同步更新)
- 缩放 slider 正常
- + / × / 🎨 / 🔒 按钮功能正常

- [ ] **Step 5: Commit**

```bash
git add Assets/Editor/LevelEditor/Sections/WaveTimelineSection.cs
git commit -m "feat(wave): WaveTimelineSection renders multi-track (Phase 3 part 3)"
```

---

### Task 17: 更新 `ActionDetailSection.cs`

**Files:**
- Modify: `Assets/Editor/LevelEditor/Sections/ActionDetailSection.cs`

- [ ] **Step 1: 改 Build 签名加 trackIdx**

```csharp
public static VisualElement Build(SerializedObject so, int waveIdx, int trackIdx, int actionIdx)
{
    // ...
    var actionsProp = so.FindProperty($"Waves.Array.data[{waveIdx}].Tracks.Array.data[{trackIdx}].Actions");
    // ...
    var header = new Label($"▸ 已选 Wave[{waveIdx}].Track[{trackIdx}].Action[{actionIdx}]");
    // ...
    section.Add(MakeRow(actionProp.FindPropertyRelative("CommandType")));
    section.Add(MakeRow(actionProp.FindPropertyRelative("TriggerTime")));  // ← 原来是 GapFromLastAction
    // ...
}
```

把所有 `waveIdx, actionIdx` 引用补上 `trackIdx`。

- [ ] **Step 2: 验证编译**

Unity Editor 重载,Console 无 error。

- [ ] **Step 3: 手动验证**

打开 LevelData → 点击任一 Action 卡片 → ActionDetailSection 显示 "▸ 已选 Wave[0].Track[0].Action[2]" + TriggerTime 字段(不再是 GapFromLastAction)。改 TriggerTime → 时间轴卡片实时移动。

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/LevelEditor/Sections/ActionDetailSection.cs
git commit -m "feat(wave): ActionDetailSection adds Track dimension + TriggerTime field (Phase 3 part 4)"
```

---

### Task 18: 更新 `LevelDataEditor.cs` 选中状态三元组

**Files:**
- Modify: `Assets/Editor/LevelEditor/LevelDataEditor.cs`

- [ ] **Step 1: 改 onActionSelected 调用与 getCurrentSelection 签名**

搜索文件中所有 `ActionSelected`、`onActionSelected`、`getCurrentSelection` 引用,改成三元组 `(waveIdx, trackIdx, actionIdx)`。

涉及的具体改动:
- 字段:`(int, int) _selectedAction` → `(int, int, int) _selectedAction`
- `OnActionSelected(int w, int a)` → `OnActionSelected(int w, int t, int a)`
- `GetCurrentSelection()` 返回类型同步改三元组

- [ ] **Step 2: 验证编译 + 手动测试**

Unity Editor 重载。打开 LevelData → 选中 Action → 详情面板显示正确路径。Undo/Redo 不会丢失选中状态。

- [ ] **Step 3: Commit**

```bash
git add Assets/Editor/LevelEditor/LevelDataEditor.cs
git commit -m "feat(wave): LevelDataEditor selection tuple adds Track (Phase 3 part 5)"
```

---

### Task 19: 运行所有测试 + Playtest 全量验证

- [ ] **Step 1: 跑全部测试**

Test Runner → EditMode → 全部 → Run All
**期望**:WaveMigrator (2) + WaveScheduler (6) + LevelDataValidator (2) + MapData 全部通过。

- [ ] **Step 2: Playtest 所有现存关卡**

打开每个 LevelData 资产,顶部 ▶ Playtest → 验证:
- 触发时刻与 v1 一致(±0.1s)
- 实体生成正常
- Wave 推进正常
- PathPrinter 显示正常

- [ ] **Step 3: 编辑器手动验证清单**

- [ ] 添加 / 重命名 / 删除 / 重排 Track
- [ ] 拖卡片改 TriggerTime,Playtest 验证新时间生效
- [ ] (右沿拖拽在 Phase 3 基础版未实现,可后续 PR)
- [ ] 锁定轨道不可拖卡片

---

## Phase 4: 文档 + 收尾

### Task 20: 更新 `docs/level-editor-usage.md`

**Files:**
- Modify: `docs/level-editor-usage.md`(如有相关段落)

- [ ] **Step 1: 补充多轨道和绝对时间相关内容**

如果有相关章节(原本描述 GapFromLastAction、Wave 内 Action 数组顺序等),更新为:
- "绝对触发时间 TriggerTime" 取代 "距上一 Action 间隔 GapFromLastAction"
- "Wave 内多 Track" 取代 "Wave 内 Action[]"

- [ ] **Step 2: Commit**

```bash
git add docs/level-editor-usage.md
git commit -m "docs: level editor usage covers multi-track + TriggerTime"
```

---

### Task 21: Phase 4 整体验收 + commit 检查

- [ ] **Step 1: 跑全部测试 + Playtest**

```bash
git log --oneline | head -30
```

**期望**:看到以下 commit 序列(每个 commit 都对应一个 Task):
1. spec(`a35d7d3`)
2. Wave.Tracks[] + Action.TriggerTime (Task 1)
3. LevelData.SchemaVersion (Task 2)
4. Wave.Tests.Editor asmdef (Task 3)
5. WaveMigrator failing tests (Task 4)
6. WaveMigrator 实现 (Task 5)
7. LevelActionManager.Initialize 读 Tracks (Task 6)
8. 删 GapFromLastAction + LegacyActions (Task 8)
9. CollectAndSortActions failing tests (Task 9)
10. LevelActionScheduler 纯函数 (Task 10)
11. WaveProcess 改造 + TryAdvanceWave (Task 11)
12. LevelDataValidator failing tests (Task 12)
13. LevelDataValidator TriggerTime ≥ 0 (Task 13)
14. WaveTrackRow + WaveActionCard (Tasks 14+15)
15. WaveTimelineSection 多轨道 (Task 16)
16. ActionDetailSection (Task 17)
17. LevelDataEditor 选中三元组 (Task 18)
18. (可选)文档更新 (Task 20)

- [ ] **Step 2: 验收清单**

逐项核对 `docs/superpowers/specs/2026-06-30-wave-multi-track-redesign-design.md` §8 验收标准:

- [ ] 所有现存 LevelData 自动迁移成功,Playtest 通过
- [ ] 编辑器可添加 / 重命名 / 删除 / 重排 Track
- [ ] 编辑器可拖卡片改 TriggerTime,内部节奏保持
- [ ] (右沿拖拽 spawner 卡片等比缩放 GapsFromLastRepeat — Phase 3 基础版未实现,留作 follow-up)
- [ ] 锁定轨道不可拖卡片
- [ ] LevelDataValidator 报 TriggerTime 负值
- [ ] 文档更新(如涉及)

完成后报告。