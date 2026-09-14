"""agent 循环骨架测试：两态路由 / plan 强制 / submit 关卡 / 裸文本回喂 / 握手。

真实路径测试用脚本化 chat_tools（不调网络）；模型分层经调用参数断言。
"""

import json

from app import agent, llm, schema as schema_mod
from app.config import Config

SCHEMA = schema_mod.load_schema()


def make_request(**overrides) -> dict:
    request = {
        "protocolVersion": SCHEMA["protocolVersion"],
        "opList": sorted(schema_mod.canonical_ops(SCHEMA)),
        "battleSnapshot": {
            "selfId": "c-1", "mapI": 8, "mapJ": 9,
            "canSetHigher": ["4,5"], "canSetLower": ["3,4"],
            "entities": [
                {"id": "c-1", "camp": 1, "isStatic": False, "hp": 1500, "maxHp": 1500,
                 "hpRate": 1.0, "pos": {"x": 4, "y": 4}, "attack": 500, "defence": 100,
                 "magicRes": 10, "job": 1, "label": None, "massLevel": 2},
                {"id": "m-1", "camp": 2, "isStatic": False, "hp": 400, "maxHp": 800,
                 "hpRate": 0.5, "pos": {"x": 5, "y": 4}, "attack": 120, "defence": 30,
                 "magicRes": 0, "job": -1, "label": "slime", "massLevel": 1},
            ],
        },
        "hostAssets": {"job": 1, "cost": 15, "damageType": 0,
                       "canSpawnEntityIds": [], "bulletCount": 0, "iconKeys": []},
        "constraints": {"request": "给一个回费技能"},
    }
    request.update(overrides)
    return request


def tiered_cfg(**overrides) -> Config:
    # 循环测试默认 llm_mock=False（直接 patch llm.chat_tools）；mock 路径测试显式传 True。
    # weak 钉 None：摘要兜底不被无关用例触发，digest 测试显式开。
    base = dict(log_dir=None, corpus_path=None, llm_mock=False,
                llm_model="base-model", llm_model_strong="strong-model",
                llm_model_mid="mid-model", llm_model_weak=None)
    base.update(overrides)
    return Config(**base)


def tool_call(call_id: str, name: str, arguments: dict) -> dict:
    return {"role": "assistant", "content": None,
            "tool_calls": [{"id": call_id, "type": "function",
                            "function": {"name": name,
                                         "arguments": json.dumps(arguments)}}]}


def submit_config() -> dict:
    return {
        "abilityId": "gen_a2", "abilityName": "回费", "description": "d",
        "sp": {"totalSp": 0, "consumeMode": "NoConsume", "openMode": "Auto"},
        "rules": [{"triggers": [{"triggerEvent": "OnInitialize", "groups": []}],
                   "steps": [{"op": "modify_cost",
                              "args": {"entries": [{"key": "amount", "value": "5",
                                                    "type": "Int", "fromBlackboard": False}]}}]}],
    }


# ---------- 路由两态机（纯函数单测，§八） ----------

def test_route_model_two_state_machine():
    cfg = tiered_cfg()
    assert agent.route_model(agent.STATE_PLAN_PENDING, cfg) == "strong-model"
    assert agent.route_model(agent.STATE_ACT, cfg) == "mid-model"
    # 档位配置为 None 时回退默认模型
    plain = Config(llm_mock=True, log_dir=None, corpus_path=None,
                   llm_model_strong=None, llm_model_mid=None)
    assert agent.route_model(agent.STATE_PLAN_PENDING, plain) == plain.llm_model
    assert agent.route_model(agent.STATE_ACT, plain) == plain.llm_model


# ---------- 握手 ----------

def test_protocol_version_drift_rejected():
    response = agent.run(make_request(protocolVersion=99), tiered_cfg(), SCHEMA,
                          on_phase=None)
    assert response["status"] == "rejected"
    assert "protocolVersion" in response["report"]["issues"][0]["message"]
    assert response["report"]["rounds"] == 0


def test_snapshot_without_self_record_rejected():
    request = make_request()
    request["battleSnapshot"]["selfId"] = "ghost"
    response = agent.run(request, tiered_cfg(), SCHEMA)
    assert response["status"] == "rejected"
    assert "selfId" in response["report"]["issues"][0]["message"]


# ---------- 主循环 ----------

def test_ok_path_plan_research_submit(monkeypatch):
    """计划(strong) → 研究(mid) → 提交(mid)：路由随两态机切换，submit 通过即结束。"""
    models_seen = []
    threads = []

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None):
        models_seen.append(model)
        threads.append(json.dumps(messages, ensure_ascii=False))
        if len(models_seen) == 1:
            return tool_call("c1", "update_plan",
                             {"plan": "1. 读 modify_cost 文档 2. OnInitialize 直接 modify_cost"})
        if len(models_seen) == 2:
            return tool_call("c2", "read_component_doc", {"name": "modify_cost"})
        return tool_call("c3", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(), SCHEMA)

    assert response["status"] == "ok", response["report"]
    assert models_seen == ["strong-model", "strong-model", "mid-model"]
    # 首轮 update_plan 成功后下一轮仍是 plan_pending（上一轮调用了 update_plan）
    assert response["ability"]["abilityId"] == "gen_a2"
    assert "iconKey" not in response["ability"]
    assert response["report"]["rounds"] == 3
    assert response["report"]["plan"].startswith("1.")
    phases = [p["phase"] for p in response["report"]["phases"]]
    assert "plan" in phases and "review" not in phases
    assert "submit" in phases


def test_plan_enforced_before_research(monkeypatch):
    """第一轮跳过计划直接调研究工具 → 回喂 PLAN_REQUIRED，补交计划后才继续。"""
    state = {"n": 0}

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None):
        state["n"] += 1
        if state["n"] == 1:  # 违规：没出计划就调研究工具
            return tool_call("c1", "list_components", {})
        if state["n"] == 2:
            # 上一轮工具结果之后应已收到计划强制提醒
            assert any(agent.PLAN_REQUIRED_FEEDBACK in m.get("content", "")
                       for m in messages if m.get("role") == "user")
            return tool_call("c2", "update_plan", {"plan": "补交计划"})
        return tool_call("c3", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(), SCHEMA)
    assert response["status"] == "ok", response["report"]
    assert response["report"]["rounds"] == 3


def test_bare_text_output_nudged_not_parsed(monkeypatch):
    """裸文本不解析交付，回喂引导 submit_skill。"""
    state = {"n": 0}

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None):
        state["n"] += 1
        if state["n"] == 1:
            return tool_call("c1", "update_plan", {"plan": "计划"})
        if state["n"] == 2:
            return {"role": "assistant",
                    "content": json.dumps(submit_config(), ensure_ascii=False)}
        # 裸文本之后应收到引导回喂
        assert any(agent.BARE_TEXT_FEEDBACK in m.get("content", "")
                   for m in messages if m.get("role") == "user")
        return tool_call("c3", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(), SCHEMA)
    assert response["status"] == "ok", response["report"]
    assert response["report"]["rounds"] == 3


def test_submit_rejection_continues_loop(monkeypatch):
    """submit 被关卡拒绝 → issues 回喂，修复后重新提交。"""
    state = {"n": 0}

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None):
        state["n"] += 1
        if state["n"] == 1:
            return tool_call("c1", "update_plan", {"plan": "计划"})
        if state["n"] == 2:
            bad = submit_config()
            bad["rules"][0]["steps"][0]["op"] = "make_big_explosion"
            return tool_call("c2", "submit_skill", {"config": bad})
        return tool_call("c3", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(), SCHEMA)
    assert response["status"] == "ok", response["report"]
    assert response["report"]["rounds"] == 3
    # 关卡拒绝后循环继续：最终交付的是修复后的配置
    assert response["ability"]["abilityId"] == "gen_a2"


def test_self_check_reminder_every_five_rounds(monkeypatch):
    state = {"n": 0}

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None):
        state["n"] += 1
        if state["n"] == 1:
            return tool_call("c1", "update_plan", {"plan": "计划"})
        if state["n"] <= 5:
            return tool_call(f"c{state['n']}", "read_component_doc", {"name": "delay"})
        # 第 6 轮的线程里应能看到第 5 轮结束注入的自检提醒
        assert any(agent.SELF_CHECK_FEEDBACK in m.get("content", "")
                   for m in messages if m.get("role") == "user")
        return tool_call(f"c{state['n']}", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(), SCHEMA)
    assert response["status"] == "ok", response["report"]
    assert response["report"]["rounds"] == 6


def test_plan_update_limit_stops_spam(monkeypatch):
    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None):
        n = sum(1 for m in messages if m.get("role") == "assistant") + 1  # 1-based 轮次
        if n <= 2:  # 连续两轮提交计划（第二次应被限额拒绝）
            return tool_call(f"c{n}", "update_plan", {"plan": f"计划 v{n}"})
        return tool_call(f"c{n}", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(agent_max_plan_updates=1), SCHEMA)
    assert response["status"] == "ok", response["report"]
    # 第二次修订被限额拒绝：计划停留在第一版
    assert response["report"]["plan"] == "计划 v1"


def test_max_rounds_exhausted_rejected(monkeypatch):
    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None):
        return tool_call("loop", "read_component_doc", {"name": "delay"})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    cfg = tiered_cfg(agent_max_rounds=3)
    response = agent.run(make_request(), cfg, SCHEMA)
    assert response["status"] == "rejected"
    assert response["report"]["rounds"] == 3
    assert response["ability"] is None


def test_llm_error_keeps_loop_alive(monkeypatch):
    calls = {"n": 0}

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None):
        calls["n"] += 1
        if calls["n"] == 1:
            raise llm.LlmError("network down")
        if calls["n"] == 2:
            return tool_call("c2", "update_plan", {"plan": "计划"})
        return tool_call("c3", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(), SCHEMA)
    assert response["status"] == "ok", response["report"]
    assert calls["n"] == 3
    phases = [p for p in response["report"]["phases"] if "llm error" in p["detail"]]
    assert phases  # 错误经 phase 可见（Unity 日志滚动）


# ---------- mock 路径与 service 接线 ----------

def test_mock_mode_scripted_plan_submit():
    """llm_mock=True 时 agent 走脚本化 plan→submit，不触 LLM。"""
    response = agent.run(make_request(), tiered_cfg(llm_mock=True), SCHEMA)
    assert response["status"] == "ok", response["report"]
    assert response["ability"]["abilityId"] == "gen_mock_1"
    assert "iconKey" not in response["ability"]
    phases = [p["phase"] for p in response["report"]["phases"]]
    assert "plan" in phases and "submit" in phases and "done" in phases


def test_service_routes_to_agent():
    """service 统一路由 agent（v1 编排已删，git 保留）。"""
    from app.service import GenerateService

    service = GenerateService(Config(llm_mock=True, log_dir=None, corpus_path=None))
    response = service.generate(make_request())
    assert response["status"] == "ok", response["report"]
    phases = [p["phase"] for p in response["report"]["phases"]]
    assert "submit" in phases


# ---------- 预算闸与降级协议（M4 §4.4） ----------

def _scripted_plan_and_draft() -> dict:
    """一条消息内同时提交计划与通过校验的草稿（多工具单轮，供降级测试用）。"""
    return {"role": "assistant", "content": None, "tool_calls": [
        {"id": "c1", "type": "function",
         "function": {"name": "update_plan", "arguments": json.dumps({"plan": "计划"})}},
        {"id": "c2", "type": "function",
         "function": {"name": "validate_draft", "arguments": json.dumps({"config": submit_config()})}},
    ]}


def test_breaker_with_validated_draft_degrades(monkeypatch):
    """连续 4 次 LLM 错误熔断：有 validate_draft 全绿草稿 → 降级交付而非拒绝。"""
    monkeypatch.setattr(agent.time, "sleep", lambda *_: None)  # 退避不真睡
    state = {"n": 0}

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None):
        state["n"] += 1
        if state["n"] == 1:
            return _scripted_plan_and_draft()
        raise llm.LlmError("quota exhausted")

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(), SCHEMA)
    assert response["status"] == "ok", response["report"]
    assert response["degraded"] is True
    assert response["report"]["degradedReason"] == "llm_error_circuit_breaker"
    assert response["ability"]["abilityId"] == "gen_a2"
    phases = [p["phase"] for p in response["report"]["phases"]]
    assert "degraded" in phases


def test_rounds_exhausted_degrades_to_last_valid_draft(monkeypatch):
    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None):
        n = sum(1 for m in messages if m.get("role") == "assistant") + 1
        if n == 1:
            return _scripted_plan_and_draft()
        return tool_call(f"c{n}", "read_component_doc", {"name": "delay"})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(agent_max_rounds=3), SCHEMA)
    assert response["status"] == "ok", response["report"]
    assert response["degraded"] is True
    assert response["report"]["degradedReason"] == "round_budget_exhausted"
    assert response["report"]["rounds"] == 3
    assert response["ability"]["abilityId"] == "gen_a2"


def test_rounds_exhausted_without_draft_rejected(monkeypatch):
    """无草稿宁 rejected——绝不交付未过校验的配置（§4.4）。"""
    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None):
        n = sum(1 for m in messages if m.get("role") == "assistant") + 1
        return tool_call(f"c{n}", "read_component_doc", {"name": "delay"})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(agent_max_rounds=2), SCHEMA)
    assert response["status"] == "rejected"
    assert "degraded" not in response
    assert response["ability"] is None


def test_wall_budget_stops_before_first_llm_call(monkeypatch):
    calls = {"n": 0}

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None):
        calls["n"] += 1
        return tool_call("c1", "update_plan", {"plan": "计划"})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(agent_max_wall_seconds=0), SCHEMA)
    assert response["status"] == "rejected"
    assert calls["n"] == 0
    assert response["report"]["rounds"] == 0


def test_token_budget_degrades_after_validated_draft(monkeypatch):
    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None):
        llm._accumulate({"usage": {"prompt_tokens": 600, "completion_tokens": 0}})
        return _scripted_plan_and_draft()

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(agent_max_total_tokens=500), SCHEMA)
    assert response["status"] == "ok", response["report"]
    assert response["degraded"] is True
    assert response["report"]["degradedReason"] == "token_budget_exhausted"


def test_call_timeout_tightened_to_remaining_wall_budget(monkeypatch):
    captured = []

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None):
        captured.append(timeout)
        if len(captured) == 1:
            return tool_call("c1", "update_plan", {"plan": "计划"})
        return tool_call("c2", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    cfg = tiered_cfg(llm_timeout_seconds=600, agent_max_wall_seconds=100)
    response = agent.run(make_request(), cfg, SCHEMA)
    assert response["status"] == "ok", response["report"]
    assert 90 <= captured[0] <= 95  # min(600, 100 - 已耗~0 - 5)


def test_tool_result_truncated_and_full_text_recorded(monkeypatch):
    """>4k chars 工具结果：截断回填 + 原文入 tool_outputs（报告落盘回放，§4.4）。"""
    from app import tools

    ctx = tools.ToolContext(make_request(), tiered_cfg(), SCHEMA, None)
    big = {"doc": "x" * 10_000}
    monkeypatch.setattr(tools, "_dispatch", lambda c, name, args: big)
    out = tools.execute_tool(ctx, "read_contract_doc", {})
    assert len(out) < tools.TOOL_RESULT_CHAR_LIMIT + 200
    assert "truncated" in out
    assert ctx.tool_outputs[0]["tool"] == "read_contract_doc"
    assert ctx.tool_outputs[0]["chars"] == len(json.dumps(big, ensure_ascii=False))


# ---------- weak 档摘要兜底（M4 §4.4，三档模型实验） ----------

def test_weak_model_digests_oversized_tool_result(monkeypatch):
    """weak 档配置时：截断背闸触发 → weak 摘要替代盲截断回喂，全文仍入报告。"""
    from app import tools

    big = {"doc": "x" * 10_000}
    real_execute = tools.execute_tool

    def fake_execute(ctx, name, args):
        if name != "read_contract_doc":  # 其余工具走真实现（update_plan/submit 语义不能劫持）
            return real_execute(ctx, name, args)
        text = json.dumps(big, ensure_ascii=False)
        ctx.tool_outputs.append({"tool": name, "chars": len(text), "full": text})
        return text[:tools.TOOL_RESULT_CHAR_LIMIT] + "...[truncated]"

    chat_calls = []

    def fake_chat(messages, cfg, model=None, timeout=None):
        chat_calls.append({"model": model, "user_chars": len(messages[1]["content"]),
                           "timeout": timeout})
        return '{"doc": "compact"}'

    monkeypatch.setattr(tools, "execute_tool", fake_execute)
    monkeypatch.setattr(llm, "chat", fake_chat)

    state = {"n": 0}

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None):
        state["n"] += 1
        if state["n"] == 1:
            return tool_call("c1", "update_plan", {"plan": "计划"})
        if state["n"] == 2:
            return tool_call("c2", "read_contract_doc", {})
        return tool_call("c3", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    cfg = tiered_cfg(llm_model_weak="weak-model")
    response = agent.run(make_request(), cfg, SCHEMA)

    assert chat_calls == [{"model": "weak-model", "timeout": 60.0,
                           "user_chars": len(json.dumps(big, ensure_ascii=False))}]
    assert response["status"] == "ok", response["report"]
    phases = [p["detail"] for p in response["report"]["phases"]]
    assert any("digested via weak-model" in d for d in phases)
    assert response["report"]["toolOutputs"][0]["chars"] == len(json.dumps(big, ensure_ascii=False))


def test_weak_digest_failure_falls_back_to_truncation(monkeypatch):
    from app import tools

    big = {"doc": "x" * 10_000}
    real_execute = tools.execute_tool

    def fake_execute(ctx, name, args):
        if name != "read_contract_doc":
            return real_execute(ctx, name, args)
        text = json.dumps(big, ensure_ascii=False)
        ctx.tool_outputs.append({"tool": name, "chars": len(text), "full": text})
        return text[:tools.TOOL_RESULT_CHAR_LIMIT] + "...[truncated]"

    monkeypatch.setattr(tools, "execute_tool", fake_execute)
    monkeypatch.setattr(llm, "chat",
                        lambda *a, **k: (_ for _ in ()).throw(llm.LlmError("weak down")))

    state = {"n": 0}

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None):
        state["n"] += 1
        if state["n"] == 1:
            return tool_call("c1", "update_plan", {"plan": "计划"})
        if state["n"] == 2:
            return tool_call("c2", "read_contract_doc", {})
        return tool_call("c3", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    cfg = tiered_cfg(llm_model_weak="weak-model")
    response = agent.run(make_request(), cfg, SCHEMA)

    assert response["status"] == "ok", response["report"]
    phases = [p["detail"] for p in response["report"]["phases"]]
    assert any("digest failed; truncation fallback" in d for d in phases)


def test_weak_none_keeps_blind_truncation(monkeypatch):
    """weak=None（默认）：截断背闸只盲截断，不发起摘要调用。"""
    from app import tools

    chat_calls = []
    monkeypatch.setattr(llm, "chat", lambda *a, **k: chat_calls.append(1))

    state = {"n": 0}

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None):
        state["n"] += 1
        if state["n"] == 1:
            return tool_call("c1", "update_plan", {"plan": "计划"})
        if state["n"] == 2:
            return tool_call("c2", "read_contract_doc", {})
        return tool_call("c3", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(), SCHEMA)  # weak=None
    assert response["status"] == "ok", response["report"]
    assert chat_calls == []
