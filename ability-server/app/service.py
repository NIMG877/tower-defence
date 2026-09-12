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
        self.schema = schema_mod.load_schema()
        self._cache = _Cache(cfg.cache_size)

    def generate(self, request: dict, on_phase=None) -> dict:
        snapshot_key = json.dumps(request.get("battleSnapshot"), sort_keys=True,
                                  ensure_ascii=False, default=str)
        cache_key = hashlib.sha256(
            (str(request.get("opList", "")) + "|" + snapshot_key).encode()).hexdigest()
        cached = self._cache.get(cache_key)
        if cached is not None:
            return {**cached, "report": {**cached.get("report", {}), "cached": True}}

        response = agent.run(request, self.cfg, self.schema, on_phase)
        self._cache.put(cache_key, response)
        self._log(request, response)
        return response

    def _log(self, request: dict, response: dict) -> None:
        if not self.cfg.log_dir:
            return
        log_dir = Path(self.cfg.log_dir)
        try:
            log_dir.mkdir(parents=True, exist_ok=True)
            stamp = time.strftime("%Y%m%d-%H%M%S")
            path = log_dir / f"gen-{stamp}-{int(time.time() * 1000) % 10000}.json"
            path.write_text(json.dumps({"request": request, "response": response},
                                       ensure_ascii=False, indent=1), encoding="utf-8")
        except OSError:
            pass  # 日志落盘失败不影响生成结果
