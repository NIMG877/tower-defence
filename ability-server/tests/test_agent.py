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
    # 落盘类配置基座全关：测试不写真实 logs/ 与 out/，要写时显式传路径。
    base = dict(log_dir=None, db_path=None, out_dir=None, llm_mock=False,
                llm_model="base-model", llm_model_strong="strong-model",
                llm_model_mid="mid-model")
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
    plain = Config(llm_mock=True, log_dir=None, db_path=None,
                   llm_model_strong=None, llm_model_mid=None)
    assert agent.route_model(agent.STATE_PLAN_PENDING, plain) == plain.llm_model
    assert agent.route_model(agent.STATE_ACT, plain) == plain.llm_model


def test_route_effort_three_tiers():
    """思考强度按轮分档：规划轮 max → 研究与设计 high → 草稿过闸后 low。"""
    from app import tools
    cfg = tiered_cfg()
    ctx = tools.ToolContext(make_request(), cfg, SCHEMA, None)
    assert agent.route_effort(agent.STATE_PLAN_PENDING, ctx, cfg) == "max"
    assert agent.route_effort(agent.STATE_ACT, ctx, cfg) == "high"
    ctx.draft = {"abilityId": "d"}
    assert agent.route_effort(agent.STATE_ACT, ctx, cfg) == "low"
    # 档位配置为 None → 不传 reasoning_effort，走 API 默认
    plain = tiered_cfg(llm_effort_act=None)
    ctx.draft = None
    assert agent.route_effort(agent.STATE_ACT, ctx, plain) is None


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

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
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

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
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

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
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

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
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

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
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
    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
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
    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        return tool_call("loop", "read_component_doc", {"name": "delay"})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    cfg = tiered_cfg(agent_max_rounds=3)
    response = agent.run(make_request(), cfg, SCHEMA)
    assert response["status"] == "rejected"
    assert response["report"]["rounds"] == 3
    assert response["ability"] is None


def test_llm_error_keeps_loop_alive(monkeypatch):
    calls = {"n": 0}

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
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


def test_deterministic_llm_error_aborts_without_retry(monkeypatch):
    """确定性 LLM 错误（上下文超限类）：跳过退避重试，直接进降级判定。"""
    calls = {"n": 0}

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        calls["n"] += 1
        if calls["n"] == 1:
            return _scripted_plan_and_draft()
        raise llm.LlmError("context length exceeded", retryable=False)

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(), SCHEMA)
    assert response["status"] == "ok", response["report"]
    assert response["degraded"] is True
    assert response["report"]["degradedReason"] == "llm_error_deterministic"
    assert calls["n"] == 2  # 第二轮失败后不再重试
    assert response["trace"][-1]["retryable"] is False
    assert response["ability"]["abilityId"] == "gen_a2"


def test_deterministic_llm_error_without_draft_rejected(monkeypatch):
    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        raise llm.LlmError("invalid api key", retryable=False)

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(), SCHEMA)
    assert response["status"] == "rejected"
    assert response["report"]["rounds"] == 1
    assert response["ability"] is None


def test_reasoning_content_preserved_across_rounds(monkeypatch):
    """官方 preserved thinking 约定：assistant 条目带 reasoning_content 原文回传。"""

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        if len(messages) > 2:  # 第二轮：上轮 assistant 条目应带思考原文
            assistant = messages[2]
            assert assistant["role"] == "assistant"
            assert assistant["reasoning_content"] == "想法A"
            return tool_call("c2", "submit_skill", {"config": submit_config()})
        return {**tool_call("c1", "update_plan", {"plan": "计划"}),
                "reasoning_content": "想法A"}

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(), SCHEMA)
    assert response["status"] == "ok", response["report"]
    assert response["trace"][0]["thinking"] == "想法A"


def test_preserve_thinking_off_drops_reasoning_echo(monkeypatch):
    """关闭保留式思考：assistant 条目不回传 reasoning_content（trace 仍记）。"""

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        if len(messages) > 2:
            assistant = messages[2]
            assert "reasoning_content" not in assistant
            return tool_call("c2", "submit_skill", {"config": submit_config()})
        return {**tool_call("c1", "update_plan", {"plan": "计划"}),
                "reasoning_content": "想法A"}

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(llm_preserve_thinking=False), SCHEMA)
    assert response["status"] == "ok", response["report"]
    assert response["trace"][0]["thinking"] == "想法A"  # 黑盒留证不受开关影响


# ---------- 描述模式（无战局快照，CLI 测试入口） ----------

def description_request(**overrides) -> dict:
    request = {
        "protocolVersion": SCHEMA["protocolVersion"],
        "opList": sorted(schema_mod.canonical_ops(SCHEMA)),
        "description": "部署时立刻对全场敌人造成 500 点真实伤害",
        "constraints": {"request": "只做一次伤害"},
    }
    request.update(overrides)
    return request


def test_description_mode_ok_without_snapshot(monkeypatch):
    """无快照有描述：免 digest 进循环；首条消息带 designBrief、系统提示带描述模式附注。"""
    threads = []

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        threads.append(list(messages))
        if len(threads) == 1:
            return tool_call("d1", "update_plan", {"plan": "1. 读文档 2. 直接伤害"})
        return tool_call("d2", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(description_request(), tiered_cfg(), SCHEMA)
    assert response["status"] == "ok", response["report"]
    system_text, first_user = threads[0][0]["content"], threads[0][1]["content"]
    assert agent.DESCRIPTION_MODE_NOTE in system_text
    brief = json.loads(first_user)
    assert brief["mode"] == "description"
    assert "500 点真实伤害" in brief["designBrief"]
    assert brief["constraints"] == {"request": "只做一次伤害"}
    assert "battleDigest" not in first_user and "hostAssets" not in first_user


def test_description_mode_excludes_snapshot_tools(monkeypatch):
    """描述模式剔除战局类工具；研究/设计/交付工具保留。"""
    from app import tools as tools_mod

    seen_defs = []

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        seen_defs.append(tools)
        return tool_call("d1", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(description_request(), tiered_cfg(), SCHEMA)
    assert response["status"] == "ok", response["report"]
    names = {d["function"]["name"] for d in seen_defs[0]}
    assert not names & set(tools_mod.SNAPSHOT_TOOLS)
    assert {"update_plan", "list_components", "read_component_doc",
            "start_draft", "put_step", "validate_draft", "submit_skill"} <= names


def test_no_snapshot_no_description_rejected():
    """快照与描述皆无：保持原闸拒绝（battleSnapshot 路径报错）。"""
    response = agent.run(description_request(description=""), tiered_cfg(), SCHEMA)
    assert response["status"] == "rejected"
    assert response["report"]["issues"][0]["path"] == "battleSnapshot"


def test_on_event_streams_thinking_and_tools(monkeypatch):
    """on_event 富事件：思考原文（含 tokens/耗时）与工具调用/结果按序外发。"""
    calls = []
    events = []

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        calls.append(1)
        if len(calls) == 1:
            return {**tool_call("e1", "read_component_doc", {"name": "modify_cost"}),
                    "reasoning_content": "先读文档再设计。"}
        return tool_call("e2", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(), SCHEMA,
                         on_event=lambda kind, data: events.append((kind, data)))
    assert response["status"] == "ok", response["report"]
    assert [k for k, _ in events] == ["thinking", "tool_call", "tool_result",
                                      "thinking", "tool_call", "tool_result"]
    thinking, call, result = (events[i][1] for i in range(3))
    assert thinking["round"] == 1 and thinking["thinking"] == "先读文档再设计。"
    assert thinking["model"] == "strong-model" and thinking["effort"] == "max"
    assert {"prompt_tokens", "completion_tokens"} <= set(thinking["tokens"])
    assert call["name"] == "read_component_doc"
    assert json.loads(call["arguments"]) == {"name": "modify_cost"}
    # 事件携带模型可见的结果串（db_path=None 时组件文档工具返回错误串，后端另有测试）
    assert isinstance(result["result"], str) and result["result"]
    assert events[3][1]["model"] == "mid-model"  # 计划轮后路由切 mid


def test_service_cache_key_includes_description(monkeypatch):
    """缓存键含 description：不同描述各自生成，相同描述命中缓存。"""
    from app.service import GenerateService

    calls = {"n": 0}

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        calls["n"] += 1
        return tool_call("c1", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    service = GenerateService(tiered_cfg())
    r1 = service.generate(description_request(description="描述A"))
    r2 = service.generate(description_request(description="描述B"))
    r3 = service.generate(description_request(description="描述A"))
    assert calls["n"] == 2
    assert not r1["report"].get("cached") and not r2["report"].get("cached")
    assert r3["report"].get("cached") is True
    assert r3["ability"]["abilityId"] == "gen_a2"


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

    service = GenerateService(Config(llm_mock=True, log_dir=None, db_path=None))
    response = service.generate(make_request())
    assert response["status"] == "ok", response["report"]
    phases = [p["phase"] for p in response["report"]["phases"]]
    assert "submit" in phases


# ---------- 逐调用黑盒（trace + thread：只落盘，不回客户端） ----------

def test_trace_captures_per_call_metrics_and_thread(monkeypatch):
    """每次模型调用记耗时/分项 tokens（含 reasoning）/思考原文；完整对话线程
    （含原始工具参数原文）随响应带出。"""
    models_seen = []

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        models_seen.append(model)
        if len(models_seen) == 1:
            return {**tool_call("c1", "update_plan", {"plan": "计划"}),
                    "reasoning_content": "先提交计划再研究。"}
        return tool_call("c2", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(), SCHEMA)
    assert response["status"] == "ok", response["report"]

    trace = response["trace"]
    assert [e["round"] for e in trace] == [1, 2]
    # 计划轮后下一轮仍是 plan_pending→strong；本例两轮即 submit，未进 mid 档
    assert [e["model"] for e in trace] == ["strong-model", "strong-model"]
    assert [e["effort"] for e in trace] == ["max", "max"]  # 计划态两轮都深想
    assert trace[0]["thinking"] == "先提交计划再研究。"
    for entry in trace:
        assert "durationS" in entry
        assert {"calls", "prompt_tokens", "completion_tokens",
                "reasoning_tokens"} <= set(entry["tokens"])
    # 原始参数在对话线程留证（assistant.tool_calls 为字符串原文，未预解析）
    assert json.loads(response["thread"][2]["tool_calls"][0]["function"]
                      ["arguments"]) == {"plan": "计划"}

    roles = [m["role"] for m in response["thread"]]
    assert roles == ["system", "user", "assistant", "tool", "assistant", "tool"]


def test_thread_preserves_malformed_arguments(monkeypatch):
    """模型发了畸形参数 JSON：不派发，回喂真因（malformed + 原文截片），线程留证。"""
    calls = []

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        calls.append(1)
        if len(calls) == 1:
            return tool_call("c1", "update_plan", {"plan": "计划"})
        if len(calls) == 2:
            return {"role": "assistant", "content": None, "tool_calls": [
                {"id": "c2", "type": "function",
                 "function": {"name": "read_component_doc",
                              "arguments": "not-json{"}}]}
        return tool_call("c3", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(), SCHEMA)
    assert response["status"] == "ok", response["report"]
    assistants = [m for m in response["thread"] if m["role"] == "assistant"]
    assert assistants[1]["tool_calls"][0]["function"]["arguments"] == "not-json{"
    # 回喂的是畸形真因与原文截片，不是缺参类下游错误（模型据此重发而非误修）
    tool_msgs = [m for m in response["thread"] if m.get("role") == "tool"]
    assert "malformed tool arguments" in tool_msgs[1]["content"]
    assert "not-json{" in tool_msgs[1]["content"]


def test_non_object_arguments_fed_back_not_dispatched(monkeypatch):
    """参数是合法 JSON 但非 object（如数组）：按畸形回喂，不进工具派发。"""
    state = {"n": 0}

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        state["n"] += 1
        if state["n"] == 1:
            return tool_call("c1", "update_plan", {"plan": "计划"})
        if state["n"] == 2:
            return {"role": "assistant", "content": None, "tool_calls": [
                {"id": "c2", "type": "function",
                 "function": {"name": "read_component_doc",
                              "arguments": "[1,2]"}}]}
        return tool_call("c3", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(), SCHEMA)
    assert response["status"] == "ok", response["report"]
    tool_msgs = [m for m in response["thread"] if m.get("role") == "tool"]
    assert "must be a JSON object" in tool_msgs[1]["content"]


def test_mock_mode_has_no_trace():
    """mock 路径不触 LLM：无黑盒可记，响应不带 trace/thread。"""
    response = agent.run(make_request(), tiered_cfg(llm_mock=True), SCHEMA)
    assert response["status"] == "ok"
    assert "trace" not in response and "thread" not in response


def test_service_strips_trace_into_log(monkeypatch, tmp_path):
    """service 从客户端响应剥离黑盒，随案例完整落盘。"""
    from app.service import GenerateService

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        if len(messages) == 2:  # 首轮：system + 首条用户消息
            return {**tool_call("c1", "update_plan", {"plan": "计划"}),
                    "reasoning_content": "想法。"}
        return tool_call("c2", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    service = GenerateService(tiered_cfg(log_dir=str(tmp_path)))
    response = service.generate(make_request())
    assert response["status"] == "ok", response["report"]
    assert "trace" not in response and "thread" not in response

    logs = list(tmp_path.glob("gen-*.json"))
    assert len(logs) == 1
    case = json.loads(logs[0].read_text(encoding="utf-8"))
    assert case["trace"][0]["thinking"] == "想法。"
    assert case["thread"][0]["role"] == "system"
    assert "trace" not in case["response"]["report"]


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

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
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
    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
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
    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        n = sum(1 for m in messages if m.get("role") == "assistant") + 1
        return tool_call(f"c{n}", "read_component_doc", {"name": "delay"})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(agent_max_rounds=2), SCHEMA)
    assert response["status"] == "rejected"
    assert "degraded" not in response
    assert response["ability"] is None


def test_wall_budget_stops_before_first_llm_call(monkeypatch):
    calls = {"n": 0}

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        calls["n"] += 1
        return tool_call("c1", "update_plan", {"plan": "计划"})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(agent_max_wall_seconds=0), SCHEMA)
    assert response["status"] == "rejected"
    assert calls["n"] == 0
    assert response["report"]["rounds"] == 0


def test_token_budget_degrades_after_validated_draft(monkeypatch):
    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        llm._accumulate({"usage": {"prompt_tokens": 600, "completion_tokens": 0}})
        return _scripted_plan_and_draft()

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    response = agent.run(make_request(), tiered_cfg(agent_max_total_tokens=500), SCHEMA)
    assert response["status"] == "ok", response["report"]
    assert response["degraded"] is True
    assert response["report"]["degradedReason"] == "token_budget_exhausted"


def test_call_timeout_tightened_to_remaining_wall_budget(monkeypatch):
    captured = []

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        captured.append(timeout)
        if len(captured) == 1:
            return tool_call("c1", "update_plan", {"plan": "计划"})
        return tool_call("c2", "submit_skill", {"config": submit_config()})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    cfg = tiered_cfg(llm_timeout_seconds=600, agent_max_wall_seconds=100)
    response = agent.run(make_request(), cfg, SCHEMA)
    assert response["status"] == "ok", response["report"]
    assert 90 <= captured[0] <= 95  # min(600, 100 - 已耗~0 - 5)


def test_tool_result_is_not_truncated(monkeypatch):
    """工具结果完整回填，不受字符上限或摘要流程影响。"""
    from app import tools

    ctx = tools.ToolContext(make_request(), tiered_cfg(), SCHEMA, None)
    big = {"doc": "x" * 10_000}
    monkeypatch.setattr(tools, "_dispatch", lambda c, name, args: big)
    out = tools.execute_tool(ctx, "read_contract_doc", {})
    assert out == json.dumps(big, ensure_ascii=False)
