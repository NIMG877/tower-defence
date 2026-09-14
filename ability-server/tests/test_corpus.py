"""语料模块测试：op 特征 / 检索排序 / 撞名 warning / 库数据源访问。"""

import os
import shutil
import sqlite3
from pathlib import Path

from app import corpus, schema as schema_mod
from app.validator import validate

SCHEMA = schema_mod.load_schema()
COMMITTED_DB = Path(__file__).resolve().parents[1] / "data" / "ability.db"

def skill(ability_id, steps, triggers=("OnInitialize",), reentry="IgnoreWhileRunning"):
    return {"abilityId": ability_id, "abilityName": ability_id, "description": "",
            "rules": [{"triggers": [{"triggerEvent": t, "groups": []} for t in triggers],
                       "reentry": reentry, "steps": steps}]}


def test_skill_ops_recurses_and_excludes_primitives():
    s = skill("s1", [
        {"op": "apply_damage"},
        {"op": "delay", "steps": [{"op": "branch", "elseSteps": [{"op": "modify_cost"}]}]},
    ])
    # elseSteps 深处也计入；原语 delay/branch 排除
    assert corpus.skill_ops(s) == {"apply_damage", "modify_cost"}


def test_get_corpus_missing_or_invalid_returns_none(tmp_path):
    assert corpus.get_corpus(str(tmp_path / "nope.db")) is None
    bad = tmp_path / "bad.db"
    bad.write_text("not a sqlite db", encoding="utf-8")
    assert corpus.get_corpus(str(bad)) is None
    assert corpus.get_corpus(None) is None


def test_get_corpus_reloads_on_mtime_change(tmp_path):
    path = tmp_path / "ability.db"
    shutil.copy(COMMITTED_DB, path)
    os.utime(path, (1_000_000_000, 1_000_000_000))
    first = corpus.get_corpus(str(path))
    n = len(first["skills"])
    assert n == 48
    assert first["existingIds"]  # 撞名检查的数据源来自库

    # 改内容 + 改 mtime → 快照重载（重建库后服务无需重启的同款语义）
    con = sqlite3.connect(path)
    con.execute("DELETE FROM skills WHERE ability_id=(SELECT min(ability_id) FROM skills)")
    con.commit()
    con.close()
    os.utime(path, (1_000_000_100, 1_000_000_100))
    assert len(corpus.get_corpus(str(path))["skills"]) == n - 1


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
