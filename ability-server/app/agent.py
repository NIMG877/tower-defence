"""v2 单线程自由循环 Agent：强制 plan + 随手校验 + submit_skill 唯一关卡。

plan-agent-framework-v2 §七：跑分达标（§六验收，见 evals/reports/ACCEPTANCE_v2.md）后替换 v1 三段接力（git 保留）。

编排（§4.1）：
  握手（opList + protocolVersion 相等断言）
  thread = [system(角色+工作方式+运行时边界), user(digest + hostAssets摘要 + constraints)]
  描述模式：无快照仅 description 时免 digest 按文字描述设计，战局类工具剔除（CLI 测试入口）
  loop（预算内）:
    按路由状态选模型档（plan_pending→strong，其余→mid）与思考档（max/high/low）
    调 chat_tools
    执行工具 → 完整结果回填
    submit_skill 通过即结束；裸文本输出 → 回喂引导，不解析交付
  预算耗尽（轮数/墙钟/tokens）或 LLM 错误到头（瞬态连续4次熔断/确定性失败直降）→
  降级：交付最近一次 validate_draft ok 的
  sanitized 草稿（response.degraded=true + report.degradedReason）；无草稿宁 rejected

路由是显式两态机（§4.3）：调用前必须定 model，而"这轮做什么"只有模型自己
知道，推断式路由错了无反馈信号。
"""

from __future__ import annotations

import json
import time

from . import battle_digest, corpus, llm, schema as schema_mod, tools, validator

# 自检间隔（§4.3）：每 N 轮向线程注入一次自检提醒，防长循环悄悄跑偏。
SELF_CHECK_EVERY = 5

# §4.4：剩余墙钟低于此值不再发起实现轮（一次调用+收尾放不下），直接进降级判定。
MIN_ROUND_MARGIN_SECONDS = 30.0

STATE_PLAN_PENDING = "plan_pending"
STATE_ACT = "act"

# 黑盒回放键：随案例落盘、service 层从 API 响应剥离（客户端 DTO 不变）。
BLACKBOX_KEYS = ("trace", "thread")


class HandshakeError(Exception):
    """客户端 op 注册表与服务端 schema 漂移——拒绝生成并返回 diff。"""


def check_handshake(request: dict, schema: dict) -> None:
    """opList 集合 diff + protocolVersion 相等断言（形状漂移在入口拒绝，不静默错位）。"""
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
    client_version = request.get("protocolVersion")
    server_version = schema.get("protocolVersion")
    if client_version != server_version:
        raise HandshakeError(json.dumps({
            "reason": "protocolVersion drift between client and server schema",
            "clientVersion": client_version, "serverVersion": server_version,
        }, ensure_ascii=False))

SYSTEM = (
    "你是塔防游戏的技能生成 Agent，自主完成一次技能设计并交付可运行的 AbilityConfig。\n"
    "工作方式：\n"
    "1. 第一轮必须用 update_plan 提交计划：打算读哪些组件文档、技能设计思路（触发事件/"
    "步骤序列/目标选择）、黑板键方案（哪些键谁写谁读）。中途重大转向时用 update_plan 修订。\n"
    "2. 研究：list_components 看索引 → read_component_doc 读参数表与行为语义 → "
    "read_contract_doc 读全局契约（先看节索引，按节取全文）→ read_schema_vocab 拿触发事件/"
    "词表/字段编码。不确定的词表一律查，不要凭记忆猜。\n"
    "3. 战局：首条消息已给战局 digest 与宿主资产摘要；明细用 entity(id)/entities_at/"
    "deploy_cells_near，派生指标用 compute_cross_items（那是设计期参考）。\n"
    "4. 先例：search_skills 查相似技能 → read_skill 看完整配置与数值量级。\n"
    "5. 交付（增量构建，禁止一次性写整份配置）：start_draft 定身份与 SP → "
    "design_rules 一次立全部规则骨架（触发事件/条件/reentry 与每个 step 的 op+intent，"
    "intent 写明该步意图与数据来源，谁产出谁消费）→ 按骨架逐个 put_step 写完整参数"
    "（每步即时反馈，有 error 先修再写下一步；规则多就分多轮，不要赶）→ "
    "validate_draft 全量自检 → submit_skill 提交（无需修改可不带 config，不要重写）。"
    "submit_skill 是唯一交付出口——不要以裸文本输出配置 JSON，那不会被接受。\n"
    "运行时边界（违反会被关卡拒绝）：\n"
    "- spawn_entity.spawnIndex / fire_bullets.bulletDataIndex 只能引用 hostAssets 实际清单"
    "的下标；apply_animation_override.resources 的动画名只能取 hostAssets.animations。\n"
    "- 战局 digest 是设计期参考，运行时条件只能走黑板：select_targets（写实体列表）→ "
    "write_blackboard source=listCount（桥接计数）→ 条件读键这条链。\n"
    "- 生成配置不产出 iconKey（统一图标）。\n"
    "设计纪律：机制范围对齐 constraints.request，只实现诉求要求的机制，不附加未要求的"
    "效果（宁简勿滥）；数值量级先查语料先例再定，不凭感觉。\n"
)

# 描述模式（无战局快照）追加在 SYSTEM 尾部：研究面收窄到文档/契约/语料，
# 宿主相关引用无从校验，约定占位写法。
DESCRIPTION_MODE_NOTE = (
    "本次运行没有战局快照与宿主资产：首条用户消息只有设计描述（designBrief）与约束，"
    "按描述设计。战局类工具（compute_cross_items/entity/entities_at/deploy_cells_near）"
    "本次不可用，不要尝试调用。宿主相关引用无从校验：spawn_entity.spawnIndex / "
    "fire_bullets.bulletDataIndex 取 0 占位并在该步 intent 注明待宿主绑定；"
    "不要使用 apply_animation_override（动画名域未知）。"
)

BARE_TEXT_FEEDBACK = (
    "检测到裸文本输出。配置 JSON 不会被当文本解析——请调用 submit_skill(config) 工具交付；"
    "若尚未提交过计划，先调用 update_plan。"
)
PLAN_REQUIRED_FEEDBACK = (
    "你还没有用 update_plan 提交过计划。第一轮必须先出计划（研究哪些文档/设计思路/黑板键方案），"
    "再继续研究与设计。"
)
SELF_CHECK_FEEDBACK = (
    "自检：对照你最新一次 update_plan 的计划，当前行为是否仍在轨道上？偏离了就修订计划，"
    "在轨道上就继续。设计成型后记得 validate_draft → submit_skill。"
)
LLM_ERROR_FEEDBACK = "上一次模型调用失败：{exc}。从中断处继续。"


class _Reporter:
    """phase 上报：异步任务模式转发给轮询客户端，同时计入 report.phases 供落盘回放。"""

    def __init__(self, on_phase, on_event=None):
        self.on_phase = on_phase
        self.on_event = on_event
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

    def emit(self, kind: str, data: dict) -> None:
        """富事件外发（思考原文/工具调用与结果）：进程内实时观测用（CLI）。
        不计入 phases——回放素材已由 trace 覆盖，不双写。"""
        if self.on_event:
            try:
                self.on_event(kind, data)
            except Exception:  # noqa: BLE001 —— 同 report：观测失败不影响生成
                pass


def route_model(state: str, cfg) -> str | None:
    """两态路由（§4.3）：plan_pending→strong（错误代价高、token 量小）；
    其余一切轮次→mid（产出体量大、有机器反馈兜底）。None 回退默认档。"""
    if state == STATE_PLAN_PENDING:
        return cfg.llm_model_strong or cfg.llm_model
    return cfg.llm_model_mid or cfg.llm_model


def route_effort(state: str, ctx: "tools.ToolContext", cfg) -> str | None:
    """思考强度分档（与 route_model 同构）：规划轮深想（max）；研究与设计轮
    次档（high）；草稿过闸后的修复/提交是对定稿的机械转写，轻档（low）。"""
    if state == STATE_PLAN_PENDING:
        return cfg.llm_effort_plan
    if ctx.draft is not None:
        return cfg.llm_effort_draft
    return cfg.llm_effort_act


def trace_entry(round_no: int, model: str | None, call_started: float,
                **extra) -> dict:
    """单条黑盒记录：耗时与调用方上下文共用构造（错误/成功两路径共用）。"""
    return {"round": round_no, "model": model,
            "durationS": round(time.monotonic() - call_started, 2), **extra}


def _assistant_entry(message: dict, cfg, tool_calls: list | None = None) -> dict:
    """assistant 条目构造：保留式思考开启时，思考原文随条目完整回传（官方约定——
    工具循环中必须未修改地保留 reasoning_content，保证推理连续性与缓存命中）；
    关闭时不回传（服务端已清除，回传无意义）。trace 另存思考原文不受影响。"""
    entry = {"role": "assistant", "content": message.get("content")}
    if cfg.llm_preserve_thinking and message.get("reasoning_content"):
        entry["reasoning_content"] = message["reasoning_content"]
    if tool_calls is not None:
        entry["tool_calls"] = tool_calls
    return entry


def run(request: dict, cfg, schema: dict, on_phase=None, on_event=None) -> dict:
    reporter = _Reporter(on_phase, on_event)
    started = time.monotonic()
    # 黑盒回放：trace 记每次模型调用的耗时/分项 tokens/思考原文；thread 记完整
    # 对话线程——原始工具参数随 assistant.tool_calls 原文在列，不另存副本。
    trace: list[dict] = []
    thread: list[dict] | None = None

    def finish(status: str, ability, issues: list[dict], rounds: int, ctx,
               tokens: dict | None = None,
               degraded_reason: str | None = None) -> dict:
        reporter.report("done", status)
        report = {"issues": issues, "rounds": rounds,
                  "phases": reporter.phases,
                  "plan": ctx.plan if ctx else None}
        if tokens is not None:
            report["tokens"] = tokens
        if degraded_reason is not None:
            report["degradedReason"] = degraded_reason
        response = {"status": status, "ability": ability, "report": report}
        if degraded_reason is not None:
            response["degraded"] = True
        if trace:
            response["trace"] = trace
        if thread is not None:
            response["thread"] = thread
        return response

    try:
        check_handshake(request, schema)
    except HandshakeError as exc:
        reporter.report("handshake", "drift rejected")
        return finish("rejected", None,
                      [{"severity": "error", "path": "handshake", "message": str(exc)}], 0, None)

    if cfg.llm_mock:
        return _run_mock(request, cfg, schema, reporter, finish)

    usage_start = llm.usage_snapshot()
    # 描述模式：无快照但带 description——免快照按文字描述设计（CLI 测试入口）。
    # 有快照走原路径（description 忽略）；两者皆无保持原闸（build 抛 ValueError）。
    description = (request.get("description") or "").strip()
    description_mode = not request.get("battleSnapshot") and bool(description)
    if description_mode:
        digest = None
    else:
        try:
            digest = battle_digest.build(request.get("battleSnapshot") or {})
        except ValueError as exc:
            return finish("rejected", None,
                          [{"severity": "error", "path": "battleSnapshot", "message": str(exc)}],
                          0, None)

    corpus_data = corpus.get_corpus(cfg.db_path)
    ctx = tools.ToolContext(request, cfg, schema, corpus_data)
    thread = [
        {"role": "system",
         "content": (SYSTEM + DESCRIPTION_MODE_NOTE) if description_mode else SYSTEM},
        {"role": "user",
         "content": _description_first_user(description, ctx) if description_mode
         else _first_user(digest, ctx)},
    ]
    tool_defs = tools.tool_definitions()
    if description_mode:
        tool_defs = [d for d in tool_defs
                     if d["function"]["name"] not in tools.SNAPSHOT_TOOLS]
    state = STATE_PLAN_PENDING
    issues: list[dict] = []
    consecutive_llm_errors = 0

    def budget_used() -> tuple[float, int]:
        """剩余墙钟与已耗 tokens（§4.4 预算闸的两个检查维度）。"""
        tokens = llm.usage_delta(usage_start)
        remaining = cfg.agent_max_wall_seconds - (time.monotonic() - started)
        return remaining, tokens["prompt_tokens"] + tokens["completion_tokens"]

    def degrade_or_reject(reason: str, rounds: int) -> dict:
        """预算耗尽出口：有 validate_draft 全绿的 sanitized 草稿则降级交付
        （status ok + degraded 标记，服务端不入缓存）；无草稿宁 rejected。"""
        tokens = llm.usage_delta(usage_start)
        if ctx.draft is not None:
            reporter.report("degraded", f"budget exhausted ({reason}); delivering last validated draft")
            return finish("ok", ctx.draft, issues, rounds, ctx, tokens,
                          degraded_reason=reason)
        reporter.report("act", f"budget exhausted ({reason}); no validated draft, rejecting")
        return finish("rejected", None, issues, rounds, ctx, tokens)

    for round_no in range(1, cfg.agent_max_rounds + 1):
        remaining_wall, used_tokens = budget_used()
        if remaining_wall < MIN_ROUND_MARGIN_SECONDS:
            return degrade_or_reject("wall_budget_exhausted", round_no - 1)
        if used_tokens > cfg.agent_max_total_tokens:
            return degrade_or_reject("token_budget_exhausted", round_no - 1)
        model = route_model(state, cfg)
        effort = route_effort(state, ctx, cfg)
        # 轮次上报落在对外 phase 词表（§4.1）：plan_pending 态计为 plan。
        reporter.report("plan" if state == STATE_PLAN_PENDING else "act",
                        f"round {round_no} model={model} effort={effort}")
        # 单次调用 timeout 收紧至剩余预算（§4.4）：挂起中的调用不受轮间检查约束，
        # 不收紧会穿破墙钟死线。能走到这里 remaining ≥ 30s，减 5s 留收尾余量。
        call_timeout = min(cfg.llm_timeout_seconds, remaining_wall - 5.0)
        call_started = time.monotonic()
        usage_before = llm.usage_snapshot()
        try:
            message = llm.chat_tools(thread, tool_defs, cfg, model=model,
                                     timeout=call_timeout, effort=effort)
        except llm.LlmError as exc:
            # 错误分类处置（llm.py 按状态码标 retryable）：瞬态错误（网络/429/5xx）
            # 指数退避重试，连续 4 次判定外部故障熔断；确定性错误（上下文超限/鉴权/
            # 配置缺失）重发同请求只会同结果，跳过重试直接进降级判定。
            consecutive_llm_errors += 1
            reporter.report("act", f"llm error ({consecutive_llm_errors}): {exc}")
            trace.append(trace_entry(round_no, model, call_started, error=str(exc),
                                     retryable=exc.retryable))
            if not exc.retryable or consecutive_llm_errors >= 4:
                reason = ("llm_error_circuit_breaker" if exc.retryable
                          else "llm_error_deterministic")
                reporter.report("act", f"{reason}; aborting")
                return degrade_or_reject(reason, round_no)
            thread.append({"role": "user", "content": LLM_ERROR_FEEDBACK.format(exc=exc)})
            time.sleep(min(5 * 2 ** (consecutive_llm_errors - 1), 30))
            continue
        consecutive_llm_errors = 0

        tool_calls = message.get("tool_calls")
        entry = trace_entry(round_no, model, call_started, effort=effort,
                            tokens=llm.usage_delta(usage_before),
                            thinking=message.get("reasoning_content") or "")
        trace.append(entry)
        reporter.emit("thinking", entry)
        if not tool_calls:
            thread.append(_assistant_entry(message, cfg))
            nudge = PLAN_REQUIRED_FEEDBACK if ctx.plan is None else BARE_TEXT_FEEDBACK
            thread.append({"role": "user", "content": nudge})
            reporter.report("act", "bare text; nudged")
            state = STATE_ACT
            continue

        thread.append(_assistant_entry(message, cfg, tool_calls))
        planned_this_round = False
        for call in tool_calls:
            function = call.get("function", {})
            name = function.get("name", "")
            raw_arguments = function.get("arguments") or ""
            reporter.emit("tool_call", {"round": round_no, "name": name,
                                        "arguments": raw_arguments})
            try:
                arguments = json.loads(raw_arguments or "{}")
                if not isinstance(arguments, dict):
                    raise ValueError("arguments must be a JSON object")
            except ValueError as exc:
                # 畸形参数（非法 JSON / 非 object）不派发：回喂真因与原文截片，
                # 模型据此重发——掩盖真因只会让它收到误导性的缺参类下游错误。
                # JSONDecodeError 是 ValueError 子类，一并在此捕获。
                result = json.dumps({"error": f"malformed tool arguments ({exc}); "
                                              "resend the tool call with corrected arguments",
                                     "rawArguments": raw_arguments[:2000]},
                                    ensure_ascii=False)
            else:
                result = tools.execute_tool(ctx, name, arguments)
            reporter.report(tools.phase_for(name), f"tool {name}")
            reporter.emit("tool_result", {"round": round_no, "name": name,
                                          "result": result})
            thread.append({"role": "tool", "tool_call_id": call.get("id"),
                           "content": result})

            if name == "submit_skill":
                try:
                    outcome = json.loads(result)
                except json.JSONDecodeError:
                    outcome = {}
                if outcome.get("accepted"):
                    issues = outcome.get("issues") or []
                    return finish("ok", ctx.submitted, issues, round_no, ctx,
                                  llm.usage_delta(usage_start))
                issues = outcome.get("issues") or issues
            elif name == "update_plan" and _result_flag(result, "ok"):
                planned_this_round = True

        state = STATE_PLAN_PENDING if planned_this_round else STATE_ACT
        if ctx.plan is None:
            thread.append({"role": "user", "content": PLAN_REQUIRED_FEEDBACK})
        elif round_no % SELF_CHECK_EVERY == 0:
            thread.append({"role": "user", "content": SELF_CHECK_FEEDBACK})

    return degrade_or_reject("round_budget_exhausted", cfg.agent_max_rounds)


def _run_mock(request: dict, cfg, schema: dict, reporter: _Reporter, finish) -> dict:
    """mock 模式：不调 LLM，脚本化 plan→submit（service 层 v2 管线/客户端联调用）。"""
    reporter.report("plan", "mock: scripted plan")
    reporter.report("submit", "mock: submit_skill")
    ok, issues, sanitized = validator.validate(MOCK_ABILITY, schema,
                                               host_assets=request.get("hostAssets"))
    return finish("ok" if ok else "rejected", sanitized if ok else None, issues, 1, None)


# mock 模式样例：modify_cost 探针技能（无 key 跑通全管线；效果肉眼可见）。
MOCK_ABILITY = {
    "abilityId": "gen_mock_1",
    "abilityName": "模拟生成技能",
    "description": "mock 管线验证：部署即获得 99 点部署费用",
    "sp": {"totalSp": 0, "initialSp": 0, "chargeNum": 1, "abilityAmount": 0,
           "recoverMode": "Natural", "consumeMode": "NoConsume", "openMode": "Auto"},
    "rules": [
        {"triggers": [{"triggerEvent": "OnInitialize", "groups": []}],
         "reentry": "IgnoreWhileRunning",
         "steps": [{"op": "modify_cost",
                    "args": {"entries": [{"key": "amount", "value": "99", "type": "Int",
                                          "fromBlackboard": False}]}}]},
    ],
}


def _result_flag(result_json: str, key: str) -> bool:
    try:
        return json.loads(result_json).get(key) is True
    except json.JSONDecodeError:
        return False


def _description_first_user(description: str, ctx: tools.ToolContext) -> str:
    """描述模式首条消息：设计描述 + 约束（无战局 digest 与宿主摘要）。"""
    return json.dumps({"mode": "description", "designBrief": description,
                       "constraints": ctx.constraints},
                      ensure_ascii=False, indent=1)


def _first_user(digest: dict, ctx: tools.ToolContext) -> str:
    """信息分档（§4.1）：digest 直入；hostAssets 决策字段直入、参考清单计数+样例；
    constraints 原文。快照不全量直入（payload 大小与 prompt 大小是两回事）。"""
    host = ctx.host_assets
    decision = {k: host[k] for k in ("job", "subJob", "cost", "baseAttackTime",
                                     "damageType", "blockOccupation", "targetPriority",
                                     "visionRadius", "visionRange") if k in host}
    animations = host.get("animations") or {}
    named = animations.get("named") or []
    groups = animations.get("groups") or []
    bullets = host.get("bullets") or []
    spawnables = host.get("canSpawnEntities") or []
    host_summary = {
        "decision": decision,
        "encoding": tools.FIELD_SEMANTICS,
        "animations": {"namedCount": len(named), "groupsCount": len(groups),
                       "namedSample": named[:5], "groupsSample": groups[:5]},
        "bullets": {"count": len(bullets), "sample": bullets[:6]},
        "canSpawnEntities": spawnables if len(spawnables) <= 4
        else {"count": len(spawnables), "sample": spawnables[:2]},
    }
    if ctx.host_skills:
        host_summary["skills"] = [
            {"abilityId": s.get("abilityId"), "name": s.get("abilityName"),
             "sp": (s.get("sp") or {}).get("totalSp"),
             "ops": sorted(corpus.skill_ops(s))}
            for s in ctx.host_skills.values()]
    return json.dumps({"battleDigest": digest, "hostAssets": host_summary,
                       "constraints": ctx.constraints},
                      ensure_ascii=False, indent=1)
