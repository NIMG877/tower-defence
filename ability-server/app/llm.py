"""LLM 客户端（OpenAI 兼容 /chat/completions）。

httpx 延迟导入：纯逻辑测试（validator/agent 循环脚本化）不需要安装它。
"""

from __future__ import annotations


class LlmError(Exception):
    """LLM 调用失败。retryable=True：瞬态故障（网络/超时/429/5xx），退避重试有意义；
    retryable=False：确定性失败（配置缺失、4xx 拒绝——上下文超限/鉴权/模型名错），
    同一请求重发只会得到同一结果，调用方应直接进降级判定。"""

    def __init__(self, message: str, retryable: bool = True):
        super().__init__(message)
        self.retryable = retryable


# 进程级 LLM 用量累计（评测的成本口径；agent 每次生成取快照差值进 report.tokens）。
_usage_totals = {"calls": 0, "prompt_tokens": 0, "completion_tokens": 0,
                 "reasoning_tokens": 0, "cached_tokens": 0}


def usage_snapshot() -> dict:
    return dict(_usage_totals)


def usage_delta(snapshot: dict) -> dict:
    return {k: _usage_totals[k] - snapshot.get(k, 0) for k in _usage_totals}


def _accumulate(data: dict) -> None:
    usage = data.get("usage") or {}
    comp_details = usage.get("completion_tokens_details") or {}
    prompt_details = usage.get("prompt_tokens_details") or {}
    _usage_totals["calls"] += 1
    _usage_totals["prompt_tokens"] += int(usage.get("prompt_tokens") or 0)
    _usage_totals["completion_tokens"] += int(usage.get("completion_tokens") or 0)
    _usage_totals["reasoning_tokens"] += int(comp_details.get("reasoning_tokens") or 0)
    _usage_totals["cached_tokens"] += int(prompt_details.get("cached_tokens") or 0)


def chat_tools(messages: list[dict], tools: list[dict], cfg,
               model: str | None = None, timeout: float | None = None,
               effort: str | None = None) -> dict:
    """function calling 单次 /chat/completions，返回首个 choice 的完整 assistant
    message（含 content 与/或 tool_calls），由 Agent 决定执行工具还是收尾。
    timeout 缺省用 cfg.llm_timeout_seconds；agent 预算闸传入 min(llm_timeout,
    剩余墙钟-5s)，防挂起中的调用穿破墙钟死线（§4.4）。effort 思考强度分档
    （GLM-5.3 系 low/high/max；None=不传走 API 默认）。"""
    model = model or cfg.llm_model
    if not cfg.llm_api_key or not model:
        raise LlmError("config.llm_api_key / config.llm_model 未配置", retryable=False)

    import httpx  # 延迟导入：纯逻辑测试不需要装它

    payload = {"model": model, "messages": messages,
               "temperature": cfg.llm_temperature, "tools": tools}
    if effort:
        payload["reasoning_effort"] = effort
    # 保留式思考（官方 thinking-mode 文档）：GLM-5.3 仅支持 type=enabled；显式传
    # clear_thinking 固定行为不随端点默认漂移（llm_preserve_thinking=True 时传
    # clear_thinking=False 保留思考），agent 侧配套决定是否回传 reasoning_content 原文。
    payload["thinking"] = {"type": "enabled",
                           "clear_thinking": not cfg.llm_preserve_thinking}
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
    except httpx.HTTPStatusError as exc:
        # 状态码分类：408/429/5xx 是瞬态（限流/过载/网关超时），退避重试有意义；
        # 其余 4xx（上下文超限/鉴权/模型名错）是确定性拒绝，重发同请求无意义。
        status = exc.response.status_code
        raise LlmError(f"LLM request failed: {exc}",
                       retryable=status in (408, 429) or status >= 500) from exc
    except Exception as exc:  # noqa: BLE001 —— 网络断连/协议错误统一转 LlmError（瞬态）
        raise LlmError(f"LLM request failed: {exc}") from exc
