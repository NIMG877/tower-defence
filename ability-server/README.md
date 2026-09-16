# ability-server —— LLM 生成技能服务端（Agent 架构）

生成过程是一个**单线程自由循环 Agent**（自写 function-calling 循环，无第三方框架，plan-agent-framework-v2 架构）：

1. **强制计划**：第一轮必须 `update_plan` 提交设计计划（读哪些组件文档/技能思路/黑板键方案），重大转向时修订（次数上限防刷）。
2. **自主研究与设计**：LLM 按需取用 18 个工具——战局明细（entity/entities_at/deploy_cells_near）、派生指标（compute_cross_items）、组件文档三件套（list_components/read_component_doc/read_contract_doc）、schema 词表（read_schema_vocab，重节分段按需取）、语料先例（search_skills/read_skill）。
3. **增量构建（CC 式微步）**：start_draft 定身份与 SP → design_rules 一次立全部规则骨架（触发/条件/reentry 与每 step 的 op+intent，语义层先于 JSON）→按骨架逐个 put_step 转写完整参数（每步即时 lint：参数键/类型/词表/钳制与骨架位置对照；drop_step/drop_rule 修正）→ validate_draft 全量自检（无 config即校验增量草稿）。
4. **唯一交付关卡**：`submit_skill` 全量校验（未知 op/hostAssets 边界/黑板键可达性/撞名），可免重写直接交付已过闸草稿；通过即结束；裸文本输出不解析，回喂引导重交。

路由两态机：plan_pending→strong 档（计划质量优先），其余→mid 档。思考强度（`reasoning_effort`，GLM-5.3 系仅 low/high/max、强制思考不可关闭）按轮分档：规划轮 max / 研究与设计轮 high / 草稿过闸后的修复与提交轮 low——交付纪律上，模型先出设计定稿再转写配置，`submit_skill` 可不带 config 直接交付已过闸草稿（免重写）。每轮发起调用前过预算闸（轮数/墙钟/tokens）：耗尽时降级交付最近一次validate_draft 全绿的 sanitized 草稿（`response.degraded=true` +`report.degradedReason`；无草稿宁 rejected；degraded 不入缓存）。单次调用timeout 收紧至剩余墙钟，防挂起穿破死线。

过程经 on_phase 实时上报：同步端点计入 `report.phases`，异步端点可轮询（phase 词表 plan/act/review/submit/degraded）。

校验规则、组件文档、技能语料统一存组件库 `data/ability.db`（**唯一真源**，见下节）；Python 校验引擎（validator.py）从库重建校验形状，服务端 submit_skill 关卡 +FromDto 严格反序列化是仅有的两道结构闸（客户端不再持有规则副本）。生成期参考的历史 md 已退役（内容收编入库，原文见 git 历史）。历史：v1 三段接力（analyze→describe→generate）经 16 黄金任务对比后被 v2 替换，对比报告见 `evals/reports/ACCEPTANCE_v2.md`（v1 代码 git 历史保留）。

## 安装与启动

配置集中在 `app/config.py`：base_url/模型/温度/mock 开关/重试上限等全部是字面值，直接改文件调行为；**唯一例外是 API key，从环境变量 `ABILITY_LLM_API_KEY` 读取**（不落代码）。

```bash
cd ability-server
pip install -r requirements.txt

# 无 key 冒烟（mock 模式，脚本化 plan→submit，验证全管线）：
# 把 app/config.py 里 llm_mock 改为 True，启动跑完记得改回
uvicorn app.main:app --host 127.0.0.1 --port 8765

# 接真实 LLM（模型分层见 config.py：strong=glm-5.3 / mid=glm-5.3-flash @ 智谱；
# 换模型/base_url 同样改 config.py）
ABILITY_LLM_API_KEY="你的key" uvicorn app.main:app --host 127.0.0.1 --port 8765
```

## 组件库（文档与语料的单一数据源）

`data/ability.db`（SQLite）存三样东西，Agent 的文档工具与语料检索全部查它：

- **组件表**（ops/op_params）：39 个 op（35 组件+4 原语）的参数表/词表/行为语义/
  组合配方——中文文档内容直接存于库，`list_components`/`read_component_doc` 查表；
- **全局契约**（global_docs 等 8 张小表）：规则形状/触发与条件/序列语义/重入/
  参数存储/操作生命周期/注册表扩展——`read_contract_doc` 按节查；
- **技能表**（skills，只读）：Unity 导出的语料 48 技能，`search_skills`/`read_skill`
  与撞名检查/风格统计的数据源。

```bash
# Unity 重新导出 skills.json 后导入语料：
python db/import_skills.py
```

**改任何内容（参数/词表/钳制值/文档文案）都直接 UPDATE 库**（SQL 或 DB Browser），服务端按库文件 mtime 惰性重载，改完即生效、无需重启。新增/下架 op = 插删`ops`/`op_params` 行，并把客户端 `AgentGenerateRequest.ProtocolVersion` 常量与`meta.protocolVersion` 同步 bump（握手做相等断言，漂移即拒绝并给出 diff）。

## 技能语料（检索范例 + 风格统计）

Unity 菜单 `Tools → AbilityGeneration → 5. 导出技能语料` 收集两类来源、按资产引用去重，导出为 `data/skills.json`（只导出通过客户端校验器的技能）：① `Resources/Abilities`下的 SubJobs 特性类技能资产；② `EntityDataCollection` 各实体 Skills/Talents 引用的AbilityConfig。**导出后重跑 `python db/import_skills.py` 入库**（见上节）。

生成时：

- **检索先例**：Agent 循环内 `search_skills` 按 op 集合 Jaccard 检索相似技能、`read_skill` 读全文——借鉴组合方式与数值量级，禁止照抄 id/名称/描述；
- **风格统计**：全语料蒸馏（各 op 参数取值范围/常用值、reentry 分布、规模）供validator 低频挡位提示；
- **撞名检查**：生成 abilityId 与语料现有技能撞名时记 warning（不拒绝）。

库缺失/损坏时静默降级为无语料，生成管线其余部分不受影响。

## 接口

- `GET /health`：mock 状态/schema 协议版本/canonical op 数。
- `POST /generate-ability`（同步）：请求 `{protocolVersion, opList[], battleSnapshot,hostAssets, constraints, description?}`，响应 `{status, ability, degraded?, report:{issues,rounds, plan, tokens, phases, degradedReason?, cached}}`——阻塞至Agent 跑完。
- `POST /generate-ability/async`：同请求体，立即返回 `{jobId}`，Agent 在后台线程执行。
- `GET /jobs/{jobId}`：`{done, phases:[{phase,detail,t}], error, response}`——客户端轮询实时展示阶段（Unity 探针菜单 ④ 即此方式）。

**版本握手**：opList 与服务端 schema 的 canonical 集合做集合 diff，不一致直接rejected 并返回缺失/多余清单——防 schema 与客户端注册表漂移。

## 交互式测试入口（CLI）

不走 HTTP、不需要 Unity：输入一段技能描述，Agent 免战局快照按描述设计，终端实时打印每轮路由（模型/思考档）、思考原文（逐调用整块，非 token 级流式）、工具调用与模型可见结果、最终 status/issues/ability。进程内直调 GenerateService，案例照常落盘（`logs/gen-*.json` 含 trace/thread 回放）。

```bash
cd ability-server
python -m app.cli "部署时立刻对全场敌人造成 500 点真实伤害"   # 一次性
python -m app.cli                                            # 交互循环，逐条输入
echo "描述" | python -m app.cli                              # 管道一次性
```

**描述模式语义**（`request` 新增可选 `description` 字段）：无 `battleSnapshot` 且有 `description` 时生效——免战局 digest，系统提示追加描述模式说明，战局类工具（compute_cross_items/entity/entities_at/deploy_cells_near）从工具表剔除；无宿主资产，spawnIndex/bulletDataIndex 等边界引用不做校验（系统提示约定取 0 占位并在intent 标注待宿主绑定）。有快照时 description 被忽略，原路径不变；快照与描述皆无保持原闸拒绝。HTTP 契约、protocolVersion、客户端均不变；缓存键已含 description（相同描述命中缓存，改描述即重跑）。on_event 富事件（thinking/tool_call/tool_result）只在进程内回调——HTTP 响应按设计剥离黑盒，不携带。

## 配置

环境变量只有一个：`ABILITY_LLM_API_KEY`（未设且非 mock 时拒绝生成）。其余全部是`app/config.py` 的字面值：base_url / 模型三档（strong/mid）/ temperature / mock 开关 /预算闸（agent_max_rounds / agent_max_wall_seconds / agent_max_total_tokens /agent_max_plan_updates）/ 单调用超时（llm_timeout_seconds）/ 鉴权 token（server_token）/ 限流（rate_limit）/ 缓存条数 / 案例落盘目录（`log_dir`，默认`ability-server/logs/`，每次生成落一份 `gen-*.json` 案例含请求全文+响应全文+黑盒回放——`trace`：每次模型调用的耗时/分项 tokens（含 reasoning）/思考原文；`thread`：完整对话线程（原始工具参数字符串原文随 assistant.tool_calls 在列）。黑盒只落盘，API 响应不携带；设 None 关闭；已 gitignore）。

## 测试

```bash
cd ability-server
python -m pytest tests/ -q     # 纯逻辑测试（无需 fastapi；agent 测试脚本化 LLM）
```

## 客户端接入

编辑器菜单 `Tools → AbilityGeneration`：

- `4. 导出技能语料`：AbilityConfig 资产 → `data/skills.json`（FromDto 严格反序列化当闸，解析失败的资产跳过；重导后跑 `python db/import_skills.py` 入库）；
- PlayMode 下 `3. 经本地服务器 Agent 生成技能`：异步提交 → 非阻塞轮询并实时打印 Agent 阶段 → ReplaceSkill 注入（服务端已过 submit_skill 关卡；FromDto严格反序列化为结构闸；替换当前技能，不动 EntityData）。

运行时管线（阶段三，真机可用）：`generate_skill` 组件（退役文档docs/skill-components/GenerateSkill.md，git 历史可查）——天赋挂在角色 `EntityData.Talents`（xlsx Talents 列配资产路径），部署时（OnInitialize）异步提交战局快照，组件OnTick 轮询，完成后 FromDto 并 ReplaceSkill 替换当前技能（仅本场有效）。样例资产 `Resources/Prefabs/Characters/3/Kroos/talents/kroos_tllm.asset`。

## 安全提醒

生产部署必须：设 `config.server_token`、HTTPS、限流收紧；API key 只存在服务端环境变量，绝不进客户端包。