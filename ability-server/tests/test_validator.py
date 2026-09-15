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


def test_array_param_type_label_normalized_to_client_vocabulary():
    """数组参数的 label 与参数类型同词表（ParamValueType）：数组值客户端按 CSV
    自解析，label 记元素标量类型——写 "stringArray" 这类自造名会让客户端枚举
    反序列化失败（实测事故）。词表等效（含大小写）静默归一。"""
    dto = {
        "abilityId": "gen_t", "abilityName": "n", "description": "d",
        "rules": [{"triggers": [{"triggerEvent": "OnInitialize", "groups": []}],
                   "steps": [{"op": "apply_buff",
                              "args": {"entries": [
                                  {"key": "attributes", "value": "Attack", "type": "Bool",
                                   "fromBlackboard": False},
                                  {"key": "ops", "value": "AddPercent", "type": "String",
                                   "fromBlackboard": False},
                                  {"key": "magnitudes", "value": "0.25", "type": "Float",
                                   "fromBlackboard": False},
                                  {"key": "buffTime", "value": "10", "type": "float",
                                   "fromBlackboard": False},
                              ]}}]}],
    }
    ok, issues, sanitized = validate(dto, SCHEMA)
    assert ok, "\n".join(messages(issues))
    entries = {e["key"]: e for e in sanitized["rules"][0]["steps"][0]["args"]["entries"]}
    assert entries["attributes"]["type"] == "String"  # Bool → 归一（真错，记 warning）
    assert entries["ops"]["type"] == "String"         # 词表等效：静默
    assert entries["magnitudes"]["type"] == "Float"   # 词表等效：静默
    assert entries["buffTime"]["type"] == "Float"     # 仅大小写差异：静默归一
    assert len([i for i in issues if "normalized" in i["message"]]) == 1
    assert all(i["type"] != "stringArray" for i in entries.values())


def test_any_param_label_outside_client_vocabulary_warns():
    """any 参数的 label 不覆写，但越 ParamValueType 词表记 warning——否则客户端
    枚举解析失败、整份注入失败。"""
    dto = valid_dto()
    dto["rules"][0]["steps"] = [
        {"op": "write_blackboard", "args": {"entries": [
            {"key": "value", "value": "42", "type": "integer",
             "fromBlackboard": False}]}}]
    ok, issues, sanitized = validate(dto, SCHEMA)
    assert ok  # warning 不拒绝
    entry = sanitized["rules"][0]["steps"][0]["args"]["entries"][0]
    assert entry["type"] == "integer"  # any 不覆写
    assert any("outside the ParamValueType vocabulary" in i["message"] for i in issues)


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
    # 快照黑板键已随 knownBlackboardKeys 清空移除，snapshot: 前缀同样是无生产者读键。
    assert any("snapshot:lowest_enemy_id" in i["message"] for i in issues)
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


def test_pascal_case_alias_is_rejected():
    """类名别名已退役：只认 canonical snake_case（与 SYSTEM 纪律对齐）。"""
    dto = valid_dto()
    dto["rules"][0]["steps"] = [{
        "op": "ApplyDamage",
        "args": {"entries": [{"key": "multiplier", "value": "2", "type": "Float",
                              "fromBlackboard": False}]}}]
    ok, issues, _ = validate(dto, SCHEMA)
    assert not ok
    assert any("ApplyDamage" in i["message"] for i in issues)


# ---------- hostAssets 边界校验（error 级） ----------

def test_spawn_index_out_of_range_is_error():
    dto = valid_dto()
    dto["rules"][0]["steps"] = [{
        "op": "spawn_entity",
        "args": {"entries": [{"key": "spawnIndex", "value": "2", "type": "Int",
                              "fromBlackboard": False}]}}]
    ok, issues, _ = validate(dto, SCHEMA, host_assets={"canSpawnEntityIds": ["s-0", "s-1"]})
    assert not ok
    assert any("spawnIndex=2 out of range" in i["message"] for i in issues)
    # 下标落在注册表范围内 → 通过。
    ok, issues, _ = validate(dto, SCHEMA, host_assets={"canSpawnEntityIds": ["s-0", "s-1", "s-2"]})
    assert ok, "\n".join(messages(issues))


def test_spawn_index_from_blackboard_skips_bound_check():
    """fromBlackboard=true 时 value 是黑板键名，下标运行时才解析，静态不查。"""
    dto = valid_dto()
    dto["rules"][0]["steps"] = [{
        "op": "spawn_entity",
        "args": {"entries": [{"key": "spawnIndex", "value": "enemy_idx", "type": "String",
                              "fromBlackboard": True}]}}]
    ok, issues, _ = validate(dto, SCHEMA, host_assets={"canSpawnEntityIds": []})
    assert ok, "\n".join(messages(issues))


def test_spawn_index_checks_contract_v2_registry_shape():
    """契约 v2 的 canSpawnEntities（实体投影列表）同样作为注册表长度来源。"""
    dto = valid_dto()
    dto["rules"][0]["steps"] = [{
        "op": "spawn_entity",
        "args": {"entries": [{"key": "spawnIndex", "value": "0", "type": "Int",
                              "fromBlackboard": False}]}}]
    ok, issues, _ = validate(dto, SCHEMA, host_assets={"canSpawnEntities": []})
    assert not ok  # 空注册表：任何下标都越界
    assert any("spawnIndex=0 out of range" in i["message"] for i in issues)


def test_bullet_data_index_out_of_range_is_error():
    dto = valid_dto()
    dto["rules"][0]["steps"] = [{
        "op": "fire_bullets",
        "args": {"entries": [{"key": "bulletDataIndex", "value": "3", "type": "Int",
                              "fromBlackboard": False}]}}]
    ok, issues, _ = validate(dto, SCHEMA, host_assets={"bulletCount": 2})
    assert not ok
    assert any("bulletDataIndex=3 out of range" in i["message"] for i in issues)
    ok, issues, _ = validate(dto, SCHEMA, host_assets={"bullets": [{}, {}, {}, {}]})  # v2 形状
    assert ok, "\n".join(messages(issues))


def test_animation_resource_outside_host_vocabulary_is_error():
    dto = valid_dto()
    dto["rules"][0]["steps"] = [{
        "op": "apply_animation_override",
        "args": {"entries": [
            {"key": "slots", "value": "AttackRemote", "type": "StringArray", "fromBlackboard": False},
            {"key": "resources", "value": "attack_far,attack_cut", "type": "StringArray",
             "fromBlackboard": False}]}}]
    host = {"animations": {"named": ["attack_far"], "groups": ["attack_cut"]}}
    ok, issues, _ = validate(dto, SCHEMA, host_assets=host)
    assert ok, "\n".join(messages(issues))
    host = {"animations": {"named": ["attack_far"], "groups": []}}
    ok, issues, _ = validate(dto, SCHEMA, host_assets=host)
    assert not ok
    assert any("attack_cut" in i["message"] and "hostAssets.animations" in i["message"]
               for i in issues)


def test_boundary_checks_skip_when_host_assets_lack_the_lists():
    """hostAssets 未携带对应清单时无从校验（契约 v2 前没有 animations 字段）。"""
    dto = valid_dto()
    dto["rules"][0]["steps"] = [{
        "op": "apply_animation_override",
        "args": {"entries": [{"key": "resources", "value": "no_such_anim", "type": "StringArray",
                              "fromBlackboard": False}]}}]
    for host in ({}, None):
        ok, issues, _ = validate(dto, SCHEMA, host_assets=host)
        assert ok, "\n".join(messages(issues))


def test_icon_key_always_stripped_from_sanitized():
    """生成技能统一图标：sanitize 恒剥离 iconKey，技能卡走 AbilityIconPool null 兜底。"""
    dto = valid_dto()
    dto["iconKey"] = "charger"
    ok, issues, sanitized = validate(dto, SCHEMA)
    assert ok, "\n".join(messages(issues))
    assert "iconKey" not in sanitized


# ---------- 语义验证层（M2：上下文 path / listCount 时序 / 低频挡位） ----------

def write_bb_rule(trigger_events, entries, steps=None):
    """构造含 write_blackboard 步骤的规则（可多 trigger 验证并集语义）。"""
    return {"triggers": [{"triggerEvent": ev, "groups": []} for ev in trigger_events],
            "steps": steps or [{"op": "write_blackboard",
                                "args": {"entries": entries}}]}


def test_event_context_path_outside_vocab_warns():
    """OnAfterAttack 是 DamageEventBase 非 HurtEventBase：没有 damage/origin 路径，
    但有 isdeadly——按 WriteBlackboard.ResolveEventValue 逐分支核对。"""
    dto = valid_dto()
    dto["rules"] = [write_bb_rule(
        ["OnAfterAttack"],
        [{"key": "key", "value": "probe", "type": "String", "fromBlackboard": False},
         {"key": "source", "value": "event", "type": "String", "fromBlackboard": False},
         {"key": "path", "value": "damage", "type": "String", "fromBlackboard": False}])]
    ok, issues, _ = validate(dto, SCHEMA)
    assert ok, "\n".join(messages(issues))  # warning 不拒
    assert any("source=event path 'damage'" in i["message"] for i in issues)
    # 换成该事件合法路径 → 无此告警
    dto["rules"][0]["steps"][0]["args"]["entries"][2]["value"] = "isdeadly"
    ok, issues, _ = validate(dto, SCHEMA)
    assert ok and not issues, "\n".join(messages(issues))


def test_event_context_multi_trigger_union():
    """多 trigger 规则的 eventContext 取并集：cumbo（BeforeAttack 专有）与
    damage（Hurt 分支）同时合法。"""
    dto = valid_dto()
    wb = [{"key": "key", "value": "probe", "type": "String", "fromBlackboard": False},
          {"key": "source", "value": "event", "type": "String", "fromBlackboard": False},
          {"key": "path", "value": "cumbo", "type": "String", "fromBlackboard": False}]
    dmg = [dict(e) for e in wb]
    dmg[2] = {"key": "path", "value": "damage", "type": "String", "fromBlackboard": False}
    dto["rules"] = [write_bb_rule(["OnBeforeAttack", "OnAfterHurt"], wb,
                                  steps=[{"op": "write_blackboard", "args": {"entries": wb}},
                                         {"op": "write_blackboard", "args": {"entries": dmg}}])]
    ok, issues, _ = validate(dto, SCHEMA)
    assert ok and not issues, "\n".join(messages(issues))


def test_entity_context_path_checked():
    dto = valid_dto()
    dto["rules"][0]["steps"] = [{
        "op": "write_blackboard",
        "args": {"entries": [
            {"key": "key", "value": "probe", "type": "String", "fromBlackboard": False},
            {"key": "source", "value": "entity", "type": "String", "fromBlackboard": False},
            {"key": "path", "value": "hp", "type": "String", "fromBlackboard": False}]}}]
    ok, issues, _ = validate(dto, SCHEMA)
    assert ok
    assert any("source=entity path 'hp'" in i["message"] for i in issues)
    dto["rules"][0]["steps"][0]["args"]["entries"][2]["value"] = "currenthprate"
    ok, issues, _ = validate(dto, SCHEMA)
    assert ok and not issues, "\n".join(messages(issues))


def test_list_count_requires_earlier_select_targets():
    """listCount 的 path 须为此前 select_targets（outputEntitiesKey）产出。"""
    select = {"op": "select_targets",
              "args": {"entries": [{"key": "outputEntitiesKey", "value": "targets",
                                    "type": "String", "fromBlackboard": False}]}}
    writer_entries = [
        {"key": "key", "value": "enemy_count", "type": "String", "fromBlackboard": False},
        {"key": "source", "value": "listCount", "type": "String", "fromBlackboard": False},
        {"key": "path", "value": "targets", "type": "String", "fromBlackboard": False}]
    writer = {"op": "write_blackboard", "args": {"entries": writer_entries}}

    # select 在前 → 干净
    dto = valid_dto()
    dto["rules"][0]["steps"] = [select, writer]
    ok, issues, _ = validate(dto, SCHEMA)
    assert ok and not issues, "\n".join(messages(issues))

    # select 缺失 → warning
    dto = valid_dto()
    dto["rules"][0]["steps"] = [writer]
    ok, issues, _ = validate(dto, SCHEMA)
    assert ok
    assert any("no earlier select_targets" in i["message"] for i in issues)

    # select 在后（步骤序颠倒）→ warning
    dto = valid_dto()
    dto["rules"][0]["steps"] = [writer, select]
    ok, issues, _ = validate(dto, SCHEMA)
    assert ok
    assert any("no earlier select_targets" in i["message"] for i in issues)

    # path 来自黑板（fromBlackboard=true）：运行时才解析路径名，静态上下文检查跳过；
    # 但它读的黑板键（targets）仍受写前读序约束——带上 select 产出则干净。
    dto = valid_dto()
    dto["rules"][0]["steps"] = [select, writer]
    dto["rules"][0]["steps"][1]["args"]["entries"][2]["fromBlackboard"] = True
    ok, issues, _ = validate(dto, SCHEMA)
    assert ok and not issues, "\n".join(messages(issues))


def test_corpus_rare_usage_hints_are_info_level():
    """语料零使用的触发事件 / reentry 记 info（不拒、不计 warning 断言）。"""
    dto = valid_dto()
    dto["rules"][0]["triggers"][0]["triggerEvent"] = "OnSummonDeath"  # 语料 1 次也算有先例
    dto["rules"][0]["reentry"] = "Restart"                            # 语料零使用
    usage = {"triggerEvents": {"OnInitialize", "OnAbilityBegin"},
             "reentries": {"IgnoreWhileRunning", "Parallel"}}
    ok, issues, _ = validate(dto, SCHEMA, corpus_usage=usage)
    assert ok, "\n".join(messages(issues))
    infos = [i for i in issues if i["severity"] == "info"]
    assert any("Restart" in i["message"] for i in infos)
    assert any("OnSummonDeath" in i["message"] for i in infos)
    assert all(i["severity"] != "info" for i in issues if i not in infos)

    # 不传 corpus_usage（v1 路径/无语料）→ 无 info 提示
    ok, issues, _ = validate(dto, SCHEMA)
    assert ok and not issues, "\n".join(messages(issues))
