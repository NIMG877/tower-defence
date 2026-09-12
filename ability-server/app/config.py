"""服务配置：仅 API key 从环境变量读取（不落代码），其余均为字面值，改这里调行为。"""

from __future__ import annotations

import os
from dataclasses import dataclass, field
from pathlib import Path


@dataclass
class Config:
    # LLM（OpenAI 兼容协议）；key 从环境变量 ABILITY_LLM_API_KEY 读取
    llm_base_url: str = "https://open.bigmodel.cn/api/paas/v4"
    llm_api_key: str = os.environ.get("ABILITY_LLM_API_KEY", "")
    llm_model: str = "glm-5.3-flash"
    llm_temperature: float = 0.4
    # true = 不调真实 LLM，返回内置样例技能（无 key 跑通管线）
    llm_mock: bool = False
    llm_timeout_seconds: float = 120.0

    # 生成 → 校验 → 带错重试 的最大轮数（含首轮，作用于 generate 阶段）
    max_attempts: int = 3
    # analyze 阶段 function-calling 工具循环的最大轮数
    agent_max_rounds: int = 8

    # 简单令牌鉴权：空 = 不鉴权（仅限本机开发）
    server_token: str = ""
    # 每 token 每窗口最大请求数
    rate_limit: int = 10
    rate_window_seconds: int = 60

    # 快照哈希 → 响应 的内存缓存条数上限
    cache_size: int = 64

    # 失败/成功案例落盘目录（回放调试用），None = 关闭。
    # 锚定本文件位置（ability-server/logs），不受进程启动目录影响；该目录已 gitignore。
    log_dir: str | None = str(Path(__file__).resolve().parent.parent / "logs")
    # 技能语料（Unity 菜单⑤导出的现有 AbilityConfig 资产）——检索范例/风格统计/
    # 撞名检查的数据源；文件缺失时静默降级为无语料，按 mtime 惰性重载。
    corpus_path: str | None = str(Path(__file__).resolve().parent.parent / "data" / "skills.json")

    server_host: str = "127.0.0.1"
    server_port: int = 8765


@dataclass
class RateLimiter:
    """内存滑动窗口限流（单进程够用；上公网时换网关限流）。"""

    limit: int
    window_seconds: int
    _hits: dict[str, list[float]] = field(default_factory=dict)

    def allow(self, key: str, now: float) -> bool:
        hits = [t for t in self._hits.get(key, []) if now - t < self.window_seconds]
        if len(hits) >= self.limit:
            self._hits[key] = hits
            return False
        hits.append(now)
        self._hits[key] = hits
        return True
