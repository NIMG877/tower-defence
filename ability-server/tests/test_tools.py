"""Agent 工具测试：渐进式披露的索引/文档读取与交叉项工具壳。"""

import json

from app import schema as schema_mod, tools

SCHEMA = schema_mod.load_schema()


def test_tool_definitions_cover_four_tools():
    names = [t["function"]["name"] for t in tools.tool_definitions()]
    assert names == ["compute_cross_items", "list_components",
                     "read_component_doc", "read_contract_doc"]
    # 交叉项工具的描述应内嵌全部可用指标词表（LLM 不需要猜）。
    desc = tools.tool_definitions()[0]["function"]["description"]
    assert "nearest_enemy_distance" in desc and "enemies_within" in desc


def test_list_components_returns_full_index():
    result = json.loads(tools.execute_tool("list_components", {}, {}, SCHEMA))
    assert len(result["components"]) == 34
    entry = next(c for c in result["components"] if c["op"] == "apply_damage")
    assert entry["class"] == "ApplyDamage"
    assert entry["summary"]  # 一句话摘要非空


def test_read_component_doc_by_op_and_alias():
    by_op = json.loads(tools.execute_tool("read_component_doc", {"name": "apply_damage"}, {}, SCHEMA))
    by_alias = json.loads(tools.execute_tool("read_component_doc", {"name": "ApplyDamage"}, {}, SCHEMA))
    assert by_op["op"] == by_alias["op"] == "apply_damage"
    assert by_op["doc"] == "docs/skill-components/ApplyDamage.md"
    assert "# ApplyDamage" in by_op["content"]


def test_read_component_doc_unknown_name_is_tool_error():
    result = json.loads(tools.execute_tool("read_component_doc", {"name": "nope"}, {}, SCHEMA))
    assert "error" in result and "list_components" in result["error"]


def test_read_contract_doc_returns_text():
    result = json.loads(tools.execute_tool("read_contract_doc", {}, {}, SCHEMA))
    assert "AbilityConfig" in result["doc"]


def test_compute_cross_items_shell():
    snapshot = {"selfPos": {"x": 0, "y": 0},
                "entities": [{"id": "m-1", "camp": 2, "pos": {"x": 1, "y": 0}, "hpRate": 0.5}]}
    result = json.loads(tools.execute_tool(
        "compute_cross_items", {"items": ["enemy_count", "bogus"]}, snapshot, SCHEMA))
    assert result["results"]["enemy_count"] == 1
    assert result["results"]["bogus"].startswith("error:")


def test_tool_errors_never_raise():
    # 未知工具名/坏参数都返回 error JSON，不抛异常（让 LLM 自行修正调用）。
    assert "error" in json.loads(tools.execute_tool("nope", {}, {}, SCHEMA))
    assert "error" in json.loads(tools.execute_tool("compute_cross_items", None, {}, SCHEMA))
