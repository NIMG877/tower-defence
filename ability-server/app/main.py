"""FastAPI 入口。

同步：POST /generate-ability（阻塞至 Agent 跑完）
异步：POST /generate-ability/async → {jobId}，GET /jobs/{jobId} 轮询阶段状态
      （phases 实时追加，词表 plan/act/review/submit/done/degraded/handshake，客户端只打印不解析）

启动：cd ability-server && uvicorn app.main:app --host 127.0.0.1 --port 8765
（无 key 冒烟：把 app/config.py 的 llm_mock 改为 True）
"""

from __future__ import annotations

import time

from fastapi import Body, FastAPI, Header, HTTPException

from . import jobs
from .config import Config, RateLimiter
from .service import GenerateService

app = FastAPI(title="TD ability generation server", version="0.2")
_cfg = Config()
_service = GenerateService(_cfg)
_limiter = RateLimiter(_cfg.rate_limit, _cfg.rate_window_seconds)
_jobs = jobs.JobStore()


def _check_access(x_auth_token: str | None, payload: dict | None = None) -> None:
    if _cfg.server_token and x_auth_token != _cfg.server_token:
        raise HTTPException(status_code=401, detail="invalid token")
    if not _limiter.allow(x_auth_token or "anonymous", time.time()):
        raise HTTPException(status_code=429, detail="rate limit exceeded")
    if payload is not None and "battleSnapshot" not in payload:
        raise HTTPException(status_code=400, detail="missing field: battleSnapshot")


@app.get("/health")
def health() -> dict:
    return {
        "status": "ok",
        "mock": _cfg.llm_mock,
        "model": "mock" if _cfg.llm_mock else (_cfg.llm_model or "(unset)"),
        "schemaProtocolVersion": _service.schema.get("protocolVersion"),
        "canonicalOpCount": len(_service.schema["primitives"]) + len(_service.schema["componentOps"]),
    }


@app.post("/generate-ability")
def generate_ability(
    payload: dict = Body(...),
    x_auth_token: str | None = Header(default=None, alias="X-Auth-Token"),
) -> dict:
    _check_access(x_auth_token, payload)
    return _service.generate(payload)


@app.post("/generate-ability/async")
def generate_ability_async(
    payload: dict = Body(...),
    x_auth_token: str | None = Header(default=None, alias="X-Auth-Token"),
) -> dict:
    _check_access(x_auth_token, payload)

    def work(record: dict) -> dict:
        return _service.generate(
            payload,
            on_phase=lambda phase, detail: record["phases"].append(
                {"phase": phase, "detail": detail}))

    return {"jobId": _jobs.submit(work)}


@app.get("/jobs/{job_id}")
def job_status(job_id: str) -> dict:
    record = _jobs.get(job_id)
    if record is None:
        raise HTTPException(status_code=404, detail="unknown job")
    return {"done": record["done"], "phases": record["phases"],
            "error": record["error"], "response": record["response"]}
