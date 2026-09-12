"""语料模块测试：op 特征 / 检索排序 / 风格蒸馏 / 撞名 warning / agent 注入集成。"""

import json
import os

from app import corpus, schema as schema_mod
from app.config import Config
from app.service import GenerateService
from app.validator import validate

SCHEMA = schema_mod.load_schema()

# 最小假 schema：只测 _canonical 的别名归一，不依赖真实 34 组件表
FAKE_SCHEMA = {"componentOps": {"modify_cost": {"aliases": ["ModifyCost"]},
                                "apply_damage": {"aliases": []}}}


def make_request(**overrides) -> dict:
    request = {
        "protocolVersion": SCHEMA["protocolVersion"],
        "opList": sorted(schema_mod.canonical_ops(SCHEMA)),
        "battleSnapshot": {"selfId": "c-1", "camp": 1, "selfHpRate": 0.8,
                           "selfPos": {"x": 0, "y": 0}, "entities": []},
        "hostAssets": {"canSpawnEntityIds": [], "bulletCount": 0, "iconKeys": ["charger"]},
        "constraints": {},
    }
    request.update(overrides)
    return request


def skill(ability_id, steps, triggers=("OnInitialize",), reentry="IgnoreWhileRunning"):
    return {"abilityId": ability_id, "abilityName": ability_id, "description": "",
            "rules": [{"triggers": [{"triggerEvent": t, "groups": []} for t in triggers],
                       "reentry": reentry, "steps": steps}]}


def test_skill_ops_recurses_and_excludes_primitives():
    s = skill("s1", [
        {"op": "apply_damage"},
        {"op": "delay", "steps": [{"op": "branch", "elseSteps": [{"op": "ModifyCost"}]}]},
    ])
    # elseSteps 深处也计入；原语 delay/branch 排除；legacy 别名 ModifyCost 归一 canonical
    assert corpus.skill_ops(s, FAKE_SCHEMA) == {"apply_damage", "modify_cost"}


def test_load_corpus_missing_or_invalid_returns_none(tmp_path):
    assert corpus.load_corpus(str(tmp_path / "nope.json")) is None
    bad = tmp_path / "bad.json"
    bad.write_text("{not json", encoding="utf-8")
    assert corpus.load_corpus(str(bad)) is None
    empty = tmp_path / "empty.json"
    empty.write_text(json.dumps({"skills": []}), encoding="utf-8")
    assert corpus.load_corpus(str(empty)) is None


def test_get_corpus_reloads_on_mtime_change(tmp_path):
    path = tmp_path / "skills.json"
    path.write_text(json.dumps({"skills": [skill("a", [])]}), encoding="utf-8")
    os.utime(path, (1_000_000_000, 1_000_000_000))
    first = corpus.get_corpus(str(path))
    assert [s["abilityId"] for s in first["skills"]] == ["a"]

    path.write_text(json.dumps({"skills": [skill("a", []), skill("b", [])]}), encoding="utf-8")
    os.utime(path, (1_000_000_100, 1_000_000_100))
    assert len(corpus.get_corpus(str(path))["skills"]) == 2  # mtime 变化触发重载

    assert corpus.get_corpus(None) is None
    assert corpus.get_corpus(str(tmp_path / "nope.json")) is None


def test_retrieve_orders_by_jaccard_and_limits_k():
    c = {"skills": [
        skill("partial", [{"op": "apply_damage"}, {"op": "fire_bullets"}]),
        skill("full", [{"op": "select_targets"}, {"op": "apply_damage"}]),
        skill("none", [{"op": "modify_cost"}]),
    ]}
    planned = ["select_targets", "apply_damage"]
    out = corpus.retrieve(c, planned, k=3)
    # full 的 Jaccard=1.0 > partial 的 1/3；none 零重叠被过滤
    assert [s["abilityId"] for s in out] == ["full", "partial"]
    assert out[0]["_score"] == 1.0
    assert [s["abilityId"] for s in corpus.retrieve(c, planned, k=1)] == ["full"]
    assert corpus.retrieve(c, []) == []
    # 同分按 abilityId 稳定排序
    tie = {"skills": [skill("b2", [{"op": "apply_damage"}]),
                      skill("a2", [{"op": "apply_damage"}])]}
    assert [s["abilityId"] for s in corpus.retrieve(tie, ["apply_damage"], k=3)] == ["a2", "b2"]


def test_style_summary_shapes():
    c = {"skills": [
        skill("a", [{"op": "apply_damage", "args": {"entries": [
            {"key": "multiplier", "value": "2", "type": "Float"},
            {"key": "multiplier", "value": "3", "type": "Float"},
            {"key": "targetMode", "value": "blackboard", "type": "String"}]}}]),
        skill("b", [{"op": "apply_damage", "args": {"entries": [
            {"key": "multiplier", "value": "1.5", "type": "Float"}]}}],
              reentry="Restart"),
    ]}
    style = corpus.style_summary(c)
    assert style["skills"] == 2
    assert style["opUsage"] == {"apply_damage": 2}
    assert style["reentry"] == {"IgnoreWhileRunning": 1, "Restart": 1}
    mult = style["paramStats"]["apply_damage"]["multiplier"]
    assert (mult["min"], mult["max"]) == (1.5, 3.0)
    assert style["paramStats"]["apply_damage"]["targetMode"]["values"] == ["blackboard"]


def test_validate_warns_on_id_collision():
    dto = {"abilityId": "existing_1", "abilityName": "n", "description": "d",
           "sp": {"totalSp": 0, "consumeMode": "NoConsume", "openMode": "Auto"},
           "rules": [{"triggers": [{"triggerEvent": "OnInitialize", "groups": []}],
                      "steps": [{"op": "apply_damage",
                                 "args": {"entries": [{"key": "multiplier", "value": "2",
                                                       "type": "Float",
                                                       "fromBlackboard": False}]}}]}]}
    ok, issues, _ = validate(dto, SCHEMA, {"existing_1"})
    assert ok and any(i["path"] == "abilityId" and i["severity"] == "warning" for i in issues)
    _, issues_no_corpus, _ = validate(dto, SCHEMA)
    assert not any(i["path"] == "abilityId" for i in issues_no_corpus)


# ---------- agent 集成：语料注入 generate 阶段 + 撞名 warning 进入 report ----------

ANALYSIS_TEXT = "分析：建议即时生效的攻击技能。"
DESIGN = {"abilityName": "测试技能", "description": "造成 2 倍伤害",
          "designNotes": "select_targets + apply_damage",
          "plannedOps": ["select_targets", "apply_damage"]}


def test_agent_injects_references_and_reports_collision(tmp_path, monkeypatch):
    from app import llm as llm_mod
    corpus_file = tmp_path / "skills.json"
    ref = skill("existing_1", [{"op": "select_targets"}, {"op": "apply_damage"}])
    corpus_file.write_text(json.dumps({"protocolVersion": 1, "skills": [ref]}), encoding="utf-8")
    os.utime(corpus_file, (1_000_000_000, 1_000_000_000))

    ability = {
        "abilityId": "existing_1", "abilityName": "测试技能", "description": "造成 2 倍伤害",
        "iconKey": "charger",
        "sp": {"totalSp": 0, "consumeMode": "NoConsume", "openMode": "Auto"},
        "rules": [{"triggers": [{"triggerEvent": "OnInitialize", "groups": []}],
                   "steps": [{"op": "apply_damage",
                              "args": {"entries": [{"key": "multiplier", "value": "2",
                                                    "type": "Float", "fromBlackboard": False}]}}]}],
    }

    state = {"chat_tools": 0}

    def fake_chat_tools(messages, tool_defs, cfg):
        state["chat_tools"] += 1
        if state["chat_tools"] <= 2:  # analyze 2 轮
            return {"role": "assistant", "content": ANALYSIS_TEXT}
        return {"role": "assistant", "content": json.dumps(DESIGN, ensure_ascii=False)}  # describe

    captured = {}

    def fake_chat(messages, cfg):
        captured["user"] = json.loads(messages[1]["content"])  # generate 的 user 消息
        return json.dumps(ability, ensure_ascii=False)

    monkeypatch.setattr(llm_mod, "chat_tools", fake_chat_tools)
    monkeypatch.setattr(llm_mod, "chat", fake_chat)
    service = GenerateService(Config(log_dir=None, corpus_path=str(corpus_file)))
    response = service.generate(make_request())

    assert response["status"] == "ok", response["report"]
    # op 重叠命中语料范例注入 generate 的 user 消息（调试 _score 已剥离）
    refs = captured["user"]["referenceSkills"]
    assert [s["abilityId"] for s in refs] == ["existing_1"]
    assert "_score" not in refs[0]
    # 撞用现有 id → warning 记入 report.issues（不拒绝）
    assert any(i["path"] == "abilityId" and i["severity"] == "warning"
               for i in response["report"]["issues"])
