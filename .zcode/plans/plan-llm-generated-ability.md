# LLM 运行时生成技能——实施计划

> **实施状态（2026-09-12）**：阶段一已完成代码落地，待 Unity 编译与 EditMode 测试验证。
> 落地偏差两处：① schema 文件路径改为 `Assets/Resources/Data/AbilityOps/ability-ops.json`
> （原定 Validation/ 目录下不可 Resources 加载）；② 图标池改为聚合现有 AbilityConfig
> 资产的 icon（`Resources.LoadAll<AbilityConfig>("Abilities")`，键为 Sprite 名），
> 无需新建图标目录。组件参数提取发现并记录了 FireBullets/UpdateBuff/WriteBlackboard/
> AttackRangeOverride 等文档-代码分歧，均以代码为准录入 schema。
> 产出：Dto/{AbilityConfigDto, AbilityConfigBuilder, AbilityIconPool, SnapshotBlackboardKeys}、
> Validation/{AbilityOpsSchema, AbilityConfigValidator}、AbilityGeneration/BattleSnapshotBuilder
> （BasicScripts 侧）、EntityAbilityRunner.AddSkill/RemoveSkill、Tests 4 件、ability-ops.json（34 组件 op + 4 原语 + 钳制域）。

## 目标
做一个可"按战局生成技能"的角色：部署时把战局快照（地图/敌我位置与血量/自身状态/交叉项）发给服务端，服务端组装提示词（AbilitySystem 组件文档 + schema + 快照 + 宿主资产清单）调用大模型，生成技能描述与 AbilityConfig DTO，经校验闭环后返回客户端，运行时构建并注入角色。

## 架构决策（已定，不重开）
1. **校验双层**：服务端全量校验 + 生成→校验→带错重试闭环（最多 2 次）；客户端终检只做防漂移（op 活注册表白名单 + 版本握手 + 数值钳制 + 未知参数键 warn）。schema JSON 是双端共同数据源，不双写词汇表。
2. **注入走新 API**：`EntityAbilityRunner.AddSkill/RemoveSkill`（镜像 AddExtraAbility），**禁止运行时改共享 `EntityData.Skills`**（跨战斗/跨池化实例泄漏）。
3. **rules 树不建镜像 POCO**：`AbilityRuleConfig/StepConfig/ParamEntry/SPConfig` 全是字符串/数值/枚举的 `[Serializable]` POCO，LLM 输出 JSON 用 StringEnumConverter 直接反序列化为运行时类型；外层 wrapper（abilityId/abilityName/description/iconKey/sp）手工拷进 `CreateInstance<AbilityConfig>()`。契约由 schema+docs 定，版本握手防漂移。
4. **触发时点 = 部署时**，异步请求，快照在请求时刻固化；战中重生成最后做。灵活性优先 `fromBlackboard` 动态参数（同一配置随黑板取值变化），LLM 只负责结构性变化。
5. **交叉项 C# 预算**（敌我最近距离/血量比/威胁值）进快照，不让模型心算几何。
6. **沙箱边界沿用组件既有约束**：spawn_entity 只能取宿主 `CanSpawnEntityIds`、fire_bullets 只能索引 `EntityData.Bullets`、动画 override 只能取宿主已有命名动画——提示词随附宿主清单，LLM 无法引用凭空资产。

## 阶段一：框架闭环（无网络，手工 JSON 端到端）
1. **schema 机器可读化**（唯一文档工程项，客户端校验器与服务端提示词共同依赖）：从 `docs/skill-components/*.md`（35 组件）+ `docs/ability-steps.md` + `docs/skill-components/README.md` 编码契约提炼 `ability-ops.json`：每 op 的 canonical 名/别名/参数表（key/type/默认/取值域/是否支持 fromBlackboard）/目标解析词汇表（**toSelf+blackboardKey 与 blackboardKey→toSelf 优先级相反、targetMode 枚举、subjectMode 四套必须显式编码**）/数值钳制域（伤害倍率/SP 等人工定）。放 `Assets/PublicScripts/GameData/AbilitySystem/Validation/ability-ops.json`（客户端 Resources.Load<TextAsset>；服务端从仓库同路径读）。
2. **DTO + 运行时构造器**：`AbilityConfigDto`（wrapper，见决策 3）+ `AbilityConfigBuilder.FromDto()`。位置 `Assets/PublicScripts/GameData/AbilitySystem/Dto/`。禁止 JsonConvert 直怼 ScriptableObject。
3. **战局快照序列化器** `BattleSnapshotBuilder`（Entity-LevelPublicScripts 侧）：遍历 EntityManager 实体按阵营投影 id/职业/CurrentHp/MaxHpS/Movement.Position/面板关键值 + 地图网格摘要 + 决策 5 的交叉项，输出紧凑 JSON。
4. **客户端校验器** `AbilityConfigValidator`（读 schema JSON）：op 未知→拒绝整技能；参数键未知→warn 丢弃该 entry；数值超钳制域→钳制；blackboard 键 producer/consumer 静态对称性→warn。
5. **AddSkill/RemoveSkill**：`EntityAbilityRunner.cs:295` AddExtraAbility 同构，Kind=Skill、走 `BuildAbilityRuntime`（:361，SPEngine/WireRuntime/BindComponentParams 全复用）、InvalidateAbilitiesCache；Remove 走 OnTeardown 同款清理（SetActive(false)+CancelStepExecutions+组件 OnTeardown+Unwire），幂等。技能卡 UI 经 `runner.Skills` 自动可见（Cards.cs:36 消费 icon/abilityName/description/sp，icon null 有兜底）。
6. **图标池**：专用 Resources 目录 `LoadAll<Sprite>`（禁文件系统枚举，先例 TerminalPanel）；DTO.iconKey→池查找，miss 兜底 null。
7. **EditMode 测试 + 手工 JSON 固件**：构造器 round-trip（DTO→SO→GetRules 逐字段比对）、校验器坏样本集（未知 op/未知键/超钳制/键不对称）、AddSkill 生命周期（构建/绑定/触发/拆除/Remove 幂等/池回收后 SO 引用不泄漏——不被 runtime 引用的 CreateInstance 实例须 Destroy）。

## 阶段二：LLM + 本地服务器（仓库根新建 `ability-server/`，Python FastAPI）
1. `POST /generate-ability`：请求 `{protocolVersion, opList[], battleSnapshot, hostAssets, constraints}`，响应 `{status: ok|rejected, ability: DTO, report}`，无状态。
2. **版本握手**：opList 与服务端 schema op 集合 diff，不一致拒绝并返回缺失集，防 schema 与客户端注册表漂移。
3. **提示词组装**：docs 子集（ability-steps + README 编码契约 + 按 schema 选中的组件文档）+ schema + 快照 + 宿主资产清单（CanSpawnEntityIds/Bullets 索引表/动画名/图标池 key）；LLM 侧用 structured output/JSON mode 约束。
4. **服务端校验引擎**（Python，读同一份 ability-ops.json）+ 重试闭环（错误信息回喂，≤2 次）。
5. token 鉴权 + 每局限次 + 快照哈希缓存 + 坏案例日志（DTO+快照落盘供回放）。
6. **Editor 工具** `Assets/Editor/AbilityGeneration/`：PlayMode 下菜单触发"对当前战局生成技能"直连本地服务器，结果 DTO 存盘人工评审——先于阶段三验证生成质量。

## 阶段三：游戏内运行时接入
1. 部署时触发（CallOut 取出后、Initialize 前后均可发起请求）：技能卡占位/禁用态（"生成中"）。
2. 响应到达后 AddSkill；宿主已死/回收则丢弃；超时/断网/服务端 rejected → 回退默认技能或占位不生效，**不阻塞战斗**。
3. fromBlackboard 最大化：快照序列化器同步把关键计数写入宿主黑板（敌人数/血量比等），生成配置尽量引用黑板键；节流 = 每次部署至多 1 次请求。
4. 战中重生成（最后做）：RemoveSkill 拆旧（旧规则异步序列由 CancelStepExecutions 兜住）→ AddSkill 挂新 + 空窗 UX。注意 RebuildSelectedSkill 仅限休眠期的先例（:128），战中路径以 Add/Remove 组合为准。
5. 服务器上公网：HTTPS/鉴权/限流，接口契约原样搬迁。

## 不做（范围外）
LLM 生成美术资产（icon/动画只从现有池选）；AbilitySystem 正交性审计修复（TargetResolver 统一等）不混入本特性；本地小模型（Ollama）；全队/多技能并行生成与生成技能持久化；离线预生成技能库（仅当网络形态被否决的退路）。

## 风险与对策
幻觉静默失效（未知 op 跳过/未知键取默认）→ 双层校验+拒绝重试；三套目标词汇表 → schema 显式编码；IL2CPP 反射注册表已在线上构建运行、无新增裁剪风险，Newtonsoft 已统一标准包；LLM 延迟 2~30s → 部署时触发+占位 UX+fromBlackboard 减少请求；服务端烧钱代理 → token+限流+缓存；schema/代码漂移 → 版本握手+opList diff（后续可从 RegisterComponent 特性+文档表半自动生成 schema）。

## 验证
EditMode 见阶段一第 7 项 + 服务端校验引擎单测（与客户端坏样本集同源）。PlayMode 实测清单：①部署→生成→SP 充能→手动释放生效；②生成中死亡/回收无异常、SO 不泄漏；③断网/超时/拒绝降级不阻塞战斗；④撤退再部署重建正常；⑤技能卡名称/描述/icon/SP 正确；⑥战中重生成时旧技能异步序列正确拆除、无残留 buff/override；⑦限流与鉴权生效。
