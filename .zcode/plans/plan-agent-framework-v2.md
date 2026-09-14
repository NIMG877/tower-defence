# ability-server Agent 框架 v2——实施计划

> 2026-09-13。重设计对象仅限**服务端 Agent 编排**（工程阶段二，已提交 aba2923）：
> analyze → describe → generate 接力棒式三段 → 单线程自由循环 + 随手校验 + 唯一强制关卡。
> 工程阶段一（客户端纯数据管线：Dto / 校验器 / 快照构建）与阶段三（GenerateSkill
> 运行时组件）不在重设计范围——前者随契约 v2 做形状调整（M1/M2），后者协议零改动
> （0.4s 轮询 / 单请求 5s 超时 / 总限 240s / ReplaceSkill 仅本场生效；phase 只打印
> 不解析、完成判定只看 `status=="ok"`、响应加字段不炸，均经代码核实）。
> 文中 v1/v2 均指服务端编排的新旧版本，不指工程阶段。
> 依据：`app/` 8 模块实读（agent/service/validator/llm/tools/crossitems/prompt/config）、
> `Assets/Resources/Data/AbilityOps/ability-ops.json`（35 组件 + 4 原语）、
> `ability-server/data/skills.json`（48 技能语料）、`docs/ability-steps.md`（287 行契约）、
> `docs/skill-components/`（37 篇，重点 WriteBlackboard/GenerateSkill），以及客户端
> 契约/组件/数据代码（AgentGenerateRequest / BattleSnapshotBuilder / GenerateSkill /
> EntityData / BulletData / AnimationResources / SpawnEntity / AbilityConfigValidator）。

## 一、数据结论（设计的依据）

1. **语料分布**：48 技能、op 使用集中于 buff/黑板/选目标五件套（apply_buff 32、
   write_blackboard 21、destroy_buff 18、select_targets 12、apply_damage 10）；21 个触发
   事件语料只用了 16 个；reentry 实际只有 IgnoreWhileRunning(83)/Parallel(3)，Restart
   零使用 → 风格统计有效，语义验证的词表覆盖按全词表做、按高频做重点。
2. **快照黑板键没有生产者**：`snapshot:*` 六键（SnapshotBlackboardKeys.cs）注释声称
   "由 BattleSnapshotBuilder 写入宿主黑板"，但全仓无任何写入代码——
   BattleSnapshotBuilder 只有序列化。knownBlackboardKeys 是兑现不了的承诺：validator
   视为合法读键不告警，运行时读到空、技能静默失效。处置见 P0-3。
3. **表达鸿沟的真实位置**：实时战局条件的正路是 select_targets（写实体列表）→
   write_blackboard source=listCount（桥接计数）→ 条件读键，这条链语料已在用。
   真正的缺口是两块运行时词表没有机器可读编码，硬编码在 WriteBlackboard.cs 的
   switch 里，LLM 无法安全引用、validator 无法核查：
   (a) `source=event` 的 path（damage 9 path / hurt 10 path / before-target-select
   4 path，另 isdeadly 散在 AfterAttack/AfterTakeDamage、cumbo 在 BeforeAttack，
   WriteBlackboard.cs:104-166）；
   (b) `source=entity` 的 path（ResolveEntityValue，WriteBlackboard.cs:185-204，
   与 SpawnEntity.passStat 共用：self/camp/currenthp/currenthprate/maxhp/attack/
   blockoccupation/monsterstatus）。
4. **hostAssets 边界是提示词软约束**：validator.py 全文不引用 hostAssets——
   spawn_index 越界、bullet_data_index 越界、动画名不存在，服务端一概不查，只有
   prompt.py GENERATE_SYSTEM_HEADER 的一句劝阻。这是校验层的实缺口。
5. **缓存键缺陷**：service.py:39-42 缓存键 = `opList + battleSnapshot`，不含
   hostAssets 与 constraints.request——同快照换宿主/换诉求会命中旧技能，入口正确性
   bug。且失败/降级响应同样入缓存，同 payload 会永久命中坏结果，失去重试机会。
6. **v1 双注入**：analyze 与 generate 各注入一遍全量快照
   （prompt.py:56-65 与 100-111）——单线程循环天然消除。
7. **文档资产**：37 篇组件文档 + 契约文档齐全且 schema 挂接（`doc` 字段），
   渐进披露的工具链路现成，v2 直接复用。

## 二、目标与非目标

**目标**：重写 ability-server 的 Agent 编排层为单线程工具循环；补齐语义验证层与
hostAssets 服务端校验；建黄金任务评测集使框架切换可度量；模型按角色分层路由；
预算/降级协议写进响应契约；修复缓存与快照键两个入口级缺陷。

**非目标（不重开）**：不动 AbilitySystem 运行时与组件正交性；不引入成熟 Agent 框架
（结论见讨论记录：领域缺口框架给不了）；不生成美术资产；不做战中重生成与持久化；
embedding 语义检索（48 条语料关键词足够）。

**客户端改动口径**：GenerateSkill 组件协议零改动；契约 v2 的客户端改动收敛在四点，
全部归入 M1/M2：
- `BattleSnapshotBuilder`：self 拍平进 entities、实体记录加 massLevel、删 skill 与顶层
  self 字段；
- `AgentGenerateRequest.Build`：hostAssets 扩充（见 §三·A）+ protocolVersion 递增；
- `AnimationResources`：`_animations/_animationGroups` 是 private 字段
  （AnimationResources.cs:74-75），类上只有按名查询——需先加公开枚举接口
  （AnimationNames / AnimationGroupNames），是 hostAssets.animations 的前置；
- `AbilityConfigValidator.Validate`：单参签名扩为 `(dto, hostAssets)`（M2，终检同款边界）。

## 三、P0 修复项（独立于框架重构，先行合入）

1. **缓存键补全 + 坏结果不缓存**：`service.py` 缓存键加入 hostAssets 与 constraints
   的规范化序列化（`sort_keys` + default=str，与 snapshot 同法）；status != "ok" 或
   response.degraded 的结果**不入缓存**（否则预算耗尽的降级产物被同 payload 永久
   命中，失去重试机会）。
2. **服务端 hostAssets 边界校验**：`agent.run` 把 `hostAssets` 传入
   `validator.validate`（签名扩参，`_run_mock` 调用点同步），新增检查（记 error，拒绝级）：
   - `spawn_entity.spawn_index` ∉ [0, len(canSpawnEntities))——与客户端 SpawnEntity
     ResolveSpawnId 越界 LogError 跳过的行为对齐（SpawnEntity.cs:143-149）；
   - `fire_bullets.bullet_data_index` ∉ [0, len(bullets))；
   - `apply_animation_override/remove_animation_override` 的 resources 动画名 ∉
     `animations.named ∪ animations.groups`。
   **iconKey 由 sanitize 强制剥离**（DTO_KEYS 移除 iconKey，输出恒无此字段），不靠
   提示词劝阻；技能卡走 AbilityIconPool null 兜底，识别靠技能名/描述。客户端终检
   同款在 M2（中间窗口由 schema 单源兜底）。
3. **〔拍板〕移除快照黑板键**：六个 `snapshot:*` 键无生产者（§一.2），三处单源一并
   清理：ability-ops.json knownBlackboardKeys 置空、删 SnapshotBlackboardKeys.cs、
   改 BattleSnapshotBuilder 头注释（其"阶段三把指标写入宿主黑板"的说法与实现不符）。
   实时战局条件一律引导 select_targets + listCount 链（语料已有先例）；残留引用由
   validator"读键无生产者"警告自然兜住（词表清空后 snapshot:* 即无生产者读键）。
   read_schema_vocab 与提示词不再输出快照键。

## 三·A、请求契约 v2（2026-09-13 拍板）

构建点仍为 `AgentGenerateRequest.Build` 单点。〔拍板〕=用户明确决定，〔同意〕=用户
确认的建议项。

### battleSnapshot

- 〔拍板〕删除 `skill` 字段（宿主静态信息的运行时投影，只投影 Skills[0] 且生成时点
  SP 恒为初值，信息量趋零；宿主技能上下文由 hostAssets.skills 接替）。
- 〔拍板〕self 拍平为与其它实体同构的 `SnapshotEntity` 记录进入 `entities`，`selfId`
  保留作指针；`selfHpRate` 由记录内 `hpRate` 读出。
- 〔拍板〕实体记录 = id/name/camp/isStatic/hp/maxHp/hpRate/pos/attack/defence/magicRes/
  job/label（job/label 现已存在，BattleSnapshotBuilder.cs:36-37）。
- 〔同意〕实体记录追加 `massLevel`（EntityData.MassLevel）——apply_impulse/失衡类
  技能需要目标质量对比。
- 保留 mapI/mapJ/canSetHigher/canSetLower。
- **protocolVersion 1 → 2**，并在握手中加 protocolVersion 相等断言（不等即
  HandshakeError，复用现有拒绝路径）——`check_handshake` 现只比 opList，形状漂移
  只会静默错位（crossitems 全 error、生成质量崩掉），检测不到。

### hostAssets

- 〔拍板〕新增 `job`、`subJob`（EntityData.CharacterJob/CharacterSubJob）——技能原型
  的第一决定因素（先锋回费/医疗治疗/近卫站场…）。
- 〔拍板〕新增 `animations: { named: [...], groups: [...] }`（不传引用）——取值源 =
  AnimationResources 的命名动画/动画组名单（依赖 §二前置的公开枚举接口），即
  ApplyAnimationOverride.resources 参数的合法域；P0 边界校验的落点。
- 〔拍板〕`bullets: [ {index, bulletType, allowNoTarget} ]`（不传引用）——BulletData
  本体是视觉/弹道参数（speed/deviation），不含伤害值，投影三字段足以支撑
  fire_bullets 设计与索引校验；bulletType 附语义说明（"1=带偏差弹道"），裸数字对
  设计无参考价值；替代现 `bulletCount`。
- 〔同意〕`canSpawnEntityIds` 扩充为 `canSpawnEntities: [ {id, name, ...实体同构静态
  投影, count} ]`（count = CanSpawnEntityCounts，与 Ids 同序）——现 payload 连
  "召唤物是谁、能召唤几个"都不含，spawn_entity 设计无从落地。
- 〔同意〕新增 `cost`（modify_cost/回费量级）、`baseAttackTime` + `damageType`
  （攻击节奏/物法方向）、`blockOccupation`（阻挡类技能）、`targetPriority`
  （attack_candidate_override 协同）、`visionRadius` + `visionRange`
  （select_targets 范围参数依据）。
- 〔拍板〕`skills` / `talents` 可选传递，默认不传（宿主现有技能/天赋上下文）。
- 〔拍板〕删除 `iconKeys`：生成技能采用统一图标，服务端生成配置一律不产出
  iconKey（见 P0-2）。

### constraints

- 不变：`{ request }`（generate_skill 的自由文本诉求）。

### 连带改动（归入 M1 + P0）

- crossitems：删 `can_set_cells`（digest 模式下快照不进提示词，全量列表无消费方；
  按需查询走 deploy_cells_near）、删 `hp_rate(id)`（entity(id) 覆盖）；**entity(id)
  保留**（digest 模式下的明细查询入口）。
- self 取数改造共三处：`_self_pos`、`_self_hp_rate`、`_within` 系的圆心——统一改从
  `entities[selfId]` 取。
- 工具词表去重：保留 `enemies_within(r)/allies_within(r)`（self 圆心，digest 直入
  self 后最常用）；任意圆心版命名 `entities_at(x, y, r, camp)`，不与 self 圆心系
  同义混淆。
- camp 数字语义（1=友军 2=敌人，Entity.cs:185/279）入 read_schema_vocab 与文档。
- 新增服务端 `battle_digest.py`：从全量快照计算注入用战局摘要，纯函数可测；
  digest 词表随 schema 出。
- 服务端快照解析按新形状（旧形状不兼容，双端同仓同改，无兼容包袱；v1 基准时序
  见 §六）。
- 不传的（评估过并排除）：实体当前 buff 列表（运行时查询成本高、变化快）、敌人
  路径（数据不存在）、MonsterStatus 等出怪元数据（与技能设计无关）、SubJobTrait
  （归入 talents 语境）。

## 四、v2 Agent 架构

### 4.1 编排：单线程自由循环 + 强制 plan + 唯一关卡

```
run(request):
  握手（opList + protocolVersion）
  thread = [system(总角色+工作方式+运行时边界声明), user(digest + hostAssets摘要 + constraints)]
  loop (预算内):
    轮次 = chat_tools(thread, tool_defs, model=路由(state))
    执行工具 → 结果回填（体积自限，见 4.4）
    调用 submit_skill → 交付关卡（全量校验+边界+语义层），通过即结束
    裸文本输出配置 JSON（未调工具）→ 回喂引导调用 submit_skill，不解析交付
  预算耗尽 → 降级：取本线程内最近一次 validate_draft 全绿的 sanitized 草稿交付
  （标 degraded=true）；无则 rejected
```

- **submit_skill 只认工具调用**：唯一出口 = 模型调用 `submit_skill(config)` 工具触发
  关卡；模型裸输出 JSON 文本时回喂一条引导（"用 submit_skill 提交"），不做文本解析
  fallback——单一机制，行为确定。
- **强制 plan**：第一轮必须产出计划（读哪些组件文档、设计思路、黑板键方案），经
  `update_plan` 工具修订；循环每 5 轮自检一次当前行为是否仍在 plan 轨道上。
- **信息注入分档**：
  - 直入（常数体积）：**战局 digest（服务端代码预计算）**——self 完整记录、敌我
    计数、最近敌方 top-K（K≈6，按距离排序的敌方完整记录）、血量/防御/法抗分布
    统计、距离分带计数（3/5/8 格）、地图尺寸 + self 所在格 + 可部署格计数与周边格；
    hostAssets 小型决策字段
    （job/subJob/cost/baseAttackTime/damageType/blockOccupation/targetPriority/vision）；
    constraints。快照不全量直入——长结构化 JSON 对 flash 档模型有
    lost-in-the-middle / 嵌套扫描不可靠 / O(N) 规模无界 / 输出复述污染四类实害，
    派生摘要必须代码算。
  - 摘要直入 + 按需取全：hostAssets 参考清单（animations/bullets/canSpawnEntities
    只给计数+样例）；skills/talents 若传递则只注入紧凑摘要（abilityId/名称/SP/
    op 集合），全文经 `read_skill(abilityId)` 按需拉取（与语料检索共用同一工具——
    host 技能与语料技能是同类资产）。
  - 完全按需：**快照明细**（全量 entities 仍随请求传输、作为服务端工具的数据源，
    只是不进提示词——payload 大小与 prompt 大小是两回事）：`entity(id)` /
    `entities_at(x,y,r,camp)` / `deploy_cells_near(pos, r)`；交叉项计算；组件/契约
    文档；schema 词表；语料检索。
  - M3 评测集设 A/B 开关 `cfg.snapshot_mode: digest|full`，用跑分验证 digest 不伤
    设计质量。
  - digest 全场只出现一次（消灭双注入）。
- **对外 phase 词表**：`plan / act / draft / review / submit / degraded`（客户端只打印
  不解析，零改动）。
- **validate_draft 随手可用**，反馈环同线程当场闭合。

### 4.2 工具集（四类）

| 工具 | 来源 | 说明 |
|---|---|---|
| `compute_cross_items` | crossitems.py 扩充 | 几何/聚合按需计算：补实体间距离 `pairwise_distance(idA,idB)`、敌群聚合（total_hp/avg_hp_rate）；**描述文本显式标注"设计期参考，运行时条件只能用黑板键"** |
| `entity(id)` / `entities_at(x,y,r,camp)` / `deploy_cells_near(pos, r)` | crossitems.py（明细查询组） | digest 模式下快照明细的按需通道，数据源=请求体全量快照（不进提示词） |
| `list_components` / `read_component_doc` / `read_contract_doc` | tools.py 沿用 | 渐进披露链路原样保留 |
| `read_schema_vocab` | 新增 | 一次返回 triggerEvents/conditionOps/ruleReentry/paramValueTypeEncoding/clamps/eventContext/entityContext（见 §五；knownBlackboardKeys 经 P0-3 清空后为空集，无快照键）——填掉设计期词表盲区 |
| `search_skills(query)` / `read_skill(abilityId)` | corpus.py 新增 | 关键词匹配能力描述/designNotes/op 名，返回紧凑列表；设计前查先例 |
| `validate_draft(config)` | validator.py 复用 | 随手校验，返回完整 issues；草案不入报告全量落盘；**ok 时暂存 sanitized 草稿**（降级交付源，见 4.4） |
| `submit_skill(config)` | 新增 | 唯一关卡：全量校验 + hostAssets 边界 + 语义层 + 撞名 warning → 通过即结束循环 |

v1 的语料 Jaccard 后置注入**移除**（search_skills 已覆盖主动检索，后置注入是不受控
变量，A/B 对比不干净）。

### 4.3 模型分层（显式状态机路由，不做接力）

`llm.chat/chat_tools` 增加 model 参数；config 定义三档：

- **strong**（如 glm-4.7-plus 档）：plan 产出、方案设计与修订——错误代价高、token 量小；
- **mid**（现 glm-5.3-flash 档）：配置实现（plan → rules）、validate_draft 后的修复轮、
  研究文档轮——产出体量大、有机器反馈兜底；
- **weak**（flash 档或关闭）：工具结果摘录压缩（可退化为非 LLM 截断）。

路由是**显式可测试的两态机**（调用前必须定 model，而"这轮做什么"只有模型自己
知道，推断式路由错了无反馈信号）：

- `plan_pending`（首轮，或上一轮调用了 `update_plan`）→ **strong**；
- 其余一切轮次（研究文档/写配置/validate 修复/submit 重试）→ **mid**。

语义一致性自审不设独立 strong 轮：submit 通过即结束、没有"submit 前"可路由，
交给 §五静态检查 + 评测集盯（省一次 strong 调用，控成本与 240s 死线）。

同一 messages 线程共享上下文，无交接成本；分层是 config + 每调用点一个参数，
不构成架构。

### 4.4 上下文与预算

- **工具结果体积自限优先**：工具层直接控制输出（列表截断 top-N、长文档分段），
  >4k chars 的回填截断版+原文入报告（落盘可回放）；weak LLM 摘要仅作兜底，不作为
  常规路径（每条大结果一次 LLM 调用，延迟与成本不成比例）。
- **预算闸**：config 定义 `max_rounds / max_total_tokens / max_wall_seconds`
  （默认 210s，对齐客户端 240s 死线留 30s 余量），每轮检查；LLM 计量在 llm.py
  响应处累加（usage 字段）。
  **单次 LLM 调用 timeout = min(cfg.llm_timeout, 剩余预算 - 5s)**——llm_timeout
  （120s）挂起期间轮间检查无效，剩余 100s 时仍可合法占满 120s 穿破死线；剩余
  预算 < 30s 不再发起实现轮，直接进降级判定。
- **降级协议（写进响应契约）**：`response.degraded: bool` + `report.degradedReason`。
  降级交付物 = 最近一次 validate_draft ok 的 **sanitized** 草稿（validator 返回值，
  非 LLM 原稿）；绝不降级交付未过校验的配置——宁 rejected；degraded 不入缓存
  （P0-1），同 payload 重触发可重跑。客户端现逻辑只看 `status=="ok"`，degraded
  照常注入，仅日志区分；若后续想对 degraded 弹提示，客户端加一行。

## 五、语义验证层（schema 数据 + validator 扩展）

1. **eventContext + entityContext 编码**：ability-ops.json 新增两节——
   - `eventContext`：每个 triggerEvent 列出其运行时上下文（Blackboard `source=event`
     的可用 path 与类型），以 WriteBlackboard.cs 事件桥接为准**逐分支核对**，含尾部
     散落项（isdeadly ∈ AfterAttack/AfterTakeDamage、cumbo ∈ BeforeAttack，
     WriteBlackboard.cs:157-162）；
   - `entityContext`：source=entity 的 path 词表（ResolveEntityValue 的 switch，与
     SpawnEntity.passStat 共用）。
   两表同时服务提示词（read_schema_vocab）与 validator；编码表注明来源文件
   （WriteBlackboard.cs 等），schema protocolVersion 递增兜底。
2. **上下文 path 校验**：`write_blackboard source=event` 的 path ∈ 所在规则全部
   triggers 的 eventContext **并集** → 词表外记 warning（与现有"读键无生产者"同级，
   不拒）；source=entity 同理查 entityContext。多 trigger 规则的触发事件运行时才定、
   静态不可知，warning 级检查以控制误报为先，漏报由运行时 WarnUnknownContextPath
   一次性告警兜底。
3. **黑板键可达性**：现检查 reads ⊆ writes ∪ knownBlackboardKeys ∪ fixedWrites 已有
  （knownBlackboardKeys 经 P0-3 清空，快照键引用将落入"读键无生产者"警告）；补上
  select_targets 实体列表键
  （write_blackboard source=listCount 的 path 引用）的写前读序静态检查（警告级）：
  path 键须为同技能内 select_targets 产出且步骤序在前。
4. **低频挡位提示**：Restart reentry、语料零使用的触发事件在 validator 命中时附
   一行"语料中无先例"提示，severity 用 `info`——客户端 LogServerIssues 的 else
   分支自然按 warning 打印，零改动。

## 六、黄金任务评测集

1. **构造**：从 48 语料选 15~20 个覆盖高频 op 与典型触发事件的任务：每个 = 手工
   构造的快照（**v2 形状**，self 在 entities）+ hostAssets + constraints.request +
   期望特征表（op 集合必须含/不得含、触发事件、数值量级带、效果一句话）。存
   `ability-server/evals/tasks/*.json`，运行器 `evals/run.py` 批量打真 LLM。
2. **v1 基准前置采集（关键时序）**：契约 v2 落地（M1）那一刻 v1 即无法解析新快照
   （crossitems 读顶层 selfPos 等），对比跑分必须在 **M1 合入前**用当前已提交代码
   （aba2923）跑完基准并存档报告。配套 `evals/legacy_adapter.py`：v2 形状任务 →
   v1 形状请求的纯函数转换（self 抽出、hostAssets 收窄为
   canSpawnEntityIds/bulletCount/iconKeys）。
3. **打分**：自动 = 提交成功率 / 成本与延迟分布 / 期望 op 集合重合度 / 每任务
   attempt/token 方差；**validator 绿率统一用 v2 校验器回放双方产物**（v2 新增了
   error 级 hostAssets 边界检查，各用各的校验器对比不公平）；语义 = 效果描述与
   配置一致性的 checklist（人工评审一次定基准，此后只跑自动分）。
4. **验收标准**：v2 对 v1 同任务集——validator 绿率不降、语义一致性基准分不降、
   单任务成本不升超过 30%；报告含每任务 attempt/token 分布（盯方差）。
   **不达标处置**：先做一轮调优（prompt/路由状态机/预算参数，不动架构），复测
   仍不过线则 v1 保留、v2 挂 `cfg.agent_version` 开关灰度——不无条件删 v1。
   达标后删除 v1 编排代码（agent.py 三段 + prompt.py 三段提示词及其 agent 侧
   测试，git 保留；客户端管线与 GenerateSkill 组件不动）。

## 七、实施顺序

| 里程碑 | 内容 | 验证 |
|---|---|---|
| M0 | P0 三项 + 对应 pytest | 49 现有测试全绿（受 P0-3 影响的断言同步更新）+ 新增用例（越界 spawnIndex / 越界 bulletDataIndex / 词表外动画名 / degraded 与非 ok 不缓存 / snapshot:* 读键转无生产者警告） |
| M0.5 | 评测集构造 + legacy_adapter + **v1 基准采集**（真 LLM，旧代码）——与 M0 并行，不依赖代码改动 | 基准报告落盘 evals/reports/ |
| M1 | v2 循环骨架（agent2.py）：plan 强制 + 工具集（§4.2 全表）+ 路由状态机 + submit 关卡 + digest；**契约 v2 双端切换**（BattleSnapshotBuilder / Build / AnimationResources 枚举接口 / protocolVersion=2） | pytest 重写 agent 侧测试；mock 冒烟；新旧服务互打被握手拒绝 |
| M2 | 语义验证层（§五）+ eventContext/entityContext 数据编码 + 客户端终检同款（Validate 签名扩参 + Build 拆 hostAssets 子构建器共享） | 校验器坏样本集扩充（越界 / 词表外 event+entity path / 多 trigger 并集 / listCount 时序） |
| M3 | v2 跑分 + v1/v2 对比报告（统一回放口径） | §六验收标准 |
| M4 | 预算/降级协议 + 工具体积自限 + docs（README 客户端章节 / GenerateSkill.md 行为描述 / 契约文档快照节同步）+ 达标则删 v1 编排 | 全量 pytest + 真实 key 冒烟（含 degraded 路径与超时收紧路径） |

M0 可立即动手；M1 起 `app/agent2.py` 并行开发，跑分达标后替换 `agent.py`，
`service/main/jobs/schema/llm/corpus/validator` 保留或小改。

## 八、风险与对策

- **弱模型自由循环漂移**（过早跳过研究直接写配置）：强制 plan + 每 5 轮自检 +
  plan 修订不计轮（update_plan 总次数设上限，防无限刷）。
- **自由循环方差大**：预算闸 + 降级协议 + 评测集盯方差（报告须含每任务
  attempt/token 分布）。
- **eventContext/entityContext 数据过期**（组件事件桥接改动）：编码表注明来源文件
  （WriteBlackboard.cs 等），schema protocolVersion 递增 + 握手兜底。
- **降级产物质量**：只交付 validate_draft ok 的 sanitized 草稿，宁 rejected；
  degraded 不入缓存，同 payload 重触发可重跑。
- **成本上升**：strong 只在 plan_pending 态触发，评测集设成本红线（§六）。
- **单轮超时穿透死线**：调用级 timeout 收紧至剩余预算（§4.4），M4 冒烟覆盖
  "预算临界时发起调用"的场景。
- **路由误判**：两态机极简（plan_pending / 其余），pytest 直接单测状态迁移；
  评测报告分轮次统计模型用量，验证 strong 占比符合预期。
