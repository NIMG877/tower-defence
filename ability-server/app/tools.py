"""Agent 工具集（plan-agent-framework-v2 §4.2 四类）。

设计原则：渐进式披露——LLM 先拿到紧凑索引，需要细节时再单篇拉取，避免
一开始把全部组件的完整说明灌进上下文。文档三件套（list_components/
read_component_doc/read_contract_doc）的 def 构造与实现后端也在这里，
本模块是 agent 唯一的工具注册表。

工具执行永不抛异常——错误以 {"error": ...} JSON 回喂，让模型自行修正。
update_plan 与增量写作五件套（start_draft/design_rules/put_step/drop_step/
drop_rule）改写 ToolContext 的会话状态：plan 用于路由（plan_pending→strong）、
working/outline/step_walk 是增量草稿与其校验游标、draft 是降级交付源与免重写
提交源、submitted 表示关卡通过。
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


# 依赖战局快照的工具：无快照的描述模式下从工具表中剔除，模型不会白调死工具。
SNAPSHOT_TOOLS = ("compute_cross_items", "entity", "entities_at", "deploy_cells_near")


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
            "name": "start_draft",
            "description": "开始增量构建技能草稿：定 abilityId/名称/描述与 SP。"
                           "之后 design_rules 立骨架、put_step 逐个转写。不要一次性写整份配置。",
            "parameters": {"type": "object",
                           "properties": {
                               "abilityId": {"type": "string"},
                               "abilityName": {"type": "string"},
                               "description": {"type": "string", "description": "技能描述（中文，写明行为与数值）"},
                               "sp": {"type": "object", "description": "SP 配置（totalSp/initialSp/…）"}},
                           "required": ["abilityId", "abilityName", "description", "sp"]},
        }},
        {"type": "function", "function": {
            "name": "design_rules",
            "description": "转 JSON 前的设计骨架（语义层）：一次给出全部规则——触发事件/条件/"
                           "reentry 与每个 step 的 op+intent。intent 一句话写明该步意图与数据来源"
                           "（谁产出谁消费，如'收集主目标周围敌人，供后续 apply_damage 读'）。"
                           "骨架经检查后按位置逐个 put_step 转写完整参数。",
            "parameters": {"type": "object",
                           "properties": {"rules": {"type": "array", "items": {"type": "object"},
                                                    "description": "规则骨架数组：[{triggerEvent, reentry?, "
                                                                   "condition?(完整 groups), detached?, "
                                                                   "steps:[{op, intent}]}]"}},
                           "required": ["rules"]},
        }},
        {"type": "function", "function": {
            "name": "put_step",
            "description": "按骨架把一个 step 的完整 JSON 写入草稿（op + args.entries，可含 "
                           "condition/steps/elseSteps 子结构）。每步即时 lint：参数键/类型/词表/"
                           "钳制与骨架位置对照，有 error 先修再写下一步。index 省略=追加到该规则末尾。",
            "parameters": {"type": "object",
                           "properties": {"ruleIndex": {"type": "integer"},
                                          "step": {"type": "object"},
                                          "index": {"type": "integer", "description": "可选：插入位置"}},
                           "required": ["ruleIndex", "step"]},
        }},
        {"type": "function", "function": {
            "name": "drop_step",
            "description": "从草稿删除一个 step（写错时修正用）。",
            "parameters": {"type": "object",
                           "properties": {"ruleIndex": {"type": "integer"}, "index": {"type": "integer"}},
                           "required": ["ruleIndex", "index"]},
        }},
        {"type": "function", "function": {
            "name": "drop_rule",
            "description": "从草稿删除整条规则（含其全部 step）。",
            "parameters": {"type": "object",
                           "properties": {"index": {"type": "integer"}}, "required": ["index"]},
        }},
        {"type": "function", "function": {
            "name": "validate_draft",
            "description": "随手校验：无 config 时校验当前增量草稿（start_draft 后的全量检查），"
                           "传 config 则校验指定配置。返回完整 issues；通过即可直接 submit_skill。",
            "parameters": {"type": "object",
                           "properties": {"config": {"type": "object", "description": "AbilityConfig JSON；省略=校验增量草稿"}}},
        }},
        {"type": "function", "function": {
            "name": "submit_skill",
            "description": "唯一交付出口：提交 AbilityConfig 进入交付关卡（全量校验+hostAssets 边界+撞名）。"
                           "config 可省略——省略时直接交付最近一次 validate_draft 通过的草稿（无需重写一遍）。"
                           "通过即结束；拒绝则按 issues 修复后重新提交。不要以裸文本输出配置 JSON。",
            "parameters": {"type": "object",
                           "properties": {"config": {"type": "object",
                                                     "description": "AbilityConfig JSON；省略=提交已过闸草稿"}}},
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

        # 增量写作状态：working=渐进构建的配置；outline=design_rules 的骨架
        # （每规则的 op 序列，put_step 对照）；step_walk=跨 put_step 持久的
        # 校验游标（黑板读写/列表键累积，撑起"到目前为止"的渐进反馈）。
        self.working: dict | None = None
        self.outline: list[list[str]] | None = None
        self.step_walk: validator.Walk | None = None


def execute_tool(ctx: ToolContext, name: str, arguments: dict) -> str:
    """执行工具并回填完整 JSON 字符串；错误不抛，回喂模型。"""
    try:
        return json.dumps(_dispatch(ctx, name, arguments), ensure_ascii=False)
    except Exception as exc:  # noqa: BLE001 —— 工具层永不抛
        return json.dumps({"error": f"{type(exc).__name__}: {exc}"}, ensure_ascii=False)


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
    if name == "start_draft":
        return _start_draft(ctx, args)
    if name == "design_rules":
        return _design_rules(ctx, args)
    if name == "put_step":
        return _put_step(ctx, args)
    if name == "drop_step":
        return _drop_step(ctx, args)
    if name == "drop_rule":
        return _drop_rule(ctx, args)
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
    """read_component_doc 后端:按 canonical 名对 schema 精确查找（无别名/大小写
    归一），未知名返回 error。"""
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
    """read_contract_doc 后端:全局契约节索引/单节全文(全文 5.5k,默认只给索引控制单次回喂体积)。"""
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


# ---------- 增量写作后端（复用 validator 的单元级 sanitize/lint，全量闸仍属 validate/submit） ----------

def _err(path: str, message: str) -> dict:
    return {"severity": "error", "path": path, "message": message}


def _draft_summary(ctx: ToolContext) -> str:
    rules = ctx.working["rules"]
    return f"{len(rules)} rules / {sum(len(r['steps']) for r in rules)} steps"


def _start_draft(ctx: ToolContext, args: dict) -> dict:
    ability_id = args.get("abilityId")
    if not isinstance(ability_id, str) or not ability_id.strip():
        return {"error": "abilityId is required"}
    walk = validator.Walk(ctx.schema, ctx.host_assets or None)
    sp = args.get("sp")
    sp_out = validator._sanitize_sp(sp, walk) if isinstance(sp, dict) else None
    issues = list(walk.issues)
    if sp_out is None:
        issues.insert(0, _err("sp", "sp is not an object"))
    discarded = sum(len(r["steps"]) for r in ctx.working["rules"]) if ctx.working else 0
    ctx.working = {"abilityId": ability_id.strip(),
                   "abilityName": str(args.get("abilityName") or ""),
                   "description": str(args.get("description") or ""),
                   "sp": sp_out, "rules": []}
    ctx.outline = None
    ctx.step_walk = validator.Walk(ctx.schema, ctx.host_assets or None)
    note = f"draft ready ({_draft_summary(ctx)}); call design_rules next"
    if discarded:
        note += f" [restart: discarded previous draft with {discarded} steps]"
    return {"ok": sp_out is not None and not any(i["severity"] == "error" for i in issues),
            "sp": sp_out, "issues": issues, "note": note}


def _design_rules(ctx: ToolContext, args: dict) -> dict:
    if ctx.working is None:
        return {"error": "call start_draft first"}
    rules_in = args.get("rules")
    if not isinstance(rules_in, list) or not rules_in:
        return {"error": "rules must be a non-empty array of rule outlines"}
    events = set(ctx.schema.get("triggerEvents", []))
    reentries = set(ctx.schema.get("ruleReentry", []))
    issues: list[dict] = []
    shells: list[dict] = []
    outline: list[list[str]] = []
    echo: list[dict] = []
    discarded = sum(len(r["steps"]) for r in ctx.working["rules"])
    walk_before = len(ctx.step_walk.issues)  # 条件组 sanitize 的 issues 落在游标里，循环后并回
    for i, rule in enumerate(rules_in):
        path = f"rules[{i}]"
        if not isinstance(rule, dict):
            issues.append(_err(path, "rule outline is not an object"))
            # 占位补齐 shell/outline，保持 put_step 的 ruleIndex 与回显编号对齐。
            shells.append({"triggers": [], "reentry": "IgnoreWhileRunning",
                           "detached": False, "steps": []})
            outline.append([])
            continue
        ev = rule.get("triggerEvent")
        ev_list = [ev] if isinstance(ev, str) else ev
        if not isinstance(ev_list, list) or not ev_list:
            issues.append(_err(f"{path}.triggerEvent", "triggerEvent (string or array) is required"))
            ev_list = []
        triggers = []
        for e in ev_list:
            if e not in events:
                issues.append(_err(f"{path}.triggerEvent",
                                   f"unknown TriggerEvent '{e}'; expected one of {sorted(events)}"))
            triggers.append({"triggerEvent": e, "groups": []})
        reentry = rule.get("reentry", "IgnoreWhileRunning")
        if reentry not in reentries:
            issues.append(_err(f"{path}.reentry",
                               f"unknown reentry '{reentry}'; expected one of {sorted(reentries)}"))
        ctx.step_walk.current_rule_events = [e for e in ev_list if isinstance(e, str)]
        condition = rule.get("condition")
        if condition:  # 触发条件跟首条 trigger 的 groups（单触发为主；多触发共用同条件）
            triggers[0]["groups"] = validator._sanitize_groups(
                condition, f"{path}.condition", ctx.step_walk) if triggers else []
        step_ops: list[str] = []
        steps_echo = []
        for j, s in enumerate(rule.get("steps") or []):
            spath = f"{path}.steps[{j}]"
            if not isinstance(s, dict):
                issues.append(_err(spath, "step outline is not an object"))
                step_ops.append("?")
                continue
            op = s.get("op")
            if schema_mod.resolve_op(ctx.schema, op) is None:
                issues.append(_err(spath, f"unknown op '{op}' (not registered in the op registry)"))
            step_ops.append(str(op))
            intent = str(s.get("intent") or "").strip()
            if not intent:
                issues.append({"severity": "warning", "path": spath,
                               "message": "outline step has no intent; state what this step does "
                                          "and where its data comes from"})
            steps_echo.append({"pos": j, "op": op, "intent": intent})
        if not step_ops:
            issues.append(_err(path, "rule outline has no steps; it would never do anything"))
        shells.append({"triggers": triggers, "reentry": reentry,
                       "detached": bool(rule.get("detached", False)), "steps": []})
        outline.append(step_ops)
        echo.append({"rule": i, "triggerEvent": ev_list, "reentry": reentry, "steps": steps_echo})
    issues.extend(ctx.step_walk.issues[walk_before:])
    ctx.working["rules"] = shells
    ctx.outline = outline
    out = {"ok": not any(i["severity"] == "error" for i in issues),
           "issues": issues, "outline": echo, "draft": _draft_summary(ctx),
           "next": "put_step(ruleIndex, step) following the outline positions"}
    if discarded:
        out["note"] = f"outline replaced; discarded {discarded} previously written steps"
    return out


def _put_step(ctx: ToolContext, args: dict) -> dict:
    if ctx.working is None:
        return {"error": "call start_draft first"}
    rules = ctx.working["rules"]
    ri = args.get("ruleIndex")
    if not isinstance(ri, int) or isinstance(ri, bool) or not 0 <= ri < len(rules):
        return {"error": f"ruleIndex {ri} out of range; draft has {len(rules)} rules "
                         "(call design_rules first if the skeleton is missing)"}
    step = args.get("step")
    if not isinstance(step, dict):
        return {"error": "step must be an object ({op, args:{entries:[...]}})"}
    steps = rules[ri]["steps"]
    index = args.get("index")
    if index is None:
        pos = len(steps)
    elif isinstance(index, int) and not isinstance(index, bool) and 0 <= index <= len(steps):
        pos = index
    else:
        return {"error": f"index {index} out of range; rule {ri} has {len(steps)} steps"}
    ctx.step_walk.current_rule_events = [t.get("triggerEvent") for t in rules[ri]["triggers"]
                                         if isinstance(t, dict)]
    before = len(ctx.step_walk.issues)
    sanitized = validator._sanitize_step(step, f"rules[{ri}].steps[{pos}]", ctx.step_walk)
    steps.insert(pos, sanitized)
    issues = ctx.step_walk.issues[before:]
    outline_ops = ctx.outline[ri] if ctx.outline and ri < len(ctx.outline) else None
    if outline_ops:
        if pos < len(outline_ops) and outline_ops[pos] not in ("?", sanitized["op"]):
            issues.append({"severity": "warning", "path": f"rules[{ri}].steps[{pos}]",
                           "message": f"outline expects '{outline_ops[pos]}' at this position; "
                                      f"got '{sanitized['op']}' — revise the step or deviate knowingly"})
        elif pos >= len(outline_ops):
            issues.append({"severity": "warning", "path": f"rules[{ri}].steps[{pos}]",
                           "message": "beyond the design_rules outline; verify the rule is still as designed"})
    return {"ok": not any(i["severity"] == "error" for i in issues),
            "issues": issues,
            "ruleSteps": [s["op"] for s in steps],
            "blackboardWritesSoFar": sorted(ctx.step_walk.writes),
            "draft": _draft_summary(ctx)}


def _drop_step(ctx: ToolContext, args: dict) -> dict:
    return _draft_mutate(ctx, args, per_rule=True)


def _drop_rule(ctx: ToolContext, args: dict) -> dict:
    return _draft_mutate(ctx, args, per_rule=False)


def _draft_mutate(ctx: ToolContext, args: dict, per_rule: bool) -> dict:
    if ctx.working is None:
        return {"error": "call start_draft first"}
    rules = ctx.working["rules"]
    ri = args.get("ruleIndex" if per_rule else "index")
    if not isinstance(ri, int) or isinstance(ri, bool) or not 0 <= ri < len(rules):
        return {"error": f"index {ri} out of range; draft has {len(rules)} rules"}
    if per_rule:
        index = args.get("index")
        steps = rules[ri]["steps"]
        if not isinstance(index, int) or isinstance(index, bool) or not 0 <= index < len(steps):
            return {"error": f"index {index} out of range; rule {ri} has {len(steps)} steps"}
        dropped = steps.pop(index)["op"]
        out = {"ok": True, "dropped": dropped, "ruleSteps": [s["op"] for s in steps]}
    else:
        dropped_rule = rules.pop(ri)
        out = {"ok": True, "dropped": f"rule {ri} ({len(dropped_rule['steps'])} steps)"}
        if ctx.outline and ri < len(ctx.outline):
            ctx.outline.pop(ri)
    out["draft"] = _draft_summary(ctx)
    out["note"] = ("blackboard accumulation keeps dropped keys; the full validate/submit "
                   "gate re-checks everything from scratch and is authoritative")
    return out


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


# 体积大的词表节不随默认返回：read_schema_vocab 默认只给轻节，重节用 section
# 参数单独取——整份 eventContext 无谓占用上下文。
_HEAVY_VOCAB_SECTIONS = ("eventContext",)


def _schema_vocab(schema: dict, section: str | None = None) -> dict:
    full = {
        "triggerEvents": schema.get("triggerEvents"),
        "conditionOps": schema.get("conditionOps"),
        "ruleReentry": schema.get("ruleReentry"),
        "spRecoverModes": schema.get("spRecoverModes"),
        "spConsumeModes": schema.get("spConsumeModes"),
        "abilityOpenModes": schema.get("abilityOpenModes"),
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
    """submit_skill 与 validate_draft 共用同一关卡：全量校验 + hostAssets 边界 + 撞名。
    config 省略时：validate_draft 校验当前增量草稿；submit 交付已过闸草稿
    （免重写；同一关卡再验一次，幂等）。"""
    if config is None:
        if submit:
            if ctx.draft is None:
                return {"accepted": False, "issues": [
                    {"severity": "error", "path": "config",
                     "message": "no validated draft to submit; call validate_draft first"}]}
            config = ctx.draft
        else:
            config = ctx.working
            if config is None:
                return {"ok": False, "issues": [
                    _err("config", "nothing to validate: no incremental draft "
                                   "(call start_draft first) and no config given")]}
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
        ctx.draft = sanitized  # 降级交付源（预算闸耗尽时由 agent 循环消费：有草稿降级交付，无草稿拒绝）
        return {"ok": True, "issues": issues}
    return {"ok": False, "issues": issues}
