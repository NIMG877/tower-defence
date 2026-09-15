"""服务门面：缓存 + 案例落盘 包住 Agent 编排。"""

from __future__ import annotations

import hashlib
import json
import time
from collections import OrderedDict
from pathlib import Path

from . import agent, schema as schema_mod


class _Cache:
    def __init__(self, size: int):
        self.size = size
        self._data: OrderedDict[str, dict] = OrderedDict()

    def get(self, key: str) -> dict | None:
        if key not in self._data:
            return None
        self._data.move_to_end(key)
        return self._data[key]

    def put(self, key: str, value: dict) -> None:
        self._data[key] = value
        self._data.move_to_end(key)
        while len(self._data) > self.size:
            self._data.popitem(last=False)


class GenerateService:
    def __init__(self, cfg):
        self.cfg = cfg
        self._cache = _Cache(cfg.cache_size)

    @property
    def schema(self) -> dict:
        """每次访问经 mtime 缓存取最新——改库后无需重启服务。"""
        return schema_mod.load_schema()

    def generate(self, request: dict, on_phase=None, on_event=None) -> dict:
        cache_key = self._cache_key(request)
        cached = self._cache.get(cache_key)
        if cached is not None:
            return {**cached, "report": {**cached.get("report", {}), "cached": True}}

        response = agent.run(request, self.cfg, self.schema, on_phase, on_event)
        # 黑盒回放只落盘不回客户端：客户端 DTO 不变、响应体积不膨胀，回放根因看案例文件。
        extras = {k: response.pop(k) for k in agent.BLACKBOX_KEYS if k in response}
        # 非 ok / 降级产物不入缓存：同 payload 重触发可重跑，坏结果不被永久命中。
        if response.get("status") == "ok" and not response.get("degraded"):
            self._cache.put(cache_key, response)
        self._log(request, response, extras)
        return response

    @staticmethod
    def _cache_key(request: dict) -> str:
        """缓存键覆盖所有影响生成结果的请求输入（opList/快照/hostAssets/constraints/
        description）——只含快照会让同战局换宿主或换诉求命中旧技能。"""
        def norm(value) -> str:
            if isinstance(value, str):
                return value
            return json.dumps(value, sort_keys=True, ensure_ascii=False, default=str)

        parts = "|".join((
            str(request.get("opList", "")),
            norm(request.get("battleSnapshot")),
            norm(request.get("hostAssets")),
            norm(request.get("constraints")),
            norm(request.get("description")),
        ))
        return hashlib.sha256(parts.encode()).hexdigest()

    def _log(self, request: dict, response: dict,
             extras: dict | None = None) -> None:
        if not self.cfg.log_dir:
            return
        log_dir = Path(self.cfg.log_dir)
        try:
            log_dir.mkdir(parents=True, exist_ok=True)
            stamp = time.strftime("%Y%m%d-%H%M%S")
            path = log_dir / f"gen-{stamp}-{int(time.time() * 1000) % 10000}.json"
            path.write_text(json.dumps({"request": request, "response": response,
                                        **(extras or {})},
                                       ensure_ascii=False, indent=1), encoding="utf-8")
        except OSError:
            pass  # 日志落盘失败不影响生成结果
