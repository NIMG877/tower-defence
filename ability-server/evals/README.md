# 黄金任务评测集（plan-agent-framework-v2 §六）

16 个手工构造的任务（`tasks/*.json`）：契约 v2 形状的快照 + hostAssets + 诉求，
附期望特征表（op 必含/任选/排除、触发事件、数值量级带、效果一句话），直接编辑维护。

## 采集

```bash
ABILITY_LLM_API_KEY=<key> uvicorn app.main:app --port 8766
python evals/run.py --server http://127.0.0.1:8766 --run <run名>
# 可选 --limit N / --only t01,t02 / --parallel N / --timeout 秒
```

任务只跑一次、失败不重试（缓存不吞坏结果，重跑前可删 reports/<run>/ 或整目录
重采）。

## 产物与打分

- `reports/<run>/<taskId>.json`：请求 envelope + 完整响应 + 耗时（可回放）。
- `reports/<run>/summary.json`：okRate / autoPassRate / rounds 与耗时分布。
- validator 绿率用统一校验器回放（`evals/replay_score.py <run> [<run> ...]`）——
  各次采集若服务端校验器口径不同，回放到同一把尺子才可比；语义一致性
  checklist 人工评审一次定基准（§六.3）。

v1 基线（`reports/v1_baseline/`）为历史存档：v1 编排（agent.py）已删，基线在
aba2923 旧代码上经 v1 形状适配采集，方法与适配器见 git 历史
（ACCEPTANCE_v2.md 记录对比结论）。
