"""tools 工具集测试：update_plan/submit_skill/validate_draft 会话状态、
明细查询、schema 词表、语料检索、文档工具（tools.py 后端）。"""

import json

from app import tools
from app.config import Config

SCHEMA_JSON = None


def schema():
    global SCHEMA_JSON
    if SCHEMA_JSON is None:
        from app import schema as schema_mod
        SCHEMA_JSON = schema_mod.load_schema()
    return SCHEMA_JSON


def make_ctx(**request_overrides) -> tools.ToolContext:
    request = {
        "protocolVersion": schema()["protocolVersion"],
        "battleSnapshot": {
            "selfId": "c-1", "mapI": 8, "mapJ": 9,
            "canSetHigher": ["4,5", "6,7"], "canSetLower": ["3,4"],
            "entities": [
                {"id": "c-1", "camp": 1, "isStatic": False, "hp": 1500, "maxHp": 1500,
                 "hpRate": 1.0, "pos": {"x": 4, "y": 4}, "attack": 500, "defence": 100,
                 "magicRes": 10, "job": 1, "label": None, "massLevel": 2},
                {"id": "m-1", "camp": 2, "isStatic": False, "hp": 400, "maxHp": 800,
                 "hpRate": 0.5, "pos": {"x": 5, "y": 4}, "attack": 120, "defence": 30,
                 "magicRes": 0, "job": -1, "label": "slime", "massLevel": 1},
                {"id": "m-2", "camp": 2, "isStatic": False, "hp": 1200, "maxHp": 1200,
                 "hpRate": 1.0, "pos": {"x": 7, "y": 7}, "attack": 200, "defence": 150,
                 "magicRes": 0, "job": -1, "label": "soldier", "massLevel": 2},
            ],
        },
        "hostAssets": {"job": 1, "cost": 15, "damageType": 0,
                       "canSpawnEntityIds": ["s-1"], "bulletCount": 1, "iconKeys": []},
        "constraints": {"request": "测试诉求"},
    }
    request.update(request_overrides)
    cfg = Config(llm_mock=True, log_dir=None, db_path=None)
    return tools.ToolContext(request, cfg, schema(), None)


def call(ctx, name, args) -> dict:
    return json.loads(tools.execute_tool(ctx, name, args))


def valid_config() -> dict:
    return {
        "abilityId": "gen_t", "abilityName": "n", "description": "d",
        "sp": {"totalSp": 0, "consumeMode": "NoConsume", "openMode": "Auto"},
        "rules": [{"triggers": [{"triggerEvent": "OnInitialize", "groups": []}],
                   "steps": [{"op": "modify_cost",
                              "args": {"entries": [{"key": "amount", "value": "5",
                                                    "type": "Int", "fromBlackboard": False}]}}]}],
    }


# ---------- 会话状态工具 ----------

def test_update_plan_records_and_counts():
    ctx = make_ctx()
    assert call(ctx, "update_plan", {"plan": "先读文档再设计"})["ok"] is True
    assert ctx.plan == "先读文档再设计" and ctx.plan_updates == 1
    assert call(ctx, "update_plan", {"plan": ""})["error"]
    assert ctx.plan_updates == 1  # 失败的修订不计数


def test_update_plan_limit_reached():
    ctx = make_ctx()
    ctx.cfg.agent_max_plan_updates = 1
    assert call(ctx, "update_plan", {"plan": "v1"})["ok"] is True
    assert "limit" in call(ctx, "update_plan", {"plan": "v2"})["error"]
    assert ctx.plan == "v1"


def test_submit_skill_accepts_and_stashes_sanitized():
    ctx = make_ctx()
    dto = valid_config()
    dto["iconKey"] = "charger"  # sanitize 恒剥离
    out = call(ctx, "submit_skill", {"config": dto})
    assert out["accepted"] is True
    assert ctx.submitted is not None and "iconKey" not in ctx.submitted
    assert ctx.draft is None  # submit 不写草稿位（草稿只来自 validate_draft）


def test_submit_skill_rejects_bad_config_and_loops_on():
    ctx = make_ctx()
    dto = valid_config()
    dto["rules"][0]["steps"][0]["op"] = "make_big_explosion"
    out = call(ctx, "submit_skill", {"config": dto})
    assert out["accepted"] is False
    assert any(i["severity"] == "error" for i in out["issues"])
    assert ctx.submitted is None


def test_validate_draft_stashes_only_on_ok():
    ctx = make_ctx()
    assert call(ctx, "validate_draft", {"config": valid_config()})["ok"] is True
    assert ctx.draft is not None
    bad = valid_config()
    bad["rules"] = []
    assert call(ctx, "validate_draft", {"config": bad})["ok"] is False
    assert ctx.draft["abilityId"] == "gen_t"  # 失败不覆盖既有草稿


def test_non_object_config_rejected():
    ctx = make_ctx()
    out = call(ctx, "submit_skill", {"config": "json string"})
    assert out["accepted"] is False


def test_submit_skill_without_config_delivers_validated_draft():
    """免重写交付：无草稿拒绝；有草稿直接提交且不污染草稿位。"""
    ctx = make_ctx()
    out = call(ctx, "submit_skill", {})
    assert out["accepted"] is False
    assert "validate_draft" in out["issues"][0]["message"]

    assert call(ctx, "validate_draft", {"config": valid_config()})["ok"] is True
    out = call(ctx, "submit_skill", {})
    assert out["accepted"] is True
    assert ctx.submitted["abilityId"] == "gen_t"
    assert ctx.draft is not None  # 草稿位不动（与带 config 提交同语义）


# ---------- 增量写作（start_draft/design_rules/put_step → validate → submit） ----------

def test_incremental_draft_full_flow():
    """骨架立规则 → 逐步转写 → 全量校验 → 免重写提交，一条龙走通。"""
    ctx = make_ctx()
    out = call(ctx, "start_draft", {"abilityId": "gen_inc", "abilityName": "增量",
                                    "description": "d", "sp": {"totalSp": 33}})
    assert out["ok"] is True and ctx.working["rules"] == []

    out = call(ctx, "design_rules", {"rules": [
        {"triggerEvent": "OnInitialize", "steps": [
            {"op": "modify_cost", "intent": "部署即回费"}]},
        {"triggerEvent": "OnTick", "steps": [{"op": "apply_damage", "intent": ""}]},
    ]})
    assert out["ok"] is True  # 空 intent 是 warning，不阻断
    assert any(i["severity"] == "warning" and "no intent" in i["message"]
               for i in out["issues"])
    assert len(ctx.working["rules"]) == 2

    out = call(ctx, "put_step", {"ruleIndex": 0, "step": {
        "op": "modify_cost", "args": {"entries": [{"key": "amount", "value": "5",
                                                   "type": "Int", "fromBlackboard": False}]}}})
    assert out["ok"] is True and out["ruleSteps"] == ["modify_cost"]

    # 骨架位置对照：rule1 骨架位是 make_boom，写 modify_cost 记 warning
    out = call(ctx, "put_step", {"ruleIndex": 1, "step": {
        "op": "modify_cost", "args": {"entries": []}}})
    assert any("outline expects" in i["message"] for i in out["issues"])

    out = call(ctx, "validate_draft", {})
    assert out["ok"] is True, out["issues"]
    assert ctx.draft["abilityId"] == "gen_inc"

    out = call(ctx, "submit_skill", {})
    assert out["accepted"] is True
    assert ctx.submitted["abilityId"] == "gen_inc"


def test_incremental_draft_guards():
    """越界/未开工/未立骨架全部 error 回喂；drop_step/drop_rule 修正可用。"""
    ctx = make_ctx()
    assert "error" in call(ctx, "put_step", {"ruleIndex": 0, "step": {"op": "delay"}})
    call(ctx, "start_draft", {"abilityId": "g", "abilityName": "n", "description": "d",
                              "sp": {"totalSp": 1}})
    assert "error" in call(ctx, "put_step", {"ruleIndex": 0, "step": {"op": "delay"}})
    call(ctx, "design_rules", {"rules": [
        {"triggerEvent": "OnInitialize", "steps": [{"op": "modify_cost", "intent": "x"}]}]})
    assert "error" in call(ctx, "put_step", {"ruleIndex": 3, "step": {"op": "delay"}})
    out = call(ctx, "put_step", {"ruleIndex": 0, "step": {
        "op": "modify_cost", "args": {"entries": [{"key": "amount", "value": "5",
                                                   "type": "Int", "fromBlackboard": False}]}}})
    assert out["ok"] is True
    assert "error" in call(ctx, "put_step", {"ruleIndex": 0, "index": 5,
                                             "step": {"op": "delay"}})
    out = call(ctx, "drop_step", {"ruleIndex": 0, "index": 0})
    assert out["ok"] is True and out["ruleSteps"] == []
    assert "error" in call(ctx, "drop_rule", {"index": 2})


# ---------- 数据源工具 ----------

def test_entity_and_entities_at():
    ctx = make_ctx()
    out = call(ctx, "entity", {"id": "m-1"})
    assert out["entity"]["id"] == "m-1" and out["entity"]["massLevel"] == 1
    assert "error" in call(ctx, "entity", {"id": "ghost"})["entity"]

    out = call(ctx, "entities_at", {"x": 4, "y": 4, "radius": 2.0, "camp": 2})
    assert [e["id"] for e in out["entities"]] == ["m-1"]
    out = call(ctx, "entities_at", {"x": 4, "y": 4, "radius": 10.0})
    assert len(out["entities"]) == 3  # 含自身（v2：self 在 entities）


def test_deploy_cells_near_tool():
    ctx = make_ctx()
    out = call(ctx, "deploy_cells_near", {"x": 4, "y": 4, "radius": 1.5})
    assert out == {"higher": ["4,5"], "lower": ["3,4"]}


def test_compute_cross_items_v2_items():
    ctx = make_ctx()
    out = call(ctx, "compute_cross_items",
               {"items": ["enemy_group_stats", "pairwise_distance(m-1,m-2)"]})
    stats = out["results"]["enemy_group_stats"]
    assert stats["count"] == 2 and stats["totalHp"] == 1600
    assert out["results"]["pairwise_distance(m-1,m-2)"] == 3.61  # (5,4)->(7,7) = √13


def test_read_schema_vocab_sections():
    ctx = make_ctx()
    out = call(ctx, "read_schema_vocab", {})
    assert out["triggerEvents"] and out["conditionOps"] and out["ruleReentry"]
    assert out["spRecoverModes"] == [
        "Natural", "OnAttackSuccessfully", "OnAfterHurt", "Other"]
    assert out["spConsumeModes"] == [
        "Natural", "OnAttackSuccessfully", "OnAfterHurt", "Instant", "Other", "NoConsume"]
    assert out["abilityOpenModes"] == [
        "Auto", "OnAttackAnimBegin", "OnBeforeHurt", "Manual", "Other", "OnDeadlyHurt"]
    assert out["paramValueTypeEncoding"] and out["clamps"]
    assert out["entityContext"]  # M2 语义词表随 schema 出（轻节默认返回）
    assert "eventContext" not in out  # 重节分段按需取（体积自限，eventContext 不随默认返回）
    assert "eventContext" in out["note"]
    heavy = call(ctx, "read_schema_vocab", {"section": "eventContext"})
    assert "isdeadly" in heavy["eventContext"]["OnAfterAttack"]
    assert call(ctx, "read_schema_vocab", {"section": "spRecoverModes"}) == {
        "spRecoverModes": ["Natural", "OnAttackSuccessfully", "OnAfterHurt", "Other"]}
    assert "currenthprate" in out["entityContext"]
    assert "1=友军 2=敌人" in out["fieldSemantics"]["camp"]
    assert "0=先锋" in out["fieldSemantics"]["job"]
    assert "unknown section" in call(ctx, "read_schema_vocab", {"section": "nope"})["error"]


def test_tool_errors_never_raise():
    # 未知工具名/坏参数都返回 error JSON，不抛异常（让 LLM 自行修正调用）。
    ctx = make_ctx()
    assert "error" in call(ctx, "no_such_tool", {})
    assert "error" in call(ctx, "compute_cross_items", None)


# ---------- 文档工具后端（真实组件库 data/ability.db） ----------

def make_db_ctx() -> tools.ToolContext:
    """默认 db_path 指向真实组件库，用于文档工具测试。"""
    return tools.ToolContext(
        {"protocolVersion": schema()["protocolVersion"], "battleSnapshot": {},
         "hostAssets": {}, "constraints": {}},
        Config(llm_mock=True, log_dir=None), schema(), None)


def test_list_components_returns_full_index():
    components = tools.component_index(make_db_ctx())["components"]
    assert len(components) == 35
    entry = next(c for c in components if c["op"] == "apply_damage")
    assert entry["summary"]
    assert "class" not in entry  # 索引只留 op/摘要（保持轻量）


def test_read_component_doc_by_op_and_alias():
    ctx = make_db_ctx()
    by_op = tools.component_doc(ctx, "apply_damage")
    assert by_op["op"] == "apply_damage"
    assert by_op["behavior"] and by_op["params"]
    assert any(p["key"] == "damageType" for p in by_op["params"])
    assert "error" in tools.component_doc(ctx, "ApplyDamage")  # 类名别名已退役
    assert "error" in tools.component_doc(ctx, "nope")


def test_doc_tools_degrade_without_db():
    ctx = make_ctx()  # db_path=None
    assert "error" in tools.component_index(ctx)
    assert "error" in tools.component_doc(ctx, "apply_damage")
    assert "error" in tools._contract_doc(ctx, None)


def test_read_contract_doc_sections():
    ctx = make_db_ctx()
    out = call(ctx, "read_contract_doc", {})
    assert {s["section"] for s in out["sections"]} >= {"stored_shape", "sequence_semantics"}
    assert "section=" in out["note"]
    full = call(ctx, "read_contract_doc", {"section": "stored_shape"})
    assert "AbilityConfig" in full["content"]
    assert "unknown section" in call(ctx, "read_contract_doc", {"section": "nope"})["error"]


# ---------- 语料检索（fixture 语料，不依赖 data/skills.json） ----------

def make_corpus_ctx() -> tools.ToolContext:
    ctx = make_ctx()
    corpus_data = {
        "skills": [
            {"abilityId": "fang_s1", "abilityName": "冲锋号令",
             "description": "开技能快速回费，回复部署费用",
             "sp": {"totalSp": 25},
             "rules": [{"triggers": [], "steps": [{"op": "modify_cost", "args": {"entries": []}}]}]},
            {"abilityId": "hibisc_s1", "abilityName": "治疗强化", "description": "提升攻击力",
             "sp": {"totalSp": 30},
             "rules": [{"triggers": [], "steps": [{"op": "apply_buff", "args": {"entries": []}}]}]},
        ],
        "existingIds": {"fang_s1", "hibisc_s1"},
    }
    return tools.ToolContext(
        {"protocolVersion": schema()["protocolVersion"], "battleSnapshot": {},
         "hostAssets": {}, "constraints": {}},
        Config(llm_mock=True, log_dir=None, db_path=None), schema(), corpus_data)


def test_search_skills_matches_text_and_ops():
    ctx = make_corpus_ctx()
    out = call(ctx, "search_skills", {"query": "回费"})
    assert [s["abilityId"] for s in out["skills"]] == ["fang_s1"]
    out = call(ctx, "search_skills", {"query": "modify_cost"})
    assert [s["abilityId"] for s in out["skills"]] == ["fang_s1"]
    assert out["skills"][0]["ops"] == ["modify_cost"]


def test_read_skill_prefers_host_skills_then_corpus():
    ctx = make_corpus_ctx()
    ctx.host_skills = {"host_skill": {"abilityId": "host_skill", "rules": []}}
    out = call(ctx, "read_skill", {"abilityId": "host_skill"})
    assert out["skill"]["abilityId"] == "host_skill"
    out = call(ctx, "read_skill", {"abilityId": "fang_s1"})
    assert out["skill"]["abilityName"] == "冲锋号令"
    assert "error" in call(ctx, "read_skill", {"abilityId": "ghost"})


def test_search_skills_without_corpus_degrades():
    ctx = make_ctx()  # corpus_data=None
    out = call(ctx, "search_skills", {"query": "回费"})
    assert out == {"skills": [], "note": "corpus unavailable; design without precedent lookup"}
