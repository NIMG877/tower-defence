"""battle_digest 纯函数测试：v2 快照 → 注入用战局摘要。"""

import pytest

from app import battle_digest


def make_snapshot():
    return {
        "selfId": "c-1",
        "mapI": 12, "mapJ": 10,
        "canSetHigher": ["4,5", "6,7"],
        "canSetLower": ["3,4", "1,1"],
        "entities": [
            {"id": "c-1", "name": "干员", "camp": 1, "isStatic": False,
             "hp": 1500, "maxHp": 1500, "hpRate": 1.0, "pos": {"x": 4, "y": 4},
             "attack": 500, "defence": 100, "magicRes": 10, "job": 1, "label": None,
             "massLevel": 2},
            {"id": "m-1", "name": "史莱姆", "camp": 2, "isStatic": False,
             "hp": 400, "maxHp": 800, "hpRate": 0.5, "pos": {"x": 5, "y": 4},
             "attack": 120, "defence": 30, "magicRes": 0, "job": -1, "label": "slime",
             "massLevel": 1},
            {"id": "m-2", "name": "士兵", "camp": 2, "isStatic": False,
             "hp": 1200, "maxHp": 1200, "hpRate": 1.0, "pos": {"x": 7, "y": 7},
             "attack": 200, "defence": 150, "magicRes": 0, "job": -1, "label": "soldier",
             "massLevel": 2},
            {"id": "m-3", "name": "重甲", "camp": 2, "isStatic": False,
             "hp": 4500, "maxHp": 4500, "hpRate": 1.0, "pos": {"x": 10, "y": 10},
             "attack": 500, "defence": 400, "magicRes": 30, "job": -1, "label": "boss",
             "massLevel": 5},
            {"id": "a-1", "name": "队友", "camp": 1, "isStatic": False,
             "hp": 900, "maxHp": 900, "hpRate": 1.0, "pos": {"x": 3, "y": 3},
             "attack": 300, "defence": 80, "magicRes": 0, "job": 5, "label": None,
             "massLevel": 2},
        ],
    }


def test_digest_self_counts_and_nearest():
    d = battle_digest.build(make_snapshot())
    assert d["self"]["id"] == "c-1" and d["self"]["tile"] == [4, 4]
    assert d["counts"] == {"enemies": 3, "allies": 1, "statics": 0}  # 自身不计入友军
    # 最近敌方按距离升序并带 distance 字段
    dists = [e["distance"] for e in d["nearestEnemies"]]
    assert dists == sorted(dists) == [1.0, pytest.approx(4.24, abs=0.01), 8.49]
    assert d["nearestEnemies"][0]["id"] == "m-1"


def test_digest_distance_bands_and_stats():
    d = battle_digest.build(make_snapshot())
    # m-1 d=1 → 3 格带；m-2 d≈4.24 → 5 格带；m-3 d≈8.49 → beyond
    assert d["enemyDistanceBands"] == {"3": 1, "5": 1, "8": 0, "beyond": 1}
    stats = d["enemyStats"]
    assert stats["hp"] == {"min": 400, "max": 4500, "avg": 2033.33}
    assert stats["defence"]["max"] == 400
    assert stats["magicRes"]["max"] == 30
    assert "attack" in stats and "hpRate" in stats


def test_digest_map_deployable_and_near_self():
    d = battle_digest.build(make_snapshot())
    assert d["map"]["sizeI"] == 12 and d["map"]["sizeJ"] == 10
    assert d["map"]["deployable"] == {"higher": 2, "lower": 2}
    # self(4,4) 半径 1.5：4,5 与 3,4 命中；6,7 与 1,1 不命中
    assert d["map"]["nearSelf"] == {"higher": ["4,5"], "lower": ["3,4"]}


def test_digest_no_enemies_degrades_to_empty():
    snap = make_snapshot()
    snap["entities"] = [snap["entities"][0]]
    d = battle_digest.build(snap)
    assert d["counts"]["enemies"] == 0
    assert d["nearestEnemies"] == []
    assert d["enemyStats"] == {}
    assert d["enemyDistanceBands"] == {"3": 0, "5": 0, "8": 0, "beyond": 0}


def test_digest_nearest_k_truncates():
    snap = make_snapshot()
    for i in range(10):  # 再铺 10 只近距离敌人，top-K 默认 6
        snap["entities"].append(
            {"id": f"m-x{i}", "name": "x", "camp": 2, "isStatic": False,
             "hp": 100, "maxHp": 100, "hpRate": 1.0, "pos": {"x": 4, "y": 5 + i},
             "attack": 10, "defence": 0, "magicRes": 0, "job": -1, "label": None,
             "massLevel": 1})
    d = battle_digest.build(snap)
    assert len(d["nearestEnemies"]) == battle_digest.NEAREST_K


def test_digest_missing_self_record_raises():
    snap = make_snapshot()
    snap["selfId"] = "ghost"
    with pytest.raises(ValueError, match="selfId"):
        battle_digest.build(snap)


def test_deploy_cells_near_filters_by_radius():
    snap = make_snapshot()
    out = battle_digest.deploy_cells_near(snap, (4.0, 4.0), 1.5)
    assert out == {"higher": ["4,5"], "lower": ["3,4"]}
    out = battle_digest.deploy_cells_near(snap, (4.0, 4.0), 10.0)
    assert out == {"higher": ["4,5", "6,7"], "lower": ["3,4", "1,1"]}
