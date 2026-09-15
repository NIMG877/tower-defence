"""交互式测试入口：输入技能描述 → Agent 免战局快照按描述设计 → 终端实时观测。

用法（ability-server 目录下）：
  python -m app.cli "部署时每秒损失 2 点生命，持续 15 秒"    # 一次性
  python -m app.cli                                          # 交互循环，逐条输入描述
  echo "描述" | python -m app.cli                            # 管道一次性

实时打印：每轮路由（模型/思考档）→ 思考原文（逐调用整块返回，非 token 级流式）→
工具调用与模型可见结果 → 最终 status/issues/ability。进程内直调 GenerateService：
自动获得握手、缓存（相同描述直接命中）与案例落盘（logs/gen-*.json 含 trace/thread
回放）。需要 ABILITY_LLM_API_KEY（config 字面值管理，无命令行开关；mock 路径不解析
描述）。描述模式下无宿主资产，spawnIndex/bulletDataIndex 等边界引用不做校验。
"""

from __future__ import annotations

import json
import sys
import time

from app.config import Config
from app.schema import canonical_ops, load_schema
from app.service import GenerateService

# 控制台展示截断：超长思考/工具结果只打印前段，全文在案例文件回放。
PRINT_CHAR_LIMIT = 2000


def build_request(description: str, schema: dict) -> dict:
    """描述模式 envelope：握手材料从组件库现读（与 evals/run.py 同源）。"""
    return {"protocolVersion": schema["protocolVersion"],
            "opList": sorted(canonical_ops(schema)),
            "description": description}


def clip(text: str, limit: int = PRINT_CHAR_LIMIT) -> str:
    if len(text) <= limit:
        return text
    return text[:limit] + f"...[显示截断 {len(text)} chars，全文见 logs/ 案例文件]"


def print_phase(phase: str, detail: str) -> None:
    print(f"[{phase}] {detail}")


def print_event(kind: str, data: dict) -> None:
    if kind == "thinking":
        tokens = data.get("tokens") or {}
        print(f"\n---- 思考 round {data['round']} model={data['model']} "
              f"effort={data['effort']} {data['durationS']}s "
              f"tokens={tokens.get('prompt_tokens', 0)}+"
              f"{tokens.get('completion_tokens', 0)} ----")
        print(clip(data.get("thinking") or "（无思考输出）"))
    elif kind == "tool_call":
        print(f"\n→ {data['name']} {clip(data['arguments'])}")
    elif kind == "tool_result":
        print(f"← {data['name']} {clip(data['result'])}")


def print_summary(response: dict, wall: float) -> None:
    report = response.get("report") or {}
    tokens = report.get("tokens") or {}
    total = tokens.get("prompt_tokens", 0) + tokens.get("completion_tokens", 0)
    marks = (" [缓存命中：相同描述，改描述即重跑]" if report.get("cached") else "") + \
            (" [degraded]" if response.get("degraded") else "")
    print(f"\n==== status={response.get('status')} wall={wall:.0f}s "
          f"rounds={report.get('rounds')} tokens={total}{marks} ====")
    for issue in report.get("issues") or []:
        print(f"  [{issue.get('severity')}] {issue.get('path')}: {issue.get('message')}")
    ability = response.get("ability")
    if ability:
        print(json.dumps(ability, ensure_ascii=False, indent=1))


def run_once(service: GenerateService, description: str) -> None:
    """单次生成：schema 现读（改库即生效），实时事件直连打印机。"""
    schema = load_schema()
    started = time.monotonic()
    response = service.generate(build_request(description, schema),
                                on_phase=print_phase, on_event=print_event)
    print_summary(response, time.monotonic() - started)


def main(argv: list[str]) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(errors="replace")  # Windows 控制台编码兜底
    service = GenerateService(Config())
    description = " ".join(argv).strip()
    if not description and not sys.stdin.isatty():
        description = sys.stdin.read().strip()
        if not description:
            print("stdin 为空：请给出技能描述。", file=sys.stderr)
            return 2
    if description:
        try:
            run_once(service, description)
        except KeyboardInterrupt:
            print("\n（已中断本次生成）", file=sys.stderr)
            return 130
        return 0
    print("输入技能描述，Agent 免战局快照按描述设计；空行或 Ctrl+C 退出。")
    while True:
        try:
            line = input("技能描述> ").strip()
        except (EOFError, KeyboardInterrupt):
            return 0
        if not line:
            return 0
        try:
            run_once(service, line)
        except KeyboardInterrupt:
            print("\n（已中断本次生成）", file=sys.stderr)


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
