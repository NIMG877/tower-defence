# Wave 多轨道 + 绝对时间重设计

**日期**: 2026-06-30
**作者**: brainstorming + Claude
**状态**: 设计已批准,进入实现

## 1. 背景与目标

### 1.1 现状

`LevelActions.Action.GapFromLastAction` 是相对值(距离上一动作的间隔),设计师在编辑器里看到的是数组顺序而不是时间顺序,编排困难:

- 编辑时只能改"间隔",无法直接对"在 5 秒时发生"建模
- 单 Wave 只有一条时间轴(隐式的 `Action[]`),无法按"主轨道 / 路径预览 / 支援"等维度分类组织 Action
- 现有 `WaveTimelineSection` 在渲染层已经把 `GapFromLastAction` 累加成绝对位置画卡,但**存储仍是相对的**

### 1.2 目标

1. **绝对时间编辑**:Action 存储绝对 `TriggerTime`(Wave 开始后 N 秒触发)
2. **Wave 内多命名轨道**:Wave 内可自由增删命名轨道,每条轨道独立编排 Action
3. **最小数据变动**:除 `GapFromLastAction → TriggerTime` 外,Action 的其它字段名/语义/填写规则**全部保留**(尤其是 `GapsFromLastRepeat[]`)

### 1.3 不在范围内

- 不新增 `Action.Duration` / `Action.RepeatCount` / `Action.RepeatInterval` 字段
- 不改任何其它 Action 字段的"何时填写"规则
- 不重构 AbilitySystem / EntityPoolManager 等周边系统
- 不改 Wave 间过渡逻辑(`_actionProcessNum` / `_waveEntities` / `_holdingWaveWhileExistWaveEntities`)
- Wave / Track struct 是新结构,不受"禁止其它变量改名"约束(`Action` 才受约束)

## 2. 数据模型

### 2.1 新结构

```csharp
[Serializable]
public struct Wave
{
    public Track[] Tracks;   // 替代原先 Action[]

    // ↓ 临时迁移辅助字段,所有 LevelData 迁移完成后删除
    [SerializeField, HideInInspector, FormerlySerializedAs("Actions")]
    internal Action[] LegacyActions;
}

[Serializable]
public struct Track
{
    public string Name;        // 设计师命名,默认"默认"
    public bool Locked;        // 锁定后时间轴上拖不动
    public Action[] Actions;
}

[Serializable]
public struct Action
{
    public int CommandType;
    public float TriggerTime;          // ← 重命名自 GapFromLastAction,语义:绝对秒数
    public float[] GapsFromLastRepeat; // ★ 不变
    public UnityEvent OnBeforeAction;
    public EntityID EntityPrefabID;
    public int Camp;
    public UnityEvent<Entity> OnActionRepeat;
    public int PathSerial;
    public bool ModifyAttributes;
    public int ModifyLevelHpConsume;
    public bool ModifyPrimary;
    public bool ModifyCountOperate;
    public Vector2 Destination;
    public int Orientation;
    public Image HeadImage;
    public string Content;
    public float DurationTime;
    public string[] Contents;
}
```

### 2.2 LevelData 变化

```csharp
public class LevelData : ScriptableObject
{
    // ... 现有字段不变 ...
    public int SchemaVersion = 2;       // ★ 新增;v1 关卡识别后迁移
    public LevelActions.Wave[] Waves;   // 内部结构变了,见 §2.1
    // ... 现有其它字段不变 ...
}
```

### 2.3 字段语义对照表

| 字段 | v1 (旧) | v2 (新) |
|---|---|---|
| `GapFromLastAction` | 距离上一 Action 的秒数 | **删除,改名 TriggerTime** |
| `TriggerTime` | (不存在) | Wave 开始后 N 秒触发 |
| `GapsFromLastRepeat[]` | spawner 重复间隔 | **不变,语义不变,填写规则不变** |
| `DurationTime` | 对话框展示时长 | **不变** |
| `Wave.Actions[]` | Wave 直接持 Action 数组 | **改为 `Wave.Tracks[]`**,每条 Track 持 Action 数组 |

### 2.4 编辑器"Range Bar"端点的派生(运行时计算,不存)

| CommandType | 卡片右端 = TriggerTime + | 重复点 |
|---|---|---|
| 0 spawner | `sum(GapsFromLastRepeat)` | 各 GapsFromLastRepeat[i] 累加点位 |
| 2/3/4 路径预览 | `sum(GapsFromLastRepeat)` ≈ `printerLifeTime`(硬编码) | — |
| 5 对话框 | `DurationTime` | — |
| 1 静态 / 6 剧情 | 无右端(纯点标记) | — |

## 3. Runtime 调度

### 3.1 核心改造

`LevelActionManager.WaveProcess` 由"按数组顺序遍历"改为"按 TriggerTime 排序后单调度循环":

```csharp
private async void WaveProcess(LevelActions.Wave wave, CancellationToken ct)
{
    _holdingWaveWhileExistWaveEntities = true;
    var scheduled = CollectAndSortActions(wave);  // (trackIdx, actionIdx, Action) 排序后列表
    float waveStartTime = Time.time;

    for (int i = 0; i < scheduled.Count; i++)
    {
        var action = scheduled[i].Item3;
        float dueTime = waveStartTime + Mathf.Max(0f, action.TriggerTime);
        float wait = dueTime - Time.time;
        if (wait > 0f)
            await UniTask.WaitForSeconds(wait, false, PlayerLoopTiming.Update, ct);
        ActionProcess(action, ct);
    }
}

private List<(int, int, LevelActions.Action)> CollectAndSortActions(LevelActions.Wave wave)
{
    var list = new List<(int, int, LevelActions.Action)>();
    if (wave.Tracks == null) return list;
    for (int t = 0; t < wave.Tracks.Length; t++)
    {
        var actions = wave.Tracks[t].Actions;
        if (actions == null) continue;
        for (int a = 0; a < actions.Length; a++)
            list.Add((t, a, actions[a]));
    }
    list.Sort((x, y) =>
    {
        int byTime = x.Item3.TriggerTime.CompareTo(y.Item3.TriggerTime);
        if (byTime != 0) return byTime;
        int byTrack = x.Item1.CompareTo(y.Item1);
        if (byTrack != 0) return byTrack;
        return x.Item2.CompareTo(y.Item2);
    });
    return list;
}
```

### 3.2 不变部分

- `ActionProcess(action, ct)`:内部按 `GapsFromLastRepeat[]` 顺序等间隔触发 `ActionRepeat`,完全保留
- `_actionProcessNum` / `_waveEntities` / `_holdingWaveWhileExistWaveEntities` 三个状态量含义不变
- `AddToWaveEntities` / `RemoveFromWaveEntities` / `ReleaseCurrentWave` / `ToStart` / `ToEnd` / `MissionEnd` / `CountTotalNeedOperateNum` 全部保持原状
- `Initialize()` 扫 Action 聚合 `EntityID → 召唤次数` 的逻辑保留,只是入口从 `wave.Actions` 改为 `wave.Tracks[].Actions`
- `Initialize()` 中 CommandType 2/3/4 的 `GapsFromLastRepeat = {0, printerLifeTime}` 硬编码保留(因为 `GapsFromLastRepeat[]` 字段没改,这条 magic behavior 不变)

### 3.3 抽出公共方法

原 `ActionProcess` 末尾和 `RemoveFromWaveEntities` 末尾重复的"是否推进 Wave"判断抽成 `TryAdvanceWave()`,由 ActionProcess 结束、RemoveFromWaveEntities 调用。

合并条件:`_actionProcessNum == 0 && (!_holdingWaveWhileExistWaveEntities || _waveEntities.Count == 0)` 时推进下一 Wave 或 MissionEnd。

### 3.4 行为差异(明确告知)

- **跨 Wave 顺序**:Wave[0] 全部 Action + Wave[0] 实体清空 → Wave[1] 开始(不变)
- **Wave 内 Action 顺序**:严格按 `TriggerTime` 升序,同时间按 TrackIndex → ActionIndex(★ 与 v1 不同)
- **不再校验 TriggerTime 倒序**(用户确认不需要)

## 4. 迁移与校验

### 4.1 迁移 V1 → V2

**核心机制**:`Wave` struct 用 `[FormerlySerializedAs("Actions")]` + 临时隐藏字段 `LegacyActions` 接收旧 YAML 数据。Unity 序列化器读到 v1 YAML 时,自动把 `Actions` 数据塞进 `LegacyActions`。Editor 端的 `[InitializeOnLoad]` 迁移器把 `LegacyActions` 转换为 `Tracks`,清空 `LegacyActions`,设置 `SchemaVersion = 2`。**所有资产迁移完成后,在后续 commit 删除 `LegacyActions` 字段**(此时已无 YAML 含 `Actions` 字段)。

```csharp
[InitializeOnLoadMethod]
private static void MigrateLegacyLevels()
{
    var guids = AssetDatabase.FindAssets("t:LevelData");
    int migrated = 0, failed = 0;
    foreach (var guid in guids)
    {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        var ld = AssetDatabase.LoadAssetAtPath<LevelData>(path);
        if (ld == null || ld.SchemaVersion >= 2) continue;
        try
        {
            MigrateV1ToV2(ld);
            ld.SchemaVersion = 2;
            EditorUtility.SetDirty(ld);
            migrated++;
        }
        catch (Exception e)
        {
            Debug.LogError($"[WaveRedesign] 迁移 {path} 失败: {e}");
            failed++;
        }
    }
    if (migrated > 0 || failed > 0)
        AssetDatabase.SaveAssets();
    if (migrated > 0 || failed > 0)
        Debug.Log($"[WaveRedesign] 迁移完成: {migrated} 成功, {failed} 失败");
}

private static void MigrateV1ToV2(LevelData ld)
{
    for (int w = 0; w < ld.Waves.Length; w++)
    {
        var wave = ld.Waves[w];

        // LegacyActions 在新结构里是隐藏字段;非 null 表示 v1 数据还在等迁移
        Action[] oldActions = wave.LegacyActions;
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

        wave.Tracks = new[] { new LevelActions.Track
        {
            Name = "默认",
            Locked = false,
            Actions = newActions,
        } };
        wave.LegacyActions = null;   // 清空,标脏
        ld.Waves[w] = wave;          // struct 回写
    }
}
```

迁移幂等:已迁移的(`SchemaVersion >= 2`)或 `LegacyActions == null` 的跳过。

### 4.1.1 迁移时序(关键)

1. **提交 A (struct 改造)**:`Wave` 增加 `LegacyActions` + `[FormerlySerializedAs("Actions")]`;`LevelData` 增加 `SchemaVersion`。**保留** `Action.GapFromLastAction` 字段(迁移需要读)
2. **Editor 自动重载**:Unity 读所有 LevelData 资产,把 YAML 里的 `Actions` 映射进 `LegacyActions`
3. **提交 A 里的 `[InitializeOnLoadMethod]` 自动跑迁移**:`LegacyActions` → `Tracks`,清空 `LegacyActions`,写回资产
4. **验证**:Playtest 1 个生产关卡,确认 Action 触发时间一致
5. **提交 B (清理)**:删除 `Action.GapFromLastAction` 字段 + 删除 `Wave.LegacyActions` 字段 + 把 Action 重命名后的 Tooltip 写最终版

### 4.2 校验

`LevelDataValidator` 现有规则全部保留(Waves 非空 / EntityPrefabID / LevelHp / MaxCost / Paths 等),新增 1 条:

- **TriggerTime ≥ 0**:任一 Action 的 TriggerTime < 0 报 Error

不校验"Action 是否按 TriggerTime 升序"(用户确认不需要)。

### 4.3 测试策略

最低验证:
1. **迁移前**:记下 1 个生产关卡(如 `Level001`)的关键 Action 时间点(PathPrinter 显示时刻、Boss 出现时刻)
2. **迁移后**:同关卡 Playtest,观察实际触发时刻,确认 ±0.1s 内一致
3. **多轨道手动测试**:为该关卡新增 1 条 Track "路径预览",把现有 2 个 Path Preview Action 移过去,Playtest 验证
4. **拖拽测试**:手动拖卡片改 TriggerTime,Playtest 验证新时间生效
5. **锁定轨道测试**:锁定某 Track,验证编辑器拖不动、详情面板仍可改

## 5. 编辑器 UI

### 5.1 文件结构

| 文件 | 职责 |
|---|---|
| `WaveTimelineSection.cs` | 入口 + 时间标尺 + 容纳 N 个 TrackRow,**重写** |
| `WaveTrackRow.cs`(新) | 单条轨道:轨道头 + 时间轴 + Action 卡片 |
| `WaveActionCard.cs`(新) | 卡片渲染 + 拖拽手柄(中段改 TriggerTime, 右沿改持续时间) |
| `WaveScaleBar.cs`(新) | 顶部时间刻度尺(ticks + labels);和卡片共用 cardsContainer,横向同步滚动 |
| `ActionDetailSection.cs` | 路径多一层 Track,`GapFromLastAction` 字段名换 `TriggerTime`,其它原样 |
| `LevelDataEditor.cs` | onActionSelected 改三元组 `(waveIdx, trackIdx, actionIdx)` |

### 5.2 整体布局(每个 Wave 内部)

```
┌─ Wave 0 ──────────────────────────────────────────────┐
│ ┌─ 缩放 1.0× ─────────────────────────────────────┐  │
│ ├─ 时间标尺(顶部,sticky,不随竖滚)───────────────┤  │
│ │ 0s   1s   2s   3s   4s   5s   6s   7s   8s ... │  │
│ ├─ Track[0] "默认"  [色] [🔓] [≡] [+] [×] ───────┤  │
│ │  ┌──────┐    ┌────┐              ◆              │  │
│ │  │S×5@1s│    │D3s │              Boss           │  │
│ │  └──────┘    └────┘                             │  │
│ ├─ Track[1] "路径预览" [色] [🔓] [≡] [+] [×] ────┤  │
│ │      ┌──┐              ┌──┐                     │  │
│ │      │  │              │  │  ← 路径预览          │  │
│ │      └──┘              └──┘                     │  │
│ ├─ [+ 新增 Track]                                │  │
│ └─────────────────────────────────────────────────┘  │
│ [× 删除 Wave]                                        │
└──────────────────────────────────────────────────────┘
```

### 5.3 交互细节

**时间标尺**
- 高度 ~22px,顶部一行 tick + label
- **位置**:Wave 顶部 sticky(每个 Wave block 独立一行,在所有 Track 行上方),不随竖滚滚动
- **横向滚动同步**:scaleScroll + 每条 Track 的 timelineScroll 共享 `horizontalScroller.value`,任意一个滚动其余跟随
- 复用现有 `ChooseTickInterval`(`WaveTimelineSection.cs:228`)
- 缩放 slider 全 Wave 共享(沿用 `_zoom` static,注释保留多 Inspector 副作用说明)

**轨道头**
- **命名**:行内可编辑 TextField,默认"默认 / 路径预览 / 支援 / Boss"
- **锁定图标**:🔓 / 🔒 toggle
- **拖拽手柄(≡)**:垂直拖动改 Track 数组顺序(同 Wave 内)
- **[+]**:在本 Track 末尾追加新 Action,默认 `CommandType=0, TriggerTime=本 Track 最大值+1s`
- **[×]**:删除 Track(若含 Action 弹确认)

**Action 卡片**

| CommandType | 形态 | 右沿拖拽 |
|---|---|---|
| 0 spawner | Range bar + 内部小刻度 | 可拖 → 按比例缩放 `GapsFromLastRepeat` |
| 2/3/4 路径预览 | Range bar | **不可拖**(`GapsFromLastRepeat[1]=printerLifeTime` 硬编码) |
| 5 对话框 | Range bar | 可拖 → 改 `DurationTime` |
| 1 静态 | 菱形 ◆ | 无 |
| 6 剧情 | 菱形 ◆ | 无 |

**拖拽规则**
- 中段:改 `TriggerTime`,内部节奏自动跟随(`GapsFromLastRepeat` / `DurationTime` 不变)
- 默认吸附到 0.1s,Shift 关闭
- 锁定轨道:卡片仍可点击选中,但 pickingMode=Ignore,不接收鼠标拖拽

### 5.4 详情面板

- 路径 `Waves.Array.data[w].Tracks.Array.data[t].Actions.Array.data[a]`
- `GapFromLastAction` PropertyField 改名 `TriggerTime` + Tooltip "绝对触发时间(秒)"
- CommandType 条件显隐逻辑**完全保留**(各自"何时填写"规则不变)

### 5.5 颜色规则

2026-06-30 移除:Track 不再带颜色(用户反馈视觉信号收益不大,锁定状态已有橙色描边)。Action 卡片填色仍按 `CommandType`(沿用 `WaveTimelineSection.CommandTypeColor`)。

## 6. 实施顺序(每个阶段单独 PR / 提交)

### Phase 1a: 数据模型 + 迁移接入
- 改 `LevelActions.cs`:
  - `Wave` 新增 `Tracks[]` + `LegacyActions`(临时,`[FormerlySerializedAs("Actions")]`)
  - `Action` **保留** `GapFromLastAction`,**新增** `TriggerTime`(暂不删旧字段,等迁移验证后再删)
  - 新增 `Track` struct
- 改 `LevelData.cs`:加 `SchemaVersion = 2`(默认;新关卡直接是 v2)
- 新增 `WaveMigrator.cs`:`MigrateV1ToV2` + `[InitializeOnLoadMethod]` 迁移钩子(读 `LegacyActions`,写 `Tracks`,清空 `LegacyActions`,`SchemaVersion=2`)
- 改 `LevelActionManager.cs`:`Initialize` 入口改 `wave.Tracks[].Actions` 读取;**不改** `WaveProcess` 内部逻辑(此时 WaveProcess 仍按数组顺序遍历)

提交 → Editor 重载 → 自动迁移所有 LevelData → Playtest 1 个生产关卡验证。

### Phase 1b: 清理临时字段
- 删 `Wave.LegacyActions` 字段
- 删 `Action.GapFromLastAction` 字段
- 把 `TriggerTime` 的 Tooltip 改成正式版

### Phase 2: Runtime 调度改造
- 改 `LevelActionManager.WaveProcess`:改为 `CollectAndSortActions` + 单调度循环
- 抽出 `TryAdvanceWave()` 方法,合并 ActionProcess 末尾 + RemoveFromWaveEntities 末尾重复判断
- Playtest 验证 Action 触发时刻与 v1 一致(±0.05s)

### Phase 3: 编辑器 UI 重写
- 新增 `WaveTrackRow.cs`、`WaveActionCard.cs`
- 重写 `WaveTimelineSection.cs`:多轨道布局
- 改 `ActionDetailSection.cs`:路径加 Track 维度 + `GapFromLastAction` PropertyField 改名为 `TriggerTime`
- 改 `LevelDataEditor.cs`:选中状态变三元组 `(waveIdx, trackIdx, actionIdx)`
- `LevelDataValidator.cs`:新增 `TriggerTime ≥ 0` 校验

### Phase 4: Playtest 全量验证
- Playtest 所有现存关卡,记录关键时间点
- 更新文档(`docs/level-editor-usage.md`)如有涉及

## 7. 风险与回滚

| 风险 | 缓解 |
|---|---|
| 迁移函数 bug 污染所有关卡 | Phase 1 完成后,**单独 commit 迁移脚本**,Playtest 1 个关卡验证再推 Phase 2 |
| Action struct 字段重命名后旧 prefab/scene 引用失效 | Unity 序列化按字段名匹配,字段名改了旧引用会丢——但 `LevelData` 是 ScriptableObject,所有引用都通过 ScriptableObject 资产本身,无 prefab/scene 内嵌,所以安全 |
| 新调度顺序与 v1 行为不完全一致(同时间 tie-break 变化) | Playtest 验证,误差应 < 0.05s |
| 编辑器拖拽性能 | 复用现有 `WaveTimelineState` 增量更新模式,新增 `WaveTrackState` |
| 锁定轨道误判 pickingMode | 详面板可编辑;只挡时间轴拖拽 |

回滚策略:每 Phase 单独 commit,出问题 `git revert` 该 commit 即可。

## 8. 验收标准

- [ ] 所有现存 LevelData 自动迁移成功,Playtest 通过
- [ ] 编辑器可添加 / 重命名 / 删除 / 重排 Track
- [ ] 编辑器可拖卡片改 TriggerTime,内部节奏保持
- [ ] 编辑器拖 spawner 卡片右沿可等比缩放重复间隔
- [ ] 锁定轨道不可拖卡片
- [ ] `LevelDataValidator` 报 TriggerTime 负值
- [ ] 文档更新(如涉及)