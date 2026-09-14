"""schema 加载：从组件库 ability.db 重建服务端校验形状（契约 JSON 已退役）。

库是唯一真源；ability_db 按 mtime 惰性缓存——改库后无需重启服务。
"""

from __future__ import annotations

from . import ability_db
from .config import Config


def load_schema() -> dict:
    snap = ability_db.get(Config().db_path)
    if snap is None:
        raise RuntimeError(
            f"组件库不可用：{Config().db_path}。请恢复提交的 ability.db，"
            "或重跑 python db/import_skills.py 重建语料（其余表随库文件自带）。")
    return snap.schema


def canonical_ops(schema: dict) -> set[str]:
    """全部 canonical op 名（原语 + 组件）。客户端握手拿它做集合 diff。"""
    return set(schema["primitives"]) | set(schema["componentOps"])


def resolve_op(schema: dict, op: str) -> tuple[str, dict] | None:
    """op 名 → (canonical, op 定义)。只认 canonical 名（类名别名已退役）。"""
    if not op:
        return None
    primitive = schema["primitives"].get(op)
    if primitive is not None:
        return op, primitive
    comp = schema["componentOps"].get(op)
    if comp is not None:
        return op, comp
    return None


def op_param(op_def: dict, key: str) -> dict | None:
    for param in op_def.get("params", []):
        if param.get("key") == key:
            return param
    return None
