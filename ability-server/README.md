# ability-server —— LLM 生成技能服务端（Agent 架构）

生成过程是一个**三阶段 Agent**（自写 function-calling 循环，无第三方框架）：

1. **analyze 分析战局**：携带工具的循环，LLM 按需取用——
   - `compute_cross_items`：派生指标按需计算（最近敌距/半径内清单/单体距离血量等，
     客户端快照只送基本信息，减少上下文消耗与噪声）；
   - `list_components`：34 个组件 op 的紧凑索引（渐进式披露入口）；
   - `read_component_doc`：按需读取单篇组件完整文档；
   - `read_contract_doc`：规则/序列/重入/生命周期契约（ability-steps.md）。
2. **describe 给出设计**：产出设计 JSON（abilityName/description/designNotes/
   plannedOps），plannedOps 含未注册 op 会触发一次设计层重试。
3. **generate 生成格式化输出**：新开轻量线程，只注入 plannedOps 的参数 schema，
   产出 AbilityConfig DTO；校验失败只重试本阶段（分析/设计成果保留），带错重试。

每个阶段经 on_phase 实时上报：同步端点计入 `report.phases`，异步端点可轮询。

schema 与客户端同源：`Assets/Resources/Data/AbilityOps/ability-ops.json`；
Python 校验引擎（validator.py）是客户端 AbilityConfigValidator 的镜像。

## 安装与启动

配置集中在 `app/config.py`：base_url/模型/温度/mock 开关/重试上限等全部是字面值，
直接改文件调行为；**唯一例外是 API key，从环境变量 `ABILITY_LLM_API_KEY` 读取**（不落代码）。

```bash
cd ability-server
pip install -r requirements.txt

# 无 key 冒烟（mock 模式，脚本化三阶段，验证全管线）：
# 把 app/config.py 里 llm_mock 改为 True，启动跑完记得改回
uvicorn app.main:app --host 127.0.0.1 --port 8765

# 接真实 LLM（模型 glm-5.3-flash @ 智谱，见 config.py；换模型/base_url 同样改 config.py）
ABILITY_LLM_API_KEY="你的key" uvicorn app.main:app --host 127.0.0.1 --port 8765
```

## 技能语料（检索范例 + 风格统计）

Unity 菜单 `Tools → AbilityGeneration → 5. 导出技能语料` 收集两类来源、按资产引用
去重，导出为 `data/skills.json`（只导出通过客户端校验器的技能）：① `Resources/Abilities`
下的 SubJobs 特性类技能资产；② `EntityDataCollection` 各实体 Skills/Talents 引用的
AbilityConfig。服务端按文件 mtime 惰性重载，重新导出无需重启 uvicorn。生成时：

- **检索范例**：describe 产出 plannedOps 后，按 op 集合 Jaccard 重叠取 top-3
  注入 generate 阶段——借鉴组合方式与数值量级，禁止照抄 id/名称/描述；
- **风格统计**：全语料蒸馏（各 op 参数取值范围/常用值、reentry 分布、规模）
  常驻 generate system；
- **撞名检查**：生成 abilityId 与语料现有技能撞名时记 warning（不拒绝）。

语料文件缺失/损坏时静默降级为无语料，生成管线其余部分不受影响。

## 接口

- `GET /health`：mock 状态/schema 协议版本/canonical op 数。
- `POST /generate-ability`（同步）：请求 `{protocolVersion, opList[], battleSnapshot,
  hostAssets, constraints}`，响应 `{status, ability, report:{attempts, issues, phases,
  analysis, design, cached}}`——阻塞至 Agent 跑完。
- `POST /generate-ability/async`：同请求体，立即返回 `{jobId}`，Agent 在后台线程执行。
- `GET /jobs/{jobId}`：`{done, phases:[{phase,detail,t}], error, response}`——
  客户端轮询实时展示阶段（Unity 探针菜单 ④ 即此方式）。

**版本握手**：opList 与服务端 schema 的 canonical 集合做集合 diff，不一致直接
rejected 并返回缺失/多余清单——防 schema 与客户端注册表漂移。

## 配置

环境变量只有一个：`ABILITY_LLM_API_KEY`（未设且非 mock 时拒绝生成）。其余全部是
`app/config.py` 的字面值：base_url / model / temperature / mock 开关 / 重试上限
（max_attempts）/ 工具循环轮数上限（agent_max_rounds）/ 鉴权 token（server_token）/
限流（rate_limit）/ 缓存条数 / 案例落盘目录（`log_dir`，默认
`ability-server/logs/`，每次生成落一份 `gen-*.json` 案例含请求全文+响应全文；
设 None 关闭；已 gitignore）。

## 测试

```bash
cd ability-server
python -m pytest tests/ -q     # 49 个纯逻辑测试（无需 fastapi；agent 测试脚本化 LLM）
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
