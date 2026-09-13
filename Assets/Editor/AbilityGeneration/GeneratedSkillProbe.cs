using System.Collections.Generic;
using AbilitySystem;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 阶段一闭环验证探针（plan-llm-generated-ability）。四个入口：
///   1. 打印战局快照 —— 验证 BattleSnapshotBuilder（需 PlayMode + 已部署干员）；
///   2. 注入探针技能 —— 走完整管线 JSON→Parse→Validate→FromDto→ReplaceSkill，
///      效果为部署即 +99 费用（modify_cost 飘字+音效，肉眼可见）；
///   3. 注入非法技能 —— op 不在注册表，验证校验器拒绝路径（应弹 error 且不注入）；
///   4. 经本地服务器 Agent 生成 —— 阶段二管线：异步提交快照+opList+宿主清单，
///      EditorApplication.update 非阻塞轮询 Agent 三阶段状态（analyze/describe/generate，
///      阶段日志实时滚动，编辑器不卡），完成后客户端终检并 ReplaceSkill 注入。
///      启动方式见 ability-server/README.md；ABILITY_LLM_MOCK=1 无 key 可跑通。
/// </summary>
public static class GeneratedSkillProbe
{
    private const string ServerBaseUrl = "http://127.0.0.1:8765";

    // 效果选 modify_cost：无目标依赖、全局立即生效、自带 UI 反馈，最适合闭环演示。
    private const string ProbeJson = @"
{
  ""abilityId"": ""gen_probe_1"",
  ""abilityName"": ""生成技能探针"",
  ""description"": ""验证闭环：部署即获得 99 点部署费用"",
  ""iconKey"": ""charger"",
  ""sp"": { ""totalSp"": 0, ""initialSp"": 0, ""chargeNum"": 1, ""abilityAmount"": 0,
            ""recoverMode"": ""Natural"", ""consumeMode"": ""NoConsume"", ""openMode"": ""Auto"" },
  ""rules"": [
    {
      ""triggers"": [ { ""triggerEvent"": ""OnInitialize"", ""groups"": [] } ],
      ""reentry"": ""IgnoreWhileRunning"",
      ""steps"": [
        { ""op"": ""modify_cost"", ""args"": { ""entries"": [
          { ""key"": ""amount"", ""value"": ""99"", ""type"": ""Int"", ""fromBlackboard"": false }
        ] } }
      ]
    }
  ]
}";

    private const string InvalidJson = @"
{
  ""abilityId"": ""gen_probe_bad"",
  ""abilityName"": ""非法技能（应被拒绝）"",
  ""description"": ""op 不在注册表"",
  ""rules"": [
    {
      ""triggers"": [ { ""triggerEvent"": ""OnInitialize"", ""groups"": [] } ],
      ""steps"": [ { ""op"": ""make_big_explosion"", ""args"": { ""entries"": [] } } ]
    }
  ]
}";

    [MenuItem("Tools/AbilityGeneration/1. 打印战局快照")]
    public static void PrintSnapshot()
    {
        Entity self = FindProbeHost();
        if (self == null) return;
        BattleSnapshotBuilder.BattleSnapshot snapshot = BattleSnapshotBuilder.Build(self);
        Debug.Log($"[Probe] 战局快照（{snapshot.entities.Length} 个实体，地图 {snapshot.mapI}x{snapshot.mapJ}，" +
                  $"自身血量比 {snapshot.selfHpRate}，交叉项由服务端 Agent 按需计算）：\n" +
                  BattleSnapshotBuilder.ToJson(snapshot, indented: true));
    }

    [MenuItem("Tools/AbilityGeneration/2. 注入探针技能（JSON→校验→ReplaceSkill）")]
    public static void InjectProbeSkill()
    {
        Entity self = FindProbeHost();
        if (self == null) return;

        AbilityConfigDto dto = AbilityConfigBuilder.Parse(ProbeJson);
        AbilityConfigValidator.Result result = AbilityConfigValidator.Validate(dto);
        AgentJobStatus.LogValidatorIssues(result, "[Probe]");
        if (!result.Ok)
        {
            Debug.LogError("[Probe] 校验未通过，拒绝注入");
            return;
        }

        AbilityConfig cfg = AbilityConfigBuilder.FromDto(result.Sanitized);
        string runtimeId = self.AbilityRunner.ReplaceSkill(cfg);
        Debug.Log($"[Probe] 已替换当前技能 id={runtimeId}，Skills.Count={self.AbilityRunner.Skills.Count}，" +
                  $"icon={(cfg.icon != null ? cfg.icon.name : "null")}；" +
                  "此刻应看到 +99 费用飘字与音效（modify_cost），撤退再部署会再触发一次");
    }

    [MenuItem("Tools/AbilityGeneration/3. 注入非法技能（应被校验拒绝）")]
    public static void InjectInvalidSkill()
    {
        Entity self = FindProbeHost();
        if (self == null) return;

        AbilityConfigDto dto = AbilityConfigBuilder.Parse(InvalidJson);
        AbilityConfigValidator.Result result = AbilityConfigValidator.Validate(dto);
        AgentJobStatus.LogValidatorIssues(result, "[Probe]");
        if (result.Ok)
        {
            Debug.LogError("[Probe] 非法技能意外通过校验——校验器有漏洞，检查 unknown-op 路径！");
            return;
        }
        Debug.Log("[Probe] 非法技能被校验器正确拒绝（未注入）。上面的 error 即 unknown-op 路径。");
    }

    [MenuItem("Tools/AbilityGeneration/4. 经本地服务器 Agent 生成技能（阶段二）")]
    public static void GenerateViaServer()
    {
        Entity self = FindProbeHost();
        if (self == null) return;

        string body = JsonConvert.SerializeObject(AgentGenerateRequest.Build(self, new { }));

        // 异步提交（短请求，本地秒回）；轮询交给 EditorApplication.update 状态机。
        string submitRaw = HttpPost($"{ServerBaseUrl}/generate-ability/async", body);
        if (submitRaw == null) return;
        var submit = JsonConvert.DeserializeObject<Dictionary<string, string>>(submitRaw);
        if (submit == null || !submit.TryGetValue("jobId", out string jobId))
        {
            Debug.LogError($"[Probe] 异步提交失败：{submitRaw}");
            return;
        }
        Debug.Log($"[Probe] 任务已提交 jobId={jobId}，后台轮询中（阶段日志实时滚动，编辑器可继续操作）");
        _poll = new ServerPoll { self = self, jobId = jobId,
                                 deadline = Time.realtimeSinceStartup + PollTimeoutSeconds };
        EditorApplication.update += PollTick;
    }

    private const float PollTimeoutSeconds = 240f; // 真实 LLM 的 analyze+generate 可能要一两分钟

    /// <summary>菜单④的在途轮询状态；_poll 非 null 即生成进行中（兼作重入闸）。</summary>
    private class ServerPoll
    {
        public Entity self;             // 完成时可能已被销毁，用 Unity null 判定
        public string jobId;
        public float deadline;
        public float nextPollAt;
        public int printed;
        public UnityWebRequest request; // 在途的单次 GET；null = 可发下一发
    }

    private static ServerPoll _poll;

    /// <summary>挂 EditorApplication.update 的非阻塞轮询：每 0.4s 发一次 GET，
    /// isDone 后在主线程解析——阶段日志实时滚动，编辑器全程可交互。</summary>
    private static void PollTick()
    {
        ServerPoll poll = _poll;
        float now = Time.realtimeSinceStartup;
        if (now > poll.deadline)
        {
            FailPoll($"[Probe] 轮询超时（{PollTimeoutSeconds}s），jobId={poll.jobId}");
            return;
        }

        if (poll.request == null)
        {
            if (now < poll.nextPollAt) return;
            poll.nextPollAt = now + 0.4f;
            poll.request = UnityWebRequest.Get($"{ServerBaseUrl}/jobs/{poll.jobId}");
            poll.request.timeout = 5;
            poll.request.SendWebRequest();
            return;
        }

        if (!poll.request.isDone) return;
        UnityWebRequest request = poll.request;
        poll.request = null;
        string raw = request.result == UnityWebRequest.Result.Success
            ? request.downloadHandler.text : null;
        string requestError = request.error;
        request.Dispose();
        if (raw == null)
        {
            FailPoll($"[Probe] 轮询请求失败：{requestError}");
            return;
        }

        AgentJobStatus status = JsonConvert.DeserializeObject<AgentJobStatus>(raw);
        if (status == null)
        {
            FailPoll("[Probe] 轮询响应解析失败");
            return;
        }
        if (status.phases != null)
        {
            for (; poll.printed < status.phases.Count; poll.printed++)
                Debug.Log($"[Probe][Agent] {status.phases[poll.printed].phase}  {status.phases[poll.printed].detail}");
        }
        if (!status.done) return;
        CompletePoll(poll.self, status);
    }

    private static void StopPolling()
    {
        EditorApplication.update -= PollTick;
        _poll = null;
    }

    private static void FailPoll(string message)
    {
        StopPolling();
        Debug.LogError(message);
    }

    /// <summary>job done：主线程终检 + 注入（原同步路径的后半段）。
    /// 先报服务端结果再判宿主——宿主已死也不能把拒绝/成功信息吞掉。</summary>
    private static void CompletePoll(Entity self, AgentJobStatus status)
    {
        StopPolling();
        if (!string.IsNullOrEmpty(status.error))
        {
            Debug.LogError($"[Probe] 服务端任务异常：{status.error}");
            return;
        }

        AgentJobStatus.GenerateResponse response = status.response;
        if (response == null || response.status != "ok" || response.ability == null)
        {
            Debug.LogError($"[Probe] 服务端拒绝生成（attempts={response?.report?.attempts ?? 0}）");
            AgentJobStatus.LogServerIssues(response, "[Probe]");
            return;
        }

        if (self == null)
        {
            Debug.LogError("[Probe] 生成成功但宿主已不在场上（阵亡/撤退/退出战斗），放弃注入；完整结果见 ability-server/logs/");
            return;
        }

        // 客户端终检（防 schema 版本漂移），再走与阶段一相同的注入路径。
        AbilityConfigValidator.Result result = AbilityConfigValidator.Validate(response.ability);
        AgentJobStatus.LogValidatorIssues(result, "[Probe]");
        AgentJobStatus.LogServerIssues(response, "[Probe]");
        if (!result.Ok)
        {
            Debug.LogError("[Probe] 服务端返回未通过客户端终检（op 注册表与服务端 schema 漂移？）");
            return;
        }

        AbilityConfig cfg = AbilityConfigBuilder.FromDto(result.Sanitized);
        string runtimeId = self.AbilityRunner.ReplaceSkill(cfg);
        Debug.Log($"[Probe] 服务器生成技能已替换当前技能 id={runtimeId}，Skills.Count={self.AbilityRunner.Skills.Count}，" +
                  $"icon={(cfg.icon != null ? cfg.icon.name : "null")}，cached={response.report?.cached}；" +
                  "SpSlider 与技能卡读实时列表，现在释放/等待的就是新技能");
    }

    private static string HttpPost(string url, string body)
    {
        using var request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        UnityWebRequestAsyncOperation op = request.SendWebRequest();
        while (!op.isDone) System.Threading.Thread.Sleep(30); // 编辑器主线程阻塞等待；localhost 秒回
        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[Probe] 服务器请求失败：{request.error}（确认已启动 ability-server，见 ability-server/README.md）");
            return null;
        }
        return request.downloadHandler.text;
    }

    private static Entity FindProbeHost()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[Probe] 请先进入 PlayMode 并部署一个干员");
            return null;
        }
        if (EntityManager.Manager == null)
        {
            Debug.LogError("[Probe] EntityManager 未初始化（需在战斗中）");
            return null;
        }
        // radius<0 = 不限距离，force=true 忽略可选性过滤——拿全量 camp1（已部署干员）。
        var turrets = EntityManager.Manager.EntitySelector_Radius((0f, 0f), 1, true, -1f, true);
        if (turrets == null || turrets.Count == 0)
        {
            Debug.LogError("[Probe] 场上没有已部署的干员，先部署一个");
            return null;
        }
        return turrets[0];
    }
}
