"""Agent 工具集：交叉项计算 / 组件索引 / 组件文档 / 契约文档。

设计原则：渐进式披露——LLM 先拿到紧凑索引，需要细节时再单篇拉取，
避免一开始把 34 个组件的完整说明灌进上下文。
"""

from __future__ import annotations

import json

from . import crossitems, schema as schema_mod


def tool_definitions() -> list[dict]:
    """OpenAI function-calling 的 tools 参数。"""
    cross_desc = "按需计算战局派生指标。items 可选值：\n" + "\n".join(
        f"- {name}: {desc}" for name, (desc, _, _) in sorted(crossitems.REGISTRY.items())
    )
    return [
        {
            "type": "function",
            "function": {
                "name": "compute_cross_items",
                "description": cross_desc,
                "parameters": {
                    "type": "object",
                    "properties": {
                        "items": {"type": "array", "items": {"type": "string"},
                                  "description": "指标名列表，如 [\"nearest_enemy_distance\", \"distance(m-3)\"]"},
                    },
                    "required": ["items"],
                },
            },
        },
        {
            "type": "function",
            "function": {
                "name": "list_components",
                "description": "列出全部可用技能组件 op 的紧凑索引（op 名/类名/一句话摘要）。"
                               "先读索引再决定读哪篇详细文档，不要凭空使用未读过文档的组件。",
                "parameters": {"type": "object", "properties": {}},
            },
        },
        {
            "type": "function",
            "function": {
                "name": "read_component_doc",
                "description": "读取单个技能组件的完整说明文档（参数表/默认值/触发限制/协议依赖）。"
                               "name 传 op 名（如 apply_damage）或类名（如 ApplyDamage）。",
                "parameters": {
                    "type": "object",
                    "properties": {"name": {"type": "string", "description": "op 名或组件类名"}},
                    "required": ["name"],
                },
            },
        },
        {
            "type": "function",
            "function": {
                "name": "read_contract_doc",
                "description": "读取规则/序列/重入/生命周期契约文档（ability-steps.md）。"
                               "设计多步骤规则、判断触发时机前先读。",
                "parameters": {"type": "object", "properties": {}},
            },
        },
    ]


def execute_tool(name: str, arguments: dict, snapshot: dict, schema: dict) -> str:
    """执行工具并返回 JSON 字符串结果（工具错误以 error 字段返回，不抛异常——
    让 LLM 看到错误自行修正调用）。"""
    try:
        if name == "compute_cross_items":
            items = arguments.get("items") or []
            if isinstance(items, str):
                items = [items]
            return json.dumps({"results": crossitems.compute_all(snapshot, items)},
                              ensure_ascii=False)
        if name == "list_components":
            return json.dumps({"components": _component_index(schema)}, ensure_ascii=False)
        if name == "read_component_doc":
            return json.dumps(_component_doc(schema, arguments.get("name", "")), ensure_ascii=False)
        if name == "read_contract_doc":
            text = schema_mod.contract_docs()
            return json.dumps({"doc": text or "error: contract docs not found"}, ensure_ascii=False)
        return json.dumps({"error": f"unknown tool '{name}'"}, ensure_ascii=False)
    except Exception as exc:  # noqa: BLE001 —— 工具层永不抛，错误回喂 LLM
        return json.dumps({"error": f"{type(exc).__name__}: {exc}"}, ensure_ascii=False)


def _summary(op_def: dict) -> str:
    """组件一句话摘要：notes 的第一句；没有 notes 时用第一个参数的描述。"""
    notes = (op_def.get("notes") or "").strip()
    for sep in ("；", "。", "; "):
        if sep in notes:
            return notes.split(sep, 1)[0].strip() + sep.strip()
    return notes or "（无摘要，详见文档）"


def _component_index(schema: dict) -> list[dict]:
    return [
        {"op": op, "class": defn.get("class"), "summary": _summary(defn)}
        for op, defn in sorted(schema["componentOps"].items())
    ]


def _component_doc(schema: dict, name: str) -> dict:
    resolved = schema_mod.resolve_op(schema, name)
    if resolved is None:
        return {"error": f"unknown component '{name}'; call list_components first"}
    canonical, op_def = resolved
    if not op_def.get("doc"):
        return {"op": canonical,
                "params": op_def.get("params", []),
                "notes": op_def.get("notes", ""),
                "doc": None,
                "error": "no standalone doc file; the embedded params/notes above are authoritative"}
    text = schema_mod.read_doc(op_def["doc"])
    if text is None:
        return {"op": canonical, "error": f"doc file missing: {op_def['doc']}"}
    return {"op": canonical, "doc": op_def["doc"], "content": text}
