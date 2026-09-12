"""LLM 客户端（OpenAI 兼容 /chat/completions）与 mock 模式。

httpx 延迟导入：纯逻辑测试（validator/prompt/pipeline-mock）不需要安装它。
"""

from __future__ import annotations

import json
import re


class LlmError(Exception):
    pass


def chat(messages: list[dict], cfg) -> str:
    """OpenAI 兼容补全。返回首个 choice 的文本内容。"""
    if not cfg.llm_api_key or not cfg.llm_model:
        raise LlmError("config.llm_api_key / config.llm_model 未配置"
                       "（key 从环境变量 ABILITY_LLM_API_KEY 读取；无 key 调试可设 config.llm_mock=True）")

    import httpx  # 延迟导入

    url = cfg.llm_base_url.rstrip("/") + "/chat/completions"
    payload = {
        "model": cfg.llm_model,
        "messages": messages,
        "temperature": cfg.llm_temperature,
        # OpenAI 兼容的 JSON 模式；不支持该参数的端点会报错，按 LlmError 走重试/拒绝路径。
        "response_format": {"type": "json_object"},
    }
    try:
        resp = httpx.post(
            url,
            headers={"Authorization": f"Bearer {cfg.llm_api_key}"},
            json=payload,
            timeout=cfg.llm_timeout_seconds,
        )
        resp.raise_for_status()
        data = resp.json()
        return data["choices"][0]["message"]["content"]
    except Exception as exc:  # noqa: BLE001 —— 网络/协议错误统一转 LlmError 供重试层处理
        raise LlmError(f"LLM request failed: {exc}") from exc


def chat_tools(messages: list[dict], tools: list[dict], cfg) -> dict:
    """OpenAI 兼容 function calling 单轮。返回完整的 assistant message
    （含 content 与/或 tool_calls），由 Agent 决定执行工具还是收尾。"""
    if not cfg.llm_api_key or not cfg.llm_model:
        raise LlmError("config.llm_api_key / config.llm_model 未配置")

    import httpx

    url = cfg.llm_base_url.rstrip("/") + "/chat/completions"
    payload = {
        "model": cfg.llm_model,
        "messages": messages,
        "temperature": cfg.llm_temperature,
        "tools": tools,
    }
    try:
        resp = httpx.post(
            url,
            headers={"Authorization": f"Bearer {cfg.llm_api_key}"},
            json=payload,
            timeout=cfg.llm_timeout_seconds,
        )
        resp.raise_for_status()
        data = resp.json()
        return data["choices"][0]["message"]
    except Exception as exc:  # noqa: BLE001
        raise LlmError(f"LLM request failed: {exc}") from exc


_FENCE_RE = re.compile(r"```(?:json)?\s*(.*?)```", re.DOTALL)


def extract_json(text: str) -> dict:
    """从模型输出提取 JSON 对象：优先裸 JSON，其次剥代码围栏，最后找首尾大括号。"""
    if not text:
        raise LlmError("empty completion")
    candidates = [text.strip()]
    fenced = _FENCE_RE.search(text)
    if fenced:
        candidates.insert(0, fenced.group(1).strip())
    for candidate in candidates:
        try:
            return json.loads(candidate)
        except json.JSONDecodeError:
            pass
    start, end = text.find("{"), text.rfind("}")
    if 0 <= start < end:
        try:
            return json.loads(text[start:end + 1])
        except json.JSONDecodeError:
            pass
    raise LlmError(f"completion is not valid JSON: {text[:200]!r}")


# mock 模式样例：modify_cost 探针技能（无 key 跑通全管线；效果肉眼可见）。
_MOCK_ABILITY = {
    "abilityId": "gen_mock_1",
    "abilityName": "模拟生成技能",
    "description": "mock 管线验证：部署即获得 99 点部署费用",
    "iconKey": "charger",
    "sp": {"totalSp": 0, "initialSp": 0, "chargeNum": 1, "abilityAmount": 0,
           "recoverMode": "Natural", "consumeMode": "NoConsume", "openMode": "Auto"},
    "rules": [
        {"triggers": [{"triggerEvent": "OnInitialize", "groups": []}],
         "reentry": "IgnoreWhileRunning",
         "steps": [{"op": "modify_cost",
                    "args": {"entries": [{"key": "amount", "value": "99", "type": "Int",
                                          "fromBlackboard": False}]}}]},
    ],
}
