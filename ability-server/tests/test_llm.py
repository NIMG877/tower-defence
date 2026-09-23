"""llm 客户端测试：思考强度/保留式思考传参、错误分类、用量账本（httpx 打桩，不触网）。"""

import pytest

from app import llm
from app.config import Config


def cfg(**overrides) -> Config:
    base = dict(llm_api_key="k", llm_model="m", llm_temperature=0.4,
                llm_timeout_seconds=10.0, llm_base_url="http://x")
    base.update(overrides)
    return Config(**base)


def _fake_post_factory(captured: dict):
    class _Resp:
        def raise_for_status(self):
            pass

        def json(self):
            return {"choices": [{"message": {"content": "ok"}}],
                    "usage": {"prompt_tokens": 10, "completion_tokens": 5,
                              "prompt_tokens_details": {"cached_tokens": 4},
                              "completion_tokens_details": {"reasoning_tokens": 3}}}

    def fake_post(url, json=None, headers=None, timeout=None):
        captured["payload"] = json
        return _Resp()

    return fake_post


def test_effort_in_payload(monkeypatch):
    """effort 传入时进请求体 reasoning_effort；None 不传（走 API 默认）。"""
    captured: dict = {}
    monkeypatch.setattr("httpx.post", _fake_post_factory(captured))

    llm.chat_tools([{"role": "user", "content": "x"}], [], cfg(), effort="low")
    assert captured["payload"]["reasoning_effort"] == "low"

    llm.chat_tools([{"role": "user", "content": "x"}], [], cfg())
    assert "reasoning_effort" not in captured["payload"]


def test_usage_ledger_includes_reasoning_and_cache(monkeypatch):
    """账本分项累计：reasoning_tokens（思考）与 cached_tokens（隐式缓存命中）。"""
    captured: dict = {}
    monkeypatch.setattr("httpx.post", _fake_post_factory(captured))
    snap = llm.usage_snapshot()
    llm.chat_tools([{"role": "user", "content": "x"}], [], cfg())
    delta = llm.usage_delta(snap)
    assert delta["calls"] == 1
    assert delta["prompt_tokens"] == 10 and delta["completion_tokens"] == 5
    assert delta["reasoning_tokens"] == 3 and delta["cached_tokens"] == 4


def test_preserve_thinking_param_in_payload(monkeypatch):
    """保留式思考开关：True 传 clear_thinking=false（保留）；False 传 true（清除）。
    thinking 组始终显式传，行为不随端点默认漂移。"""
    captured: dict = {}
    monkeypatch.setattr("httpx.post", _fake_post_factory(captured))

    llm.chat_tools([{"role": "user", "content": "x"}], [], cfg())
    assert captured["payload"]["thinking"] == {"type": "enabled", "clear_thinking": False}

    llm.chat_tools([{"role": "user", "content": "x"}], [], cfg(llm_preserve_thinking=False))
    assert captured["payload"]["thinking"] == {"type": "enabled", "clear_thinking": True}


def _status_post_factory(status: int):
    import httpx

    def fake_post(url, json=None, headers=None, timeout=None):
        return httpx.Response(status_code=status, request=httpx.Request("POST", url))

    return fake_post


@pytest.mark.parametrize("status,retryable", [
    (400, False), (401, False), (404, False), (422, False),
    (408, True), (429, True), (500, True), (503, True),
])
def test_http_status_classifies_retryable(monkeypatch, status, retryable):
    """状态码分类：408/429/5xx 瞬态可重试；其余 4xx 确定性不可重试。"""
    monkeypatch.setattr("httpx.post", _status_post_factory(status))
    with pytest.raises(llm.LlmError) as exc_info:
        llm.chat_tools([{"role": "user", "content": "x"}], [], cfg())
    assert exc_info.value.retryable is retryable
