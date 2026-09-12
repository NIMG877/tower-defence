"""服务端全量校验引擎——客户端 AbilityConfigValidator（C#）的 Python 镜像。

两份引擎读同一份 ability-ops.json，规则对齐：
  Error（整技能拒绝）：未知 op、空 rules、规则无 triggers、枚举名非法
    （triggerEvent/reentry/conditionOp/paramValueType——这些在客户端是强类型反
    序列化，非法名会在 Parse 层抛异常，等价于拒绝）。
  Warning（仅记录）：未知参数键（丢弃）、字面量不可解析（保留原值，运行时按
    默认值兜底）、词表外 token、读键无生产者、死配置（component op 上的
    condition / 非 branch 的 elseSteps）。

sanitize 产物是白名单重建的干净 DTO——客户端 Parse 开了
MissingMemberHandling.Error，任何未知字段都会让注入失败，所以服务端必须
先把形状洗净（LLM 幻觉字段在这里被剥掉）。
"""

from __future__ import annotations

import json
import re

from . import schema as schema_mod

# 客户端 DTO 白名单（AbilityConfigDto / SPConfig / AbilityRuleConfig / StepConfig /
# ConditionConfig / ConditionUnit / ParamEntry 的字段集，见 docs/ability-steps.md 存储形态）
DTO_KEYS = ("abilityId", "abilityName", "description", "iconKey", "sp", "rules")
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
    def __init__(self, schema: dict):
        self.schema = schema
        self.issues: list[dict] = []
        self.reads: set[str] = set()
        self.writes: set[str] = set()
        self.fixed_writes: set[str] = set()

    def add(self, is_error: bool, path: str, message: str) -> None:
        self.issues.append({"severity": "error" if is_error else "warning", "path": path, "message": message})

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
             existing_ids: set[str] | None = None) -> tuple[bool, list[dict], dict]:
    """返回 (ok, issues, sanitized)。sanitized 总是产出；调用方仅在 ok 时使用。
    existing_ids（来自技能语料）非空时，abilityId 与现有技能撞名记 warning。"""
    schema = schema or schema_mod.load_schema()
    ctx = Walk(schema)

    if not isinstance(dto, dict):
        ctx.add(True, "", "dto is not an object")
        return False, ctx.issues, {}

    sanitized: dict = {k: dto.get(k) for k in ("abilityId", "abilityName", "description", "iconKey")}

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
        out.append({
            "triggers": _sanitize_triggers(triggers, events, ctx),
            "reentry": rule.get("reentry", "IgnoreWhileRunning"),
            "detached": bool(rule.get("detached", False)),
            "steps": _sanitize_steps(rule.get("steps"), f"{path}.steps", ctx),
        })
        reentry = out[-1]["reentry"]
        if reentry not in reentries:
            ctx.add(True, f"{path}.reentry", f"unknown reentry '{reentry}'; expected one of {sorted(reentries)}")
    return out


def _sanitize_triggers(triggers, events: set[str], ctx: Walk) -> list[dict]:
    if not isinstance(triggers, list):
        return []
    out = []
    for i, trigger in enumerate(triggers):
        path = f"triggers[{i}]"
        if not isinstance(trigger, dict):
            ctx.add(True, path, "trigger is not an object; dropped")
            continue
        event = trigger.get("triggerEvent")
        if event not in events:
            ctx.add(True, f"{path}.triggerEvent",
                    f"unknown TriggerEvent '{event}'; expected one of {sorted(events)}")
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
        ctx.add(True, path, f"unknown op '{step.get('op')}' (not registered in ability-ops.json)")
        # 保留原步骤（整技能已拒绝，保留仅为诊断）。
        return {k: step.get(k) for k in STEP_KEYS}

    canonical, op_def = resolved
    clone: dict = {"op": canonical}

    args = step.get("args")
    entries = args.get("entries") if isinstance(args, dict) else None
    clone["args"] = {"entries": _sanitize_entries(entries, op_def, canonical, path, ctx)}
    for fw in op_def.get("fixedWrites", []):
        ctx.fixed_writes.add(fw)

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
        # type 标签对齐 schema：值已按 schema 类型验证过，标签写错（如数组参数标成
        # 标量）不影响运行时取值，但违反四元组契约、误导人与工具——统一改写并记
        # warning（仅大小写差异静默归一，不算问题）。
        schema_type = param.get("type")
        if schema_type and schema_type != "any":
            if str(clone["type"] or "").lower() != str(schema_type).lower():
                ctx.add(False, path, f"param '{key}' type label {clone['type']!r} "
                                     f"coerced to schema type {schema_type!r}")
            clone["type"] = schema_type
        role = param.get("bbRole")
        if clone["value"]:
            if role == "readKey":
                ctx.reads.add(clone["value"])
            elif role == "writeKey":
                ctx.writes.add(clone["value"])
        out.append(clone)
    return out


def _validate_entry_value(entry: dict, param: dict, canonical: str, path: str, ctx: Walk) -> None:
    raw = entry["value"]
    # fromBlackboard=true 时 value 是黑板键名而非字面量，不做字面量解析。
    if entry.get("fromBlackboard") or not raw:
        return

    ptype = param.get("type")
    values = param.get("values")
    clamp_key = f"{canonical}.{entry['key']}"

    if ptype == "int":
        if _coerce_num(raw) is None or not re.fullmatch(r"[+-]?\d+", raw.strip()):
            ctx.add(False, path, f"value '{raw}' on '{canonical}.{entry['key']}' does not parse as int; "
                                 "runtime will fall back to the parameter default")
    elif ptype == "float":
        num = _coerce_num(raw)
        if num is None:
            ctx.add(False, path, f"value '{raw}' on '{canonical}.{entry['key']}' does not parse as float; "
                                 "runtime will fall back to the parameter default")
        elif clamp_key in ctx.schema.get("clamps", {}):
            clamped = _clamp(num, ctx.schema["clamps"][clamp_key])
            if clamped != num:
                entry["value"] = _fmt(clamped)
                ctx.add(False, clamp_key, f"value {_fmt(num)} clamped to {ctx.schema['clamps'][clamp_key]} -> {entry['value']}")
    elif ptype == "bool":
        if raw not in ("True", "False", "true", "false"):
            ctx.add(False, path, f"value '{raw}' on '{canonical}.{entry['key']}' does not parse as bool")
    elif ptype == "vector2int":
        if not _try_parse_vector2int(raw):
            ctx.add(False, path, f"value '{raw}' on '{canonical}.{entry['key']}' does not parse as vector2int \"x,y\"")
    elif ptype == "vector2intArray":
        if not _try_parse_vector2int_array(raw):
            ctx.add(False, path, f"value '{raw}' on '{canonical}.{entry['key']}' does not parse as "
                                 'vector2int array "[[x,y],...]"')
    elif ptype == "floatArray":
        for token in _csv_tokens(raw):
            if token and _coerce_num(token) is None:
                ctx.add(False, path, f"CSV token '{token}' on '{canonical}.{entry['key']}' does not parse as float")
    elif ptype == "intArray":
        for token in _csv_tokens(raw):
            if token and (not re.fullmatch(r"[+-]?\d+", token)):
                ctx.add(False, path, f"CSV token '{token}' on '{canonical}.{entry['key']}' does not parse as int")
    elif ptype == "stringArray":
        if values and not all(_in_values(values, t) for t in _csv_tokens(raw) if t):
            ctx.add(False, path, f"CSV token on '{canonical}.{entry['key']}' is outside the accepted tokens; "
                                 f"expected one of {values}")
    elif ptype == "string":
        if values and not _in_values(values, raw):
            ctx.add(False, path, f"value '{raw}' on '{canonical}.{entry['key']}' is outside the accepted tokens; "
                                 f"expected one of {values}")
    elif ptype == "any":
        pass
    # 未知类型标签：宽松跳过（运行时按字符串存取）。


def _check_blackboard_symmetry(ctx: Walk) -> None:
    known = set(ctx.schema.get("knownBlackboardKeys", []))
    for key in sorted(ctx.reads):
        if key in ctx.writes or key in known or key in ctx.fixed_writes:
            continue
        ctx.add(False, "blackboard",
                f"read key '{key}' has no producer in this ability "
                "(externally-fed keys are legitimate; verify intent)")
