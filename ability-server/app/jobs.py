"""异步任务注册表：Agent 在后台线程跑，客户端轮询阶段状态。

记录结构：{id, done, phases, response, error}；phases 由 Agent 的 on_phase
回调实时追加（CPython list.append 原子性足够，单进程内使用）。
"""

from __future__ import annotations

import threading
import uuid


class JobStore:
    def __init__(self, max_jobs: int = 64):
        self.max_jobs = max_jobs
        self._jobs: dict[str, dict] = {}
        self._lock = threading.Lock()

    def submit(self, work) -> str:
        """work(record) → response dict；在守护线程执行。返回 jobId。"""
        job_id = uuid.uuid4().hex[:12]
        record = {"id": job_id, "done": False, "phases": [], "response": None, "error": None}
        with self._lock:
            # 上限裁剪：丢最旧的已完成任务，防止长跑泄漏内存。
            done_ids = [j for j, r in self._jobs.items() if r["done"]]
            while len(self._jobs) >= self.max_jobs and done_ids:
                self._jobs.pop(done_ids.pop(0), None)

        def runner():
            try:
                record["response"] = work(record)
            except Exception as exc:  # noqa: BLE001 —— 任务线程永不静默死亡
                record["error"] = f"{type(exc).__name__}: {exc}"
            finally:
                record["done"] = True

        threading.Thread(target=runner, daemon=True).start()
        with self._lock:
            self._jobs[job_id] = record
        return job_id

    def get(self, job_id: str) -> dict | None:
        return self._jobs.get(job_id)
