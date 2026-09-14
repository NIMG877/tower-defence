"""语料模块测试：op 特征 / 检索排序 / 撞名 warning。"""

import json
import os

from app import corpus, schema as schema_mod
from app.validator import validate

SCHEMA = schema_mod.load_schema()

# 最小假 schema：只测 _canonical 的别名归一，不依赖真实组件表
FAKE_SCHEMA = {"componentOps": {"modify_cost": {"aliases": ["ModifyCost"]},
                                "apply_damage": {"aliases": []}}}


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
