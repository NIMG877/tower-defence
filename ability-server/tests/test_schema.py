"""schema 加载与握手基线：客户端注册表与 schema 的 op 集合必须一致。"""

from app import schema as schema_mod


def test_schema_loads_with_expected_counts():
    schema = schema_mod.load_schema()
    assert schema["protocolVersion"] >= 1
    assert len(schema["componentOps"]) == 35
    assert len(schema["primitives"]) == 4
    assert schema["spRecoverModes"] == [
        "Natural", "OnAttackSuccessfully", "OnAfterHurt", "Other"]
    assert schema["spConsumeModes"] == [
        "Natural", "OnAttackSuccessfully", "OnAfterHurt", "Instant", "Other", "NoConsume"]
    assert schema["abilityOpenModes"] == [
        "Auto", "OnAttackAnimBegin", "OnBeforeHurt", "Manual", "Other", "OnDeadlyHurt"]
    assert schema_mod.canonical_ops(schema) == set(schema["primitives"]) | set(schema["componentOps"])
    assert all("fixedWrites" not in op_def for op_def in schema["componentOps"].values())


def test_resolve_canonical_only():
    schema = schema_mod.load_schema()
    assert schema_mod.resolve_op(schema, "delay")[0] == "delay"
    assert schema_mod.resolve_op(schema, "apply_damage")[0] == "apply_damage"
    assert schema_mod.resolve_op(schema, "select_targets")[0] == "select_targets"
    assert schema_mod.resolve_op(schema, "ApplyDamage") is None  # 类名别名已退役
    assert schema_mod.resolve_op(schema, "EntitySelector") is None
    assert schema_mod.resolve_op(schema, "nope") is None
