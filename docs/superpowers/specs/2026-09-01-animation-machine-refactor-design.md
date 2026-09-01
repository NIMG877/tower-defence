# 动画状态机重构设计（AnimationMachine 解耦）

- 日期：2026-09-01
- 状态：设计已获用户逐节确认，待实施
- 前置审计：见记忆 `animation-machine-coupling-audit`（2026-09-01）

## 1. 背景与问题

`AnimationMachine.cs`（约 1000 行）事实上的实体主状态机 + 表现层 + 生命周期管理器三合一：

1. **9 种职责混在一个类**：状态转换、攻击五相位子状态机、覆盖解析、Spine 细节、DOTween 变色、朝向、池回收、受击反馈、时长查询。
2. **`CurrentState` 是全局逻辑真相源**：22 个文件扇入查询（MoveBase/AttackBase/BuffController/技能系统），同时它反伸手 Entity（Stats/Movement/OnAfterHurt/池）。
3. **表现驱动逻辑与生命周期**：FadeOut 补间回调直接 `pool.Return`；跳跃物理与技能时序取自动画时长。
4. **扩展成本高**：新增一个 AnimationSlot 需 ≈15 处镜像 switch；新增逻辑状态受枚举顺序优先级牵制。
5. **Die 后门**：HeadSeterSkill1 / WitherTalent2 / WdslmSkill3 借 `AddOverride(Start=x)+TrySetState(Die)` 播自定义动画。
6. **状态真值靠动画资产身份反推**（ClassifyAnimation），同资产多槽错分类风险；`SetState` 不直接赋值，状态经 Spine Start 事件异步回写。

## 2. 目标与非目标

**目标**：
- 逻辑状态与动画表现分离，数据流单向（逻辑 → 表现）。
- 回收时机归 Entity/池；表现层只上报时机事件。
- 新增槽位/状态的修改点收敛到 O(1) 处。
- 技能播自定义动画走正式通道（新增 `Cast` 状态），`Die` 回归纯死亡语义。
- 逻辑状态机成为纯 C# 类，可 EditMode 单测。

**非目标**：
- 不改 `AnimationResources` SO 的结构（已分层良好：固定模板 + 命名资源）。
- 不改"跳跃物理/技能时序取自动画时长"的现状（核心等价约束；是否改由数值驱动另议）。
- 不迁移遗留 prefab 脚本到数据管线（wdslm 未接入 EntityDataCollection 属另一条迁移线）。
- 不清理 prefab 上 `o_*` 孤儿序列化字段（Unity 下次保存自动丢弃，不冒险手改 YAML）。

**约束**：
- **核心等价**：移动/攻击/死亡/部署的对外表现逐帧等价（含高攻速连击缩放，不得回归 de3f195 修过的问题）。
- **边缘重对齐**：Die 后门技能的表现按意图在新机制下重写（见 §7）。
- **干净无壳**：不留转发属性、兼容层、迁移标记；旧 API 删除即删净。
- 一次到位，不分批。

## 3. 架构总览

```
Entity（拥有者/协调者，PreWarm 构造注入，同 AttributeStore/EntityMovement 先例）
 ├─ EntityStateMachine   纯C#类：EntityState + AttackPhase + 显式优先级表 + ban表
 ├─ AnimationMachine     MonoBehaviour（保留类名）：槽位解析(字典化) + Spine播放/mix + 时机上报
 ├─ EntityVisuals        MonoBehaviour：受击闪红 / 淡入淡出（运行时 AddComponent，同 BuffController 装配先例）
 └─ EntityFacing         MonoBehaviour：朝向与翻转（运行时 AddComponent）
```

数据流单向：逻辑方（Move/Attack/Buff/Skill）→ `entity.StateMachine`；状态机 →(StateChanged 事件)→ 动画层；动画层 →(时机上报)→ 状态机/Entity。`currentState` 唯一持有者是状态机，`SetState` 即时生效。

## 4. EntityStateMachine

### 4.1 状态集与优先级

`Default, Idle, Move, Attack, Start, Cast(新增), Die`。

优先级集中一处（替代枚举声明顺序魔法）：

```
Default=0 < Idle=1 < Move=2 < Attack=3 < Start=4 < Cast=5 < Die=6
```

### 4.2 转换规则（保持现状语义，核心等价）

- 非强制：目标优先级 > 当前（或 Attack→Attack 连击窗口特例）且目标未被 ban → 允许。
- 强制：当前 != Die 且目标未被 ban → 允许。
- 收敛进唯一可测方法 `CanTransition(from, to, force)`。

### 4.3 AttackPhase（None/Active/ComboWindow/End）

归逻辑侧：连击判定、连击 index、0.05s ComboWindow 计时（状态机内计时，Entity 的 FixedUpdate 驱动 tick）。动画层只上报"攻击主动段播完"。旧实现的 Begin（前摇段）从未被任何判定读取，属表现细节，留在动画层编排里，逻辑侧不建模。

### 4.4 API

```csharp
bool TrySetState(EntityState state, bool force = false);
bool TrySetAttackState(bool force, Action attackAction);   // action 于 OnAttack 帧触发，一次性
void AddStateToBan(EntityState[] states);
void RemoveStateFromBan(EntityState[] states);
EntityState CurrentState { get; }                            // 即时生效
AttackPhase CurrentAttackPhase { get; }

// 动画层→状态机的时机上报
void NotifyAttackActiveCompleted(int groupLength);          // 主动段播完 → ComboWindow / 推进连击 index
void NotifyAttackEndCompleted();                            // End 段播完（或单发无 End 主动段播完）→ 回 Idle
void NotifyStartAnimationCompleted();                       // Start 播完 → 回 Idle（Cast 为粘性演出态，不走此通道）

// 事件
event Action<EntityState, EntityState> StateChanged;
event Action DieAnimationCompleted;                          // Entity 订阅，驱动淡出+回池
```

**废除反推**：删除 `ClassifyAnimation` 与资产身份判定；动画层用自己排队的 `TrackEntry` 引用判断"哪段播完"。

## 5. AnimationMachine（瘦身后）

只做三件事：

1. **播放**：订阅 `StateChanged` → 按状态播对应槽（Move 分支、攻击 Begin→Active→End 序列编排与接续排队、Start 播完回 Idle、Cast 粘性保持末帧、Die）。攻击动画按 `Stats.BaseAttackTimeS` 缩放的行为原样保留。
2. **覆盖系统**：`AddOverride / AddOneShotOverride / RemoveOverride(s)` 签名不变，内部字典化；one-shot 消费时机不变（真正播放槽位时消费整个条目）。
3. **时机上报**：Spine `OnAttack` → 触发状态机一次性攻击回调；TrackEntry 播完 → `NotifyAttackActiveCompleted / NotifyStateAnimationCompleted / DieAnimationCompleted`。

保留在动画层的对外 API：
- `OnAttackAnimationBegin` 事件（攻击动画开播帧，表现时机语义；EntityAbilityRunner/Skill 订阅不变）。
- `ResolveAnimationDuration / ResolveNamedAnimationDuration`（时长查询服务）。
- `SetMoveBranch(MoveAnimationBranch)`（Move 分支是纯表现分支；逻辑方先 `TrySetState(Move)` 成功后再设分支，替代原 `TrySetMoveState` 的原子性）。

### 5.1 槽位字典化

```csharp
sealed class AnimationSet {
    Dictionary<AnimationSlot, AnimationReferenceAsset>   _singles; // Idle/Move/Die/Start/AttackBegin/Charge/...
    Dictionary<AnimationSlot, AnimationReferenceAsset[]> _groups;  // AttackRemote/AttackClose/Charge
}
```

- 消除 7 组镜像 switch/if（`GetSingle/GetGroup/Clear/Apply/GetCoveredSlots/From/Copy`）。
- 新增槽位 = 枚举加一项 + `From` 填一行 + SetState 映射表一行。
- 两份 15 行 mix 注册清单合并为 `RegisterMixes(AnimationSet)`，`PreWarm` 与 `AddOverride` 共用。

### 5.2 AnimationOverride 新形态

```csharp
// Dictionary<AnimationSlot, string> + Cleared 集合
new AnimationOverride { [AnimationSlot.Idle] = "skillLoop", [AnimationSlot.Cast] = "skillBegin" }
    .Clear(AnimationSlot.Move)
```

`GetCoveredSlots = Entries.Keys ∪ Cleared`，一处逻辑。

## 6. EntityVisuals / EntityFacing / 回收链

- **EntityVisuals**：`FlashRed / FadeIn / FadeOut(duration, onComplete)` 公开 API；自订阅 `OnAfterHurt` 做受击闪红（`applyType != 2` 提取为命名常量后原样迁移）。
- **EntityFacing**：`SetDirection(target)` + `CurrentDirection`。
- **回收链**（表现不再驱动生命周期）：
  - 死亡：`Entity.Die()` → 状态机转 Die → 动画层播 Die → 播完 `DieAnimationCompleted` → Entity：`visuals.FadeOut(0.2s, () => pool.Return(this))`。
  - 到达终点：`MoveBase` 调 `entity.ArriveEnd()`（转 Default + 淡出 + 回池）；删除 `AnimationMachine.ArriveEnd`。
- 组件装配：`EntityVisuals/EntityFacing` 在 `EntityPool.CreateNewEntity` 运行时 `AddComponent`（同 `BuffController/Entity` 先例，零 prefab 改动）。
- 生命周期：三个 MonoBehaviour 各自实现 `IPoolOperation` 并自复位全部实例字段；`EntityStateMachine` 由 Entity 的 PreWarm/Initialize/Dormancy 显式 Reset（连 `Attack` 组数组等易滞留字段一并清）。

## 7. Cast 状态与遗留技能重对齐

**Cast 语义**：技能演出态；播 Cast 槽动画（不循环），播完**保持末帧（粘性）**，不自动回 Idle——钻地潜伏等演出依赖末帧保持；转出由技能显式 `TrySetState(Idle/Default, true)` 负责。优先级介于 Start 与 Die 之间（死亡可打断演出，演出优先于普攻）。

| 技能 | 现状（Die 后门/正常覆盖） | 重对齐为 |
|---|---|---|
| HeadSeterSkill1（钻地） | `override Start` + `TrySetState(Die)` ×2 | 两阶段各覆盖 Cast 槽 + `TrySetState(Cast)`（粘性保持潜伏姿态）；钻出动画播完显式回 Idle 恢复行走；不可选中 buff 对不变 |
| WitherTalent2（凋灵复活） | Idle 覆盖（正常）；终局 `override Start` + `Die` | 复活循环照旧；终局 = Cast 播 `_start2` → 播完爆炸 → HpCheck 狂暴 + 显式回 Idle（**存活**，恢复正常行为） |
| WdslmSkill2（策反） | `override Attack*`（正常） | 仅索引化语法，行为不变 |
| WdslmSkill3（变身） | `override Start/Idle` + `Die` ×2 | 开局 Cast 播 `_skillStart` → 落地后显式回 Idle 循环 `_skillLoop`；结束 Cast 播 `_skillEnd` → 播完显式退场（本体未入数据管线，编译级验证） |
| HeadSeterTalent1 / WitchSkill / BeefSkill | 正常覆盖 API | 仅索引化语法，行为不变 |

**旧后门机制（考古结论）**：`SetState(Die)` 播的是 SO 的 Die 槽资产（非覆盖的 Start）；覆盖 Start 的作用是劫持 `ClassifyAnimation`（判定顺序 Start 在 Die 前）把播放中的动画分类成 Start → Die-complete 分支不触发 → 不淡出不回池。三个旧技能的终局意图（恢复行走/复活存活/显式退场）在旧机制下并无通路——属意图明确、机制残缺的遗留代码，故按意图重写。

## 8. 调用方迁移映射（22 文件）

| 调用方 | 迁移 |
|---|---|
| Entity.cs | PreWarm 构造 `_stateMachine`；`Die()/Initialize` 走状态机；订阅 `DieAnimationCompleted`；新增 `ArriveEnd()` |
| MoveBase / JumpMove | 状态查询与转换 → 状态机；`TrySetMoveState(branch)` → `TrySetState(Move)` + `SetMoveBranch`；终点 → `entity.ArriveEnd()` |
| AttackBase / NormalAttack / ChargeAttack / WitherAttack | `TrySetAttackState` → 状态机；状态查询 → 状态机 |
| BuffController | ban 表 API → 状态机 |
| Bullet | `CurrentDirection` → `EntityFacing` |
| EntityAbilityRunner / Skill.cs | `OnAttackAnimationBegin` 订阅保留在动画层 |
| ApplyAnimationOverride / RemoveAnimationOverride / SharedTargetExtraAttack / ChargeStateController | 覆盖 API 索引化；`TrySetSlot` switch 随字典化消失，`Cast` 槽自动可解析 |
| AttackBase 的 `SetDirection`、SetDirection 调用点 | → `EntityFacing` |

## 9. 测试计划

- **适配**：`AnimationOverrideCoveredSlotsTests`、`ApplyAnimationOverrideTests`（新覆盖 API）。
- **新增（EditMode，纯 C#）**：
  - `EntityStateMachineTests`：优先级表、非强制单向、Attack 连击窗口、ban 表、强制转换（Die 终态）、播完回 Idle、one-shot 攻击回调消费。
  - `AnimationSetTests`：覆盖解析、Clear 槽、优先级排序应用、字典解析。
- **编译**：全 asmdef 零错误。
- **用户 PlayMode 验收清单**：干员部署/移动/攻击/受击闪红/死亡淡出回池；已接入管线的 6 种怪物同上；HeadSeter 钻地；高攻速连击缩放不回归。

## 10. 实施顺序（同一重构系列，分步提交保持编译绿）

1. 新增 `EntityStateMachine` + 单测（纯增量）。
2. AnimationMachine 字典化 + 编排/上报改造 + `EntityVisuals/EntityFacing` 拆出。
3. Entity 接线（状态机构造、回收链、ArriveEnd）。
4. 全部调用方迁移。
5. 遗留技能重写（§7 表）。
6. 测试适配 + 全量编译 + 自审。

## 11. 风险

- **攻击编排逐帧等价**是本次最脆弱处：Begin/Active/ComboWindow/End 的切槽与 TimeScale 缩放逻辑原样搬运，只换宿主，不顺带"优化"。
- **池复用复位**：状态机 + 三组件的全部实例字段需在 Dormancy/Initialize 链路复位（审计已发现 `Attack` 数组滞留先例）。
- **一次到位的编译面**：22 个调用方同批迁移，靠编译器兜底找全改点。
- Die 后门技能重对齐无法全部 PlayMode 验证（wdslm 未入管线），以编译 + 已入管线怪物（HeadSeter/Wither）验证。
