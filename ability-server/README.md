# ability-server —— LLM 生成技能服务端（Agent 架构）

生成过程是一个**单线程自由循环 Agent**（自写 function-calling 循环，无第三方框架，
plan-agent-framework-v2 架构）：

1. **强制计划**：第一轮必须 `update_plan` 提交设计计划（读哪些组件文档/技能思路/
   黑板键方案），重大转向时修订（次数上限防刷）。
2. **自主研究与设计**：LLM 按需取用 13 个工具——战局明细（entity/entities_at/
   deploy_cells_near）、派生指标（compute_cross_items）、组件文档三件套
   （list_components/read_component_doc/read_contract_doc）、schema 词表
   （read_schema_vocab，重节分段按需取）、语料先例（search_skills/read_skill）。
   设计中随手 validate_draft 校验草稿，按 issues 修复。
3. **唯一交付关卡**：`submit_skill` 全量校验（未知 op/hostAssets 边界/黑板键可达性/
   撞名），通过即结束；裸文本输出不解析，回喂引导重交。

路由两态机：plan_pending→strong 档（计划质量优先），其余→mid 档。每轮发起调用前
过预算闸（轮数/墙钟/tokens）：耗尽时降级交付最近一次 validate_draft 全绿的
sanitized 草稿（`response.degraded=true` + `report.degradedReason`；无草稿宁
rejected；degraded 不入缓存）。单次调用 timeout 收紧至剩余墙钟，防挂起穿破死线。

过程经 on_phase 实时上报：同步端点计入 `report.phases`，异步端点可轮询
（phase 词表 plan/act/review/submit/degraded）。

schema 与客户端同源：`Assets/Resources/Data/AbilityOps/ability-ops.json`；
Python 校验引擎（validator.py）是客户端 AbilityConfigValidator 的镜像。
历史：v1 三段接力（analyze→describe→generate）经 16 黄金任务对比后被 v2 替换，
对比报告见 `evals/reports/ACCEPTANCE_v2.md`（v1 代码 git 历史保留）。

## 安装与启动

配置集中在 `app/config.py`：base_url/模型/温度/mock 开关/重试上限等全部是字面值，
直接改文件调行为；**唯一例外是 API key，从环境变量 `ABILITY_LLM_API_KEY` 读取**（不落代码）。

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

## 技能语料（检索范例 + 风格统计）

Unity 菜单 `Tools → AbilityGeneration → 5. 导出技能语料` 收集两类来源、按资产引用
去重，导出为 `data/skills.json`（只导出通过客户端校验器的技能）：① `Resources/Abilities`
下的 SubJobs 特性类技能资产；② `EntityDataCollection` 各实体 Skills/Talents 引用的
AbilityConfig。服务端按文件 mtime 惰性重载，重新导出无需重启 uvicorn。生成时：

- **检索先例**：Agent 循环内 `search_skills` 按 op 集合 Jaccard 检索相似技能、
  `read_skill` 读全文——借鉴组合方式与数值量级，禁止照抄 id/名称/描述；
- **风格统计**：全语料蒸馏（各 op 参数取值范围/常用值、reentry 分布、规模）供
  validator 低频挡位提示；
- **撞名检查**：生成 abilityId 与语料现有技能撞名时记 warning（不拒绝）。

语料文件缺失/损坏时静默降级为无语料，生成管线其余部分不受影响。

## 接口

- `GET /health`：mock 状态/schema 协议版本/canonical op 数。
- `POST /generate-ability`（同步）：请求 `{protocolVersion, opList[], battleSnapshot,
  hostAssets, constraints}`，响应 `{status, ability, degraded?, report:{issues,
  rounds, plan, tokens, phases, toolOutputs?, degradedReason?, cached}}`——阻塞至
  Agent 跑完。
- `POST /generate-ability/async`：同请求体，立即返回 `{jobId}`，Agent 在后台线程执行。
- `GET /jobs/{jobId}`：`{done, phases:[{phase,detail,t}], error, response}`——
  客户端轮询实时展示阶段（Unity 探针菜单 ④ 即此方式）。

**版本握手**：opList 与服务端 schema 的 canonical 集合做集合 diff，不一致直接
rejected 并返回缺失/多余清单——防 schema 与客户端注册表漂移。

## 配置

环境变量只有一个：`ABILITY_LLM_API_KEY`（未设且非 mock 时拒绝生成）。其余全部是
`app/config.py` 的字面值：base_url / 模型三档（strong/mid）/ temperature / mock 开关 /
预算闸（agent_max_rounds / agent_max_wall_seconds / agent_max_total_tokens /
agent_max_plan_updates）/ 单调用超时（llm_timeout_seconds）/ 鉴权 token
（server_token）/ 限流（rate_limit）/ 缓存条数 / 案例落盘目录（`log_dir`，默认
`ability-server/logs/`，每次生成落一份 `gen-*.json` 案例含请求全文+响应全文；
设 None 关闭；已 gitignore）。

## 测试

```bash
cd ability-server
python -m pytest tests/ -q     # 93 个纯逻辑测试（无需 fastapi；agent 测试脚本化 LLM）
```

## 客户端接入

编辑器菜单 `Tools → AbilityGeneration`：

- `5. 导出技能语料`：AbilityConfig 资产 → 服务端检索库（见上节，随时重跑）；
- PlayMode 下 `4. 经本地服务器 Agent 生成技能`：异步提交 → 非阻塞轮询并实时
  打印 Agent 阶段 → 客户端终检（AbilityConfigValidator）→ ReplaceSkill 注入
  （替换当前技能，不动 EntityData）。

运行时管线（阶段三，真机可用）：`generate_skill` 组件（见
docs/skill-components/GenerateSkill.md）——天赋挂在角色 `EntityData.Talents`
（xlsx Talents 列配资产路径），部署时（OnInitialize）异步提交战局快照，组件
OnTick 轮询，完成后客户端终检并 ReplaceSkill 替换当前技能（仅本场有效）。
样例资产 `Resources/Prefabs/Characters/3/Kroos/talents/kroos_tllm.asset`。

## 安全提醒

生产部署必须：设 `config.server_token`、HTTPS、限流收紧；API key 只存在服务端
环境变量，绝不进客户端包。
