"""技能语料：现有 AbilityConfig 资产经 Unity 菜单导出的 data/skills.json。

两个用途（用户拍板的 2+3 方案）：
- retrieve：按 plannedOps 与语料技能的 op 集合 Jaccard 重叠取 top-k，作为
  generate 阶段的少样本范例（组合惯用法与数值量级）；
- style_summary：全语料蒸馏的风格统计（各 op 参数取值范围/常用值、reentry
  分布、规则规模），作为 generate system 的常数先验。
- 语料的 existingIds 另供校验器做 abilityId 撞名 warning。

文件按 mtime 惰性重载：Unity 重新导出后无需重启服务。缺失/损坏 → 返回
None，调用方全部静默降级为无语料（不注入、不统计、不查撞名）。
"""

from __future__ import annotations

import json
from collections import Counter
from pathlib import Path

# 通用原语几乎每个技能都有，参与 Jaccard 只会稀释区分度，不计入重叠。
PRIMITIVE_OPS = {"delay", "wait_until", "branch", "loop"}

_cache: dict = {"path": None, "mtime": None, "corpus": None}


def load_corpus(path: str) -> dict | None:
    """读取并校验语料文件。返回 {"skills": [...], "existingIds": set}；不可用返回 None。"""
    try:
        raw = json.loads(Path(path).read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError, TypeError):
        return None
    skills = raw.get("skills")
    if not isinstance(skills, list) or not skills:
        return None
    existing = {s.get("abilityId") for s in skills if isinstance(s, dict) and s.get("abilityId")}
    return {"skills": skills, "existingIds": existing}


def get_corpus(path: str | None) -> dict | None:
    """带 mtime 缓存的语料访问入口（同一进程内重复调用只 stat 一次文件）。"""
    if not path:
        return None
    try:
        mtime = Path(path).stat().st_mtime
    except OSError:
        _cache.update(path=path, mtime=None, corpus=None)
        return None
    if _cache["path"] == path and _cache["mtime"] == mtime:
        return _cache["corpus"]
    corpus = load_corpus(path)
    _cache.update(path=path, mtime=mtime, corpus=corpus)
    return corpus


def _canonical(schema: dict, op: str) -> str:
    """资产里的 op 可能是 legacy 组件类名（GetRules 的别名路径），统一到 canonical。"""
    if schema:
        resolved = schema.get("componentOps", {}).get(op)
        if resolved is not None:
            return op  # 已是 canonical 键
        for name, op_def in schema.get("componentOps", {}).items():
            if op in op_def.get("aliases", []):
                return name
    return op


def skill_ops(skill: dict, schema: dict | None = None) -> set[str]:
    """技能用到的组件 op 集合（递归 steps/elseSteps，排除原语，canonical 化）。"""
    ops: set[str] = set()

    def walk(steps) -> None:
        for step in steps or []:
            if not isinstance(step, dict):
                continue
            op = step.get("op")
            if isinstance(op, str) and op and op not in PRIMITIVE_OPS:
                ops.add(_canonical(schema, op))
            walk(step.get("steps"))
            walk(step.get("elseSteps"))

    for rule in skill.get("rules") or []:
        if isinstance(rule, dict):
            walk(rule.get("steps"))
    return ops


def retrieve(corpus: dict, planned_ops: list[str], schema: dict | None = None,
             k: int = 3) -> list[dict]:
    """按 op 集合 Jaccard 取 top-k 范例（score>0 才返回；同分按 abilityId 稳定排序）。
    返回项为语料技能原 dict，附 "_score" 仅供调试/上报，注入提示词前会剥离。"""
    planned = {_canonical(schema, op) for op in planned_ops}
    if not planned:
        return []
    scored = []
    for skill in corpus["skills"]:
        ops = skill_ops(skill, schema)
        union = ops | planned
        score = len(ops & planned) / len(union) if union else 0.0
        if score > 0:
            scored.append((score, str(skill.get("abilityId") or ""), skill))
    scored.sort(key=lambda item: (-item[0], item[1]))
    out = []
    for score, _, skill in scored[:k]:
        out.append({**skill, "_score": round(score, 3)})
    return out


def style_summary(corpus: dict, schema: dict | None = None) -> dict:
    """全语料蒸馏：每 op 的使用数与参数取值统计 + 全局规模/reentry 分布。

    数值参数给 min/max；字符串参数给 top-3 常用值（截断 24 字符）。输出体量
    有界（只含有技能使用的 op），作为 generate system 的常数先验。
    """
    op_skills: Counter = Counter()
    op_params: dict[str, dict[str, dict]] = {}
    reentry: Counter = Counter()
    rules_counts: list[int] = []
    steps_counts: list[int] = []

    def collect_params(op: str, entries) -> None:
        bucket = op_params.setdefault(op, {})
        for entry in entries or []:
            if not isinstance(entry, dict) or not entry.get("key"):
                continue
            key, raw, ptype = entry["key"], entry.get("value"), entry.get("type")
            stat = bucket.setdefault(key, {"type": ptype, "n": 0, "min": None, "max": None,
                                           "values": Counter()})
            stat["n"] += 1
            # 客户端导出的 type 是枚举名 PascalCase（Float/Int），schema 编码是小写——归一后判定
            if isinstance(ptype, str) and ptype.lower() in ("int", "float") and isinstance(raw, str):
                try:
                    num = float(raw)
                except ValueError:
                    pass
                else:
                    stat["min"] = num if stat["min"] is None else min(stat["min"], num)
                    stat["max"] = num if stat["max"] is None else max(stat["max"], num)
            elif isinstance(raw, str):
                stat["values"][raw[:24]] += 1

    def walk(steps) -> None:
        """与 skill_ops 同深度递归：嵌套 steps/elseSteps 里的参数也进统计。"""
        for step in steps or []:
            if not isinstance(step, dict):
                continue
            op = step.get("op")
            if isinstance(op, str):
                entries = (step.get("args") or {}).get("entries") \
                    if isinstance(step.get("args"), dict) else None
                collect_params(_canonical(schema, op), entries)
            walk(step.get("steps"))
            walk(step.get("elseSteps"))

    for skill in corpus["skills"]:
        ops = skill_ops(skill, schema)
        for op in ops:
            op_skills[op] += 1
        rules = skill.get("rules") or []
        rules_counts.append(len(rules))
        for rule in rules:
            if not isinstance(rule, dict):
                continue
            reentry[rule.get("reentry", "IgnoreWhileRunning")] += 1
            rule_steps = rule.get("steps") or []
            steps_counts.append(len(rule_steps))
            walk(rule_steps)

    params_out = {}
    for op, bucket in op_params.items():
        params_out[op] = {}
        for key, stat in bucket.items():
            entry = {"type": stat["type"], "n": stat["n"]}
            if stat["min"] is not None:
                entry["min"], entry["max"] = stat["min"], stat["max"]
            if stat["values"]:
                entry["values"] = [v for v, _ in stat["values"].most_common(3)]
            params_out[op][key] = entry

    return {
        "skills": len(corpus["skills"]),
        "opUsage": dict(op_skills.most_common()),
        "reentry": dict(reentry.most_common()),
        "rulesPerSkill": {"min": min(rules_counts), "max": max(rules_counts)} if rules_counts else {},
        "stepsPerRule": {"min": min(steps_counts), "max": max(steps_counts)} if steps_counts else {},
        "paramStats": params_out,
    }
