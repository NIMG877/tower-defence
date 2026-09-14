"""战局派生指标计算器（Agent 的 compute_cross_items 工具后端）。

客户端快照只含基本信息（自身/实体/地图），派生交叉项由 Agent 按需计算——
避免把所有可能的指标一次性塞进上下文。

指标语法：`name` 或 `name(arg)`。registry 是唯一词表，工具的描述文本从
AVAILABLE_ITEMS 生成，LLM 不需要猜。
"""

from __future__ import annotations

import math

# 指标名 → (说明, 需要参数, 计算函数)。函数签名 fn(snapshot, arg)。
REGISTRY: dict[str, tuple[str, bool, object]] = {}


def item(name: str, desc: str, needs_arg: bool = False):
    def register(fn):
        REGISTRY[name] = (desc, needs_arg, fn)
        return fn
    return register


def parse_item(item: str) -> tuple[str, str | None]:
    """'distance(m-3)' → ('distance', 'm-3')；'enemy_count' → ('enemy_count', None)。"""
    if "(" in item and item.endswith(")"):
        name, arg = item[:-1].split("(", 1)
        return name.strip(), arg.strip()
    return item.strip(), None


def compute(snapshot: dict, item: str):
    """计算单个指标；未知名或参数不符抛 ValueError（工具层转成错误信息回给 LLM）。"""
    name, arg = parse_item(item)
    if name not in REGISTRY:
        raise ValueError(f"unknown cross item '{item}'; available: {', '.join(sorted(REGISTRY))}")
    desc, needs_arg, fn = REGISTRY[name]
    if needs_arg and not arg:
        raise ValueError(f"cross item '{name}' requires an argument: {name}(<...>)")
    if not needs_arg and arg is not None:
        raise ValueError(f"cross item '{name}' takes no argument")
    return fn(snapshot, arg)


def compute_all(snapshot: dict, items: list[str]) -> dict:
    """批量计算；单个失败不影响其它，错误以字符串值返回。"""
    return {item: _safe(snapshot, item) for item in items}


def _safe(snapshot: dict, item: str):
    try:
        return compute(snapshot, item)
    except (ValueError, KeyError, TypeError) as exc:
        return f"error: {exc}"


# ---------- 内部取数 helpers ----------

def _entities(snapshot: dict, camp: int | None = None) -> list[dict]:
    out = []
    for e in snapshot.get("entities") or []:
        if camp is None or e.get("camp") == camp:
            out.append(e)
    return out


def dist(a: tuple[float, float], b: tuple[float, float]) -> float:
    return math.sqrt((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2)


def self_record(snapshot: dict) -> dict | None:
    """契约 v2：self 拍平进 entities，selfId 作指针。无 selfId 或指针悬空返回
    None（generate 入口的 battle_digest.build 先做形状校验，工具层拿到的是已
    过检的快照）。"""
    self_id = snapshot.get("selfId")
    if not self_id:
        return None
    for e in snapshot.get("entities") or []:
        if e.get("id") == self_id:
            return e
    return None


def _self_pos(snapshot: dict) -> tuple[float, float] | None:
    rec = self_record(snapshot)
    if rec is None:
        return None
    pos = rec.get("pos")
    if isinstance(pos, dict) and "x" in pos and "y" in pos:
        return (float(pos["x"]), float(pos["y"]))
    return None


# ---------- 指标实现 ----------

@item("enemy_count", "场上敌人数")
def _enemy_count(snapshot, arg):
    return len(_entities(snapshot, 2))


@item("ally_count", "场上友军数（不含自身）")
def _ally_count(snapshot, arg):
    self_id = snapshot.get("selfId")
    return sum(1 for e in _entities(snapshot, 1) if e.get("id") != self_id)


@item("self_hp_rate", "自身血量比 0..1")
def _self_hp_rate(snapshot, arg):
    rec = self_record(snapshot)
    if rec is not None and isinstance(rec.get("hpRate"), (int, float)):
        return rec["hpRate"]
    return None


@item("nearest_enemy_distance", "自身到最近敌人的距离；无敌人返回 -1")
def _nearest_enemy(snapshot, arg):
    self_pos = _self_pos(snapshot)
    enemies = _entities(snapshot, 2)
    if self_pos is None or not enemies:
        return -1
    best = min(dist(self_pos, (e["pos"]["x"], e["pos"]["y"])) for e in enemies
               if isinstance(e.get("pos"), dict))
    return round(best, 2)


@item("lowest_hp_enemy", "血量比最低的敌人 {id, hpRate}；无敌人返回 null")
def _lowest_hp_enemy(snapshot, arg):
    enemies = [e for e in _entities(snapshot, 2) if isinstance(e.get("hpRate"), (int, float))]
    if not enemies:
        return None
    target = min(enemies, key=lambda e: e["hpRate"])
    return {"id": target.get("id"), "hpRate": target.get("hpRate")}


@item("entity", "按 id 取单个实体的完整记录，如 entity(m-3)", needs_arg=True)
def _entity(snapshot, arg):
    for e in _entities(snapshot):
        if e.get("id") == arg:
            return e
    return f"error: entity '{arg}' not in snapshot"


@item("distance", "自身到指定实体的距离，如 distance(m-3)", needs_arg=True)
def _distance(snapshot, arg):
    self_pos = _self_pos(snapshot)
    if self_pos is None:
        return "error: self entity has no pos in snapshot"
    for e in _entities(snapshot):
        if e.get("id") == arg and isinstance(e.get("pos"), dict):
            return round(dist(self_pos, (e["pos"]["x"], e["pos"]["y"])), 2)
    return f"error: entity '{arg}' not in snapshot"


@item("hp_rate", "指定实体的血量比，如 hp_rate(m-3)", needs_arg=True)
def _hp_rate(snapshot, arg):
    for e in _entities(snapshot):
        if e.get("id") == arg:
            return e.get("hpRate")
    return f"error: entity '{arg}' not in snapshot"


def _within(snapshot, arg, camp: int | None):
    self_pos = _self_pos(snapshot)
    if self_pos is None:
        return "error: self entity has no pos in snapshot"
    radius = float(arg)
    self_id = snapshot.get("selfId")
    ids = [e.get("id") for e in _entities(snapshot, camp)
           if e.get("id") != self_id
           and isinstance(e.get("pos"), dict)
           and dist(self_pos, (e["pos"]["x"], e["pos"]["y"])) <= radius]
    return {"radius": radius, "count": len(ids), "ids": ids}


@item("enemies_within", "自身半径内敌人清单，如 enemies_within(3.5)", needs_arg=True)
def _enemies_within(snapshot, arg):
    return _within(snapshot, arg, 2)


@item("allies_within", "自身半径内友军清单（不含自身），如 allies_within(2)", needs_arg=True)
def _allies_within(snapshot, arg):
    return _within(snapshot, arg, 1)


@item("can_set_cells", "可部署格（高台+地面合并列表）")
def _can_set_cells(snapshot, arg):
    return {"higher": snapshot.get("canSetHigher") or [],
            "lower": snapshot.get("canSetLower") or []}


@item("pairwise_distance", "任意两实体间距离，如 pairwise_distance(m-1,m-2)", needs_arg=True)
def _pairwise_distance(snapshot, arg):
    id_a, _, id_b = (s.strip() for s in arg.partition(","))
    if not id_b:
        return "error: pairwise_distance takes two ids: pairwise_distance(idA,idB)"
    positions: dict[str, tuple[float, float]] = {}
    for e in _entities(snapshot):
        if e.get("id") in (id_a, id_b) and isinstance(e.get("pos"), dict):
            positions[e["id"]] = (float(e["pos"]["x"]), float(e["pos"]["y"]))
    for wanted in (id_a, id_b):
        if wanted not in positions:
            return f"error: entity '{wanted}' not in snapshot"
    return round(dist(positions[id_a], positions[id_b]), 2)


@item("enemy_group_stats", "敌群聚合：count/totalHp/avgHpRate/avgDefence/avgMagicRes")
def _enemy_group_stats(snapshot, arg):
    enemies = _entities(snapshot, 2)

    def nums(key) -> list[float]:
        return [e[key] for e in enemies if isinstance(e.get(key), (int, float))]

    hp = nums("hp")
    hp_rate = nums("hpRate")
    defence = nums("defence")
    magic_res = nums("magicRes")
    avg = lambda xs: round(sum(xs) / len(xs), 2) if xs else None  # noqa: E731
    return {"count": len(enemies), "totalHp": sum(hp) if hp else None,
            "avgHpRate": avg(hp_rate), "avgDefence": avg(defence),
            "avgMagicRes": avg(magic_res)}


# ---------- 明细查询（agent 工具层的结构化入口，非 registry 指标） ----------

def query_entities_at(snapshot: dict, x: float, y: float, radius: float,
                      camp: int | None = None) -> list[dict]:
    """任意圆心 (x,y) 半径 radius 内的实体完整记录；camp 过滤可选。"""
    out = []
    for e in _entities(snapshot, camp):
        pos = e.get("pos")
        if isinstance(pos, dict) and "x" in pos and "y" in pos:
            if dist((float(x), float(y)), (float(pos["x"]), float(pos["y"]))) <= radius:
                out.append(e)
    return out
