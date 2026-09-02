# SetEntityState 组件设计

日期：2026-09-02
状态：已与用户确认

## 背景与缺口

AbilitySystem（配置层 GameData / 运行时层 Entity-LevelPublicScripts / 组件层 Components）已有 30+ 数据驱动组件，但**没有任何组件能驱动 EntityStateMachine 切换状态**。旧硬编码 Skill（钻地/变身/终局存活等走 Cast 通道的演出）随提交 29f0afb 全删后，`TrySetState` 的调用方只剩行为层内部（MoveBase / AttackBase / BuffController / Entity）。本组件把"技能切实体状态"数据化。

关键对接点：`Entity.StateMachine.TrySetState(EntityState state, bool forceChange)` → bool。
状态优先级=声明序（Default<Idle<Move<Attack<Start<Cast<Die）；非强制=目标优先级更高才切（Attack 连击窗口特例）；强制=当前非 Die；ban 表两分支都生效；Cast 为粘性演出态（播完保持末帧，转出需显式 force）。

## 决策记录（用户确认）

| 决策点 | 结论 | 理由 |
|---|---|---|
| 可切状态范围 | 全部 7 态开放 | 信任配置者；Attack/Die 的专属通道语义（攻击帧回调 / Entity.Die() 完整死亡链）由配置侧负责 |
| 切换失败处理 | 纯静默 | 失败是合法竞争结果（优先级不够/ban 命中/已 Die），不是错误数据；YAGNI 不写黑板结果 |
| 组件范围 | 只做切换 | ban 表操作（AddStateToBan/RemoveStateFromBan）暂不组件化，现有 ban 需求已由 BuffController 覆盖 |
| 方案形态 | 单组件 | 场景化组件族会随 7 态×强制组合繁殖；并入 ApplyAnimationOverride 会混淆逻辑/表现层 |
| 目标解析顺序 | blackboardKey 优先 | ApplyAbnormalState 同款：配 key=明确要外部目标列表 |
| teardown 回滚 | 无 | 状态切换是单向事实；需要回滚时将来再加 Restore 语义或由另一条规则显式切回 |

## 组件定义

新文件 `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/SetEntityState.cs`：

```csharp
[RegisterComponent("SetEntityState")]
public class SetEntityState : AbilityComponentBase
```

标准契约：`OnInit` 存 lazy 参数字段，`OnTrigger` 执行；不覆写 `OnTick`/`OnTeardown`（一次性动作、无留存状态，符合 OnInit 自复位契约）。

## 参数表

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `state` | string | 必填 | 目标状态名（"Idle"/"Move"/"Cast"…），`Enum.TryParse<EntityState>` 解析 |
| `force` | bool | `false` | 强制切换（当前非 Die 即切）；false=走优先级竞争 |
| `toSelf` | bool | `true` | 无 blackboardKey 时切自己（`ctx.entity`） |
| `blackboardKey` | string | `""` | 黑板 `List<Entity>` 目标列表；配了则优先于 toSelf |

`force` 默认 false：非强制是状态机核心竞争机制；必须强切的场景（如 Cast 粘性态转出）由配置显式写 `force=true`，让"强制"在配置里可见。

## OnTrigger 逻辑

```
ResolveTargets(ctx)
    // blackboardKey 非空 → 读黑板 List<Entity>（键存在但值 null/空 → 静默返回）
    // 否则 toSelf → ctx.entity；两者皆无 → 静默返回
Enum.TryParse<EntityState>(_state())
    失败 → OneShotWarn.WarnOnce + return   // 唯一警告点：真正的配置错误
foreach target in targets:
    target?.StateMachine.TrySetState(state, _force())
```

- 失败静默：多目标逐个独立切换，单个失败不影响其余。
- 与 ApplyAnimationOverride 正交：本组件切逻辑状态（表现层经 StateChanged 被动播对应槽位），后者换"某状态播哪套资源"。

## 测试

`Assets/Tests/Editor/AbilitySystem/SetEntityStateTests.cs`，覆盖：

1. 参数解析：合法状态名 / 非法状态名（OneShotWarn + 不切换）
2. 目标解析三通道：toSelf / blackboardKey / 两者皆无（静默不切）
3. blackboardKey 优先于 toSelf（都配时切黑板目标不切自己）
4. 切换语义：force=true 成功切低优先级态；非强制被优先级挡住（失败静默，状态不变）
5. 多目标独立切换

状态机本身的行为（优先级/ban/连击）已由 `EntityStateMachineTests` 覆盖，不重复。
