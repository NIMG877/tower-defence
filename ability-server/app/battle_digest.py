"""战局 digest：从战局快照计算注入用战局摘要（agent 首条 user 消息）。

设计依据（plan-agent-framework-v2 §4.1）：长结构化 JSON 对 flash 档模型有
lost-in-the-middle / 嵌套扫描不可靠 / 规模无界 / 输出复述污染四类实害，派生
摘要必须代码算——全量 entities 只作为工具层的数据源随请求传输，不进提示词。

digest 是常数体积的：self 完整记录、敌我计数、最近敌方 top-K（默认 6）、
敌方血量/攻击/防御/法抗分布统计、距离分带计数（默认 3/5/8）、地图尺寸 +
self 所在格 + 可部署格计数与 self 周边可部署格。
"""

from __future__ import annotations

from . import crossitems

NEAREST_K = 6
DISTANCE_BANDS = (3.0, 5.0, 8.0)
NEAR_SELF_RADIUS = 1.5

# camp 数字语义（Entity.cs 死亡/波次分支）：1=友军 2=敌人。提示词与工具共用。
CAMP_ENCODING = "1=友军 2=敌人"


def _pos(e: dict) -> tuple[float, float] | None:
    pos = e.get("pos")
    if isinstance(pos, dict) and "x" in pos and "y" in pos:
        return (float(pos["x"]), float(pos["y"]))
    return None


def _tile(pos: tuple[float, float]) -> list[int]:
    return [int(round(pos[0])), int(round(pos[1]))]


def _stat(values: list[float]) -> dict | None:
    if not values:
        return None
    return {"min": min(values), "max": max(values),
            "avg": round(sum(values) / len(values), 2)}


def _self_record(snapshot: dict) -> dict:
    """self 记录查找复用 crossitems.self_record（None 语义）；本模块是 generate
    入口的形状校验点，指针悬空升格为 ValueError 由调用方拒绝请求。"""
    rec = crossitems.self_record(snapshot)
    if rec is None:
        raise ValueError(f"battleSnapshot has no entity matching "
                         f"selfId={snapshot.get('selfId')!r} "
                         "(contract v2 puts self into entities)")
    return rec


def build(snapshot: dict, nearest_k: int = NEAREST_K,
          bands: tuple[float, ...] = DISTANCE_BANDS) -> dict:
    """快照 → digest（纯函数，pytest 可测）。快照形状：self 拍平在 entities。"""
    self_rec = _self_record(snapshot)
    self_pos = _pos(self_rec)
    entities = snapshot.get("entities") or []
    allies = [e for e in entities if e.get("camp") == 1 and e is not self_rec
              and e.get("id") != self_rec.get("id")]
    enemies = [e for e in entities if e.get("camp") == 2]

    nearest = []
    bands_out = {f"{int(b)}": 0 for b in bands}
    bands_out["beyond"] = 0
    if self_pos is not None:
        with_dist = []
        for e in enemies:
            pos = _pos(e)
            if pos is None:
                continue
            d = crossitems.dist(self_pos, pos)
            with_dist.append((d, e))
            hit = False
            for b in bands:
                if d <= b:
                    bands_out[f"{int(b)}"] += 1
                    hit = True
                    break
            if not hit:
                bands_out["beyond"] += 1
        with_dist.sort(key=lambda pair: (pair[0], str(pair[1].get("id"))))
        nearest = [{**e, "distance": round(d, 2)} for d, e in with_dist[:nearest_k]]

    def nums(records, key) -> list[float]:
        return [e[key] for e in records if isinstance(e.get(key), (int, float))]

    enemy_stats = {key: stat for key in ("hp", "hpRate", "attack", "defence", "magicRes")
                   if (stat := _stat(nums(enemies, key))) is not None}

    higher = snapshot.get("canSetHigher") or []
    lower = snapshot.get("canSetLower") or []
    near = (deploy_cells_near(snapshot, self_pos, NEAR_SELF_RADIUS)
            if self_pos is not None else {"higher": [], "lower": []})

    return {
        "self": {**self_rec, "tile": _tile(self_pos) if self_pos else None},
        "counts": {"enemies": len(enemies), "allies": len(allies),
                   "statics": sum(1 for e in entities if e.get("isStatic"))},
        "nearestEnemies": nearest,
        "enemyStats": enemy_stats,
        "enemyDistanceBands": bands_out,
        "map": {"sizeI": snapshot.get("mapI"), "sizeJ": snapshot.get("mapJ"),
                "deployable": {"higher": len(higher), "lower": len(lower)},
                "nearSelf": near},
    }


def deploy_cells_near(snapshot: dict, center: tuple[float, float], radius: float) -> dict:
    """以任意圆心查可部署格（deploy_cells_near 工具后端；半径内高台/地面分列）。
    格子 "i,j" 格式由客户端 CollectCanSet 保证，脏格式直接抛错（digest 在 generate
    入口被 ValueError 拒绝；工具层由 execute_tool 转 error 回喂）。"""
    out = {"higher": [], "lower": []}
    for kind, key in (("higher", "canSetHigher"), ("lower", "canSetLower")):
        for cell in snapshot.get(key) or []:
            ci, cj = cell.split(",")[:2]
            if crossitems.dist(center, (float(ci), float(cj))) <= radius:
                out[kind].append(cell)
    return out
