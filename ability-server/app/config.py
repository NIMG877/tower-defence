"""服务配置：仅 API key 从环境变量读取（不落代码），其余均为字面值，改这里调行为。"""

from __future__ import annotations

import os
from dataclasses import dataclass, field
from pathlib import Path


@dataclass
class Config:
    # LLM（OpenAI 兼容协议）；key 从环境变量 ABILITY_LLM_API_KEY 读取
    llm_base_url: str = "https://open.bigmodel.cn/api/coding/paas/v4"
    llm_api_key: str = os.environ.get("ABILITY_LLM_API_KEY", "")
    llm_model: str = "glm-5.3-flash"
    llm_temperature: float = 0.4
    # true = 不调真实 LLM，返回内置样例技能（无 key 跑通管线）
    llm_mock: bool = False
    # 须大于最慢单次生成：glm-5.3-flash 写完整配置 JSON 实测可达 120~180s，
    # 上限贴着生成时长会让收尾轮反复超时重试直至熔断（t09/t15 rejected 根因）。
    llm_timeout_seconds: float = 600.0

    # v2 Agent 按角色分层路由（agent.py 两态机：plan_pending→strong，其余→mid）。
    # mid 为 None 时回退 llm_model。
    llm_model_strong: str | None = "glm-5.3-flash"
    llm_model_mid: str | None = "glm-5.3-flash"
    # 思考强度分档（GLM-5.3 系仅支持 low/high/max，API 默认 max）：按轮路由——
    # 规划轮深想；研究与设计轮次档；草稿过闸后的修复/提交是对定稿的机械转写，轻档。
    llm_effort_plan: str | None = "max"
    llm_effort_act: str | None = "high"
    llm_effort_draft: str | None = "low"

    # v2 单线程自由循环的最大轮数（plan 修订含在内）
    agent_max_rounds: int = 24
    # update_plan 修订总次数上限（防无限刷计划）
    agent_max_plan_updates: int = 6
    # 预算闸（§4.4）：墙钟/tokens 双闸，每轮发起 LLM 调用前检查；耗尽时降级交付
    # 最近一次 validate_draft ok 的 sanitized 草稿，无草稿宁 rejected。
    # 计划默认 210s 对齐旧客户端 240s 死线；死线放开后按实测任务尾部（567.6s）
    # +余量定 660。单次调用 timeout = min(llm_timeout_seconds, 剩余墙钟-5s)。
    agent_max_wall_seconds: float = 660.0
    agent_max_total_tokens: int = 500_000

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
    # 组件库（唯一真源：规则/文档/语料；Unity 语料经 db/import_skills.py 导入）
    # ——文档工具与语料检索的数据源；文件缺失时静默降级，按 mtime 惰性重载。
    db_path: str | None = str(Path(__file__).resolve().parent.parent / "data" / "ability.db")


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
