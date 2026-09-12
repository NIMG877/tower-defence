"""校验引擎测试——与客户端 AbilityConfigValidatorTests 同案例集同源。"""

from app import schema as schema_mod
from app.validator import validate

SCHEMA = schema_mod.load_schema()


def valid_dto() -> dict:
    return {
        "abilityId": "gen_validate_1",
        "abilityName": "校验样例",
        "description": "d",
        "rules": [
            {"triggers": [{"triggerEvent": "OnInitialize", "groups": []}],
             "reentry": "IgnoreWhileRunning",
             "steps": [{"op": "write_blackboard",
                        "args": {"entries": [
                            {"key": "key", "value": "probe", "type": "String", "fromBlackboard": False},
                            {"key": "method", "value": "set", "type": "String", "fromBlackboard": False},
                            {"key": "source", "value": "value", "type": "String", "fromBlackboard": False},
                            {"key": "value", "value": "1", "type": "Int", "fromBlackboard": False},
                        ]}}]},
        ],
    }


def messages(issues):
    return [f"[{i['severity']}] {i['path']}: {i['message']}" for i in issues]


def test_valid_dto_passes_without_issues():
    ok, issues, sanitized = validate(valid_dto(), SCHEMA)
    assert ok, "\n".join(messages(issues))
    assert issues == []
    assert len(sanitized["rules"]) == 1


def test_null_and_non_object_dto_rejected():
    assert not validate(None, SCHEMA)[0]
    assert not validate([], SCHEMA)[0]


def test_unknown_op_rejects_whole_ability():
    dto = valid_dto()
    dto["rules"][0]["steps"][0]["op"] = "make_big_explosion"
    ok, issues, sanitized = validate(dto, SCHEMA)
    assert not ok
    assert any(i["severity"] == "error" and "make_big_explosion" in i["message"] for i in issues)
    assert sanitized["rules"][0]["steps"][0]["op"] == "make_big_explosion"


def test_array_param_type_label_coerced_to_schema_type():
    """LLM 把数组参数标成标量 type（实测样例）：值按 schema 验证后强制对齐标签。"""
    dto = {
        "abilityId": "gen_t", "abilityName": "n", "description": "d",
        "rules": [{"triggers": [{"triggerEvent": "OnInitialize", "groups": []}],
                   "steps": [{"op": "apply_buff",
                              "args": {"entries": [
                                  {"key": "attributes", "value": "Attack", "type": "String",
                                   "fromBlackboard": False},
                                  {"key": "ops", "value": "AddPercent", "type": "String",
                                   "fromBlackboard": False},
                                  {"key": "magnitudes", "value": "0.25", "type": "Float",
                                   "fromBlackboard": False},
                                  {"key": "buffTime", "value": "10", "type": "Float",
                                   "fromBlackboard": False},
                              ]}}]}],
    }
    ok, issues, sanitized = validate(dto, SCHEMA)
    assert ok, "\n".join(messages(issues))
    entries = {e["key"]: e for e in sanitized["rules"][0]["steps"][0]["args"]["entries"]}
    assert entries["attributes"]["type"] == "stringArray"
    assert entries["ops"]["type"] == "stringArray"
    assert entries["magnitudes"]["type"] == "floatArray"
    assert entries["buffTime"]["type"] == "float"  # 仅大小写差异：静默归一
    assert len([i for i in issues if "coerced" in i["message"]]) == 3


def test_empty_rules_and_triggers_are_errors():
    dto = valid_dto()
    dto["rules"] = []
    assert not validate(dto, SCHEMA)[0]
    dto = valid_dto()
    dto["rules"][0]["triggers"] = []
    assert not validate(dto, SCHEMA)[0]


def test_unknown_enum_names_are_errors():
    dto = valid_dto()
    dto["rules"][0]["triggers"][0]["triggerEvent"] = "OnBogus"
    assert not validate(dto, SCHEMA)[0]
    dto = valid_dto()
    dto["rules"][0]["reentry"] = "Bogus"
    assert not validate(dto, SCHEMA)[0]


def test_unknown_param_key_dropped_with_warning():
    dto = valid_dto()
    dto["rules"][0]["steps"][0]["args"]["entries"].append(
        {"key": "frobnicate", "value": "1", "type": "Int", "fromBlackboard": False})
    ok, issues, sanitized = validate(dto, SCHEMA)
    assert ok, "\n".join(messages(issues))
    assert any("frobnicate" in i["message"] for i in issues)
    assert not any(e["key"] == "frobnicate" for e in sanitized["rules"][0]["steps"][0]["args"]["entries"])
    assert len(sanitized["rules"][0]["steps"][0]["args"]["entries"]) == 4


def test_out_of_range_float_clamped_with_warning():
    dto = valid_dto()
    dto["rules"][0]["steps"] = [{
        "op": "apply_damage",
        "args": {"entries": [
            {"key": "multiplier", "value": "5000", "type": "Float", "fromBlackboard": False},
            {"key": "baseValueMode", "value": "fixed", "type": "String", "fromBlackboard": False},
            {"key": "baseValue", "value": "120", "type": "Float", "fromBlackboard": False},
        ]}}]
    ok, issues, sanitized = validate(dto, SCHEMA)
    assert ok, "\n".join(messages(issues))
    entries = {e["key"]: e["value"] for e in sanitized["rules"][0]["steps"][0]["args"]["entries"]}
    assert entries["multiplier"] == "1000"
    assert entries["baseValue"] == "120"
    assert any("clamped" in i["message"] for i in issues)


def test_sp_clamped():
    dto = valid_dto()
    dto["sp"] = {"totalSp": 5000, "chargeNum": 99, "abilityAmount": 9999}
    ok, issues, sanitized = validate(dto, SCHEMA)
    assert ok, "\n".join(messages(issues))
    assert sanitized["sp"]["totalSp"] == 999
    assert sanitized["sp"]["chargeNum"] == 9
    assert sanitized["sp"]["abilityAmount"] == 120.0


def test_unparsable_value_is_warning_value_kept():
    dto = valid_dto()
    dto["rules"][0]["steps"] = [{
        "op": "delay",
        "args": {"entries": [{"key": "seconds", "value": "soon", "type": "Float", "fromBlackboard": False}]}}]
    ok, issues, sanitized = validate(dto, SCHEMA)
    assert ok
    assert any("does not parse" in i["message"] for i in issues)
    assert sanitized["rules"][0]["steps"][0]["args"]["entries"][0]["value"] == "soon"


def test_token_outside_vocabulary_is_warning():
    dto = valid_dto()
    dto["rules"][0]["steps"] = [{
        "op": "apply_damage",
        "args": {"entries": [{"key": "targetMode", "value": "everyone", "type": "String",
                              "fromBlackboard": False}]}}]
    ok, issues, _ = validate(dto, SCHEMA)
    assert ok
    assert any("outside the accepted tokens" in i["message"] for i in issues)


def test_scalar_coerced_to_string_like_newtonsoft():
    dto = valid_dto()
    dto["rules"][0]["steps"][0]["args"]["entries"][3]["value"] = 42  # LLM 偶发输出数字
    ok, issues, sanitized = validate(dto, SCHEMA)
    assert ok, "\n".join(messages(issues))
    assert sanitized["rules"][0]["steps"][0]["args"]["entries"][3]["value"] == "42"


def test_condition_on_component_op_is_warning():
    dto = valid_dto()
    # groups 与运行时 ConditionGroup 同形：对象数组、units 内 AND。
    dto["rules"][0]["steps"][0]["condition"] = [
        {"units": [{"op": "Equal", "leftKey": "k", "rightValue": "v", "rightKey": None}]}]
    ok, issues, sanitized = validate(dto, SCHEMA)
    assert ok
    assert any("condition ignored" in i["message"] for i in issues)
    assert sanitized["rules"][0]["steps"][0]["condition"][0]["units"][0]["leftKey"] == "k"


def test_condition_group_bare_array_shape_is_error():
    # LLM 输出 [[unit,...]] 的数组形态与客户端反序列化不兼容，必须报 error。
    dto = valid_dto()
    dto["rules"][0]["steps"] = [{
        "op": "wait_until",
        "condition": [[{"op": "Equal", "leftKey": "k", "rightValue": "v", "rightKey": None}]]}]
    ok, issues, _ = validate(dto, SCHEMA)
    assert not ok
    assert any("bare array" in i["message"] for i in issues)


def test_blackboard_read_without_producer_is_warning():
    dto = valid_dto()
    dto["rules"][0]["steps"] = [
        {"op": "select_targets",
         "args": {"entries": [{"key": "subjectBlackboardKey", "value": "snapshot:lowest_enemy_id",
                               "type": "String", "fromBlackboard": False}]}},
        {"op": "apply_damage",
         "args": {"entries": [{"key": "targetMode", "value": "blackboard", "type": "String",
                               "fromBlackboard": False},
                              {"key": "blackboardKey", "value": "ghost_key", "type": "String",
                               "fromBlackboard": False}]}}]
    ok, issues, _ = validate(dto, SCHEMA)
    assert ok, "\n".join(messages(issues))
    assert not any("snapshot:lowest_enemy_id" in i["message"] for i in issues)
    assert any("ghost_key" in i["message"] for i in issues)


def test_producer_consumer_pair_passes():
    dto = valid_dto()
    dto["rules"][0]["steps"] = [
        {"op": "select_targets",
         "args": {"entries": [{"key": "outputEntitiesKey", "value": "targets", "type": "String",
                               "fromBlackboard": False}]}},
        {"op": "apply_damage",
         "args": {"entries": [{"key": "targetMode", "value": "blackboard", "type": "String",
                               "fromBlackboard": False},
                              {"key": "blackboardKey", "value": "targets", "type": "String",
                               "fromBlackboard": False}]}}]
    ok, issues, _ = validate(dto, SCHEMA)
    assert ok, "\n".join(messages(issues))
    assert issues == []


def test_alias_resolution_like_runtime_registry():
    dto = valid_dto()
    dto["rules"][0]["steps"] = [{
        "op": "ApplyDamage",  # PascalCase 别名
        "args": {"entries": [{"key": "multiplier", "value": "2", "type": "Float",
                              "fromBlackboard": False}]}}]
    ok, issues, sanitized = validate(dto, SCHEMA)
    assert ok, "\n".join(messages(issues))
    assert sanitized["rules"][0]["steps"][0]["op"] == "apply_damage"
