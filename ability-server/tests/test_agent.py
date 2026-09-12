"""三阶段 Agent 编排测试：mock 全管线 / 工具循环 / 渐进披露 / 独立重试 / 握手。"""

import json

from app import agent, schema as schema_mod
from app.config import Config
from app.service import GenerateService

SCHEMA = schema_mod.load_schema()


def make_request(**overrides) -> dict:
    request = {
        "protocolVersion": SCHEMA["protocolVersion"],
        "opList": sorted(schema_mod.canonical_ops(SCHEMA)),
        "battleSnapshot": {
            "selfId": "c-1", "camp": 1, "selfHpRate": 0.8, "selfPos": {"x": 0, "y": 0},
            "entities": [{"id": "m-1", "camp": 2, "hpRate": 0.5, "pos": {"x": 3, "y": 4}}],
        },
        "hostAssets": {"canSpawnEntityIds": [], "bulletCount": 0, "iconKeys": ["charger"]},
        "constraints": {},
    }
    request.update(overrides)
    return request


def mock_service(**config_overrides) -> GenerateService:
    # 测试不落盘：log_dir 默认指向 ability-server/logs，这里显式关闭隔离
    return GenerateService(Config(llm_mock=True, log_dir=None, corpus_path=None, **config_overrides))


def test_mock_pipeline_reports_all_phases():
    response = mock_service().generate(make_request())
    assert response["status"] == "ok", response["report"]
    assert response["ability"]["abilityId"] == "gen_mock_1"
    phases = [p["phase"] for p in response["report"]["phases"]]
    for expected in ("analyze", "describe", "generate", "validate", "done"):
        assert expected in phases
    assert response["report"]["attempts"] == 1


def test_cache_hits_same_snapshot():
    service = mock_service()
    first = service.generate(make_request())
    second = service.generate(make_request())
    assert first["report"].get("cached") is None
    assert second["report"]["cached"] is True
    assert second["ability"] == first["ability"]


def test_handshake_drift_rejected():
    response = mock_service().generate(make_request(opList=["delay", "nope"]))
    assert response["status"] == "rejected"
    assert "drift" in response["report"]["issues"][0]["message"]


def test_on_phase_callback_streams_live():
    seen = []
    mock_service().generate(make_request(), on_phase=lambda phase, detail: seen.append(phase))
    assert "analyze" in seen and "generate" in seen


# ---------- 真实路径（脚本化 LLM）：工具循环 / 渐进披露 / 设计保留的独立重试 ----------

ANALYSIS_TEXT = "分析：单一敌人 m-1 距离 5，血量 50%，建议即时生效的攻击技能。"
DESIGN = {"abilityName": "测试技能", "description": "对攻击目标造成 2 倍伤害",
          "designNotes": "OnAfterAttack 事件 + apply_damage", "plannedOps": ["apply_damage"]}


def tool_call_message(name: str, arguments: dict) -> dict:
    return {"role": "assistant", "content": None,
            "tool_calls": [{"id": "call_1", "type": "function",
                            "function": {"name": name,
                                         "arguments": json.dumps(arguments)}}]}


def valid_ability_json() -> str:
    ability = {
        "abilityId": "gen_agent_1", "abilityName": DESIGN["abilityName"],
        "description": DESIGN["description"], "iconKey": "charger",
        "sp": {"totalSp": 0, "consumeMode": "NoConsume", "openMode": "Auto"},
        "rules": [{"triggers": [{"triggerEvent": "OnAfterAttack", "groups": []}],
                   "steps": [{"op": "apply_damage",
                              "args": {"entries": [{"key": "multiplier", "value": "2",
                                                    "type": "Float", "fromBlackboard": False}]}}]}],
    }
    return json.dumps(ability)


def test_tool_loop_and_progressive_disclosure(monkeypatch):
    from app import llm as llm_mod
    service = GenerateService(Config(log_dir=None, corpus_path=None))
    seen = {"chat_tools": 0, "chat": 0, "cross_result": None, "generate_system": None}

    def fake_chat_tools(messages, tool_defs, cfg):
        seen["chat_tools"] += 1
        if seen["chat_tools"] == 1:
            # analyze 第一轮：Agent 主动调交叉项工具
            return tool_call_message("compute_cross_items", {"items": ["enemy_count"]})
        if seen["chat_tools"] == 2:
            # 工具结果应已作为 role=tool 消息回填进线程
            tool_msg = next(m for m in messages if m.get("role") == "tool")
            seen["cross_result"] = json.loads(tool_msg["content"])["results"]["enemy_count"]
            return {"role": "assistant", "content": ANALYSIS_TEXT}
        if seen["chat_tools"] == 3:
            # describe 轮也允许工具：设计前复读一篇组件文档
            return tool_call_message("read_component_doc", {"name": "apply_damage"})
        return {"role": "assistant", "content": json.dumps(DESIGN, ensure_ascii=False)}

    def fake_chat(messages, cfg):
        seen["chat"] += 1
        seen["generate_system"] = messages[0]["content"]
        return valid_ability_json()

    monkeypatch.setattr(llm_mod, "chat_tools", fake_chat_tools)
    monkeypatch.setattr(llm_mod, "chat", fake_chat)

    response = service.generate(make_request())
    assert response["status"] == "ok", response["report"]
    assert seen["cross_result"] == 1  # 工具真实执行并回填
    assert seen["chat_tools"] == 4    # analyze 2 轮 + describe 2 轮（含 1 次复读文档）
    assert seen["chat"] == 1          # generate
    # describe 阶段的工具调用经 reporter 可见（Unity 阶段日志）
    assert any(p["phase"] == "describe" and "tool" in p["detail"]
               for p in response["report"]["phases"])
    # 渐进披露：generate 的 system 里有 plannedOps 的 schema，没有未计划组件的
    assert '"apply_damage"' in seen["generate_system"]
    assert '"write_blackboard"' not in seen["generate_system"]
    assert response["report"]["design"]["abilityName"] == "测试技能"
    assert response["report"]["analysis"] == ANALYSIS_TEXT
    assert response["report"]["attempts"] == 1


def test_generate_phase_retries_with_design_preserved(monkeypatch):
    from app import llm as llm_mod
    service = GenerateService(Config(log_dir=None, corpus_path=None))
    calls = {"chat_tools": 0, "chat": 0}

    def fake_chat_tools(messages, tool_defs, cfg):
        calls["chat_tools"] += 1
        if calls["chat_tools"] <= 2:  # analyze 2 轮
            return {"role": "assistant", "content": ANALYSIS_TEXT}
        return {"role": "assistant", "content": json.dumps(DESIGN, ensure_ascii=False)}  # describe

    def fake_chat(messages, cfg):
        calls["chat"] += 1
        if calls["chat"] == 1:  # generate 首轮：未知 op → 校验拒绝
            bad = json.loads(valid_ability_json())
            bad["rules"][0]["steps"][0]["op"] = "make_big_explosion"
            return json.dumps(bad)
        return valid_ability_json()

    monkeypatch.setattr(llm_mod, "chat_tools", fake_chat_tools)
    monkeypatch.setattr(llm_mod, "chat", fake_chat)
    response = service.generate(make_request())
    assert response["status"] == "ok", response["report"]
    assert response["report"]["attempts"] == 2
    assert calls["chat_tools"] == 3
    # 设计成果跨重试保留，没有被推倒重来
    assert response["report"]["design"]["plannedOps"] == ["apply_damage"]
    assert response["report"]["analysis"] == ANALYSIS_TEXT


def test_describe_phase_retries_on_unknown_planned_ops(monkeypatch):
    from app import llm as llm_mod
    service = GenerateService(Config(log_dir=None, corpus_path=None))
    state = {"chat_tools": 0}

    def fake_chat_tools(messages, tool_defs, cfg):
        state["chat_tools"] += 1
        if state["chat_tools"] == 1:  # analyze 一轮文本即收尾
            return {"role": "assistant", "content": ANALYSIS_TEXT}
        if state["chat_tools"] == 2:  # describe 首轮：plannedOps 未注册
            bad = dict(DESIGN, plannedOps=["make_big_explosion"])
            return {"role": "assistant", "content": json.dumps(bad, ensure_ascii=False)}
        return {"role": "assistant", "content": json.dumps(DESIGN, ensure_ascii=False)}  # 重试

    monkeypatch.setattr(llm_mod, "chat_tools", fake_chat_tools)
    monkeypatch.setattr(llm_mod, "chat", lambda messages, cfg: valid_ability_json())
    response = service.generate(make_request())
    assert response["status"] == "ok", response["report"]
    assert state["chat_tools"] == 3  # 未注册 op 触发了一次设计层重试


def test_always_invalid_generate_rejected_after_max_attempts(monkeypatch):
    from app import llm as llm_mod
    service = GenerateService(Config(log_dir=None, corpus_path=None, max_attempts=2))
    state = {"chat_tools": 0}

    def fake_chat_tools(messages, tool_defs, cfg):
        state["chat_tools"] += 1
        if state["chat_tools"] <= 2:
            return {"role": "assistant", "content": ANALYSIS_TEXT}
        return {"role": "assistant", "content": json.dumps(DESIGN, ensure_ascii=False)}

    monkeypatch.setattr(llm_mod, "chat_tools", fake_chat_tools)
    monkeypatch.setattr(llm_mod, "chat", lambda messages, cfg: '{"abilityId": "gen_x", "rules": []}')
    response = service.generate(make_request())
    assert response["status"] == "rejected"
    assert response["report"]["attempts"] == 2
