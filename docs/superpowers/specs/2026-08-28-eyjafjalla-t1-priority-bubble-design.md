# eyjafjalla_t1 优先攻击泡泡 + 特殊攻击动画 · 设计文档

日期：2026-08-28
状态：已与用户对齐（新列表前插组件 + once 动画按原始思路配置，系统缺陷运行期再修）

## 1. 背景

eyjafjalla_t1 资产 description 中挂起的「缺口②」：存在泡泡时优先攻击泡泡并切换本次攻击动画。
决策点在索敌返回→TryToAttack 之间，即 `OnBeforeTargetSelect`（triggerEvent 18）。

现状两个空缺：

1. **无"前插"原语**：InjectAttackTargets（源列表去重后插到候选最前）在统一 override 重构时已删除；
   现有组件面（WriteBlackboard/EntityFilter/AttackCandidateOverride）只能表达整表覆盖。
2. **动画 once 模式在 trigger 18 时机有系统级缺陷**（见 §5，本次搁置）。

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
- 去重：源中已存在于目标的实体不重复插入（视界内泡泡本就在候选里，前插≈提权到最前）。
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
    # ⑥ 一次性特殊攻击动画（Talent 三段）
    - op: apply_animation_override
      args: {entries: [{key: mode, value: once, fromBlackboard: 0, type: 3},
                       {key: state, value: Attack, fromBlackboard: 0, type: 3},
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
- description 中原「注（待运行时扩展）」段落改为已实现说明，并标注 once 时机缺陷待修。

## 4. 文档与测试同步

- 新增 `docs/skill-components/PrependEntities.md`（参数表 + 与 write副本/filter/override 流水线的搭配说明）。
- `docs/abilities-inventory.md`：组件表加一行；eyjafjalla_t1 描述更新。
- `docs/ability-steps.md`：op 表加 `prepend_entities` 行（如有表）。
- `Assets/Tests/Editor/AbilitySystem/PrependEntitiesTests.cs`（EditMode，参照 AttackCandidateOverrideTests）：
  基本前插保序、去重（源内重复 + 源与目标重复）、null 跳过、目标/源键缺失 warn+跳过、空源无操作。

## 5. 已知缺陷（本次搁置，运行期修）

`ApplyAnimationOverride` once 模式在 OnBeforeTargetSelect 时机死锁：once 立即
`TrySetAttackState(forceChange:true, attackAction:null)` 抢先进入攻击态 → 同一 FixedUpdate 内
紧随的 `TryToAttack(forceChange:false)` 因 `state > currentState` 不成立且非 ComboWindow 而失败 →
`_attackTimer` 不重置 → 下帧再索敌再触发，循环强切、永不造成伤害。
修复方向（不在本次范围）：给资产通向 `AttackBase.PendingAnimationOverride`（TryToAttackWithAnimation
管线，SharedTargetExtraAttack 已在用）的路径，如 ApplyAnimationOverride 新增 nextAttack 模式。

## 6. 验证

- EditMode：PrependEntitiesTests 全绿（本次交付）。
- PlayMode（用户执行）：泡泡存在时优先打泡泡 + Talent 三段动画；无泡泡时正常索敌/普通动画；
  重部署重统计回归（规则 1-4 不受影响）。
