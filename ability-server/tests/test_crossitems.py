"""交叉项计算器测试（compute_cross_items 工具的后端）。"""

import pytest

from app import crossitems

SNAPSHOT = {
    "selfId": "c-1", "camp": 1, "selfHpRate": 0.8,
    "selfPos": {"x": 0, "y": 0},
    "entities": [
        {"id": "m-1", "camp": 2, "hpRate": 0.5, "pos": {"x": 3, "y": 4}},
        {"id": "m-2", "camp": 2, "hpRate": 0.2, "pos": {"x": 0, "y": 1}},
        {"id": "a-1", "camp": 1, "hpRate": 1.0, "pos": {"x": 2, "y": 0}},
    ],
}


def test_basic_counts_and_self():
    assert crossitems.compute(SNAPSHOT, "enemy_count") == 2
    assert crossitems.compute(SNAPSHOT, "ally_count") == 1
    assert crossitems.compute(SNAPSHOT, "self_hp_rate") == 0.8


def test_nearest_enemy_and_lowest_hp():
    assert crossitems.compute(SNAPSHOT, "nearest_enemy_distance") == 1.0
    assert crossitems.compute(SNAPSHOT, "lowest_hp_enemy") == {"id": "m-2", "hpRate": 0.2}


def test_parameterized_items():
    assert crossitems.compute(SNAPSHOT, "distance(m-1)") == 5.0
    assert crossitems.compute(SNAPSHOT, "hp_rate(m-1)") == 0.5
    within = crossitems.compute(SNAPSHOT, "enemies_within(2)")
    assert within == {"radius": 2.0, "count": 1, "ids": ["m-2"]}
    allies = crossitems.compute(SNAPSHOT, "allies_within(5)")
    assert allies["ids"] == ["a-1"]
    assert crossitems.compute(SNAPSHOT, "entity(m-1)")["id"] == "m-1"


def test_can_set_cells_merged():
    snapshot = {**SNAPSHOT, "canSetHigher": ["1,1"], "canSetLower": ["0,0", "0,1"]}
    cells = crossitems.compute(snapshot, "can_set_cells")
    assert cells == {"higher": ["1,1"], "lower": ["0,0", "0,1"]}


def test_unknown_item_and_missing_arg_raise():
    # compute 直接调用抛 ValueError；工具层（compute_all/_safe）负责转成 error 字符串。
    with pytest.raises(ValueError, match="unknown cross item"):
        crossitems.compute(SNAPSHOT, "no_such_item")
    with pytest.raises(ValueError, match="requires an argument"):
        crossitems.compute(SNAPSHOT, "distance")


def test_compute_all_isolates_failures():
    results = crossitems.compute_all(SNAPSHOT, ["enemy_count", "no_such_item", "distance(m-2)"])
    assert results["enemy_count"] == 2
    assert results["no_such_item"].startswith("error:")
    assert results["distance(m-2)"] == 1.0


def test_snapshot_without_self_pos_degrades_gracefully():
    result = crossitems.compute({"entities": SNAPSHOT["entities"]}, "nearest_enemy_distance")
    assert result == -1  # 无敌人返回 -1；此处无敌人字段视为空
    assert "error" in crossitems.compute({"entities": SNAPSHOT["entities"]}, "distance(m-1)")
