"""黄金任务评测集生成器：构造 v2 形状的请求样例 + 期望特征表，落盘 tasks/*.json。

任务设计原则（plan-agent-framework-v2 §六.1）：
- 覆盖高频 op（apply_buff/destroy_buff/write_blackboard/select_targets/apply_damage…）
  与典型触发事件（OnAbilityBegin/End、OnTick、OnAfterTakeDamage、OnAfterAttack…）；
- 快照为契约 v2 形状：self 拍平进 entities、selfId 作指针、实体含 massLevel；
- hostAssets 为契约 v2 形状：job/subJob/animations/bullets/canSpawnEntities/决策字段；
- expected = op 集合必须含/任选其一/不得含 + 触发事件 + 数值量级带 + 效果一句话。

用法：python evals/build_tasks.py   （幂等，重跑覆盖 tasks/*.json）
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
SCHEMA_PATH = HERE.parents[1] / "Assets" / "Resources" / "Data" / "AbilityOps" / "ability-ops.json"
OUT_DIR = HERE / "tasks"

# ===== 实体/宿主工厂 =====


def ent(eid, camp, pos, hp, attack, defence=50, magic_res=0, is_static=False,
        name=None, job=-1, label=None, mass=1, hp_rate=1.0):
    return {"id": eid, "name": name or eid, "camp": camp, "isStatic": is_static,
            "hp": round(hp * hp_rate), "maxHp": hp, "hpRate": hp_rate,
            "pos": {"x": pos[0], "y": pos[1]}, "attack": attack, "defence": defence,
            "magicRes": magic_res, "job": job, "label": label, "massLevel": mass}


def self_ent(eid, pos, hp, attack, defence=100, magic_res=10, job=1, mass=2, hp_rate=1.0):
    """camp=1（友军），job 用 EntityData.CharacterJob 编码。"""
    return ent(eid, 1, pos, hp, attack, defence, magic_res, False, name=eid,
               job=job, label=None, mass=mass, hp_rate=hp_rate)


# 敌方（camp=2）原型，量级对齐语料怪物数值。
def slime(eid, pos, hp_rate=1.0):
    return ent(eid, 2, pos, 550, 120, 30, 0, name="史莱姆", label="slime", mass=1, hp_rate=hp_rate)


def soldier(eid, pos, hp_rate=1.0):
    return ent(eid, 2, pos, 1200, 200, 150, 0, name="士兵", label="soldier", mass=2, hp_rate=hp_rate)


def caster(eid, pos, hp_rate=1.0):
    return ent(eid, 2, pos, 800, 250, 20, 50, name="术师", label="caster", mass=1, hp_rate=hp_rate)


def boss(eid, pos, hp_rate=1.0):
    return ent(eid, 2, pos, 4500, 500, 400, 30, name="重甲领主", label="boss", mass=5, hp_rate=hp_rate)


def host(job, sub_job=0, cost=12, base_attack_time=1.2, damage_type=0, block=1,
         target_priority=0, vision_radius=2.5, vision_range=None,
         animations=None, bullets=None, can_spawn=None, skills=None):
    """契约 v2 hostAssets。animations/bullets/canSpawnEntities 不传引用（投影三要素）。"""
    if vision_range is None:
        vision_range = [[0, 0], [1, 0], [0, 1], [-1, 0], [0, -1]]
    if animations is None:
        animations = {"named": ["Attack", "Skill2"], "groups": ["AttackRemote", "AttackClose"]}
    out = {
        "job": job, "subJob": sub_job, "cost": cost,
        "baseAttackTime": base_attack_time, "damageType": damage_type,
        "blockOccupation": block, "targetPriority": target_priority,
        "visionRadius": vision_radius, "visionRange": vision_range,
        "animations": animations,
    }
    if bullets is not None:
        out["bullets"] = bullets
    if can_spawn is not None:
        out["canSpawnEntities"] = can_spawn
    if skills is not None:
        out["skills"] = skills
    return out


def spawnable(eid, name, hp, attack, defence=80, magic_res=0, is_static=True, count=1):
    return {"id": eid, "name": name, "isStatic": is_static, "hp": hp, "maxHp": hp,
            "hpRate": 1.0, "attack": attack, "defence": defence, "magicRes": magic_res,
            "count": count}


def snapshot(self_rec, enemies, map_i=8, map_j=9, higher=None, lower=None):
    """契约 v2 battleSnapshot：self 在 entities 里，selfId 作指针。"""
    if higher is None:
        higher = ["4,4", "4,5", "5,4"]
    if lower is None:
        lower = ["3,2", "3,3", "4,2", "5,2", "5,3", "5,5"]
    return {
        "selfId": self_rec["id"],
        "mapI": map_i, "mapJ": map_j,
        "canSetHigher": higher, "canSetLower": lower,
        "entities": [self_rec] + enemies,
    }


def task(task_id, title, notes, snap, host_assets, request_text, expected):
    return {
        "taskId": task_id, "title": title, "notes": notes,
        "request": {
            "battleSnapshot": snap,
            "hostAssets": host_assets,
            "constraints": {"request": request_text},
        },
        "expected": expected,
    }


# ===== 任务集 =====


def build_tasks():
    tasks = []

    # t01 先锋回费：job 驱动技能原型的代表。
    s = self_ent("fang", (3, 3), 1300, 280, job=0)
    tasks.append(task(
        "t01_vanguard_cost_recovery", "先锋回费",
        "覆盖：modify_cost / OnAbilityBegin / job 决定技能原型",
        snapshot(s, [slime("m1", (6, 4)), slime("m2", (7, 6))]),
        host(job=0, sub_job=2, cost=9, damage_type=0, block=2),
        "我的部署费用经常跟不上，开技能时帮我回一笔费用，量级对齐先锋标准。",
        {
            "opsMustInclude": ["modify_cost"],
            "opsMustExclude": ["apply_damage", "apply_buff"],
            "triggerEventsMustInclude": ["OnAbilityBegin"],
            "magnitude": [{"path": "modify_cost.amount", "min": 4, "max": 8}],
            "effectSummary": "开启技能时立即获得一笔部署费用（4~8 点）",
        }))

    # t02 医疗攻击强化：buff 开关配对（语料 hibisc_s1 模式）。
    s = self_ent("hibisc", (4, 4), 1000, 350, job=5)
    tasks.append(task(
        "t02_medic_attack_buff", "医疗攻击强化",
        "覆盖：apply_buff+destroy_buff 配对 / OnAbilityBegin+OnAbilityEnd",
        snapshot(s, [soldier("m1", (5, 6)), caster("m2", (7, 3))]),
        host(job=5, cost=14, damage_type=1, block=1),
        "给我一个开技能时强化自己攻击（治疗量随之提升）、关技能时恢复原样的技能。",
        {
            "opsMustInclude": ["apply_buff", "destroy_buff"],
            "opsMustExclude": ["apply_damage", "modify_cost"],
            "triggerEventsMustInclude": ["OnAbilityBegin"],
            "magnitude": [{"path": "apply_buff.magnitudes", "min": 0.2, "max": 0.8}],
            "effectSummary": "技能期间自身攻击力提升 20%~80%，技能结束恢复",
        }))

    # t03 重装双抗：attributes 多值 CSV。
    s = self_ent("beagle", (3, 4), 2200, 180, defence=400, job=2)
    tasks.append(task(
        "t03_defender_defense_buff", "重装双抗提升",
        "覆盖：apply_buff attributes 多值 / 重装原型",
        snapshot(s, [soldier("m1", (4, 5)), soldier("m2", (5, 4)), caster("m3", (6, 6))]),
        host(job=2, cost=18, damage_type=0, block=3, base_attack_time=1.5),
        "前线压力很大，开技能时让我的防御和法抗都明显提升，关技能后恢复。",
        {
            "opsMustInclude": ["apply_buff"],
            "opsMustExclude": ["apply_damage", "modify_cost"],
            "triggerEventsMustInclude": ["OnAbilityBegin"],
            "magnitude": [{"path": "apply_buff.magnitudes", "min": 0.3, "max": 1.0}],
            "effectSummary": "技能期间自身防御与法抗提升 30%~100%",
        }))

    # t04 近卫单体爆发：select_targets + apply_damage 或直伤路径。
    s = self_ent("guard", (4, 3), 1600, 620, job=1)
    tasks.append(task(
        "t04_guard_single_target_burst", "近卫单体爆发",
        "覆盖：apply_damage 高倍率 / select_targets 可选",
        snapshot(s, [soldier("m1", (5, 3)), slime("m2", (6, 5)), boss("m3", (7, 7))]),
        host(job=1, cost=20, damage_type=0, block=2),
        "给一个主动技：开技能时对眼前最近的敌人打出一发高倍率物理伤害。",
        {
            "opsMustInclude": ["apply_damage"],
            "opsMustExclude": ["modify_cost", "spawn_entity"],
            "triggerEventsMustInclude": ["OnAbilityBegin"],
            "magnitude": [{"path": "apply_damage.multiplier", "min": 1.5, "max": 3.5}],
            "effectSummary": "开启技能对最近敌人造成约 1.5~3.5 倍攻击力的物理伤害",
        }))

    # t05 术师溅射扩展：SplashRadius buff（catap_s1 模式）。
    s = self_ent("catap", (5, 2), 900, 480, job=4)
    tasks.append(task(
        "t05_caster_splash_radius", "溅射范围扩大",
        "覆盖：apply_buff SplashRadius / 术师原型",
        snapshot(s, [slime("m1", (6, 3)), slime("m2", (6, 4)), soldier("m3", (7, 5))]),
        host(job=4, cost=16, damage_type=1, block=1),
        "让我的攻击溅射范围扩大一倍，方便清群。",
        {
            "opsMustInclude": ["apply_buff"],
            "opsMustExclude": ["apply_damage", "modify_cost"],
            "triggerEventsMustInclude": ["OnAbilityBegin"],
            "magnitude": [{"path": "apply_buff.magnitudes", "min": 0.5, "max": 1.5}],
            "effectSummary": "技能期间攻击溅射半径提升 50%~150%",
        }))

    # t06 荆棘反伤：事件上下文链（write_blackboard source=event damage）。
    s = self_ent("thorn", (4, 4), 2600, 220, defence=350, job=2)
    tasks.append(task(
        "t06_thorns_reflect", "荆棘反伤",
        "覆盖：write_blackboard source=event / OnAfterTakeDamage 事件上下文",
        snapshot(s, [soldier("m1", (4, 5)), caster("m2", (6, 6))]),
        host(job=2, cost=16, damage_type=0, block=3),
        "被打的时候把受到的伤害的一部分反弹给攻击者。",
        {
            "opsMustInclude": ["write_blackboard", "apply_damage"],
            "opsMustExclude": ["modify_cost", "spawn_entity"],
            "triggerEventsMustInclude": ["OnAfterTakeDamage"],
            "effectSummary": "受到伤害时按所受伤害的一部分反弹给攻击来源",
        }))

    # t07 狙击多目标：attack_target_count_modifier。
    s = self_ent("sniper", (6, 2), 850, 520, job=3)
    tasks.append(task(
        "t07_sniper_extra_target", "狙击额外目标",
        "覆盖：attack_target_count_modifier / 狙击原型",
        snapshot(s, [slime("m1", (2, 3)), slime("m2", (3, 5)), soldier("m3", (4, 6))]),
        host(job=3, cost=15, damage_type=0, block=1, base_attack_time=2.4),
        "技能开启期间我的攻击可以同时多打一个目标。",
        {
            "opsMustInclude": ["attack_target_count_modifier"],
            "triggerEventsMustInclude": ["OnAbilityBegin"],
            "effectSummary": "技能期间每次攻击可命中额外一个目标",
        }))

    # t08 召唤阻挡物：spawn_entity + 注册表边界。
    s = self_ent("summoner", (4, 4), 1100, 300, job=6)
    tasks.append(task(
        "t08_summon_blocker", "召唤阻挡物",
        "覆盖：spawn_entity / canSpawnEntities 注册表 / watch_summon_death 可选",
        snapshot(s, [slime("m1", (6, 4)), soldier("m2", (7, 5))]),
        host(job=6, cost=15, damage_type=0, block=1, can_spawn=[
            spawnable("summon_blocker", "碎石偶", 900, 60, count=2)]),
        "在我身边召唤一个能挡路的召唤物（用我注册的召唤物），召唤物阵亡时我要能感知到。",
        {
            "opsMustInclude": ["spawn_entity"],
            "opsMustExclude": ["fire_bullets", "apply_damage"],
            "triggerEventsMustInclude": ["OnAbilityBegin"],
            "magnitude": [{"path": "spawn_entity.spawnIndex", "min": 0, "max": 0}],
            "effectSummary": "召唤注册表中的碎石偶挡路，并桥接其阵亡事件",
        }))

    # t09 弹幕齐射：select_landing_points + fire_bullets + OnBulletLanded 可选。
    s = self_ent("mortar", (6, 3), 950, 460, job=4)
    tasks.append(task(
        "t09_mortar_barrage", "弹幕齐射",
        "覆盖：select_landing_points+fire_bullets / bullets 注册表 / OnBulletLanded 可选",
        snapshot(s, [slime("m1", (3, 4)), slime("m2", (3, 5)), soldier("m3", (4, 5)),
                     caster("m4", (5, 6))]),
        host(job=4, cost=18, damage_type=1, block=1, bullets=[
            {"index": 0, "bulletType": 1, "allowNoTarget": False}]),
        "朝敌人最密集的地方发射一轮弹幕，每发弹着点附近炸开一次范围伤害。",
        {
            "opsMustInclude": ["fire_bullets", "select_landing_points"],
            "opsMustExclude": ["modify_cost", "spawn_entity"],
            "triggerEventsAny": ["OnAbilityBegin", "OnAbilityEnd"],
            "magnitude": [{"path": "fire_bullets.bulletDataIndex", "min": 0, "max": 0}],
            "effectSummary": "选取密集落点发射弹幕，弹着造成范围伤害",
        }))

    # t10 换装演出：apply/remove_animation_override，resources 必须落在清单内。
    s = self_ent("duelist", (4, 4), 1500, 560, job=1)
    tasks.append(task(
        "t10_animation_costume_swap", "换装演出",
        "覆盖：apply_animation_override+remove_animation_override / animations 清单",
        snapshot(s, [soldier("m1", (5, 4)), soldier("m2", (6, 6))]),
        host(job=1, cost=17, damage_type=0, block=2,
             animations={"named": ["Attack", "Skill2_Fire"],
                         "groups": ["AttackRemote", "AttackClose", "Charge"]}),
        "开技能时把我的攻击动画换成 Skill2_Fire 的演出，关技能后恢复原样。",
        {
            "opsMustInclude": ["apply_animation_override"],
            "opsMustExclude": ["apply_damage", "modify_cost"],
            "triggerEventsMustInclude": ["OnAbilityBegin"],
            "effectSummary": "技能期间攻击动画换为 Skill2_Fire，结束还原",
        }))

    # t11 失衡击退：massLevel 对比（轻推得动、重推不动）。
    s = self_ent("pusher", (4, 4), 1700, 540, job=1)
    tasks.append(task(
        "t11_mass_knockback", "失衡击退",
        "覆盖：apply_impulse / massLevel 对比",
        snapshot(s, [slime("m1", (5, 4)), soldier("m2", (5, 5)), boss("m3", (6, 6))]),
        host(job=1, cost=19, damage_type=0, block=2),
        "开技能时把身边的轻装敌人往外推开，重甲敌人推不动。",
        {
            "opsMustInclude": ["apply_impulse"],
            "opsMustExclude": ["modify_cost", "fire_bullets"],
            "triggerEventsMustInclude": ["OnAbilityBegin"],
            "effectSummary": "对身边轻质量敌人施加击退脉冲，重质量敌人不受影响",
        }))

    # t12 概率增益：random_roll + branch 原语。
    s = self_ent("gambler", (4, 4), 1200, 400, job=1)
    tasks.append(task(
        "t12_gamble_buff", "概率增益",
        "覆盖：random_roll+branch / 原语分支",
        snapshot(s, [soldier("m1", (5, 5)), boss("m2", (7, 7))]),
        host(job=1, cost=13, damage_type=0, block=2),
        "开技能时掷一次骰子，一半概率让我的攻击力大幅提升一段时间。",
        {
            "opsMustInclude": ["random_roll", "branch", "apply_buff"],
            "triggerEventsMustInclude": ["OnAbilityBegin"],
            "magnitude": [{"path": "random_roll.input", "min": 0.4, "max": 0.6},
                          {"path": "apply_buff.magnitudes", "min": 0.4, "max": 1.5}],
            "effectSummary": "开启技能 50% 概率获得一次大幅攻击提升",
        }))

    # t13 持续伤害光环：OnTick + 半径选目标，小倍率高频伤害。
    s = self_ent("aura", (4, 4), 1300, 320, job=4)
    tasks.append(task(
        "t13_tick_damage_aura", "持续伤害光环",
        "覆盖：OnTick / select_targets 半径 / apply_damage 小倍率",
        snapshot(s, [slime("m1", (5, 4)), slime("m2", (5, 5)), caster("m3", (7, 7))]),
        host(job=4, cost=17, damage_type=1, block=1),
        "以我为中心持续灼烧周围一圈敌人，伤害低但稳定。",
        {
            "opsMustInclude": ["apply_damage"],
            "opsMustExclude": ["modify_cost", "spawn_entity"],
            "triggerEventsMustInclude": ["OnTick"],
            "magnitude": [{"path": "apply_damage.multiplier", "min": 0.05, "max": 0.5}],
            "effectSummary": "每跳对周围敌人造成约 5%~50% 攻击力的持续伤害",
        }))

    # t14 沉默施法单位：apply_abnormal_state。
    s = self_ent("witch_hunter", (5, 3), 1100, 380, job=7)
    tasks.append(task(
        "t14_silence_casters", "沉默施法单位",
        "覆盖：apply_abnormal_state / 特种原型",
        snapshot(s, [caster("m1", (6, 4)), caster("m2", (7, 5)), soldier("m3", (4, 6))]),
        host(job=7, cost=12, damage_type=0, block=1),
        "让附近正在施法的敌人沉默几秒，打断他们的节奏。",
        {
            "opsMustInclude": ["apply_abnormal_state"],
            "opsMustExclude": ["modify_cost", "spawn_entity"],
            "triggerEventsMustInclude": ["OnAbilityBegin"],
            "magnitude": [{"path": "apply_abnormal_state.duration", "min": 2, "max": 8}],
            "effectSummary": "周围敌人被沉默数秒无法施法",
        }))

    # t15 阻挡优先集火：attack_candidate_override 或 select_targets 链。
    s = self_ent("tactician", (4, 4), 1200, 420, job=4)
    tasks.append(task(
        "t15_blocked_priority", "阻挡集火",
        "覆盖：attack_candidate_override 或 select_targets 链 / 协同 targetPriority",
        snapshot(s, [soldier("m1", (4, 5)), slime("m2", (6, 6)), caster("m3", (7, 7))]),
        host(job=4, cost=16, damage_type=1, block=1, target_priority=0),
        "技能开启期间，让我只攻击被友军阻挡住的敌人，没有被阻挡的就照常攻击。",
        {
            "opsMustInclude": ["select_targets"],
            "opsAnyOf": [["attack_candidate_override", "attack_behavior_override",
                          "write_blackboard"]],
            "triggerEventsMustInclude": ["OnAbilityBegin"],
            "effectSummary": "技能期间优先攻击被阻挡的敌人",
        }))

    # t16 击杀回费：isdeadly 事件上下文（AfterAttack/AfterTakeDamage）。
    s = self_ent("reaper", (4, 4), 1500, 500, job=1)
    tasks.append(task(
        "t16_kill_reward_cost", "击杀回费",
        "覆盖：modify_cost / OnAfterAttack+isdeadly 事件上下文 / branch",
        snapshot(s, [slime("m1", (5, 4)), slime("m2", (6, 5)), soldier("m3", (7, 6))]),
        host(job=1, cost=11, damage_type=0, block=2),
        "我每次亲手击杀敌人时，给我回一点部署费用。",
        {
            "opsMustInclude": ["modify_cost"],
            "opsMustExclude": ["spawn_entity", "apply_animation_override"],
            "triggerEventsAny": ["OnAfterAttack", "OnAfterTakeDamage", "OnAttackSuccessfully"],
            "magnitude": [{"path": "modify_cost.amount", "min": 1, "max": 3}],
            "effectSummary": "每次击杀敌人回复少量部署费用（1~3 点）",
        }))

    return tasks


# ===== 落盘与自检 =====


def check(tasks: list[dict], schema: dict) -> None:
    registered = set(schema["primitives"]) | set(schema["componentOps"])
    events = set(schema["triggerEvents"])
    for t in tasks:
        tid = t["taskId"]
        exp = t["expected"]
        snap = t["request"]["battleSnapshot"]
        assert any(e["id"] == snap["selfId"] for e in snap["entities"]), f"{tid}: self 不在 entities"
        for op in exp.get("opsMustInclude", []) + exp.get("opsMustExclude", []):
            assert op in registered, f"{tid}: 期望 op 未注册 {op}"
        for group in exp.get("opsAnyOf", []):
            assert any(op in registered for op in group), f"{tid}: opsAnyOf 全组未注册 {group}"
        for ev in exp.get("triggerEventsMustInclude", []) + exp.get("triggerEventsAny", []):
            assert ev in events, f"{tid}: 未知触发事件 {ev}"
        for m in exp.get("magnitude", []):
            op = m["path"].split(".")[0]
            assert op in registered, f"{tid}: 量级路径 op 未注册 {m['path']}"
        for e in snap["entities"]:
            assert {"id", "name", "camp", "isStatic", "hp", "maxHp", "hpRate",
                    "pos", "attack", "defence", "magicRes", "job", "label",
                    "massLevel"} <= set(e), f"{tid}: 实体记录缺 v2 字段 {e['id']}"


def main() -> None:
    schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))
    tasks = build_tasks()
    check(tasks, schema)
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for t in tasks:
        path = OUT_DIR / f"{t['taskId']}.json"
        path.write_text(json.dumps(t, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
        print(f"wrote {path.relative_to(HERE)} ({t['title']})")
    print(f"total {len(tasks)} tasks, self-check passed")


if __name__ == "__main__":
    sys.exit(main())
