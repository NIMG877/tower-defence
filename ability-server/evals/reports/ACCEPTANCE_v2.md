# Agent v2 验收实验报告（M3-6）

日期：2026-09-13。结论先行：**三条验收判据全部达标，建议切换 v2 为默认编排**（已获用户确认，进入 M4 删 v1）。

## 一、方法与口径

- 任务集：`evals/tasks/` 16 个黄金任务（契约 v2 形状快照 + hostAssets + 期望特征表）。
- 控制变量：v1 基线与 v2 三档模型全用 glm-5.3-flash（用户拍板，排除模型差异）；
  同一 key、同一智谱 coding 端点。
- v1 基线：`git archive aba2923` 旧代码采集，报告存档 `v1_baseline/baseline_final.json`。
- v2：主仓 agent2 采集 `v2_first/`（16 题）。
- validator 绿率：`replay_score.py` 用 **v2 校验器统一回放**双方产物（v2 新增
  error 级 hostAssets 边界检查，各用各的校验器对比不公平）。
- autoPass：`run.py auto_score` 纯函数离线重算（先在 v1 基线复现采集时口径
  7/15=0.467，确认打分器对齐后再比 v2）。
- 请求发出前 llm 单调用超时已从 120s 放宽至 600s（根因修复，见 §四）；除此之外
  无任何时间限制（评测脚本轮询不设限）。

## 二、验收判据（计划 §六）

| 判据 | v1 基线 | v2 | 结论 |
|---|---|---|---|
| validator 绿率（统一回放） | 0.938（15/16） | **1.0（16/16）** | 达标（升） |
| autoPass 语义代理分 | 0.467（7/15） | **0.750（12/16）** | 达标（升） |
| 单任务成本不升超 30% | 无 token 计量 | 墙钟中位 201.7s vs 167.6s（**+20%**） | 达标 |

- 成本判据说明：usage 计量是 M3 才加给 agent2 的，v1 基线进程无 token 数据，
  只能用墙钟代理（v2 侧 16 题真实消耗 2.66M prompt + 250k completion ≈ 2.91M tokens）。
- 墙钟口径：v2 仅 8 道"非缓存"题有真实墙钟（其余 8 题经缓存救援，墙钟无意义，
  tokens 仍为原跑真实值）；v1 15 道全真实。中位 201.7（n=8）vs 167.6（n=15）。
- 旧 240s 客户端死线口径：v1 超 4/15，v2 超 2/8（均为超时题，600s 下完成）。

## 三、每任务明细

### v2（v2_first，16/16 ok）

| 任务 | 状态 | 墙钟 | 轮数 | tokens(p+c) | autoPass |
|---|---|---|---|---|---|
| t01_vanguard_cost_recovery | ok | (缓存) | 6 | 53k+4k | pass |
| t02_medic_attack_buff | ok | (缓存) | 7 | 112k+10k | pass |
| t03_defender_defense_buff | ok | (缓存) | 11 | 148k+11k | FAIL¹ |
| t04_guard_single_target_burst | ok | (缓存) | 11 | 196k+19k | pass |
| t05_caster_splash_radius | ok | (缓存) | 6 | 60k+6k | pass |
| t06_thorns_reflect | ok | (缓存) | 7 | 137k+16k | FAIL² |
| t07_sniper_extra_target | ok | (缓存) | 8 | 108k+9k | pass |
| t08_summon_blocker | ok | (缓存) | 8 | 298k+31k | pass |
| t09_mortar_barrage | ok | 510s | 8 | 109k+23k | pass |
| t10_animation_costume_swap | ok | 223s | 8 | 216k+21k | pass |
| t11_mass_knockback | ok | 158s | 8 | 216k+13k | pass |
| t12_gamble_buff | ok | 220s | 7 | 330k+20k | pass |
| t13_tick_damage_aura | ok | 184s | 9 | 244k+18k | pass |
| t14_silence_casters | ok | 164s | 7 | 187k+16k | FAIL¹ |
| t15_blocked_priority | ok | 568s | 9 | 163k+26k | FAIL³ |
| t16_kill_reward_cost | ok | 99s | 6 | 80k+8k | pass |

1. t03/t14：量级带路径（apply_buff.magnitudes / apply_abnormal_state.duration）在
   **v1 与 v2 上均为 found=[]**——疑似任务规格的量级带路径与实际 JSON 形状不符
   （评测基建 artifact），不影响对比公平性，待修任务规格。
2. t06：触发事件缺 OnAfterTakeDamage（v1 该题 pass）。
3. t15：触发事件缺 OnAbilityBegin（用了 OnBeforeTargetSelect——每次选目标时重估）；
   生成结构未用 attack_candidate_override，"只攻击被友军阻挡的敌人"的语义落实度
   需人工 checklist 判定。**人工语义 checklist 尚未执行**（计划 §六.3 的一次性基准评审）。

### v1（v1_baseline，15/16 ok，autoPass 7/15）

ok 题墙钟：t01 134.8 / t02 173.3 / t03 153.1 / t04 114.4 / t05 330.9 / t06 328.4 /
t07 117.8 / t08 167.6 / t10 146.3 / t11 193.7 / t12 160.4 / t13 139.0 / t14 207.0 /
t15 379.6 / t16 280.8（中位 167.6，最大 379.6）；t09 error（三次尝试均超时）。
autoPass 失败：t01/t03/t04/t05/t07/t13/t14/t16（多为量级带）。

## 四、超时题重跑与根因（600s 放宽后）

| 任务 | 结果 | 墙钟 | 轮数/工具 | LLM 错误 | autoPass |
|---|---|---|---|---|---|
| v2 t09 | ok | 510.1s | 8 轮/19 工具 | 0 | True |
| v2 t15 | ok | 567.6s | 9 轮/23 工具 | 0 | False（见上） |
| v1 t09 | ok | 474.6s | （v1 无计量） | 0 | True |

- v1 t09 经 `git worktree aba2923` 重建服务（补丁：coding 端点 + 600s）重跑，落
  `v1_timeout_rerun/`，基线目录未动。
- **根因实锤**：原 120s 单调用读超时卡在 glm-5.3-flash 写完整配置 JSON（120~180s）
  的生成时长两侧；v1/v2 双方同死于此，放宽后三题一次通过、0 次错误。
  与"轮数过多/自我循环"无关：成功题与失败题同为 6~11 轮/10~23 工具，工具执行≈0s
  （本地查表），重复调用均为查询各异的语料浏览。

## 五、评测期环境变更记录（收尾需回滚/拍板项）

| 项 | 原 | 评测期现值 | 处置建议 |
|---|---|---|---|
| config.llm_timeout_seconds | 120 | 600 | 保留（根因修复，非临时放宽） |
| evals/run.py --timeout | 360 | 0（不设限） | 保留（服务端有轮数+预算闸兜底） |
| Unity GenerateSkill/探针 PollTimeoutSeconds | 240f | 3600f | 待 Unity 实测后回 600f 量级（另拍板） |
| config.llm_model_strong | glm-5.3 | glm-5.3-flash（控制变量） | M4 恢复 glm-5.3 |
| config.llm_model_weak | None | glm-5.3-flash（控制变量） | M4 恢复 None |
| config.llm_base_url | /api/paas/v4 | /api/coding/paas/v4 | 保留（原端点余额耗尽；走编程套餐额度） |

## 六、决策

验收达标 → 删除 v1 编排（计划 §六"达标后删除 v1 编排代码，git 保留"），进入 M4
（预算/降级协议 + 工具体积自限 + docs 同步）。
