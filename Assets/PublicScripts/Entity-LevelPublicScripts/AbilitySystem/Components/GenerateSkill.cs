using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace AbilitySystem.Components
{
    /// <summary>
    /// 运行时 LLM 生成技能（plan-llm-generated-ability 阶段三）。OnTrigger 把战局快照 +
    /// 宿主清单异步提交给 ability-server 后立即返回（fire-and-forget，不阻塞步骤序列），
    /// OnTick 轮询任务状态；新增阶段经同一增量游标分流——Console 全量滚动 +
    /// LevelMessagePanel 飘字短文案（ProgressTextOf 映射）；完成后 FromDto →
    /// ReplaceSkill 替换当前技能（生成物仅本场有效：ReplaceSkill 不触碰
    /// EntityData，下场战斗 PreWarm 恢复原技能）。
    ///
    /// 生成期间经 TimeScaleManager.SetSlow 申请战场慢速（计数制，与查看慢速
    /// 独立），任务结束/中止时释放——等 LLM 出技能的几十秒战场放缓而非停摆。
    ///
    /// 典型配置：常驻天赋规则触发 OnInitialize（部署时生成一次；休眠重部署会重新触发，
    /// OnTeardown 已中止上一笔在途任务）。轮询靠 OnTick 驱动，要求宿主能力常驻 active
    /// （天赋 sp 为 totalSp=0 + Auto + NoConsume，首帧自动开启）；配在会结束的能力上，
    /// 结束期间轮询暂停、重新开启后续跑。
    /// </summary>
    [RegisterComponent("GenerateSkill")]
    public class GenerateSkill : AbilityComponentBase
    {
        private const string DefaultServerUrl = "http://127.0.0.1:8765";
        private const float PollIntervalSeconds = 1f;
        private const float PollTimeoutSeconds = 900f; // 服务端 v2 预算 660s 硬闸 + 轮询/握手余量
        // 生成进度飘字底色：深紫罗兰，区别于伤害红/治疗绿/费用橙/SP 蓝
        private static readonly Color ProgressColor = new Color(0.55f, 0.20f, 0.85f);

        private enum TaskPhase { Idle, AwaitJobId, Polling }

        private Func<string> _requestText;
        private Func<string> _serverUrl;
        private Entity _host;

        private TaskPhase _phase = TaskPhase.Idle;
        private UnityWebRequest _inFlight;   // 在途单发请求（提交 POST 或轮询 GET）
        private string _jobId;
        private float _deadline;
        private float _nextPollAt;
        private int _printed;
        private AbilityConfig _lastGenerated; // 上一代生成物；新一轮替换成功后销毁

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            Blackboard bb = ctx.sharedBlackboard;
            _requestText = p.GetStringLazy("request", "", bb);
            _serverUrl = p.GetStringLazy("serverUrl", DefaultServerUrl, bb);
            _host = ctx.entity;
            ResetTask();
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            // 有未完成任务时忽略再触发——组件可配任意事件，防 OnTick 类高频触发刷请求。
            if (_phase != TaskPhase.Idle) return;
            Submit();
        }

        public override void OnTick(AbilityContext ctx, float dt)
        {
            if (_phase == TaskPhase.Idle) return;
            float now = Time.realtimeSinceStartup;
            if (now > _deadline)
            {
                Fail($"生成轮询超时（{PollTimeoutSeconds}s），jobId={_jobId}");
                return;
            }

            if (_inFlight == null)
            {
                if (now < _nextPollAt) return;
                _inFlight = MakeGet($"{_serverUrl()}/jobs/{_jobId}");
                return;
            }
            if (!_inFlight.isDone) return;

            UnityWebRequest request = _inFlight;
            _inFlight = null;
            string raw = request.result == UnityWebRequest.Result.Success
                ? request.downloadHandler.text : null;
            string requestError = request.error;
            request.Dispose();
            if (raw == null)
            {
                Fail($"生成请求失败：{requestError}（确认 ability-server 已启动，见 ability-server/README.md）");
                return;
            }

            if (_phase == TaskPhase.AwaitJobId)
            {
                // serverUrl 是数据驱动参数，指错服务可能 200 返回非契约体；组件在 Tick 路径上，
                // 解析异常会穿透到 FixedUpdate 每帧刷错——统一在解析边界复位并报错（不吞）。
                Dictionary<string, string> submit;
                try
                {
                    submit = JsonConvert.DeserializeObject<Dictionary<string, string>>(raw);
                }
                catch (Exception exc)
                {
                    Fail($"异步提交响应解析失败：{exc.Message}");
                    return;
                }
                if (submit == null || !submit.TryGetValue("jobId", out _jobId))
                {
                    Fail($"异步提交失败：{raw}");
                    return;
                }
                _phase = TaskPhase.Polling;
                _nextPollAt = now + PollIntervalSeconds;
                Debug.Log($"[GenerateSkill] 任务已提交 jobId={_jobId}，后台轮询中（阶段日志实时滚动）");
                return;
            }

            // Polling：阶段日志增量滚动；done 才进完成处理。
            AgentJobStatus status;
            try
            {
                status = JsonConvert.DeserializeObject<AgentJobStatus>(raw);
            }
            catch (Exception exc)
            {
                Fail($"轮询响应解析失败：{exc.Message}");
                return;
            }
            if (status == null)
            {
                Fail("轮询响应解析失败");
                return;
            }
            if (status.phases != null)
            {
                for (; _printed < status.phases.Count; _printed++)
                {
                    AgentJobStatus.PhaseEntry entry = status.phases[_printed];
                    Debug.Log($"[GenerateSkill][Agent] {entry.phase}  {entry.detail}");
                    string progress = ProgressTextOf(entry.phase, entry.detail);
                    if (progress != null)
                        ShowProgressText(progress);
                }
            }
            _nextPollAt = now + PollIntervalSeconds;
            if (!status.done) return;
            ReleaseSlow();
            ResetTask();
            HandleServerResponse(raw);
        }

        public override void OnTeardown(AbilityContext ctx)
        {
            // 宿主休眠/阵亡：中止在途任务，重部署后规则重新触发。
            // _lastGenerated 不销毁——它还是当前在场技能。
            ReleaseSlow();
            ResetTask();
        }

        /// <summary>
        /// 阶段 → 飘字短文案（与 Console 同一增量游标逐条触发，null = 不飘）。
        /// 游戏化词表：推演=plan、构筑=act、校准=review(validate_draft)、
        /// 定型=submit(submit_skill)、降格=degraded、故障=handshake 契约漂移、
        /// 习得/失败=done 结果。
        /// plan/act 轮次条目从 detail 取轮号（"round N model=..."），工具/异常条目
        /// 退回不带轮号。public 供 EditMode 测试直接断言（测试 asmdef 无
        /// InternalsVisibleTo）。
        /// </summary>
        public static string ProgressTextOf(string phase, string detail)
        {
            switch (phase)
            {
                case "plan":
                case "act":
                    return WithRound(phase == "plan" ? "推演" : "构筑", detail);
                case "review": return "校准";
                case "submit": return "定型";
                case "degraded": return "降格";
                case "handshake": return "故障";
                case "done":
                    if (detail == "ok") return "习得！";
                    if (detail == "rejected") return "失败";
                    return null;
                default: return null;
            }
        }

        private static string WithRound(string word, string detail)
        {
            if (detail != null && detail.StartsWith("round "))
            {
                string tail = detail.Substring("round ".Length);
                int end = tail.IndexOf(' ');
                if (int.TryParse(end < 0 ? tail : tail.Substring(0, end), out int round))
                    return $"{word} {round}";
            }
            return word;
        }

        private void ShowProgressText(string content)
        {
            MyUI.LevelMessagePanel.Panel.ShowText(
                _host.transform.position, content, ProgressColor);
        }

        /// <summary>
        /// 处理 GET /jobs/{jobId} 的完成响应（done 后的原始 JSON）：服务端任务异常/拒绝
        /// → 报错返回；ok → 终检并注入。public 供 EditMode 测试直接断言。
        /// </summary>
        public void HandleServerResponse(string rawJson)
        {
            AgentJobStatus status;
            try
            {
                status = JsonConvert.DeserializeObject<AgentJobStatus>(rawJson);
            }
            catch (Exception exc)
            {
                Debug.LogError($"[GenerateSkill] 轮询响应解析失败：{exc.Message}");
                return;
            }
            if (status == null)
            {
                Debug.LogError("[GenerateSkill] 轮询响应解析失败：空对象");
                return;
            }

            // 先报服务端结果再谈注入——拒绝/异常信息不能被宿主状态吞掉。
            // 组件随宿主销毁的场景走不到这里（OnTeardown 已中止轮询），
            // 因此无需探针那套 Unity null 宿主检查。
            if (!string.IsNullOrEmpty(status.error))
            {
                Debug.LogError($"[GenerateSkill] 服务端任务异常：{status.error}");
                return;
            }
            AgentJobStatus.GenerateResponse response = status.response;
            if (response == null || response.status != "ok" || response.ability == null)
            {
                Debug.LogError($"[GenerateSkill] 服务端拒绝生成（attempts={response?.report?.attempts ?? 0}）");
                AgentJobStatus.LogServerIssues(response, "[GenerateSkill]");
                return;
            }

            AgentJobStatus.LogServerIssues(response, "[GenerateSkill]");
            ApplyGeneratedAbility(_host.AbilityRunner, response.ability);
        }

        /// <summary>
        /// FromDto（严格反序列化即结构闸）→ ReplaceSkill → 销毁上一代生成物。
        /// 返回 runtimeId。public 供 EditMode 测试直接断言。
        /// </summary>
        public string ApplyGeneratedAbility(EntityAbilityRunner runner, AbilityConfigDto dto)
        {
            AbilityConfig cfg = AbilityConfigBuilder.FromDto(dto);
            string runtimeId = runner.ReplaceSkill(cfg);
            if (_lastGenerated != null) UnityEngine.Object.Destroy(_lastGenerated);
            _lastGenerated = cfg;
            Debug.Log($"[GenerateSkill] 服务器生成技能已替换当前技能 id={runtimeId}，SpSlider 与技能卡读实时列表");
            return runtimeId;
        }

        private void Submit()
        {
            string body = JsonConvert.SerializeObject(
                AgentGenerateRequest.Build(_host, new { request = _requestText() }));

            var request = new UnityWebRequest($"{_serverUrl()}/generate-ability/async", "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 5;
            request.SendWebRequest();

            _inFlight = request;
            _phase = TaskPhase.AwaitJobId;
            _deadline = Time.realtimeSinceStartup + PollTimeoutSeconds;
            TimeScaleManager.Manager.SetSlow(true);
            Debug.Log($"[GenerateSkill] 已提交生成任务（request={_requestText()}），后台轮询中");
        }

        private static UnityWebRequest MakeGet(string url)
        {
            UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = 5;
            request.SendWebRequest();
            return request;
        }

        /// <summary>任务在身（_phase 非 Idle）时释放战场慢速。任务结束的三条路径
        /// （Fail/完成/OnTeardown）都经此收口；Idle 时（如未提交先休眠）不动计数。</summary>
        private void ReleaseSlow()
        {
            if (_phase == TaskPhase.Idle) return;
            TimeScaleManager.Manager.SetSlow(false);
        }

        private void Fail(string message)
        {
            ReleaseSlow();
            ResetTask();
            Debug.LogError($"[GenerateSkill] {message}");
        }

        private void ResetTask()
        {
            if (_inFlight != null)
            {
                _inFlight.Dispose();
                _inFlight = null;
            }
            _jobId = null;
            _phase = TaskPhase.Idle;
            _printed = 0;
        }
    }
}
