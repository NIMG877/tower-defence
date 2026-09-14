"""组件库 ability.db 访问:mtime 惰性缓存快照(文档工具与语料检索的数据源)。

库是组件规则/文档/语料的唯一真源(文档列与规则列直接 UPDATE 维护;skills
经 db/import_skills.py 从 Unity 导出的 skills.json 导入)。本模块按库文件
mtime 惰性缓存——改库后无需重启服务。缺失/损坏返回 None,调用方按 corpus
同款纪律静默降级。
"""

from __future__ import annotations

import json
import sqlite3
from pathlib import Path

_cache: dict = {"path": None, "mtime": None, "snapshot": None}


class Snapshot:
    """一次库读取的全量快照(纯数据,连接即关)。"""

    def __init__(self, con: sqlite3.Connection):
        self.components = [  # list_components 紧凑索引(op/一句话摘要;不带 class,尺寸回 4k 线下)
            {"op": r["op"], "summary": r["summary"]}
            for r in con.execute(
                "SELECT op, summary FROM ops WHERE kind='component' ORDER BY op")
        ]
        self.docs: dict[str, dict] = {}  # canonical op → read_component_doc 响应
        for r in con.execute(
                "SELECT op, kind, category, summary, behavior, wiring"
                " FROM ops ORDER BY op"):
            params = []
            for p in con.execute(
                    "SELECT key, type, default_value, allowed_values, bb_role, desc"
                    " FROM op_params WHERE op=? ORDER BY rowid", (r["op"],)):
                entry = {"key": p["key"], "type": p["type"],
                         "default": json.loads(p["default_value"]),
                         "values": json.loads(p["allowed_values"]) if p["allowed_values"] is not None else None}
                if p["bb_role"] is not None:
                    entry["bbRole"] = p["bb_role"]
                entry["desc"] = p["desc"]
                params.append(entry)
            self.docs[r["op"]] = {
                "op": r["op"],
                "category": r["category"],
                "summary": r["summary"],
                "behavior": r["behavior"],
                "wiring": r["wiring"],
                "params": params,
            }
        self.contract_sections: dict[str, str] = {
            r["section"]: r["content"] for r in con.execute(
                "SELECT section, content FROM global_docs ORDER BY rowid")}
        self.skills = [  # 语料检索投影：身份/描述/SP 量级 + 规则本体（不存全量配置）
            {"abilityId": r["ability_id"], "abilityName": r["name"],
             "description": r["description"], "sp": json.loads(r["sp"]),
             "rules": json.loads(r["rules"])}
            for r in con.execute(
                "SELECT ability_id, name, description, sp, rules FROM skills ORDER BY ability_id")]
        self.existing_ids = {r["ability_id"] for r in con.execute(
            "SELECT ability_id FROM skills")}

        # ---- 服务端校验形状（validator/tools/握手消费；库即唯一真源） ----
        self.meta = {k: v for k, v in con.execute("SELECT key, value FROM meta")}

        def contract_params(op: str) -> list[dict]:
            out = []
            for p in con.execute(
                    "SELECT key, type, default_value, allowed_values, bb_role, desc"
                    " FROM op_params WHERE op=? ORDER BY rowid", (op,)):
                entry = {"key": p["key"], "type": p["type"],
                         "default": json.loads(p["default_value"])}
                if p["allowed_values"] is not None:
                    entry["values"] = json.loads(p["allowed_values"])
                entry["bbRole"] = p["bb_role"]
                entry["desc"] = p["desc"]
                out.append(entry)
            return out

        primitives: dict[str, dict] = {}
        for r in con.execute(
                "SELECT op, uses_condition, has_else FROM ops WHERE kind='primitive' ORDER BY rowid"):
            od: dict = {"usesCondition": bool(r["uses_condition"])}
            if r["has_else"] is not None:
                od["hasElse"] = bool(r["has_else"])
            od["params"] = contract_params(r["op"])
            primitives[r["op"]] = od
        components: dict[str, dict] = {}
        for r in con.execute(
                "SELECT op, fixed_writes FROM ops WHERE kind='component' ORDER BY rowid"):
            od = {}
            if r["fixed_writes"] is not None:
                od["fixedWrites"] = json.loads(r["fixed_writes"])
            od["params"] = contract_params(r["op"])
            components[r["op"]] = od

        def vocab(name: str) -> list:
            return json.loads(con.execute(
                "SELECT items FROM vocab_lists WHERE list_name=?", (name,)).fetchone()[0])

        self.schema: dict = {
            "protocolVersion": int(self.meta["protocolVersion"]),
            "triggerEvents": vocab("triggerEvents"),
            "conditionOps": vocab("conditionOps"),
            "ruleReentry": vocab("ruleReentry"),
            "paramValueTypeEncoding": {r["type"]: r["encoding"] for r in con.execute(
                "SELECT type, encoding FROM value_type_encodings ORDER BY rowid")},
            "primitives": primitives,
            "componentOps": components,
            "clamps": {r["clamp_key"]: [int(r["min_value"]) if float(r["min_value"]).is_integer() else r["min_value"],
                                        int(r["max_value"]) if float(r["max_value"]).is_integer() else r["max_value"]]
                       for r in con.execute(
                           "SELECT clamp_key, min_value, max_value FROM clamps ORDER BY rowid")},
            "eventContext": {r["event"]: {} for r in con.execute(
                "SELECT DISTINCT event FROM event_context ORDER BY rowid")},
            "entityContext": {r["name"]: {"type": r["type"], "desc": r["desc"]} for r in con.execute(
                "SELECT name, type, desc FROM entity_context ORDER BY rowid")},
        }
        for r in con.execute("SELECT event, path, type, desc FROM event_context ORDER BY rowid"):
            self.schema["eventContext"][r["event"]][r["path"]] = {"type": r["type"], "desc": r["desc"]}


def get(db_path: str | None) -> Snapshot | None:
    """带 mtime 缓存的快照入口(同一进程内重复调用只 stat 一次文件)。"""
    if not db_path:
        return None
    try:
        mtime = Path(db_path).stat().st_mtime
    except OSError:
        _cache.update(path=None, mtime=None, snapshot=None)
        return None
    if _cache["path"] == db_path and _cache["mtime"] == mtime:
        return _cache["snapshot"]
    snapshot = None
    try:
        con = sqlite3.connect(f"file:{Path(db_path).as_posix()}?mode=ro", uri=True)
        con.row_factory = sqlite3.Row
        try:
            snapshot = Snapshot(con)
        finally:
            con.close()
    except sqlite3.Error:
        snapshot = None
    _cache.update(path=db_path, mtime=mtime, snapshot=snapshot)
    return snapshot
