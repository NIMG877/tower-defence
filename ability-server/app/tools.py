"""Agent 工具集（plan-agent-framework-v2 §4.2 四类）。

设计原则：渐进式披露——LLM 先拿到紧凑索引，需要细节时再单篇拉取，避免
一开始把全部组件的完整说明灌进上下文。文档三件套（list_components/
read_component_doc/read_contract_doc）的 def 构造与实现后端也在这里，
本模块是 agent 唯一的工具注册表。

工具执行永不抛异常——错误以 {"error": ...} JSON 回喂，让模型自行修正。
update_plan / validate_draft / submit_skill 会改写 ToolContext 的会话状态：
plan 用于路由（plan_pending→strong）、draft 是降级交付源、submitted 表示
关卡通过。
"""

from __future__ import annotations

import json

from . import ability_db, battle_digest, corpus, crossitems, schema as schema_mod, validator

# 数值字段语义（唯一出处）：camp 入 read_schema_vocab 与文档（§三·A）；
# job/subJob/bulletType 随 hostAssets 摘要注入。
FIELD_SEMANTICS = {
    "camp": battle_digest.CAMP_ENCODING,
    "job": "EntityData.CharacterJob：0=先锋 1=近卫 2=重装 3=狙击 4=术师 5=医疗 6=辅助 7=特种 8=装置 9=_",
    "subJob": "EntityData.CharacterSubJob：0=无 1=秘术师 2=冲锋手 3=凝滞师（其余按主职业序）",
    "bulletType": "1=带偏差弹道",
}

# 工具结果回填体积自限（§4.4）：>4k chars 截断回填（截断标记注明去向），
# 原文记入 ToolContext.tool_outputs → report.toolOutputs 落盘回放。
TOOL_RESULT_CHAR_LIMIT = 4096

_PHASE_BY_TOOL = {"update_plan": "plan", "validate_draft": "review", "submit_skill": "submit"}


def phase_for(name: str) -> str:
    """工具调用的对外 phase 词表（plan/act/review/submit；客户端只打印不解析）。"""
    return _PHASE_BY_TOOL.get(name, "act")


def list_components_def() -> dict:
    return {
        "type": "function",
        "function": {
            "name": "list_components",
            "description": "列出全部可用技能组件 op 的紧凑索引（op 名/一句话摘要）。"
                           "先读索引再决定读哪篇详细文档，不要凭空使用未读过文档的组件。",
            "parameters": {"type": "object", "properties": {}},
        },
    }


def read_component_doc_def() -> dict:
    return {
        "type": "function",
        "function": {
            "name": "read_component_doc",
            "description": "读取单个技能组件的完整说明（参数表/行为语义/组合配方）。"
                           "name 传 op 名（如 apply_damage）。",
            "parameters": {
                "type": "object",
                "properties": {"name": {"type": "string", "description": "op 名"}},
                "required": ["name"],
            },
        },
    }


def read_contract_doc_def() -> dict:
    return {
        "type": "function",
        "function": {
            "name": "read_contract_doc",
            "description": "读取全局规则契约（规则形状/触发与条件系统/序列语义/重入/"
                           "参数存储与 damageType/操作生命周期/注册表扩展）。默认返回节索引，"
                           "传 section 取单节全文。设计多步骤规则、判断触发时机前先读相关节。",
            "parameters": {"type": "object",
                           "properties": {"section": {"type": "string",
                                                      "description": "可选：只取某一节全文"}}},
        },
    }


def tool_definitions() -> list[dict]:
    cross_desc = ("按需计算战局派生指标（设计期参考；运行时条件只能用黑板键——"
                  "select_targets 写实体列表 + write_blackboard source=listCount 桥接，"
                  "digest 数字不能直接进技能参数）。items 可选值：\n" + "\n".join(
        f"- {name}: {desc}" for name, (desc, _, _) in sorted(crossitems.REGISTRY.items())
    ))
    return [
        {"type": "function", "function": {
            "name": "update_plan",
            "description": "提交或修订当前工作计划（打算读哪些组件文档、技能设计思路、"
                           "黑板键方案）。第一轮必须先提交计划；此后重大转向时修订。",
            "parameters": {"type": "object",
                           "properties": {"plan": {"type": "string", "description": "计划全文（中文分点）"}},
                           "required": ["plan"]},
        }},
        {"type": "function", "function": {
            "name": "compute_cross_items",
            "description": cross_desc,
            "parameters": {"type": "object", "properties": {
                "items": {"type": "array", "items": {"type": "string"},
                          "description": "指标名列表，如 [\"enemy_group_stats\", \"pairwise_distance(m-1,m-2)\"]"}},
                "required": ["items"]},
        }},
        {"type": "function", "function": {
            "name": "entity",
            "description": "按 id 取单个实体的完整记录（快照明细的按需查询入口）。",
            "parameters": {"type": "object",
                           "properties": {"id": {"type": "string"}}, "required": ["id"]},
        }},
        {"type": "function", "function": {
            "name": "entities_at",
            "description": "任意圆心 (x,y) 半径 radius 内的实体完整记录清单（camp 可选过滤：1=友军 2=敌人）。",
            "parameters": {"type": "object", "properties": {
                "x": {"type": "number"}, "y": {"type": "number"},
                "radius": {"type": "number"},
                "camp": {"type": "integer", "description": "可选：1=友军 2=敌人"}},
                "required": ["x", "y", "radius"]},
        }},
        {"type": "function", "function": {
            "name": "deploy_cells_near",
            "description": "以 (x,y) 为圆心查半径内可部署格（高台/地面分列），用于召唤落点与范围设计。",
            "parameters": {"type": "object", "properties": {
                "x": {"type": "number"}, "y": {"type": "number"}, "radius": {"type": "number"}},
                "required": ["x", "y", "radius"]},
        }},
        list_components_def(),
        read_component_doc_def(),
        read_contract_doc_def(),
        {"type": "function", "function": {
            "name": "read_schema_vocab",
            "description": "一次返回服务端 schema 词表：triggerEvents/conditionOps/ruleReentry/"
                           "paramValueTypeEncoding/clamps/eventContext/entityContext 与数值字段"
                           "语义（camp/job 等）。设计触发事件与参数取值前先读，避免凭记忆猜词表。"
                           "eventContext（各触发事件的运行时上下文 path 表）体积大不随默认返回，"
                           '需要时传 section="eventContext" 单独取。',
            "parameters": {"type": "object",
                           "properties": {"section": {"type": "string",
                                                      "description": "可选：只取某一节（如 eventContext）"}}},
        }},
        {"type": "function", "function": {
            "name": "search_skills",
            "description": "按关键词检索现有技能语料（匹配 id/名称/描述/op 名），返回紧凑清单。"
                           "设计前查先例：相似技能怎么组合、数值量级多少。",
            "parameters": {"type": "object", "properties": {
                "query": {"type": "string", "description": "关键词，如 回费 沉默 召唤 apply_buff"},
                "limit": {"type": "integer", "description": "默认 5"}},
                "required": ["query"]},
        }},
        {"type": "function", "function": {
            "name": "read_skill",
            "description": "按 abilityId 读取技能全文（语料技能或宿主已有技能），含完整 rules。",
            "parameters": {"type": "object",
                           "properties": {"abilityId": {"type": "string"}}, "required": ["abilityId"]},
        }},
        {"type": "function", "function": {
            "name": "validate_draft",
            "description": "随手校验一份 AbilityConfig 草案（全量校验+hostAssets 边界），返回完整 issues。"
                           "submit 前先自检；通过即可直接 submit_skill。",
            "parameters": {"type": "object",
                           "properties": {"config": {"type": "object", "description": "AbilityConfig JSON"}},
                           "required": ["config"]},
        }},
        {"type": "function", "function": {
            "name": "submit_skill",
            "description": "唯一交付出口：提交 AbilityConfig 进入交付关卡（全量校验+hostAssets 边界+撞名）。"
                           "通过即结束；拒绝则按 issues 修复后重新提交。不要以裸文本输出配置 JSON。",
            "parameters": {"type": "object",
                           "properties": {"config": {"type": "object", "description": "AbilityConfig JSON"}},
                           "required": ["config"]},
        }},
    ]


class ToolContext:
    """单次生成的工具执行上下文：数据源 + 会话状态。"""

    def __init__(self, request: dict, cfg, schema: dict, corpus_data: dict | None):
        self.cfg = cfg
        self.schema = schema
        self.snapshot = request.get("battleSnapshot") or {}
        self.host_assets = request.get("hostAssets") or {}
        self.constraints = request.get("constraints") or {}
        self.corpus = corpus_data
        self.corpus_usage = corpus.usage_stats(corpus_data) if corpus_data else None
        host_skills = (self.host_assets.get("skills") or []) + (self.host_assets.get("talents") or [])
        self.host_skills = {s.get("abilityId"): s for s in host_skills
                            if isinstance(s, dict) and s.get("abilityId")}
        self.plan: str | None = None
        self.plan_updates = 0
        self.draft: dict | None = None      # 最近一次 validate_draft ok 的 sanitized 草稿
        self.submitted: dict | None = None  # submit_skill 通过时的 sanitized 配置
        self.tool_outputs: list[dict] = []  # 被截断工具结果的全文（报告落盘回放用）


def execute_tool(ctx: ToolContext, name: str, arguments: dict) -> str:
    """执行工具并回填 JSON 字符串（错误不抛，回喂模型；超限截断保预算，§4.4）。"""
    try:
        text = json.dumps(_dispatch(ctx, name, arguments), ensure_ascii=False)
    except Exception as exc:  # noqa: BLE001 —— 工具层永不抛
        return json.dumps({"error": f"{type(exc).__name__}: {exc}"}, ensure_ascii=False)
    if len(text) <= TOOL_RESULT_CHAR_LIMIT:
        return text
    ctx.tool_outputs.append({"tool": name, "chars": len(text), "full": text})
    return text[:TOOL_RESULT_CHAR_LIMIT] + (
        f"...[truncated {len(text)}->{TOOL_RESULT_CHAR_LIMIT} chars; "
        f"full text in report.toolOutputs[{len(ctx.tool_outputs) - 1}]]")


def _dispatch(ctx: ToolContext, name: str, args: dict):
    if name == "update_plan":
        return _update_plan(ctx, args)
    if name == "compute_cross_items":
        items = args.get("items") or []
        if isinstance(items, str):
            items = [items]
        return {"results": crossitems.compute_all(ctx.snapshot, items)}
    if name == "entity":
        return {"entity": crossitems.compute(ctx.snapshot, f"entity({args.get('id', '')})")}
    if name == "entities_at":
        return {"entities": crossitems.query_entities_at(
            ctx.snapshot, float(args.get("x", 0)), float(args.get("y", 0)),
            float(args.get("radius", 0)), args.get("camp"))}
    if name == "deploy_cells_near":
        return battle_digest.deploy_cells_near(
            ctx.snapshot, (float(args.get("x", 0)), float(args.get("y", 0))),
            float(args.get("radius", 0)))
    if name == "list_components":
        return component_index(ctx)
    if name == "read_component_doc":
        return component_doc(ctx, args.get("name", ""))
    if name == "read_contract_doc":
        return _contract_doc(ctx, args.get("section"))
    if name == "read_schema_vocab":
        return _schema_vocab(ctx.schema, args.get("section"))
    if name == "search_skills":
        return _search_skills(ctx, args)
    if name == "read_skill":
        return _read_skill(ctx, args)
    if name == "validate_draft":
        return _validate(ctx, args.get("config"), submit=False)
    if name == "submit_skill":
        return _validate(ctx, args.get("config"), submit=True)
    return {"error": f"unknown tool '{name}'"}


def component_index(ctx: "ToolContext") -> dict:
    """list_components 后端:紧凑索引来自组件库(summary 已是第一句摘要)。"""
    snap = ability_db.get(ctx.cfg.db_path)
    if snap is None:
        return {"error": "component db unavailable; design from schema vocab only"}
    return {"components": snap.components}


def component_doc(ctx: "ToolContext", name: str) -> dict:
    """read_component_doc 后端:别名/大小写经 schema 解析到 canonical 再查库。"""
    resolved = schema_mod.resolve_op(ctx.schema, name)
    if resolved is None:
        return {"error": f"unknown component '{name}'; call list_components first"}
    canonical, _ = resolved
    snap = ability_db.get(ctx.cfg.db_path)
    if snap is None:
        return {"error": "component db unavailable; design from schema vocab only"}
    doc = snap.docs.get(canonical)
    if doc is None:
        return {"error": f"component '{canonical}' missing from component db"}
    return doc


def _contract_doc(ctx: "ToolContext", section: str | None) -> dict:
    """read_contract_doc 后端:全局契约节索引/单节全文(总量 5.5k 超背闸,默认只给索引)。"""
    snap = ability_db.get(ctx.cfg.db_path)
    if snap is None:
        return {"error": "component db unavailable"}
    sections = snap.contract_sections
    if section:
        if section in sections:
            return {"section": section, "content": sections[section]}
        return {"error": f"unknown section '{section}'; available: {', '.join(sections)}"}
    return {
        "sections": [{"section": name, "topic": (text.splitlines()[0] if text else "")[:80]}
                     for name, text in sections.items()],
        "note": "call again with section='<name>' for the full text of that section",
    }


def _update_plan(ctx: ToolContext, args: dict) -> dict:
    limit = ctx.cfg.agent_max_plan_updates
    if ctx.plan_updates >= limit:
        return {"error": f"plan update limit reached ({limit}); "
                         "continue with the current plan"}
    plan = args.get("plan")
    if not isinstance(plan, str) or not plan.strip():
        return {"error": "plan must be a non-empty string"}
    ctx.plan = plan.strip()
    ctx.plan_updates += 1
    return {"ok": True, "note": "plan recorded; proceed with research and design"}


# 体积大的词表节不随默认返回（§4.4 长文档分段）：read_schema_vocab 默认只给轻节，
# 重节用 section 参数单独取——避免 4k 截断背闸把词表 JSON 切坏。
_HEAVY_VOCAB_SECTIONS = ("eventContext",)


def _schema_vocab(schema: dict, section: str | None = None) -> dict:
    full = {
        "triggerEvents": schema.get("triggerEvents"),
        "conditionOps": schema.get("conditionOps"),
        "ruleReentry": schema.get("ruleReentry"),
        "paramValueTypeEncoding": schema.get("paramValueTypeEncoding"),
        "clamps": schema.get("clamps"),
        "eventContext": schema.get("eventContext"),
        "entityContext": schema.get("entityContext"),
        "fieldSemantics": FIELD_SEMANTICS,
    }
    if section:
        if section in full:
            return {section: full[section]}
        return {"error": f"unknown section '{section}'; available: {', '.join(full)}"}
    light = {k: v for k, v in full.items() if k not in _HEAVY_VOCAB_SECTIONS}
    light["note"] = ("heavy sections omitted: " + ", ".join(_HEAVY_VOCAB_SECTIONS)
                     + "; fetch with section='<name>'")
    return light


def _search_skills(ctx: ToolContext, args: dict) -> dict:
    if not ctx.corpus:
        return {"skills": [], "note": "corpus unavailable; design without precedent lookup"}
    limit = args.get("limit") if isinstance(args.get("limit"), int) else 5
    return {"skills": corpus.search_skills(ctx.corpus, args.get("query", ""),
                                           limit=max(1, min(limit, 10)))}


def _read_skill(ctx: ToolContext, args: dict) -> dict:
    ability_id = args.get("abilityId", "")
    skill = ctx.host_skills.get(ability_id)  # 宿主技能与语料技能是同类资产
    if skill is None and ctx.corpus:
        skill = corpus.read_skill(ctx.corpus, ability_id)
    if skill is None:
        return {"error": f"unknown abilityId '{ability_id}'; call search_skills first"}
    return {"skill": skill}


def _validate(ctx: ToolContext, config, submit: bool) -> dict:
    """submit_skill 与 validate_draft 共用同一关卡：全量校验 + hostAssets 边界 + 撞名。"""
    if not isinstance(config, dict):
        return {"accepted" if submit else "ok": False, "issues": [
            {"severity": "error", "path": "config", "message": "config is not an object"}]}
    existing = ctx.corpus["existingIds"] if ctx.corpus else None
    ok, issues, sanitized = validator.validate(config, ctx.schema, existing,
                                               host_assets=ctx.host_assets or None,
                                               corpus_usage=ctx.corpus_usage)
    if submit:
        if ok:
            ctx.submitted = sanitized
            return {"accepted": True, "config": sanitized, "issues": issues}
        return {"accepted": False, "issues": issues}
    if ok:
        ctx.draft = sanitized  # 降级交付源（§4.4；M4 接预算闸）
        return {"ok": True, "issues": issues}
    return {"ok": False, "issues": issues}
