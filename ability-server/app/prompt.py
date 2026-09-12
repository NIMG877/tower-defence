"""三阶段 Agent 的提示词构建：分析战局 → 给出设计 → 生成格式化输出。

渐进式披露：analyze 阶段的 system 只带角色与工具用法说明，组件细节靠 LLM
按需调 read_component_doc；generate 阶段才注入 design.plannedOps 涉及的
参数 schema——上下文里永远只有正在用的东西。
"""

from __future__ import annotations

import json

from . import schema as schema_mod

ANALYZE_SYSTEM = (
    "你是塔防游戏的技能生成 Agent，负责第一步：分析战局。\n"
    "客户端快照只含基本信息（自身/实体/地图可部署格）；派生指标（最近敌距、半径内敌人、"
    "单体距离/血量等）用 compute_cross_items 工具按需计算，不要心算几何。\n"
    "组件信息采用渐进式披露：先 list_components 看紧凑索引，对打算使用的组件调 "
    "read_component_doc 读完整参数表与触发限制；设计多步骤规则前先 read_contract_doc。\n"
    "分析目标：敌我态势（数量/距离/血量）、可利用的机制机会、本次生成技能的约束\n"
    "（宿主可召唤物/子弹/动画来自 hostAssets，不可凭空引用）。\n"
    "工具调用完成后，直接输出分析结论文本（中文，简洁分点；不要输出 JSON）。\n"
)

DESCRIBE_INSTRUCTION = (
    "基于上面的分析，设计一个适配当前战局的技能。需要确认组件参数能力时可先用 "
    "read_component_doc / read_contract_doc 复读文档，然后必须直接输出一个 JSON 对象"
    "（不要围栏、不要解释、不要再调用工具），形状：\n"
    "{\n"
    '  "abilityName": "中文技能名",\n'
    '  "description": "玩家可读的效果描述（必须与后续配置严格一致）",\n'
    '  "designNotes": "机制要点：触发事件/步骤序列/目标选择/数值依据（内部用）",\n'
    '  "plannedOps": ["该技能会用到的组件 op 名（canonical snake_case）"]\n'
    "}\n"
    "plannedOps 只列你读过文档、确认参数都填得出来的 op；宁简勿猜。\n"
)

GENERATE_SYSTEM_HEADER = (
    "你是塔防游戏的技能配置生成器，负责最后一步：把既定设计落成 AbilityConfig JSON。\n"
    "设计（abilityName/description/designNotes/plannedOps）已经定稿，不要更改设计本身，"
    "只负责把机制翻译成 rules 数据。\n"
    "存储形态：rules[] 每条 = triggers[](事件+条件组) + reentry + steps[]；步骤 op 是注册表"
    "字符串，参数是 (key,type,value,fromBlackboard) 四元组、value 一律字符串；"
    "entry.type 必须与该参数 schema 声明的 type 一致（数组参数用 StringArray/FloatArray，"
    "多值按逗号 CSV 书写），不一致会被服务端改写并记 warning。\n"
    "硬性边界：spawn_entity.spawnIndex 只能取 hostAssets.canSpawnEntityIds 下标；"
    "fire_bullets.bulletDataIndex 只能取 0..hostAssets.bulletCount-1；动画/图标只能用"
    "hostAssets 已列出的；表意不确定的字段宁缺勿猜（缺省由运行时兜底）。\n"
    "输出要求：只输出一个 JSON 对象（无围栏无解释），形状 = {abilityId, abilityName, "
    "description, iconKey, sp, rules}，字段契约见下方 schema。\n"
    "参考技能（referenceSkills）是已验证可运行的现有配置：可借鉴 op 组合方式、参数"
    "结构与数值量级；但设计已定稿，不得照抄其 abilityId/名称/描述，不得偏离当前设计。\n"
)


def analyze_messages(request: dict) -> list[dict]:
    user = {
        "battleSnapshot": request.get("battleSnapshot"),
        "hostAssets": request.get("hostAssets"),
        "constraints": request.get("constraints") or {},
    }
    return [
        {"role": "system", "content": ANALYZE_SYSTEM},
        {"role": "user", "content": json.dumps(user, ensure_ascii=False, indent=1)},
    ]


def describe_user() -> dict:
    return {"role": "user", "content": DESCRIBE_INSTRUCTION}


def generate_system(schema: dict, planned_ops: list[str], style: dict | None = None) -> str:
    """输出契约 + 全部原语 + 仅 plannedOps 的组件 schema（渐进披露的收口）；
    style 为全语料蒸馏的风格统计（无语料时省略该节）。"""
    component_ops = {
        op: schema["componentOps"][op]
        for op in planned_ops
        if op in schema.get("componentOps", {})
    }
    bundle = {
        "ruleShape": schema.get("ruleShape"),
        "triggerEvents": schema.get("triggerEvents"),
        "conditionOps": schema.get("conditionOps"),
        "ruleReentry": schema.get("ruleReentry"),
        "paramValueTypeEncoding": schema.get("paramValueTypeEncoding"),
        "knownBlackboardKeys": schema.get("knownBlackboardKeys"),
        "clamps": schema.get("clamps"),
        "primitives": schema.get("primitives"),
        "componentOps": component_ops,
    }
    text = (GENERATE_SYSTEM_HEADER
            + "\n== 本次生成使用的 op schema ==\n"
            + json.dumps(bundle, ensure_ascii=False, indent=1))
    if style:
        text += ("\n== 现有技能风格统计（全部已验证资产的蒸馏，数值量级参考） ==\n"
                 + json.dumps(style, ensure_ascii=False))
    return text


def generate_user(request: dict, analysis: str, design: dict,
                  reference_skills: list[dict] | None = None) -> str:
    skills = [{k: v for k, v in skill.items() if k != "_score"}
              for skill in reference_skills or []]
    return json.dumps({
        "analysis": analysis,
        "design": design,
        "battleSnapshot": request.get("battleSnapshot"),
        "hostAssets": request.get("hostAssets"),
        "constraints": request.get("constraints") or {},
        "referenceSkills": skills,
    }, ensure_ascii=False, indent=1)


def retry_feedback(issues: list[dict]) -> str:
    lines = "\n".join(f"- [{i['severity']}] {i['path']}: {i['message']}" for i in issues)
    return ("上一轮 JSON 未通过校验，问题如下。保持设计不变，修正后重新输出完整 JSON"
            "（只输出 JSON）：\n" + lines)
