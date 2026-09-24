"""服务端交付关卡校验引擎（唯一校验方；源自已退役的客户端校验环，规则同源）。

校验规则来自组件库 ability.db（经 schema.load_schema 重建），双端规则同源：
  Error（整技能拒绝）：未知 op、空 rules、规则无 triggers、枚举名非法
    （triggerEvent/reentry/conditionOp——这些在客户端是强类型反序列化，非法名
    会在 Parse 层抛异常，等价于拒绝）。
  Warning（仅记录）：未知参数键（丢弃）、字面量不可解析（保留原值，运行时按
    默认值兜底）、词表外 token、type 标签越 ParamValueType 词表（any 参数保留
    原标签，其余归一为 schema 类型）、读键无生产者、死配置（op 带 condition 但
    未声明 usesCondition——component op 或 delay 等原语一视同仁 / 非 branch 的
    elseSteps）。

sanitize 产物是白名单重建的干净 DTO——客户端 Parse 开了
MissingMemberHandling.Error，任何未知字段都会让注入失败，所以服务端必须
先把形状洗净（LLM 幻觉字段在这里被剥掉）。
"""

from __future__ import annotations

import json
import re

from . import schema as schema_mod

# 客户端 DTO 白名单（AbilityConfigDto / SPConfig / AbilityRuleConfig / StepConfig /
# ConditionConfig / ConditionUnit / ParamEntry 的字段集，存储形态见库 global_docs.stored_shape）。
# iconKey 不在其中：生成技能统一图标，sanitize 恒剥离（技能卡走 AbilityIconPool null 兜底）。
DTO_KEYS = ("abilityId", "abilityName", "description", "sp", "rules")
SP_KEYS = ("totalSp", "initialSp", "chargeNum", "abilityAmount",
           "recoverMode", "consumeMode", "openMode", "recoverForbidDuringAbility", "canManualClose")
SP_NUMERIC_KEYS = ("totalSp", "initialSp", "chargeNum", "abilityAmount")
RULE_KEYS = ("triggers", "reentry", "detached", "steps")
TRIGGER_KEYS = ("triggerEvent", "groups")
UNIT_KEYS = ("op", "leftKey", "rightValue", "rightKey")
STEP_KEYS = ("op", "args", "condition", "steps", "elseSteps")
ENTRY_KEYS = ("key", "value", "type", "fromBlackboard")

_SP_CLAMP_KEYS = {
    "totalSp": "sp.totalSp",
    "initialSp": "sp.initialSp",
    "chargeNum": "sp.chargeNum",
    "abilityAmount": "sp.abilityAmount",
}


class Walk:
    def __init__(self, schema: dict, host_assets: dict | None = None,
                 corpus_usage: dict | None = None):
        self.schema = schema
        self.host_assets = host_assets or {}
        self.corpus_usage = corpus_usage or {}
        self.issues: list[dict] = []
        self.reads: set[str] = set()
        self.writes: set[str] = set()
        # 当前规则的触发事件（eventContext 并集检查用）与已见实体列表键（listCount 写前读序用）
        self.current_rule_events: list[str] = []
        self.list_keys: set[str] = set()

    def add(self, is_error: bool, path: str, message: str) -> None:
        self.issues.append({"severity": "error" if is_error else "warning", "path": path, "message": message})

    def add_info(self, path: str, message: str) -> None:
        self.issues.append({"severity": "info", "path": path, "message": message})

    @property
    def ok(self) -> bool:
        return not any(i["severity"] == "error" for i in self.issues)


def _coerce_str(value) -> str:
    """客户端 Newtonsoft 会把 JSON 原始标量静默转成 string 字段，镜像该行为。"""
    if value is None:
        return ""
    if isinstance(value, bool):
        return "True" if value else "False"
    if isinstance(value, (int, float)):
        return repr(value) if isinstance(value, float) else str(value)
    return str(value)


def _coerce_num(value):
    if isinstance(value, bool):
        return int(value)
    if isinstance(value, (int, float)):
        return value
    try:
        text = str(value).strip()
        return int(text) if re.fullmatch(r"[+-]?\d+", text) else float(text)
    except (ValueError, TypeError):
        return None


def _try_parse_vector2int(raw: str) -> bool:
    parts = raw.split(",")
    if len(parts) != 2:
        return False
    try:
        int(parts[0].strip())
        int(parts[1].strip())
        return True
    except ValueError:
        return False


def _try_parse_vector2int_array(raw: str) -> bool:
    try:
        parsed = json.loads(raw)
    except (json.JSONDecodeError, TypeError):
        return False
    return isinstance(parsed, list)  # 与运行时同语义：畸形组被丢弃，整体非 JSON 才算错


def _csv_tokens(raw: str) -> list[str]:
    return [t.strip() for t in raw.split(",")]


def _in_values(values: list[str], token: str) -> bool:
    return any(v.strip().lower() == token.strip().lower() for v in values)


def _clamp(value: float, clamp: list) -> float:
    return min(clamp[1], max(clamp[0], value))


def _fmt(value: float) -> str:
    # 对齐客户端 float.ToString("R")：整数值不带 ".0"。
    text = repr(float(value))
    return text[:-2] if text.endswith(".0") else text


def validate(dto: dict, schema: dict | None = None,
             existing_ids: set[str] | None = None,
             host_assets: dict | None = None,
             corpus_usage: dict | None = None) -> tuple[bool, list[dict], dict]:
    """返回 (ok, issues, sanitized)。sanitized 总是产出；调用方仅在 ok 时使用。
    existing_ids（来自技能语料）非空时，abilityId 与现有技能撞名记 warning。
    host_assets（请求的宿主资产清单）非空时，spawn/fire/动画引用做边界校验（error 级）。
    corpus_usage（{"triggerEvents": set, "reentries": set}，来自技能语料）非空时，
    语料零使用的触发事件/reentry 附 info 级"无先例"提示。"""
    schema = schema or schema_mod.load_schema()
    ctx = Walk(schema, host_assets, corpus_usage)

    if not isinstance(dto, dict):
        ctx.add(True, "", "dto is not an object")
        return False, ctx.issues, {}

    sanitized: dict = {k: dto.get(k) for k in DTO_KEYS}

    if existing_ids and isinstance(sanitized.get("abilityId"), str) \
            and sanitized["abilityId"] in existing_ids:
        ctx.add(False, "abilityId",
                f"'{sanitized['abilityId']}' already exists in the skill corpus; "
                "use a distinct id unless overriding intentionally")

    sp = dto.get("sp")
    sanitized["sp"] = _sanitize_sp(sp, ctx) if isinstance(sp, dict) else None

    rules = dto.get("rules")
    if not isinstance(rules, list) or not rules:
        ctx.add(True, "rules", "no rules; the ability would never execute")
        sanitized["rules"] = []
    else:
        sanitized["rules"] = _sanitize_rules(rules, ctx)

    _check_blackboard_symmetry(ctx)
    return ctx.ok, ctx.issues, sanitized


def _sanitize_sp(sp: dict, ctx: Walk) -> dict:
    out: dict = {}
    for key in SP_KEYS:
        if key not in sp:
            continue
        value = sp[key]
        if key in SP_NUMERIC_KEYS:
            num = _coerce_num(value)
            if num is None:
                ctx.add(True, f"sp.{key}", f"cannot parse as number: {value!r}")
                continue
            clamp_key = _SP_CLAMP_KEYS[key]
            if clamp_key in ctx.schema.get("clamps", {}):
                clamped = _clamp(num, ctx.schema["clamps"][clamp_key])
                if clamped != num:
                    ctx.add(False, clamp_key,
                            f"value {_fmt(num)} clamped to {ctx.schema['clamps'][clamp_key]} -> {_fmt(clamped)}")
                num = clamped
            out[key] = int(num) if key != "abilityAmount" else float(num)
        else:
            out[key] = value
    return out


def _sanitize_rules(rules: list, ctx: Walk) -> list[dict]:
    out = []
    events = set(ctx.schema.get("triggerEvents", []))
    reentries = set(ctx.schema.get("ruleReentry", []))
    for i, rule in enumerate(rules):
        path = f"rules[{i}]"
        if not isinstance(rule, dict):
            ctx.add(True, path, "rule is not an object; dropped")
            continue
        triggers = rule.get("triggers")
        if not isinstance(triggers, list) or not triggers:
            ctx.add(True, path, "rule has no triggers; it would never execute")
        # 当前规则触发事件：eventContext 并集检查的数据域（多 trigger 取并集——
        # 实际触发哪个运行时才定，静态不可知，warning 级以控制误报为先）。
        ctx.current_rule_events = [t.get("triggerEvent") for t in (triggers or [])
                                   if isinstance(t, dict)]
        out.append({
            "triggers": _sanitize_triggers(triggers, events, ctx),
            "reentry": rule.get("reentry", "IgnoreWhileRunning"),
            "detached": bool(rule.get("detached", False)),
            "steps": _sanitize_steps(rule.get("steps"), f"{path}.steps", ctx),
        })
        reentry = out[-1]["reentry"]
        if reentry not in reentries:
            ctx.add(True, f"{path}.reentry", f"unknown reentry '{reentry}'; expected one of {sorted(reentries)}")
        elif ctx.corpus_usage and reentry not in ctx.corpus_usage.get("reentries", set()):
            ctx.add_info(f"{path}.reentry",
                         f"reentry '{reentry}' has no precedent in the skill corpus; verify intent")
    return out


def _sanitize_triggers(triggers, events: set[str], ctx: Walk) -> list[dict]:
    if not isinstance(triggers, list):
        return []
    out = []
    corpus_events = ctx.corpus_usage.get("triggerEvents")
    for i, trigger in enumerate(triggers):
        path = f"triggers[{i}]"
        if not isinstance(trigger, dict):
            ctx.add(True, path, "trigger is not an object; dropped")
            continue
        event = trigger.get("triggerEvent")
        if event not in events:
            ctx.add(True, f"{path}.triggerEvent",
                    f"unknown TriggerEvent '{event}'; expected one of {sorted(events)}")
        elif corpus_events is not None and event not in corpus_events:
            ctx.add_info(f"{path}.triggerEvent",
                         f"trigger event '{event}' has no precedent in the skill corpus; verify intent")
        out.append({
            "triggerEvent": event,
            "groups": _sanitize_groups(trigger.get("groups"), f"{path}.groups", ctx),
        })
    return out


def _sanitize_groups(groups, path: str, ctx: Walk) -> list[dict]:
    """groups: [{units: [{op,leftKey,rightValue,rightKey}]}]——对象数组，与运行时
    ConditionGroup（含 units 字段）同形；外层 OR，units 内 AND。"""
    if not isinstance(groups, list):
        return []
    ops = set(ctx.schema.get("conditionOps", []))
    out = []
    for g, group in enumerate(groups):
        group_path = f"{path}[{g}]"
        if isinstance(group, list):
            # LLM 偶发输出 [[unit,...]] 的数组形态：与运行时反序列化不兼容，报错拒绝该组。
            ctx.add(True, group_path,
                    "condition group must be an object with a 'units' field "
                    "(got a bare array); client deserialization would fail")
            continue
        if not isinstance(group, dict):
            ctx.add(False, group_path, "condition group is not an object; dropped")
            continue
        units = []
        raw_units = group.get("units")
        if isinstance(raw_units, list):
            for u, unit in enumerate(raw_units):
                unit_path = f"{group_path}.units[{u}]"
                if not isinstance(unit, dict):
                    ctx.add(False, unit_path, "condition unit is not an object; dropped")
                    continue
                clone = {k: unit.get(k) for k in UNIT_KEYS}
                if clone.get("op") not in ops:
                    ctx.add(True, f"{unit_path}.op",
                            f"unknown ConditionOp '{clone.get('op')}'; expected one of {sorted(ops)}")
                left = clone.get("leftKey")
                if isinstance(left, str) and left:
                    ctx.reads.add(left)
                if clone.get("op") in ("KeyEqual", "KeyNotEqual"):
                    right = clone.get("rightKey")
                    if isinstance(right, str) and right:
                        ctx.reads.add(right)
                units.append(clone)
        out.append({"units": units})
    return out


def _sanitize_steps(steps, path: str, ctx: Walk) -> list[dict]:
    if not isinstance(steps, list):
        return []
    out = []
    for i, step in enumerate(steps):
        step_path = f"{path}[{i}]"
        if not isinstance(step, dict):
            ctx.add(False, step_path, "null step dropped")
            continue
        out.append(_sanitize_step(step, step_path, ctx))
    return out


def _sanitize_step(step: dict, path: str, ctx: Walk) -> dict:
    resolved = schema_mod.resolve_op(ctx.schema, step.get("op"))
    if resolved is None:
        ctx.add(True, path, f"unknown op '{step.get('op')}' (not registered in the op registry)")
        # 保留原步骤（整技能已拒绝，保留仅为诊断）。
        return {k: step.get(k) for k in STEP_KEYS}

    canonical, op_def = resolved
    clone: dict = {"op": canonical}

    args = step.get("args")
    entries = args.get("entries") if isinstance(args, dict) else None
    clone["args"] = {"entries": _sanitize_entries(entries, op_def, canonical, path, ctx)}
    if canonical == "write_blackboard":
        _check_context_paths(clone["args"]["entries"], path, ctx)

    condition = step.get("condition")
    has_condition = isinstance(condition, list) and bool(condition)
    if has_condition and not op_def.get("usesCondition"):
        ctx.add(False, path, f"condition ignored on op '{step.get('op')}' (only wait_until/branch/loop consume it)")
    clone["condition"] = _sanitize_groups(condition, f"{path}.condition", ctx) if has_condition else []

    else_steps = step.get("elseSteps")
    if isinstance(else_steps, list) and else_steps and not op_def.get("hasElse"):
        ctx.add(False, path, f"elseSteps ignored on op '{step.get('op')}' (only branch consumes them)")
    clone["steps"] = _sanitize_steps(step.get("steps"), f"{path}.steps", ctx)
    clone["elseSteps"] = _sanitize_steps(else_steps, f"{path}.elseSteps", ctx)
    return clone


def _check_context_paths(entries: list[dict], path: str, ctx: Walk) -> None:
    """write_blackboard 取值来源的语义检查（全部 warning 级，§五.2/§五.3）：
    - source=event：path ∈ 所在规则全部 triggers 的 eventContext 并集（词表外运行时
      会 WarnUnknownContextPath 返回 null，写值静默跳过）；
    - source=entity：path ∈ entityContext；
    - source=listCount：path 须为本技能内先前 select_targets（outputEntitiesKey）
      产出的实体列表键，否则运行时读到空列表、写入跳过。"""
    by_key = {e.get("key"): e for e in entries if isinstance(e, dict)}
    source_entry = by_key.get("source")
    if source_entry and source_entry.get("fromBlackboard"):
        return  # source 运行时才解析，静态不查
    source = str((source_entry or {}).get("value") or "value").strip().lower()
    path_entry = by_key.get("path")
    if not path_entry or path_entry.get("fromBlackboard") or not path_entry.get("value"):
        return
    path_value = path_entry["value"]

    if source == "event":
        # path 在 schema 里是 readKey（listCount 语义），但 event/entity 源下它是
        # 事件字段/实体路径名，不是黑板键——从 reads 剔除，避免"读键无生产者"误报。
        ctx.reads.discard(path_value)
        event_context = ctx.schema.get("eventContext") or {}
        legal: set[str] = set()
        for event in ctx.current_rule_events:
            legal.update((event_context.get(event) or {}).keys())
        if path_value not in legal:
            ctx.add(False, f"{path}.args",
                    f"source=event path '{path_value}' is outside the eventContext of "
                    f"{ctx.current_rule_events}; runtime would warn and write nothing "
                    f"(legal: {sorted(legal) or 'no context paths on these events'})")
    elif source == "entity":
        ctx.reads.discard(path_value)
        entity_context = ctx.schema.get("entityContext") or {}
        if path_value not in entity_context:
            ctx.add(False, f"{path}.args",
                    f"source=entity path '{path_value}' is outside entityContext; runtime "
                    f"would warn and write nothing (legal: {sorted(entity_context)})")
    elif source == "listcount":
        if path_value not in ctx.list_keys:
            ctx.add(False, f"{path}.args",
                    f"source=listCount path '{path_value}' has no earlier select_targets "
                    "outputEntitiesKey producer in this ability; runtime would find no "
                    "list and skip the write")


def _sanitize_entries(entries, op_def: dict, canonical: str, path: str, ctx: Walk) -> list[dict]:
    if not isinstance(entries, list):
        return []
    out = []
    for entry in entries:
        if not isinstance(entry, dict) or not entry.get("key"):
            continue
        key = entry["key"]
        param = schema_mod.op_param(op_def, key)
        if param is None:
            ctx.add(False, path, f"unknown param key '{key}' on op '{canonical}'; entry dropped")
            continue
        clone = {
            "key": key,
            "value": _coerce_str(entry.get("value")),
            "type": entry.get("type"),
            "fromBlackboard": bool(entry.get("fromBlackboard", False)),
        }
        _validate_entry_value(clone, param, canonical, path, ctx)
        # type 标签与参数类型同词表（ParamValueType，见 value_type_encodings）：
        # 值已按 schema 类型验证过，label 只做大小写归一。any 不覆写（GetValueLazy
        # 功能路径保持模型原标签），但越客户端词表的 label 记 warning——枚举解析
        # 失败会让整份配置注入失败。
        schema_type = param.get("type")
        if schema_type and schema_type != "any":
            if str(clone["type"] or "").lower() != str(schema_type).lower():
                ctx.add(False, path, f"param '{key}' type label {clone['type']!r} "
                                     f"normalized to '{schema_type}'")
            clone["type"] = schema_type
        else:
            label = str(clone["type"] or "")
            encodings = ctx.schema.get("paramValueTypeEncoding") or {}
            if label and encodings and label.lower() not in {k.lower() for k in encodings}:
                ctx.add(False, path, f"param '{key}' type label {label!r} is outside the "
                                     "ParamValueType vocabulary; client deserialization would fail")
        role = param.get("bbRole")
        if clone["value"]:
            if role == "readKey":
                ctx.reads.add(clone["value"])
            elif role == "writeKey":
                ctx.writes.add(clone["value"])
        # 实体列表产出登记：listCount 写前读序检查（§五.3）只认 select_targets 的
        # outputEntitiesKey（其它 writeKey 不产出 List<Entity>）。
        if canonical == "select_targets" and key == "outputEntitiesKey" \
                and not clone["fromBlackboard"] and clone["value"]:
            ctx.list_keys.add(clone["value"])
        # hostAssets 边界校验只管字面量（fromBlackboard 的值是黑板键名，运行时才解析）。
        if not clone["fromBlackboard"] and clone["value"]:
            _check_host_asset_bounds(clone, canonical, path, ctx)
        out.append(clone)
    return out


def _check_host_asset_bounds(entry: dict, canonical: str, path: str, ctx: Walk) -> None:
    """hostAssets 边界校验（error 级，拒绝级）：引用只能指向宿主实际持有的资产，
    与客户端运行时行为对齐（SpawnEntity.ResolveSpawnId 越界 LogError 跳过本次生成、
    AnimationResources 按名精确解析）。hostAssets 未携带对应清单时无从校验，跳过。"""
    host = ctx.host_assets
    if not isinstance(host, dict):
        return
    if canonical == "spawn_entity" and entry["key"] == "spawnIndex":
        if isinstance(host.get("canSpawnEntities"), list):      # 实体投影列表
            registry = host["canSpawnEntities"]
        elif isinstance(host.get("canSpawnEntityIds"), list):   # 旧版 id 列表（兼容）
            registry = host["canSpawnEntityIds"]
        else:
            return
        _check_index(entry["value"], len(registry), "spawn_entity.spawnIndex",
                     "hostAssets.canSpawnEntityIds", path, ctx)
    elif canonical == "fire_bullets" and entry["key"] == "bulletDataIndex":
        bullets = host.get("bullets")
        count = host.get("bulletCount")
        if isinstance(bullets, list):                           # 弹幕投影列表
            count = len(bullets)
        elif isinstance(count, bool) or not isinstance(count, (int, float)):  # 旧版计数字段（兼容）
            return
        _check_index(entry["value"], int(count), "fire_bullets.bulletDataIndex",
                     "hostAssets.bullets", path, ctx)
    elif canonical == "apply_animation_override" and entry["key"] == "resources":
        animations = host.get("animations")
        names = animations.get("named") if isinstance(animations, dict) else None
        groups = animations.get("groups") if isinstance(animations, dict) else None
        legal = {n for n in (names or []) + (groups or []) if isinstance(n, str)}
        if not legal:
            return  # 未携带动画清单则无从校验
        for token in _csv_tokens(entry["value"]):
            if token and token not in legal:  # 客户端按名精确解析（大小写敏感）
                ctx.add(True, path,
                        f"animation resource '{token}' is not in hostAssets.animations "
                        "(named ∪ groups); client resolution would fail")


def _check_index(value: str, count: int, param: str, source: str, path: str, ctx: Walk) -> None:
    num = _coerce_num(value)
    if num is None or num != int(num):
        return  # 非整数字面量已由 int 类型校验告警；此处只管越界
    if 0 <= int(num) < count:
        return
    ctx.add(True, path,
            f"{param}={int(num)} out of range: {source} has {count} entries "
            "(client logs an error and skips the step)")


def _validate_entry_value(entry: dict, param: dict, canonical: str, path: str, ctx: Walk) -> None:
    raw = entry["value"]
    # fromBlackboard=true 时 value 是黑板键名而非字面量，不做字面量解析。
    if entry.get("fromBlackboard") or not raw:
        return

    ptype = param.get("type")
    is_csv = param.get("shape") == "csv"
    values = param.get("values")
    clamp_key = f"{canonical}.{entry['key']}"

    if ptype == "Int":
        if is_csv:
            for token in _csv_tokens(raw):
                if token and not re.fullmatch(r"[+-]?\d+", token):
                    ctx.add(False, path, f"CSV token '{token}' on '{canonical}.{entry['key']}' does not parse as int")
        elif _coerce_num(raw) is None or not re.fullmatch(r"[+-]?\d+", raw.strip()):
            ctx.add(False, path, f"value '{raw}' on '{canonical}.{entry['key']}' does not parse as int; "
                                 "runtime will fall back to the parameter default")
    elif ptype == "Float":
        if is_csv:
            for token in _csv_tokens(raw):
                if token and _coerce_num(token) is None:
                    ctx.add(False, path, f"CSV token '{token}' on '{canonical}.{entry['key']}' does not parse as float")
        else:
            num = _coerce_num(raw)
            if num is None:
                ctx.add(False, path, f"value '{raw}' on '{canonical}.{entry['key']}' does not parse as float; "
                                     "runtime will fall back to the parameter default")
            elif clamp_key in ctx.schema.get("clamps", {}):
                clamped = _clamp(num, ctx.schema["clamps"][clamp_key])
                if clamped != num:
                    entry["value"] = _fmt(clamped)
                    ctx.add(False, clamp_key, f"value {_fmt(num)} clamped to {ctx.schema['clamps'][clamp_key]} -> {entry['value']}")
    elif ptype == "Bool":
        if raw not in ("True", "False", "true", "false"):
            ctx.add(False, path, f"value '{raw}' on '{canonical}.{entry['key']}' does not parse as bool")
    elif ptype == "Vector2Int":
        if is_csv:
            if not _try_parse_vector2int_array(raw):
                ctx.add(False, path, f"value '{raw}' on '{canonical}.{entry['key']}' does not parse as "
                                     'vector2int array "[[x,y],...]"')
        elif not _try_parse_vector2int(raw):
            ctx.add(False, path, f"value '{raw}' on '{canonical}.{entry['key']}' does not parse as vector2int \"x,y\"")
    elif ptype == "String":
        if is_csv:
            if values and not all(_in_values(values, t) for t in _csv_tokens(raw) if t):
                ctx.add(False, path, f"CSV token on '{canonical}.{entry['key']}' is outside the accepted tokens; "
                                     f"expected one of {values}")
        elif values and not _in_values(values, raw):
            ctx.add(False, path, f"value '{raw}' on '{canonical}.{entry['key']}' is outside the accepted tokens; "
                                 f"expected one of {values}")
    elif ptype == "any":
        pass
    # 未知类型标签：宽松跳过（运行时按字符串存取）。


def _check_blackboard_symmetry(ctx: Walk) -> None:
    for key in sorted(ctx.reads):
        if key in ctx.writes:
            continue
        ctx.add(False, "blackboard",
                f"read key '{key}' has no producer in this ability "
                "(externally-fed keys are legitimate; verify intent)")
