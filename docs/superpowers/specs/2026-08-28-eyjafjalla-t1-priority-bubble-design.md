# eyjafjalla_t1 优先攻击泡泡 + 特殊攻击动画 · 设计文档

日期：2026-08-28
状态：已与用户对齐（新列表前插组件 + once 动画按原始思路配置）；§5 once 时机缺陷已于同日治本修复
（once 改为注册待用一次性覆盖，不切状态，见 §5 修复记录）

## 1. 背景

eyjafjalla_t1 资产 description 中挂起的「缺口②」：存在泡泡时优先攻击泡泡并切换本次攻击动画。
决策点在索敌返回→TryToAttack 之间，即 `OnBeforeTargetSelect`（triggerEvent 18）。

现状两个空缺：

1. **无"前插"原语**：InjectAttackTargets（源列表去重后插到候选最前）在统一 override 重构时已删除；
   现有组件面（WriteBlackboard/EntityFilter/AttackCandidateOverride）只能表达整表覆盖。
2. ~~**动画 once 模式在 trigger 18 时机有系统级缺陷**~~（已于同日治本修复，见 §5 修复记录）。

## 2. 新组件 PrependEntities

**Canonical op:** `prepend_entities`（自动注册：`[RegisterComponent("PrependEntities")]` → ToSnakeCase）
**文件:** `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/PrependEntities.cs`

把黑板一个 `List<Entity>` 源列表**去重后插到另一个目标列表最前**（原地，目标列表对象不变——
沿用 AttackCandidateOverride 的"Clear+AddRange 同对象"约定，下游继续读同一键）。

### 参数

| Key | Type | Default | Description |
|---|---|---|---|
| `blackboardKey` | String | `""` | 目标列表键（插入 destination）。 |
| `sourceKey` | String | `""` | 源列表键（被前插的实体，保持源顺序）。 |

### 语义与边界

- 目标键缺失/非列表、源键缺失/非列表：OneShotWarn + 跳过（配线错误暴露，与 EntityFilter/AttackCandidateOverride 同口径）。
- 去重是"提权"语义：源实体按源顺序整体排到最前，目标里原有的重复项从原位移除（视界内泡泡本就在候选里——效果是提权到最前，而非重复计入或保留在后面）。
- 源列表中的 null 实体跳过（与 EntitySelector subjectMode=blackboard 同口径）；目标列表中的 null 不动。
- 源为空列表：无操作（合法状态）。

## 3. eyjafjalla_t1.asset 新增规则 5

triggerEvent 18（OnBeforeTargetSelect），reentry 0。流水线按用户原始步骤：

```yaml
- triggers: [{triggerEvent: 18, groups: []}]
  reentry: 0
  steps:
  # ① 获取 bb 泡泡数量
  - op: write_blackboard
    args: {entries: [{key: key, value: eyjafjalla_t1_bubble_count, fromBlackboard: 0, type: 3},
                     {key: source, value: listCount, fromBlackboard: 0, type: 3},
                     {key: path, value: eyjafjalla_t1_bubbles, fromBlackboard: 0, type: 3}]}
  # ② 数量 ≤ 0 → 不进分支即结束
  - op: branch
    args: {entries: []}
    condition: [{units: [{op: 3, leftKey: eyjafjalla_t1_bubble_count, rightValue: 0}]}]
    steps:
    # ③ 攻击候选副本写入 bb
    - op: write_blackboard
      args: {entries: [{key: key, value: eyjafjalla_t1_candidates, fromBlackboard: 0, type: 3},
                       {key: source, value: event, fromBlackboard: 0, type: 3},
                       {key: path, value: targets, fromBlackboard: 0, type: 3}]}
    # ④ bb 泡泡列表前插到候选前面（去重）
    - op: prepend_entities
      args: {entries: [{key: blackboardKey, value: eyjafjalla_t1_candidates, fromBlackboard: 0, type: 3},
                       {key: sourceKey, value: eyjafjalla_t1_bubbles, fromBlackboard: 0, type: 3}]}
    # ⑤ 覆盖提交
    - op: attack_candidate_override
      args: {entries: [{key: blackboardKey, value: eyjafjalla_t1_candidates, fromBlackboard: 0, type: 3}]}
    # ⑥ 一次性特殊攻击动画（Talent 三段，注册待用覆盖，等攻击系统自然消费）
    - op: apply_animation_override
      args: {entries: [{key: mode, value: once, fromBlackboard: 0, type: 3},
                       {key: slots, value: 'AttackBegin,AttackRemote,AttackClose,AttackEnd', fromBlackboard: 0, type: 3},
                       {key: resources, value: 'Talent_Begin,Talent_Attack,Talent_Attack,Talent_End', fromBlackboard: 0, type: 3}]}
      condition: []
      steps: []
      elseSteps: []
    elseSteps: []
```

资源映射（[eyjafjalla.asset](../../../Assets/Resources/Prefabs/Characters/6/Eyjafjalla/spine/eyjafjallaSpine/ReferenceAssets/eyjafjalla.asset) 已备好）：
命名动画 `Talent_Begin`/`Talent_End` + 命名组 `Talent_Attack`；`TalentAttack` spine 动画带 `OnAttack` 事件。
AttackBegin/AttackEnd 槽位被覆盖后，攻击结构从单段变为 Begin→主段→End 三段（状态机回环已核实可行）。

### 已知并接受的语义

- 前插的是**全局**泡泡列表（非视界内筛选）。事件派发后 AttackBase 无射程复检，
  重部署后视界外的旧泡泡也会被攻击（与被删的 InjectAttackTargets 同款语义，用户知情选择）。
- description 中原「注（待运行时扩展）」段落改为已实现说明（once 语义见 §5 修复记录）。

## 4. 文档与测试同步

- 新增 `docs/skill-components/PrependEntities.md`（参数表 + 与 write副本/filter/override 流水线的搭配说明）。
- `docs/abilities-inventory.md`：组件表加一行；eyjafjalla_t1 描述更新。
- `docs/ability-steps.md`：op 表加 `prepend_entities` 行（如有表）。
- `Assets/Tests/Editor/AbilitySystem/PrependEntitiesTests.cs`（EditMode，参照 AttackCandidateOverrideTests）：
  基本前插保序、去重（源内重复 + 源与目标重复）、null 跳过、目标/源键缺失 warn+跳过、空源无操作。

## 5. 缺陷修复记录（2026-08-28 同日治本）

原缺陷：`ApplyAnimationOverride` once 模式在 OnBeforeTargetSelect 时机死锁——once 立即
`TrySetAttackState(forceChange:true, attackAction:null)` 抢先进入攻击态 → 同一 FixedUpdate 内
紧随的 `TryToAttack(forceChange:false)` 因 `state > currentState` 不成立且非 ComboWindow 而失败 →
`_attackTimer` 不重置 → 下帧再索敌再触发，循环强切、永不造成伤害。

治本方案（用户设计）：**once 与状态切换彻底解耦，改为机器级"待用一次性覆盖"**。

- `AnimationMachine.AddOneShotOverride(owner, animations, priority)`：往 `_overrides` 注册一条
  待用条目，与持久覆盖同规则参与 `(priority, id)` 排序叠层；**不切状态**。
- 消费语义（PlayMode 修正后定稿）：**整条消费**——任何一个被覆盖槽位
  （`AnimationOverride.GetCoveredSlots()` = 非空资源槽 + Cleared 槽）被机器实际播放时，
  条目整体移除。首版实现过"每槽位各播一次才移除"，被 T1 实测否决：资产同时覆盖
  AttackRemote+AttackClose，远程干员永远不播 AttackClose → 条目永不消费 → 动画永不恢复。
  消费点挂在播放处（`SetState` switch / `PlayAttackAnimation` / `FinishComboWindow` /
  `HandleAnimationStateStart` 补播 Idle），不在解析处——解析处会把"切状态顺带解析"误判为使用。
- 连贯性：`_activeAnimations` 是切换时刻快照，攻击循环（Begin→主段→End）中途不重解析，
  三段成套动画天然整循环生效；条目移除只影响下一次解析，不打断当前播放。
- 打断残留：未消费的条目留待下一次播放消费；提前撤销走 handle（`RemoveOverride`
  对待用条目同样有效），不做 TTL。
- 组件侧：once 模式重定义为注册待用覆盖，`state`/`forceChange`/`moveBranch`/`attackBranch`
  四个控制流参数删除；once 与 override 一样写 `outputKey` 记录（handle 可撤销）。
- `AttackBase.PendingAnimationOverride`（同调用栈传参通路）保持不动，二者并存不冲突。

## 6. 验证

- EditMode：PrependEntitiesTests / AnimationOverrideCoveredSlotsTests / ApplyAnimationOverrideTests
  全绿（后两者为本缺陷修复交付）。
- PlayMode（用户执行）：泡泡存在时优先打泡泡 + Talent 三段动画；无泡泡时正常索敌/普通动画；
  重部署重统计回归（规则 1-4 不受影响）。
