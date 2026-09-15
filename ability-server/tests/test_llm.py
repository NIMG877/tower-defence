"""llm 客户端测试：思考强度传参与用量账本（httpx 打桩，不触网）。"""

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
