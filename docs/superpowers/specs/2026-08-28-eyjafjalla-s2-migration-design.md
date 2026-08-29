# eyjafjalla Skill2（"火山"）迁移资产 · 设计文档

日期：2026-08-28
状态：已实施（2026-08-29）。五点决策（落点算法/专属动画/恢复落点泡泡/不暂停索敌/数值原值）
+ 子弹替换（`_extraEffectDatas[0]` 迁原版弹道配置）均已落地；EditMode 测试
SelectLandingPoints/BulletLandedBridge/FireBullets 就绪，PlayMode 验证待用户执行。

## 1. 背景：现状脚本剖析

`scripts/Skill2.cs`（prefab 组件，待删）现存逻辑与问题：

- `_skill2Attack` 在 prefab 上是**空串**——覆盖动画实际无效；`ReferenceAssets/Skill2.asset`
  里有命名动画 `Skill2`（单段，无 Begin/End），从未接线。
- `_targetPos` 硬编码 `(1,1)(1,2)(2,2)(2,1)` 四个调试坐标。
- `OnBulletDestroy` 在 T1 迁移时被掏空（旧版调 `_talent1.Skill2SetBubble(pos)` 落点生成泡泡）。
- 施法流程：`SkillBegin` → `TryToAttackWithAnimation(空目标, forceChange, once:Skill2动画)`
  → 订阅 `OnAttackSuccessfully` → 伤害帧发 4 发**纯视觉弹**（damage 0、无实体目标、
  BulletData.AllowNoTarget=1、抛物线 BulletType=1）→ 落点回调生成泡泡（现缺失）。
- SP 原值：totalSp 6 / initialSp 5 / chargeNum 3 / skillAmount 1、Natural 恢复、
  OnAttackSuccessfully 消耗、Manual 开、不禁恢复、不可手动关闭。

git 887cc0f 的原版交叉调用：`Skill2Open()`=解除 T1 优先索敌订阅；`Skill2SetBubble(pos)`
=生成泡泡（含攻击叠层记账）；`Skill2End()`=重进优先索敌。

## 2. 决策记录（用户 2026-08-28）

1. **落点选取**：~~4 个位置，优先级 = 爆炸范围(半径1.5)覆盖敌人数最多 → 泡泡间距最大；
   写专门组件。**落点限制在攻击范围内**（补充确认）~~
   **2026-08-29 改版（用户定稿，取代覆盖最优搜索）**：范围内敌群（排除泡泡花名册）
   随机取 4 个原位；不足补射程格（地面层先于高台层、层内随机不重复），格点加
   ±0.24 双轴随机偏移；去重名额用尽后从敌人重新选起（交替、可重复）。
   落点不再以"必须是射程格"为设计约束——敌人原位与偏移漂移天然可能出格。
2. **专属动画**：接线 `Skill2` 命名动画（AttackRemote+AttackClose 双槽，原版同款）。
3. **恢复落点生成泡泡**：子弹实际落点（含抛物线偏移）生成，走 t1 同款记账。
4. **不暂停优先索敌**：分析结论（见 §3.4）——S2 发射与索敌无关；动画期间主循环每 tick
   触发 t1 R5 注册的一次性覆盖会在下次攻击被整条消费，视觉正确无死锁；不写任何干预标志。
5. **SP 数值原值保留**。

## 3. 系统层新增件

### 3.1 SelectLandingPoints（新组件，`select_landing_points`）

**文件:** `AbilitySystem/Components/SelectLandingPoints.cs`

随机流水选 N 个落点（2026-08-29 用户定稿改版；首版"覆盖最优穷举搜索"已整体退役，
见 §2 决策记录）。

| Key | Type | Default | Description |
|---|---|---|---|
| `count` | Int | `4` | 泡泡数量。 |
| `offset` | Float | `0.24` | 格点源的双轴随机偏移（敌人源不加偏移取原位）。 |
| `excludeKey` | String | `""` | 从敌池剔除的黑板 `List<Entity>`（泡泡花名册——泡泡是敌对阵营，会被当成"敌人"）。 |
| `outputKey` | String | `""` | 写入 `List<Vector2>`（世界坐标）。 |

算法：

1. **敌人优先**：`Vision.NearbyMonsters`（索敌同口径的每帧缓存列表），剔除
   `excludeKey` 名单后随机取 ≤count 个**原位**（不加偏移）。
2. **不足补格点**：`Vision.Range` 格子，**地面层先于高台层**（`Tile.highland`），
   层内随机不重复；每个入选格 = 格心 + ±offset 双轴随机偏移。
3. **用尽循环**：去重名额耗尽后从敌人重新选起，敌人↔格点交替、允许重复
   （空池自动跳过——保证只要任一池非空必有结果）。
4. `Range == null`（半径视野）/ 地图未加载 / 无格点且无敌人：配线错误暴露
   （LogError+跳过）。

随机源注入 `Func<float>`（[0,1]）：运行时 `RandomHelper.RandomF()`，EditMode
测试用常量序列驱动。语义边界：敌人位置取触发时快照，飞行期间移动不追踪。

### 3.2 AttackBase._extraEffectDatas（子弹配置实体侧登记处）

ParamList 纯 string，装不下 `AttackEffectData`（含 BulletData GameObject 引用 + 出生骨骼）。
仿 ChargeAttack 自带 `_chargeEffectData` 先例，在 `AttackBase` 加：

```csharp
[SerializeField] private List<AttackEffectData> _extraEffectDatas = new();
public AttackEffectData GetExtraEffectData(int index);
```

S2 的 AttackEffectData YAML 块从 Skill2 组件**迁入** Eyjafjalla prefab NormalAttack 的
该列表（index 0）。资产步骤按 `effectDataIndex` 引用——与 SpawnEntity 的
`spawnIndex`→`CanSpawnEntityIds` 登记处模式同构。

### 3.3 FireBullets（新组件，`fire_bullets`）

**文件:** `AbilitySystem/Components/FireBullets.cs`

向黑板点列表发纯视觉载弹，落点桥接回宿主 runner。

| Key | Type | Default | Description |
|---|---|---|---|
| `pointsKey` | String | `""` | 黑板 `List<Vector2>` 目标点。 |
| `effectDataIndex` | Int | `0` | `AttackBase.GetExtraEffectData` 索引。 |

行为：每点 `new Bullet(null, null, onLand, data.BulletData, ctx.entity, null, point,
data.BulletSpawnTransform.position, 0, 1, 0,0,0,0, 0, 0)`（视觉载弹参数固化——
damage 0 / multiplyer 1 / 穿透与类型 0，本组件语义即"无伤害载弹"）。`onLand` =
`ctx.entity.AbilityRunner.DispatchEvent(new BulletLandedEvent{ position })`
（WatchSummonDeath 同款宿主桥接）。

### 3.4 不暂停索敌的影响分析（结论：不做）

- 强制攻击不重置 `_attackTimer`（AttackBase.FixedUpdate L128）→ S2 动画期间主循环
  每 tick 索敌并触发 t1 R5（trigger 18）→ 有泡泡时每 tick 注册一条机器级一次性覆盖。
- 这些条目在**下次攻击**首次播放覆盖槽位时被整条消费；该攻击因 T1 优先索敌本来就该
  打泡泡播 Talent 动画 → 视觉正确、无死锁，代价只是注册开销。
- S2 专属动画不受堆积条目影响：force_attack 在 R1 执行帧内同步 `SetState`，
  快照先落，中途不重解析。
- 已知共性怪癖（原版同样存在，非 T1 特有）：动画期间索敌成功会 `SetDirection` 拉朝向。

### 3.5 TriggerEvent.OnBulletLanded（=19）+ BulletLandedEvent

`TriggerEvent` 枚举**尾部追加**（保持既有资产枚举序号稳定，OnTick/OnSummonDeath/
OnBeforeTargetSelect 同款先例）。`AbilityEvents.cs` 加
`BulletLandedEvent : AbilityEvent { public Vector2 position; }`。

### 3.6 SpawnEntity positionMode: event

新 `positionMode` 取值 `event`：读当前事件携带位置（`BulletLandedEvent.position`；
实现为事件上的显式类型解析，配线错事件 warn+跳过）。既有 self/eventTarget/fixed 不动。
原版泡泡生成在**实际偏移后落点**（抛物线 DevitationXRate 0.2），非瞄准格——必须读事件。

### 3.7 ForceAttack（新组件，`force_attack`）

空目标强制攻击：`AttackBase.TryToAttackWithAnimation(Array.Empty<Entity>(), true, false, null)`。
动画由 apply_animation_override(once) 的机器级待用覆盖提供（注册在前、
同帧 SetState 解析消费）——Skill2.cs 退役后 legacy once 通道调用者 6→5。

## 4. eyjafjalla_s2.asset（skills/，"火山"）+ eyjafjalla_t1.asset 追加落点规则

SP 原值照搬（§1）。

**eyjafjalla_s2.asset 两规则：**

- **R1 OnAbilityBegin(2)，reentry 0**：
  1. `select_landing_points`（count 4 / offset 0.24 / excludeKey `eyjafjalla_t1_bubbles` /
     outputKey `eyjafjalla_s2_points`）
  2. `apply_animation_override`（mode once / slots `AttackRemote,AttackClose` /
     resources `Skill2,Skill2`）
  3. `force_attack`
- **R2 OnAbilityEnd(3)，reentry 0**：`fire_bullets`（pointsKey / effectDataIndex 0）。
  发弹时机=技能结束（SP 消耗→End 与 OnAttackSuccessfully 同调用栈同帧），每施法恰好一次，
  无需守卫 flag（trigger 10 每次普攻都响）。实体死亡中断施法则 End 不派发→不发弹，
  与原版"未完成攻击不发弹"等价。

**eyjafjalla_t1.asset 追加 R6 OnBulletLanded(19)，reentry 2（Parallel）**（4 弹落点相近需并发）：

1. `write_blackboard` atk_pct +0.2（add）
2. `update_buff`（Attack AddPercent ← bb atk_pct）
3. `spawn_entity`（spawnIndex 0 / positionMode **event** / camp 2 / appendToListKey
   `eyjafjalla_t1_bubbles` / passStat attack / passStatKey lavabubble_atk_snapshot）

即 t1 R2 的记账三步（顺序保持 记账→buff→spawn，第 N 个泡泡吃到 N 层快照）。

**落点规则归 t1 而非 s2（2026-08-29 PlayMode 修正）**：首版把 trigger-19 规则写在
s2 上，实测弹到无泡泡——S2 是瞬发技能（abilityAmount 1 + OnAttackSuccessfully 消耗），
`force_attack` 当帧走完 开始→强制攻击→攻击成功→End，子弹落地时 s2 已非激活，
`DispatchToAbility` 的激活门（EntityAbilityRunner，bypass 名单仅生命周期事件）把
`BulletLandedEvent` 挡下。归 t1（常驻激活的被动 Talent，与原版 `Skill2SetBubble`
住在 Talent1 同构）后落点事件常可达。**通用结论：挂点事件的消费规则必须住在
常驻激活的 runtime（天赋/持续技能）上，瞬发技能只能当发射器。**

跨能力协作全部走既有黑板键（`eyjafjalla_t1_bubbles`/`atk_pct`/buff 对），无新协议。

## 5. 接线与清理

1. `skills/eyjafjalla_s2.asset` 创建；EntityData.Skills 接线（c/2，S1=c/1 先例）。
2. prefab YAML 手术：Skill2 组件块删除；`_skill2AttackEffectData` 块迁入 NormalAttack
   `_extraEffectDatas[0]`（BulletData/骨23 出生点/特效引用原样搬）。
3. `scripts/Skill2.cs` + .meta 删除（干净迁移，无 legacy 桥）。
4. SPConfig/UI 链无需改动（S1 已适配资产技能的手动施放按钮）。

## 6. 测试与文档

- EditMode：
  - `SelectLandingPointsTests`：注入随机源驱动纯函数——敌人优先原位无偏移、
    地面层先于高台层、±offset 符号映射、名额用尽交替回退、排除名单 glue、
    配线错误（Range null / count≤0 / 双空池）。
  - `FireBulletsTests`：黑板点列表 → Bullet 创建（视觉参数/damage 0）；落点回调派发
    BulletLandedEvent（headless runner 验证 trigger 19 路由）。
  - `SpawnEntity` positionMode=event：事件携带位置透传（错事件 warn）。
- 文档：`docs/skill-components/` 新增三组件页；`abilities-inventory.md`、
  `ability-steps.md` op 表、`SpawnEntity.md` positionMode 行同步。
- PlayMode（用户执行）：施法→专属动画→4 弹→落点泡泡（记账/爆炸/优先索敌回归）。

## 7. 明确不做

- 不暂停/不干预 t1 优先索敌（§3.4）。
- legacy once 通道其余 5 调用者（NormalAttack/ChargeAttack/WitherAttack/
  SharedTargetExtraAttack/BeefSkill）不在此收编——后续独立任务。
- 爆炸 VFX、S1 火焰特效缺口维持既有记录。
