"""schema 与文档加载。

ability-ops.json 是双端单一数据源：客户端（C# AbilityOpsSchema）与本服务端读
仓库里同一份文件，校验词汇表永远同源。文档（docs/ability-steps.md、
docs/skill-components/）是提示词素材。
"""

from __future__ import annotations

import json
from functools import lru_cache
from pathlib import Path

APP_DIR = Path(__file__).resolve().parent
REPO_ROOT = APP_DIR.parents[1]
SCHEMA_PATH = REPO_ROOT / "Assets" / "Resources" / "Data" / "AbilityOps" / "ability-ops.json"
DOCS_DIR = REPO_ROOT / "docs"


@lru_cache(maxsize=1)
def load_schema() -> dict:
    with open(SCHEMA_PATH, encoding="utf-8") as f:
        return json.load(f)


def canonical_ops(schema: dict) -> set[str]:
    """全部 canonical op 名（原语 + 组件）。客户端版本握手拿它做集合 diff。"""
    return set(schema["primitives"]) | set(schema["componentOps"])


def resolve_op(schema: dict, op: str) -> tuple[str, dict] | None:
    """op 名 → (canonical, op 定义)。原语按 canonical；组件接受 canonical 与
    PascalCase 别名（镜像运行时注册表）。未注册返回 None。"""
    if not op:
        return None
    primitive = schema["primitives"].get(op)
    if primitive is not None:
        return op, primitive
    comp = schema["componentOps"].get(op)
    if comp is not None:
        return op, comp
    for canonical, comp in schema["componentOps"].items():
        aliases = comp.get("aliases") or []
        if op in aliases:
            return canonical, comp
    return None


def op_param(op_def: dict, key: str) -> dict | None:
    for param in op_def.get("params", []):
        if param.get("key") == key:
            return param
    return None


def read_doc(rel_path: str) -> str | None:
    """读 docs 下的组件/契约文档；缺失返回 None（文档与代码可能滞后）。"""
    path = DOCS_DIR / rel_path.removeprefix("docs/")
    try:
        return path.read_text(encoding="utf-8")
    except OSError:
        return None


@lru_cache(maxsize=1)
def contract_docs() -> str:
    """规则契约文档（ability-steps + 组件 README）。缺失时降级为空串。"""
    parts = []
    for rel in ("docs/ability-steps.md", "docs/skill-components/README.md"):
        text = read_doc(rel)
        if text:
            parts.append(f"<!-- {rel} -->\n{text}")
    return "\n\n".join(parts)
