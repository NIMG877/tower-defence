"""三阶段生成 Agent：分析战局 → 给出设计 → 生成格式化输出。

- analyze：携带工具（交叉项计算/组件索引/组件文档/契约文档）的 function-calling
  循环，LLM 按需取用——客户端快照只送基本信息，派生指标服务端按需算。
- describe：同样是工具循环（设计时可复读组件文档确认参数能力），出口是设计
  JSON（abilityName/description/designNotes/plannedOps），plannedOps 全部已注册。
- generate：新开无工具线程，只注入 plannedOps 的参数 schema + 语料范例，产出
  AbilityConfig DTO；校验失败只重试本阶段（分析/设计成果保留），重试反馈附
  上一轮问题清单。

每个阶段经 on_phase(phase, detail) 实时上报（异步任务模式转发给轮询客户端），
同时计入 report.phases 供落盘回放。
"""

from __future__ import annotations

import json
import time

from . import corpus, llm, prompt as prompt_mod, schema as schema_mod, tools, validator


class HandshakeError(Exception):
    """客户端 op 注册表与服务端 schema 漂移——拒绝生成并返回 diff。"""


def check_handshake(request: dict, schema: dict) -> None:
    client_ops = set(request.get("opList") or [])
    server_ops = schema_mod.canonical_ops(schema)
    missing_on_client = sorted(server_ops - client_ops)
    unknown_on_server = sorted(client_ops - server_ops)
    if missing_on_client or unknown_on_server:
        raise HandshakeError(json.dumps({
            "reason": "op registry drift between client and server schema",
            "missingOnClient": missing_on_client,
            "unknownToServer": unknown_on_server,
        }, ensure_ascii=False))


class _Reporter:
    def __init__(self, on_phase):
        self.on_phase = on_phase
        self.phases: list[dict] = []
        self._start = time.monotonic()

    def report(self, phase: str, detail: str = "") -> None:
        entry = {"phase": phase, "detail": detail,
                 "t": round(time.monotonic() - self._start, 2)}
        self.phases.append(entry)
        if self.on_phase:
            try:
                self.on_phase(phase, detail)
            except Exception:  # noqa: BLE001 —— 上报失败不影响生成
                pass


def run(request: dict, cfg, schema: dict, on_phase=None) -> dict:
    reporter = _Reporter(on_phase)

    try:
        check_handshake(request, schema)
    except HandshakeError as exc:
        reporter.report("handshake", "drift rejected")
        return {"status": "rejected", "ability": None,
                "report": {"issues": [{"severity": "error", "path": "handshake",
                                       "message": str(exc)}], "attempts": 0,
                           "phases": reporter.phases}}

    snapshot = request.get("battleSnapshot") or {}

    if cfg.llm_mock:
        return _run_mock(request, cfg, schema, reporter)

    # ---- 阶段一：分析战局（工具循环）----
    reporter.report("analyze", "start")
    thread = prompt_mod.analyze_messages(request)
    tool_defs = tools.tool_definitions()
    analysis = ""
    for round_no in range(max(1, cfg.agent_max_rounds)):
        message = llm.chat_tools(thread, tool_defs, cfg)
        tool_calls = message.get("tool_calls")
        if not tool_calls:
            analysis = message.get("content") or ""
            thread.append({"role": "assistant", "content": analysis})
            break
        thread.append({"role": "assistant", "content": message.get("content"),
                       "tool_calls": tool_calls})
        for call in tool_calls:
            function = call.get("function", {})
            try:
                arguments = json.loads(function.get("arguments") or "{}")
            except json.JSONDecodeError:
                arguments = {}
            result = tools.execute_tool(function.get("name", ""), arguments, snapshot, schema)
            reporter.report("analyze", f"tool {function.get('name')} round {round_no + 1}")
            thread.append({"role": "tool", "tool_call_id": call.get("id"),
                           "content": result})
    else:
        analysis = "（分析轮数达到上限，直接进入设计）"

    # ---- 阶段二：给出设计（保留工具：设计时模型需要复读组件文档确认参数能力——
    #      完全禁用工具会把"想调工具"的话当文本吐出来（实测两次）。工具子循环的
    #      出口是合法设计 JSON，轮数受 agent_max_rounds 限制；解析失败/未注册 op
    #      按分析层同款方式回喂重试）----
    reporter.report("describe", "start")
    design = None
    last_error = None
    for attempt in range(2):  # 设计层只做一次带错重试（JSON 非法 / plannedOps 未注册）
        thread.append(prompt_mod.describe_user())
        for _ in range(max(1, cfg.agent_max_rounds)):
            message = llm.chat_tools(thread, tool_defs, cfg)
            tool_calls = message.get("tool_calls")
            if not tool_calls:
                content = message.get("content") or ""
                thread.append({"role": "assistant", "content": content})
                try:
                    design = llm.extract_json(content)
                except llm.LlmError as exc:
                    last_error = str(exc)
                    thread.append({"role": "user",
                                   "content": f"输出不是合法 JSON：{exc}。重新输出设计 JSON。"})
                    design = None
                break
            thread.append({"role": "assistant", "content": message.get("content"),
                           "tool_calls": tool_calls})
            for call in tool_calls:
                function = call.get("function", {})
                try:
                    arguments = json.loads(function.get("arguments") or "{}")
                except json.JSONDecodeError:
                    arguments = {}
                result = tools.execute_tool(function.get("name", ""), arguments, snapshot, schema)
                reporter.report("describe", f"tool {function.get('name')} (attempt {attempt + 1})")
                thread.append({"role": "tool", "tool_call_id": call.get("id"),
                               "content": result})
        else:
            last_error = "tool rounds exhausted"
            thread.append({"role": "user",
                           "content": "工具轮数已达上限。不要再调用工具，直接输出设计 JSON。"})
            continue
        if design is None:
            continue
        if not isinstance(design, dict):
            last_error = "design is not a JSON object"
            thread.append({"role": "user", "content": "设计必须是单个 JSON 对象。重新输出设计 JSON。"})
            design = None
            continue
        unknown = [op for op in design.get("plannedOps", [])
                   if schema_mod.resolve_op(schema, op) is None]
        if unknown:
            thread.append({"role": "user",
                           "content": f"plannedOps 含未注册 op：{unknown}。"
                                      "请改用 list_components 里确认过的 op，重新输出设计 JSON。"})
            design = None
            continue
        break
    if not isinstance(design, dict):
        return {"status": "rejected", "ability": None,
                "report": {"issues": [{"severity": "error", "path": "design",
                                       "message": f"design phase failed to produce valid JSON "
                                                  f"(last attempt: {last_error})"}],
                           "attempts": 0, "phases": reporter.phases}}
    planned_ops = [op for op in design.get("plannedOps", [])
                   if schema_mod.resolve_op(schema, op) is not None]
    reporter.report("describe", f"plannedOps={planned_ops}")

    # ---- 语料检索：op 重叠 top-k 范例 + 全语料风格统计（语料缺失/损坏则静默降级）----
    corpus_data = corpus.get_corpus(cfg.corpus_path)
    examples = corpus.retrieve(corpus_data, planned_ops, schema) if corpus_data else []
    style = corpus.style_summary(corpus_data, schema) if corpus_data else None
    existing_ids = corpus_data["existingIds"] if corpus_data else None
    if examples:
        reporter.report("retrieve", "top: " + ", ".join(
            f"{e['abilityId']}({e['_score']})" for e in examples))

    # ---- 阶段三：生成格式化输出（独立重试；设计成果保留）----
    issues: list[dict] = []
    dto = None
    attempts = 0
    generate_thread = [
        {"role": "system", "content": prompt_mod.generate_system(schema, planned_ops, style)},
        {"role": "user", "content": prompt_mod.generate_user(request, analysis, design, examples)},
    ]
    for attempt in range(max(1, cfg.max_attempts)):
        attempts = attempt + 1
        reporter.report("generate", f"attempt {attempts}")
        try:
            content = llm.chat(generate_thread, cfg)
            candidate = llm.extract_json(content)
        except llm.LlmError as exc:
            issues = [{"severity": "error", "path": "llm", "message": str(exc)}]
            generate_thread.append({"role": "user", "content": f"{exc}。重新输出完整 JSON。"})
            continue
        ok, issues, sanitized = validator.validate(candidate, schema, existing_ids)
        reporter.report("validate", "ok" if ok else f"{sum(1 for i in issues if i['severity'] == 'error')} errors")
        if ok:
            dto = sanitized
            break
        generate_thread.append({"role": "assistant", "content": content})
        generate_thread.append({"role": "user", "content": prompt_mod.retry_feedback(issues)})

    reporter.report("done", "ok" if dto is not None else "rejected")
    if dto is None:
        return {"status": "rejected", "ability": None,
                "report": {"issues": issues, "attempts": attempts, "phases": reporter.phases,
                           "analysis": analysis, "design": design}}
    return {"status": "ok", "ability": dto,
            "report": {"issues": issues, "attempts": attempts, "phases": reporter.phases,
                       "analysis": analysis, "design": design}}


def _run_mock(request: dict, cfg, schema: dict, reporter: _Reporter) -> dict:
    """mock 模式：不调 LLM，脚本化走完三阶段（管线/客户端联调用）。"""
    reporter.report("analyze", "mock: skip tool loop")
    analysis = "mock 分析：敌方 1 单位接近，建议即时生效的全局收益技能。"
    planned_ops = ["modify_cost"]
    reporter.report("describe", f"plannedOps={planned_ops}")
    design = {"abilityName": llm._MOCK_ABILITY["abilityName"],
              "description": llm._MOCK_ABILITY["description"],
              "designNotes": "mock", "plannedOps": planned_ops}
    reporter.report("generate", "attempt 1")
    ok, issues, sanitized = validator.validate(llm._MOCK_ABILITY, schema)
    reporter.report("validate", "ok" if ok else "errors")
    reporter.report("done", "ok" if ok else "rejected")
    if not ok:
        return {"status": "rejected", "ability": None,
                "report": {"issues": issues, "attempts": 1, "phases": reporter.phases,
                           "analysis": analysis, "design": design}}
    return {"status": "ok", "ability": sanitized,
            "report": {"issues": issues, "attempts": 1, "phases": reporter.phases,
                       "analysis": analysis, "design": design}}
