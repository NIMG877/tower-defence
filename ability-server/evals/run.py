"""黄金任务评测运行器：批量打真 LLM，产物落盘 evals/reports/<run>/。

用法（在 ability-server 目录下）：
  python evals/run.py --server http://127.0.0.1:8766 --run v2_first
  可选 --limit N / --only t01,t02 / --parallel N / --timeout 秒

服务端需先启动（真 key）：
  cd ability-server
  ABILITY_LLM_API_KEY=... uvicorn app.main:app --port 8766

产物：reports/<run>/<taskId>.json（请求 envelope + 完整响应 + 耗时）
     reports/<run>/summary.json（自动分：提交成功率 / op 集合重合度 / rounds
     与耗时分布）
语义一致性 checklist 人工评审一次定基准（§六.3），本脚本只出自动分。
v1 基线（reports/v1_baseline/）为历史存档：经 v1 形状适配在 aba2923 旧代码上
采集，方法与适配器见 git 历史（ACCEPTANCE_v2.md 记录结论）。
"""

from __future__ import annotations

import argparse
import json
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
TASKS_DIR = HERE / "tasks"
REPORTS_DIR = HERE / "reports"
SCHEMA_PATH = HERE.parents[1] / "Assets" / "Resources" / "Data" / "AbilityOps" / "ability-ops.json"


def http_json(method: str, url: str, body: dict | None = None,
              token: str | None = None, timeout: int = 30) -> dict:
    data = json.dumps(body, ensure_ascii=False).encode("utf-8") if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("X-Auth-Token", token)
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


def submit_and_wait(server: str, envelope: dict, token: str | None,
                    timeout: float) -> tuple[dict | None, str, float]:
    """返回 (response, error, wall_seconds)。error 非空表示 HTTP/轮询层失败。"""
    start = time.monotonic()
    try:
        job = http_json("POST", f"{server}/generate-ability/async", envelope, token)
    except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError) as exc:
        return None, f"submit failed: {exc}", time.monotonic() - start
    job_id = job.get("jobId")
    if not job_id:
        return None, f"no jobId in reply: {job}", time.monotonic() - start

    poll_url = f"{server}/jobs/{job_id}"
    while timeout <= 0 or time.monotonic() - start < timeout:
        try:
            status = http_json("GET", poll_url, timeout=15)
        except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError) as exc:
            return None, f"poll failed: {exc}", time.monotonic() - start
        if status.get("error"):
            return None, f"job error: {status['error']}", time.monotonic() - start
        if status.get("done"):
            return status.get("response"), "", time.monotonic() - start
        time.sleep(1.0)
    return None, "poll timeout", time.monotonic() - start


# ===== 自动打分 =====


def collect_ops_and_params(ability: dict) -> tuple[set[str], dict[str, list[float]]]:
    """从 ability.rules 收集 op 集合与数值参数（op.param -> 值列表，供量级带检查）。"""
    ops: set[str] = set()
    values: dict[str, list[float]] = {}

    def as_num(text) -> float | None:
        try:
            return float(text)
        except (TypeError, ValueError):
            return None

    def walk(steps) -> None:
        for step in steps or []:
            op = step.get("op")
            if op:
                ops.add(op)
            for entry in (step.get("args") or {}).get("entries") or []:
                if entry.get("fromBlackboard"):
                    continue
                num = as_num(entry.get("value"))
                if num is not None:
                    values.setdefault(f"{op}.{entry['key']}", []).append(num)
            walk(step.get("steps"))
            walk(step.get("elseSteps"))

    for rule in ability.get("rules") or []:
        walk(rule.get("steps"))
    return ops, values


def auto_score(expected: dict, response: dict) -> dict:
    """自动分：提交状态 + op 集合重合度 + 量级带。validator 绿率在 M3 用 v2 校验器统一回放。"""
    ok = response is not None and response.get("status") == "ok"
    result: dict = {"submitted": ok}
    if not ok:
        return result
    ability = response.get("ability") or {}
    ops, values = collect_ops_and_params(ability)

    missing = [op for op in expected.get("opsMustInclude", []) if op not in ops]
    banned = [op for op in expected.get("opsMustExclude", []) if op in ops]
    any_missing = [group for group in expected.get("opsAnyOf", [])
                   if not any(op in ops for op in group)]
    events = {t.get("triggerEvent") for r in ability.get("rules") or []
              for t in r.get("triggers") or []}
    ev_missing = [e for e in expected.get("triggerEventsMustInclude", []) if e not in events]
    ev_any_missing = (None if not expected.get("triggerEventsAny")
                      else not any(e in events for e in expected["triggerEventsAny"]))
    out_of_band = []
    for band in expected.get("magnitude", []):
        nums = values.get(band["path"]) or []
        if not nums or not any(band["min"] <= n <= band["max"] for n in nums):
            out_of_band.append({"path": band["path"], "found": nums})

    result.update({
        "opCoverage": {
            "missing": missing, "banned": banned,
            "anyOfUnmet": any_missing,
            "triggerEventsMissing": ev_missing,
            "triggerEventsAnyUnmet": bool(ev_any_missing) if ev_any_missing is not None else False,
        },
        "magnitudeOutOfBand": out_of_band,
        "autoPass": not (missing or banned or any_missing or ev_missing
                         or ev_any_missing or out_of_band),
    })
    return result


def collect_one(server: str, token: str | None, timeout: float, out_dir: Path,
                schema_ops: list, protocol_version: int, path: Path) -> dict:
    """单任务：构建 envelope → 提交轮询 → 落盘 → 自动分。线程池的并行单元。"""
    task = json.loads(path.read_text(encoding="utf-8"))
    envelope = {"protocolVersion": protocol_version,
                "opList": schema_ops, **task["request"]}

    response, error, wall = submit_and_wait(server, envelope, token, timeout)
    record = {"taskId": task["taskId"], "wallSeconds": round(wall, 2),
              "error": error, "response": response}
    (out_dir / f"{task['taskId']}.json").write_text(
        json.dumps({"envelope": envelope, **record}, ensure_ascii=False, indent=1),
        encoding="utf-8")

    status = error or (response or {}).get("status") or "no-response"
    report = (response or {}).get("report") or {}
    tokens = report.get("tokens") or {}
    cached = bool(report.get("cached"))
    score = auto_score(task["expected"], response) if response else {}
    return {"taskId": task["taskId"], "status": status, "cached": cached,
            "wallSeconds": record["wallSeconds"],
            "rounds": report.get("rounds"),
            "totalTokens": tokens.get("prompt_tokens", 0) + tokens.get("completion_tokens", 0),
            "auto": score,
            "print": f"[{task['taskId']}] {status} wall={record['wallSeconds']}s "
                     f"{'(cached)' if cached else ''} autoPass={score.get('autoPass')}"}


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--server", required=True, help="如 http://127.0.0.1:8766")
    ap.add_argument("--run", required=True, help="报告目录名，如 v2_first")
    ap.add_argument("--protocol-version", type=int, default=None,
                    help="envelope 版本号，默认取 ability-ops.json 的 protocolVersion")
    ap.add_argument("--token", default=None, help="X-Auth-Token（服务端未设 token 可省）")
    ap.add_argument("--limit", type=int, default=None)
    ap.add_argument("--only", default=None, help="逗号分隔 taskId 过滤")
    ap.add_argument("--timeout", type=float, default=0,
                    help="单任务轮询上限秒；0=不设限（测试期默认，服务端自身有轮数与熔断兜底）")
    ap.add_argument("--parallel", type=int, default=1,
                    help="并发任务数（>1 时延迟指标含并发争用，绿率/tokens 不受影响）")
    args = ap.parse_args()

    schema_text = SCHEMA_PATH.read_text(encoding="utf-8")
    schema_json = json.loads(schema_text)
    if args.protocol_version is None:
        args.protocol_version = schema_json["protocolVersion"]

    tasks = sorted(TASKS_DIR.glob("*.json"))
    if args.only:
        wanted = {t.strip() for t in args.only.split(",")}
        tasks = [p for p in tasks if p.stem in wanted]
    if args.limit:
        tasks = tasks[:args.limit]
    if not tasks:
        print("no tasks matched", file=sys.stderr)
        return 2

    out_dir = REPORTS_DIR / args.run
    out_dir.mkdir(parents=True, exist_ok=True)
    schema_ops = sorted(set(schema_json["primitives"]) | set(schema_json["componentOps"]))

    from concurrent.futures import ThreadPoolExecutor, as_completed
    workers = max(1, args.parallel)
    summary = []
    with ThreadPoolExecutor(max_workers=workers) as pool:
        futures = {pool.submit(collect_one, args.server, args.token, args.timeout,
                               out_dir, schema_ops, args.protocol_version,
                               path): path for path in tasks}
        for future in as_completed(futures):
            entry = future.result()
            summary.append(entry)
            print(entry["print"], flush=True)

    summary.sort(key=lambda s: s["taskId"])

    fresh = [s for s in summary if not s["cached"]]
    ok_count = sum(1 for s in fresh if s["status"] == "ok")
    auto_pass = sum(1 for s in fresh if s.get("auto", {}).get("autoPass"))
    report = {
        "run": args.run, "server": args.server,
        "tasks": len(summary), "cachedHits": len(summary) - len(fresh),
        "okRate": round(ok_count / len(fresh), 3) if fresh else None,
        "autoPassRate": round(auto_pass / len(fresh), 3) if fresh else None,
        "rounds": sorted(s["rounds"] for s in fresh if s["rounds"] is not None),
        "totalTokens": sorted(s["totalTokens"] for s in fresh
                              if s["status"] == "ok"),
        "wallSeconds": sorted(s["wallSeconds"] for s in fresh),
        "perTask": summary,
    }
    (out_dir / "summary.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"\nokRate={report['okRate']} autoPassRate={report['autoPassRate']} "
          f"-> {out_dir / 'summary.json'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
