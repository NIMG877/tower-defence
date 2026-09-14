"""统一校验器回放（plan §六.3）：双方产物都用当前 v2 校验器重放绿率。

v1 基线产自 aba2923 的校验器（无 hostAssets 边界、iconKey 保留），v2 产自
当前校验器——各用各的口径对比不公平，绿率判定必须回放到同一把尺子上。

用法（在 ability-server 目录）：
  python evals/replay_score.py v1_baseline v2_first
对每个 run 目录读 tasks/<taskId>.json（取 hostAssets 作校验域）与 reports/<run>/
<taskId>.json（取 response.ability 作被检产物），逐个 validator.validate（hostAssets
边界 error 计入不绿），写 reports/<run>/replay.json 并打印对比表。

绿率定义：提交成功（status=ok 且 ability 非空）且回放校验无 error 级 issue。
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))  # app 包

from app import schema as schema_mod, validator  # noqa: E402

TASKS_DIR = HERE / "tasks"
REPORTS_DIR = HERE / "reports"


def replay_run(run: str, schema: dict) -> dict:
    run_dir = REPORTS_DIR / run
    per_task = []
    for path in sorted(run_dir.glob("t*.json")):
        record = json.loads(path.read_text(encoding="utf-8"))
        task_id = record.get("taskId") or path.stem
        task = json.loads((TASKS_DIR / f"{task_id}.json").read_text(encoding="utf-8"))
        response = record.get("response") or {}
        ability = response.get("ability")
        # run.py 成功时 error 写空串而非 null，用真值判断。
        submitted = not record.get("error") and response.get("status") == "ok" \
            and isinstance(ability, dict)

        green = False
        error_count = 0
        if submitted:
            ok, issues, _ = validator.validate(
                ability, schema,
                host_assets=task["request"].get("hostAssets") or None)
            error_count = sum(1 for i in issues if i["severity"] == "error")
            green = ok
        per_task.append({"taskId": task_id, "submitted": submitted,
                         "replayGreen": green, "replayErrors": error_count})

    green_count = sum(1 for t in per_task if t["replayGreen"])
    out = {"run": run, "tasks": len(per_task),
           "submitOkRate": round(sum(1 for t in per_task if t["submitted"]) / len(per_task), 3)
           if per_task else None,
           "replayGreenRate": round(green_count / len(per_task), 3) if per_task else None,
           "perTask": per_task}
    (run_dir / "replay.json").write_text(
        json.dumps(out, ensure_ascii=False, indent=1), encoding="utf-8")
    return out


def main() -> int:
    runs = sys.argv[1:]
    if not runs:
        print("usage: python evals/replay_score.py <run> [<run> ...]", file=sys.stderr)
        return 2
    schema = schema_mod.load_schema()
    results = [replay_run(run, schema) for run in runs]

    print(f"{'run':16s} {'tasks':>5s} {'submitOk':>8s} {'replayGreen':>11s}")
    for r in results:
        print(f"{r['run']:16s} {r['tasks']:>5d} {r['submitOkRate']:>8} {r['replayGreenRate']:>11}")
    if len(results) == 2:
        (a, b) = results
        print(f"\n逐任务对照（回放口径，{a['run']} vs {b['run']}）：")
        for ta, tb in zip(a["perTask"], b["perTask"]):
            mark = "=" if ta["replayGreen"] == tb["replayGreen"] else ("<" if tb["replayGreen"] else ">")
            print(f"  {ta['taskId']:34s} {a['run']}={'G' if ta['replayGreen'] else '-'} "
                  f"{mark} {b['run']}={'G' if tb['replayGreen'] else '-'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
