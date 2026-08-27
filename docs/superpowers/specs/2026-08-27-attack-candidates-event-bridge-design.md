# 攻击候选事件桥接与 EntityFilter 职责拆分 · 设计文档

日期：2026-08-27
状态：已与用户对齐（方案 A + attackCandidates 黑板保留键）

## 1. 背景与问题

`EntityFilter` 当前承担两个职责：

1. **纯列表筛选**（`mode=list`）：对黑板 `List<Entity>` 原地过滤（eyjafjalla_t1 重部署重统计）。
2. **攻击目标修改**（`mode=attackCandidates`）：自行订阅目标实体 `AttackBase.OnBeforeTargetSelect`，在索敌候选列表上过滤（攻击偏好）。

`InjectAttackTargets`（把黑板列表插到候选最前，为"优先攻击泡泡"预留）复制了同一套订阅管理。

根因：`AttackBase` 的攻击/伤害事件已全部桥接成 runner 事件（`OnBeforeAttack`/`OnAfterTakeDamage` 等，见 `EntityAbilityRunner` 的事件桥接区），**唯独 `OnBeforeTargetSelect` 未桥接**——组件被迫各自维护 `_subscribedAttacks` 订阅列表、toggle/AbilityEnd 退订、Dormancy 后重挂等生命周期代码。

## 2. 目标

职责三分，彼此正交：

- **时机由事件提供**：桥接 `OnBeforeTargetSelect` 为一等 runner 事件；
- **数据由黑板提供**：候选列表在派发窗口内挂到保留键 `attackCandidates`；
- **组件纯操作**：`EntityFilter` 只做"过滤黑板某键处的列表"，`InjectAttackTargets` 只做"把源列表插到候选最前"，两者对攻击系统零感知。

## 3. 方案选择

- **A（选定）事件桥接 + 保留键**：订阅管理职责整个消失，拼接单元是系统原生的规则+步骤。
- B 新组件 AttackCandidateOverride + 嵌套步骤拼接：step 运行时需新增"回调时执行子步骤"机制，订阅代码保留，黑板往返——引入新机制，不选。
- C 最小拆分（改名搬运）：无拼接、谓词引擎两份，不选。

用户方向的关键修正（优于最初的 mode=event 设计）：**"attackCandidates" 不是组件的特殊模式，而是一个普通黑板键**。组件的列表输入参数只有一个 `blackboardKey`，`attackCandidates` 只是由资产提供的键值。任何未来吃列表的组件（排序/计数/注入……）都能无差别使用这两种来源。

## 4. 详细设计

### 4.1 TriggerEvent 枚举

`ComponentConfig.TriggerEvent` **末尾追加** `OnBeforeTargetSelect`（= 18）。遵守既有约定：追加在末尾以保持既有资产枚举序号稳定。

### 4.2 事件类

`AbilityEvents.cs` 追加：

```csharp
// 索敌候选确定后、数量裁剪前派发（AttackBase.AttackTargetSelect 内）。
// targets 为候选列表本体（引用，订阅方可直接增删）；三个标量派发后回写。
public class BeforeTargetSelectEvent : AbilityEvent
{
    public override TriggerEvent TriggerEvent => AbilitySystem.TriggerEvent.OnBeforeTargetSelect;
    public List<Entity> targets;
    public int selectMaxNum;
    public int selectMinNum;
    public bool sameComp;
}
```

### 4.3 桥接（EntityAbilityRunner）

在现有 AttackBase 事件接线处加一条订阅，桥接方法照抄 `OnBeforeAttack` 的"派发+回写"模式：

```
OnBeforeTargetSelect(targets, ref max, ref min, ref sameComp) 触发时：
  sharedBlackboard.Set("attackCandidates", targets)   // 同一 List 引用
  DispatchEvent(evt)                                   // evt 携带 targets + 三标量
  max/min/sameComp ← evt 回写
  sharedBlackboard.Remove("attackCandidates")          // 派发窗口结束即摘除
```

要点：

- **键只在派发的同步窗口内存在**。窗口外读取 → 键缺失 → 组件走现有"blackboard key holds no entity list"告警路径，配线错误自然暴露，无需防御检查。
- `sharedBlackboard` 是 runner 私有实例（每实体一块），无跨实体串写；池回收时 `Clear()`，残留键活不过回收。
- 重入安全：规则处理中再触发 `ForceResetAttack`（嵌套索敌）时，嵌套桥接的 Set→Remove 在外层派发内完成，外层 Remove 幂等（`Dictionary.Remove` 对缺失键无副作用）。
- 管线位置不变：仍发生在 `PriorityOrder` 排序之后、`AttackNum/AttackMinNum` 数量裁剪之前，语义与今天的直接订阅一致。
- 桥接订阅与其它 AttackBase 桥接同生命周期（含 `Dormancy()` 清空后的重挂路径）。

### 4.4 EntityFilter 瘦身

删除：`_mode`（含两种模式分支）、`_toSelf`、`_subscribedAttacks`、`AbilityEndEvent` 分支、`FilterTargets` 订阅壳、`UnsubscribeAll`、`ResolveTargets`。

保留：`blackboardKey` + 谓词四件套（fields/ops/values/groups、OR 组 AND 条件、IdC/IdN 等字段表、数值解析与告警）——即纯粹的"过滤黑板某键处的实体列表"。

### 4.5 InjectAttackTargets 瘦身

删除：`_subscribedAttacks`、`_toggle`、`_toSelf`、`AbilityEndEvent` 分支、`UnsubscribeAll`、`ResolveSubscribeTargets`。

`OnTrigger` 新形态：读 `attackCandidates` 键的列表（缺失 = 触发时机配错，OneShotWarn + 跳过）→ 读源 `blackboardKey` 列表 → 去重后插到最前。插入逻辑本体不变。

目标键 `attackCandidates` 为**硬编码保留键**（桥接与组件两侧共用一个常量，如 `BlackboardKeys.AttackCandidates`），不做参数——本组件的用途就是攻击候选注入，泛化为通用列表拼接的需求出现时再扩展。

### 4.6 拼接形态（原 attackCandidates 功能）

```
- triggers: [{triggerEvent: 18}]          # OnBeforeTargetSelect
  steps:
  - op: filter_targets
    args: {blackboardKey: attackCandidates, fields: ..., ops: ..., values: ..., groups: ...}
```

技能开关语义由派发侧 `isActive` 门控免费获得：`SetActive(true/false)` 与 SP 开关同步（`SPEngine.OnBegin/OnEnd` → `AbilityRuntime.SetActive` → `OnAbilityBegin/OnAbilityEnd`），技能未激活时规则收不到任何非生命周期事件——与旧"Begin 订阅 + End 退订"严格等价。

### 4.7 舍弃的能力

`toSelf=false`（从自己的 ability 订阅并修改**其它实体**的索敌）：现有资产零使用，语义怪异，事件桥接天然只作用于自身（索敌发生在谁身上就派发给谁的 runner）。将来真有需求再扩展。

## 5. 资产迁移

| 资产 | 改动 |
|---|---|
| ebnhlz_s3 | 规则2：triggers `[2,3]→[18]`（双触发退化为单触发）；args 增 `blackboardKey: attackCandidates`；谓词四件套不动 |
| eyjafjalla_t1 | 仅删 filter_targets args 里失效的 `mode: list` 条目（mode 参数没了，死配置一并清掉） |
| test_talent | **整个删除**（含 .meta）。已验证为孤儿资产：GUID `47b0e6b2e1342f84491dbbc675c7d51f` 全库无引用（Kroos EntityData 的 Talents 实际引用的是 kroos_t1） |

## 6. 错误处理口径（全部复用现有路径）

| 场景 | 行为 |
|---|---|
| filter 的 blackboardKey 为空 / 键上无列表（含配错触发时机读到窗口外的 attackCandidates） | OneShotWarn + 跳过 |
| 未知字段/op、数值解析失败 | OneShotWarn + 该条件失败 |
| inject 的 attackCandidates 键缺失 | OneShotWarn + 跳过（新增告警，暴露配线错误） |
| inject 源列表空 | 无操作（现有语义） |

## 7. 文档同步

- `docs/skill-components/EntityFilter.md`：删 mode 参数；参数表说明 `attackCandidates` 保留键用法
- `docs/skill-components/InjectAttackTargets.md`：删 toggle/toSelf；改为事件触发用法
- `docs/abilities-inventory.md`：组件表两行更新；删 test_talent 行；ebnhlz_s3 描述更新
- `docs/ability-steps.md`：filter_targets 行核对（op 名不变，预计无需改）

## 8. 验证方案（PlayMode，沿用项目惯例）

1. **ebnhlz_s3**：开技能 → 仅打精英/领袖（MonsterStatus 1–2）；关技能 → 恢复正常索敌；蓄力 ×1.4 天赋联动不回归
2. **eyjafjalla_t1**：重部署重统计回归（select all → filter IdC/IdN → listCount×0.2 路径，仅删死配置）
3. **InjectAttackTargets 冒烟**：eyjafjalla 测试场景临时挂 trigger 18 → inject 规则验证泡泡优先，验证后移除
4. **常驻验证点**：无 SP 天赋的 runner 部署后常驻 active（ebnhlz_s3 之外若需天赋挂 filter 时的前提）
5. **Kroos 回归**：删除 test_talent 后 Kroos（实际用 kroos_t1）行为无变化

## 9. 性能注记

无有效目标时索敌每物理帧重试，桥接派发约 50Hz/实体（事件对象 + 条件上下文分配）。与现有 `BeforeAttackEvent` 等桥接同数量级成本，可接受；若 profiler 报警再优化（如事件对象复用）。
