"""LLM 客户端（OpenAI 兼容 /chat/completions）。

httpx 延迟导入：纯逻辑测试（validator/agent 循环脚本化）不需要安装它。
"""

from __future__ import annotations


class LlmError(Exception):
    pass


# 进程级 LLM 用量累计（评测的成本口径；agent 每次生成取快照差值进 report.tokens）。
_usage_totals = {"calls": 0, "prompt_tokens": 0, "completion_tokens": 0}


def usage_snapshot() -> dict:
    return dict(_usage_totals)


def usage_delta(snapshot: dict) -> dict:
    return {k: _usage_totals[k] - snapshot.get(k, 0) for k in _usage_totals}


def _accumulate(data: dict) -> None:
    usage = data.get("usage") or {}
    _usage_totals["calls"] += 1
    _usage_totals["prompt_tokens"] += int(usage.get("prompt_tokens") or 0)
    _usage_totals["completion_tokens"] += int(usage.get("completion_tokens") or 0)


def _complete(cfg, messages: list[dict], tools: list[dict] | None,
              model: str | None, timeout: float | None) -> dict:
    """单次 /chat/completions，返回首个 choice 的 message（纯文本与 function
    calling 共用一条请求路径）。timeout 缺省用 cfg.llm_timeout_seconds；
    agent 预算闸传入 min(llm_timeout, 剩余墙钟-5s)，防挂起中的调用穿破墙钟死线
    （§4.4）。"""
    model = model or cfg.llm_model
    if not cfg.llm_api_key or not model:
        raise LlmError("config.llm_api_key / config.llm_model 未配置")

    import httpx  # 延迟导入：纯逻辑测试不需要装它

    payload = {"model": model, "messages": messages,
               "temperature": cfg.llm_temperature}
    if tools is not None:
        payload["tools"] = tools
    try:
        resp = httpx.post(
            cfg.llm_base_url.rstrip("/") + "/chat/completions",
            headers={"Authorization": f"Bearer {cfg.llm_api_key}"},
            json=payload,
            timeout=timeout or cfg.llm_timeout_seconds,
        )
        resp.raise_for_status()
        data = resp.json()
        _accumulate(data)
        return data["choices"][0]["message"]
    except Exception as exc:  # noqa: BLE001 —— 网络/协议错误统一转 LlmError
        raise LlmError(f"LLM request failed: {exc}") from exc


def chat(messages: list[dict], cfg, model: str | None = None,
         timeout: float | None = None) -> str:
    """纯文本补全（agent 的 weak 档工具结果摘要用）。"""
    return _complete(cfg, messages, None, model, timeout)["content"]


def chat_tools(messages: list[dict], tools: list[dict], cfg,
               model: str | None = None, timeout: float | None = None) -> dict:
    """function calling 单轮。返回完整 assistant message（含 content 与/或
    tool_calls），由 Agent 决定执行工具还是收尾。"""
    return _complete(cfg, messages, tools, model, timeout)
