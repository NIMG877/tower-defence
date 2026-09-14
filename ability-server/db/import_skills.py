"""导入技能语料：Unity 菜单④导出的 data/skills.json → ability.db skills 表。

skills 表是只读语料（真相源为 Unity 资产），本脚本是唯一写入通道。
其余表（组件/参数/词表/文档列）真源就是库本身，直接改库，与本脚本无关；
本脚本无外部依赖。服务端按库文件 mtime 惰性重载，导入完成即生效，无需重启。
"""

from __future__ import annotations

import json
import sqlite3
import sys
from datetime import date
from pathlib import Path

DB_DIR = Path(__file__).resolve().parent
ABILITY_SERVER = DB_DIR.parent
SKILLS_JSON = ABILITY_SERVER / "data" / "skills.json"
OUT_DB = ABILITY_SERVER / "data" / "ability.db"
IMPORTED_AT = date.today().isoformat()


def jdump(v) -> str:
    return json.dumps(v, ensure_ascii=False, separators=(",", ":"))


def main() -> None:
    raw = json.loads(SKILLS_JSON.read_text(encoding="utf-8"))
    skills = raw.get("skills")
    if not isinstance(skills, list) or not skills:
        print("skills.json 无可用技能，放弃导入（既有 skills 表保持不变）")
        return

    con = sqlite3.connect(OUT_DB)
    try:
        con.execute("DELETE FROM skills")
        for s in skills:
            if not isinstance(s, dict) or not s.get("abilityId"):
                continue
            con.execute(
                "INSERT INTO skills (ability_id, name, description, sp, rules, imported_at)"
                " VALUES (?,?,?,?,?,?)",
                (
                    s.get("abilityId"), s.get("abilityName") or "", s.get("description") or "",
                    jdump(s.get("sp")), jdump(s.get("rules") or []), IMPORTED_AT,
                ),
        )
        kept = con.execute("SELECT COUNT(*) FROM skills").fetchone()[0]
        con.commit()
        print(f"导入 {kept} 个技能 → {OUT_DB}（服务端 mtime 感知，无需重启）")
    finally:
        con.close()


if __name__ == "__main__":
    main()
