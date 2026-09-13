"""schema 加载与握手基线：客户端注册表与 schema 的 op 集合必须一致。"""

from app import schema as schema_mod


def test_schema_loads_with_expected_counts():
    schema = schema_mod.load_schema()
    assert schema["protocolVersion"] >= 1
    assert len(schema["componentOps"]) == 35
    assert len(schema["primitives"]) == 4
    assert schema_mod.canonical_ops(schema) == set(schema["primitives"]) | set(schema["componentOps"])


def test_resolve_primitives_and_aliases():
    schema = schema_mod.load_schema()
    assert schema_mod.resolve_op(schema, "delay")[0] == "delay"
    assert schema_mod.resolve_op(schema, "apply_damage")[0] == "apply_damage"
    assert schema_mod.resolve_op(schema, "ApplyDamage")[0] == "apply_damage"
    assert schema_mod.resolve_op(schema, "select_targets")[0] == "select_targets"
    assert schema_mod.resolve_op(schema, "EntitySelector")[0] == "select_targets"
    assert schema_mod.resolve_op(schema, "nope") is None


def test_contract_docs_available():
    text = schema_mod.contract_docs()
    assert "AbilityConfig" in text  # ability-steps.md 或 README 至少加载到了一份
