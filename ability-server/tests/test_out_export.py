"""_export_out 收件箱导出测试：ok 才落、原子替换无残留、同 id 覆盖、缓存命中重灌。

复刻 test_service_strips_trace_into_log 的脚本化 chat_tools 模式；out 文件是纯
ability DTO，供 Unity GeneratedSkillImporter.Parse 直接消费（无协议包装）。
"""

import json

from app import agent, llm
from app.service import GenerateService
from tests.test_agent import (_scripted_plan_and_draft, make_request,
                              submit_config, tiered_cfg, tool_call)


def _scripted_submit(monkeypatch, configs):
    """按提交次序依次交付不同 config（最后一个重复使用），便于断言覆盖语义。"""
    state = {"n": 0}

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        if len(messages) == 2:
            return tool_call("c1", "update_plan", {"plan": "计划"})
        index = min(state["n"], len(configs) - 1)
        state["n"] += 1
        return tool_call("c2", "submit_skill", {"config": configs[index]})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)


def _run(monkeypatch, tmp_path, **overrides):
    _scripted_submit(monkeypatch, [submit_config()])
    overrides.setdefault("out_dir", str(tmp_path / "out"))
    service = GenerateService(tiered_cfg(**overrides))
    return service, service.generate(make_request())


def test_ok_exports_pure_ability_dto(monkeypatch, tmp_path):
    service, response = _run(monkeypatch, tmp_path)
    assert response["status"] == "ok", response["report"]
    out_dir = tmp_path / "out"
    files = list(out_dir.glob("*.json"))
    assert [f.name for f in files] == ["gen_a2.json"]
    assert not list(out_dir.glob("*.tmp"))  # 原子替换后无临时名残留
    payload = json.loads(files[0].read_text(encoding="utf-8"))
    assert payload == response["ability"]
    assert set(payload) == {"abilityId", "abilityName", "description", "sp", "rules"}
    assert response["report"]["outPath"] == str(files[0])


def test_degraded_not_exported(monkeypatch, tmp_path):
    """降级稿结构上全绿但流程未走完：与缓存同谓词，只进 logs 不进 out/。"""
    monkeypatch.setattr(agent.time, "sleep", lambda *_: None)  # 退避不真睡
    state = {"n": 0}

    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        state["n"] += 1
        if state["n"] == 1:
            return _scripted_plan_and_draft()
        raise llm.LlmError("quota exhausted")

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    service = GenerateService(tiered_cfg(out_dir=str(tmp_path / "out")))
    response = service.generate(make_request())
    assert response["status"] == "ok" and response["degraded"] is True
    assert not list((tmp_path / "out").glob("*.json"))


def test_rejected_not_exported(monkeypatch, tmp_path):
    """轮数耗尽且无草稿 → rejected（ability=None），out/ 不落文件也不炸。"""
    def fake_chat_tools(messages, tools, cfg, model=None, timeout=None, effort=None):
        return tool_call("c1", "update_plan", {"plan": "计划"})

    monkeypatch.setattr(llm, "chat_tools", fake_chat_tools)
    service = GenerateService(tiered_cfg(out_dir=str(tmp_path / "out"),
                                         agent_max_rounds=2))
    response = service.generate(make_request())
    assert response["status"] == "rejected"
    assert not list((tmp_path / "out").glob("*.json"))


def test_same_id_overwrites_with_latest(monkeypatch, tmp_path):
    """同 abilityId 重生成：单文件、内容为最新（Unity 侧 CopySerialized 同语义）。"""
    first = submit_config()
    second = {**submit_config(), "description": "d2"}
    _scripted_submit(monkeypatch, [first, second])
    service = GenerateService(tiered_cfg(out_dir=str(tmp_path / "out")))
    service.generate(make_request())
    response = service.generate(make_request(constraints={"round": 2}))
    assert response["status"] == "ok", response["report"]
    out_dir = tmp_path / "out"
    assert [f.name for f in out_dir.glob("*.json")] == ["gen_a2.json"]
    payload = json.loads((out_dir / "gen_a2.json").read_text(encoding="utf-8"))
    assert payload["description"] == "d2"


def test_cache_hit_refills_inbox(monkeypatch, tmp_path):
    """Unity 导入即消费 json；同 payload 缓存命中重灌收件箱，不必改描述重跑。"""
    service, response = _run(monkeypatch, tmp_path)
    out_file = tmp_path / "out" / "gen_a2.json"
    assert out_file.exists()
    out_file.unlink()

    hit = service.generate(make_request())
    assert hit["report"]["cached"] is True
    payload = json.loads(out_file.read_text(encoding="utf-8"))
    assert payload == hit["ability"]
    assert hit["report"]["outPath"] == str(out_file)


def test_out_dir_disabled(monkeypatch, tmp_path):
    """out_dir=None 关闭导出：目录都不创建，生成流程不受影响。"""
    service, response = _run(monkeypatch, tmp_path, out_dir=None)
    assert response["status"] == "ok", response["report"]
    assert list(tmp_path.iterdir()) == []
