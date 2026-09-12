using AbilitySystem;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 阶段一闭环验证探针（plan-llm-generated-ability）。临时工具，阶段二 Editor 触发
/// 工具成型后可删。三个入口：
///   1. 打印战局快照 —— 验证 BattleSnapshotBuilder（需 PlayMode + 已部署干员）；
///   2. 注入探针技能 —— 走完整管线 JSON→Parse→Validate→FromDto→AddSkill，效果为
///      部署即 +99 费用（modify_cost 飘字+音效，肉眼可见）；
///   3. 注入非法技能 —— op 不在注册表，验证校验器拒绝路径（应弹 error 且不注入）。
/// </summary>
public static class GeneratedSkillProbe
{
    private const string ProbeAbilityId = "gen_probe_1";

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
                  $"敌 {snapshot.cross.enemyCount}/友 {snapshot.cross.allyCount}，最近敌距 {snapshot.cross.nearestEnemyDistance}）：\n" +
                  BattleSnapshotBuilder.ToJson(snapshot, indented: true));
    }

    [MenuItem("Tools/AbilityGeneration/2. 注入探针技能（JSON→校验→AddSkill）")]
    public static void InjectProbeSkill()
    {
        Entity self = FindProbeHost();
        if (self == null) return;

        // 幂等按 abilityId（AddSkill 的幂等按 cfg 引用，这里每次 Parse 出新实例）。
        foreach (var ability in self.AbilityRunner.Skills)
        {
            if (ability.config != null && ability.config.abilityId == ProbeAbilityId)
            {
                Debug.Log($"[Probe] {ProbeAbilityId} 已注入，跳过（每次部署的 InitializeEvent 会再触发一次 +99）");
                return;
            }
        }

        AbilityConfigDto dto = AbilityConfigBuilder.Parse(ProbeJson);
        AbilityConfigValidator.Result result = AbilityConfigValidator.Validate(dto);
        LogIssues(result);
        if (!result.Ok)
        {
            Debug.LogError("[Probe] 校验未通过，拒绝注入");
            return;
        }

        AbilityConfig cfg = AbilityConfigBuilder.FromDto(result.Sanitized);
        string runtimeId = self.AbilityRunner.AddSkill(cfg);
        Debug.Log($"[Probe] 注入成功 id={runtimeId}，Skills.Count={self.AbilityRunner.Skills.Count}，" +
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
        LogIssues(result);
        if (result.Ok)
        {
            Debug.LogError("[Probe] 非法技能意外通过校验——校验器有漏洞，检查 unknown-op 路径！");
            return;
        }
        Debug.Log("[Probe] 非法技能被校验器正确拒绝（未注入）。上面的 error 即 unknown-op 路径。");
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

    private static void LogIssues(AbilityConfigValidator.Result result)
    {
        for (int i = 0; i < result.Issues.Count; i++)
        {
            AbilityConfigValidator.Issue issue = result.Issues[i];
            if (issue.IsError) Debug.LogError($"[Probe][校验 error] {issue}");
            else Debug.LogWarning($"[Probe][校验 warning] {issue}");
        }
    }
}
