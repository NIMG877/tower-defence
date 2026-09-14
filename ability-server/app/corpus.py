"""技能语料：现有 AbilityConfig 资产经 Unity 菜单导出的 data/skills.json。

用途（agent 工具层与校验器的数据源）：
- search_skills / read_skill：设计前查先例——相似技能怎么组合、数值量级多少；
- usage_stats：语料零使用的触发事件/reentry 记 info"无先例"（validator 低频挡位）；
- existingIds：abilityId 撞名 warning（validator）。

文件按 mtime 惰性重载：Unity 重新导出后无需重启服务。缺失/损坏 → 返回
None，调用方全部静默降级为无语料（不检索、不统计、不查撞名）。
"""

from __future__ import annotations

import json
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


def search_skills(corpus: dict, query: str, schema: dict | None = None,
                  limit: int = 5) -> list[dict]:
    """关键词检索（agent 的 search_skills 工具后端）：匹配 abilityId/名称/描述
    子串与 op 名全词，按命中项数排序。返回紧凑列表，全文走 read_skill——
    设计前查先例，替代 v1 的语料后置注入（不受控变量）。"""
    terms = [t for t in (query or "").split() if t]
    whole = (query or "").strip()
    if whole and whole not in terms:
        terms.append(whole)  # CJK 诉求无空格，整串也作为一个词参与匹配
    if not terms:
        return []
    scored = []
    for skill in corpus["skills"]:
        ops = skill_ops(skill, schema)
        haystack = " ".join(filter(None, (skill.get("abilityId"),
                                          skill.get("abilityName"),
                                          skill.get("description")))).lower()
        hits = sum(1 for t in terms
                   if t.lower() in haystack or t in ops)
        if hits:
            scored.append((hits, str(skill.get("abilityId") or ""), skill))
    scored.sort(key=lambda item: (-item[0], item[1]))
    out = []
    for _, _, skill in scored[:limit]:
        out.append({"abilityId": skill.get("abilityId"),
                    "abilityName": skill.get("abilityName"),
                    "sp": (skill.get("sp") or {}).get("totalSp"),
                    "ops": sorted(skill_ops(skill, schema))})
    return out


def read_skill(corpus: dict, ability_id: str) -> dict | None:
    """按 abilityId 取语料技能全文（agent 的 read_skill 工具后端）。"""
    for skill in corpus["skills"]:
        if skill.get("abilityId") == ability_id:
            return skill
    return None


def usage_stats(corpus: dict) -> dict:
    """语料使用统计（validator 低频挡位提示的数据源）：全部语料技能用到的
    triggerEvent 集合与 reentry 集合。配置命中集合外的取值时记 info"无先例"。"""
    events: set[str] = set()
    reentries: set[str] = set()
    for skill in corpus["skills"]:
        for rule in skill.get("rules") or []:
            if not isinstance(rule, dict):
                continue
            reentries.add(rule.get("reentry", "IgnoreWhileRunning"))
            for trigger in rule.get("triggers") or []:
                if isinstance(trigger, dict) and trigger.get("triggerEvent"):
                    events.add(trigger["triggerEvent"])
    return {"triggerEvents": events, "reentries": reentries}
