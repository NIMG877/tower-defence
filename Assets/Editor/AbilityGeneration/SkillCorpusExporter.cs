using System;
using System.Collections.Generic;
using System.IO;
using AbilitySystem;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 导出服务端技能语料（ability-server/data/skills.json）：LLM 生成技能时按 op
/// 重叠检索的少样本范例 + 全语料风格统计 + abilityId 撞名检查的数据源。
/// 两个来源，按资产引用去重：
///   ① Resources/Abilities 下的 SubJobs 特性类技能资产；
///   ② EntityDataCollection 各实体的 Skills/Talents 引用的 AbilityConfig。
/// 只导出通过客户端校验器的技能；服务端按文件 mtime 惰性重载，重新导出后
/// 无需重启 uvicorn。
/// </summary>
public static class SkillCorpusExporter
{
    private const string ServerDataDir = "ability-server/data";
    // 与 GameDataService.EntityCollectionPath 同源（那边是 private const，这里镜像一份）
    private const string EntityCollectionPath = "GameDatas/EntityDataCollection";

    [MenuItem("Tools/AbilityGeneration/4. 导出技能语料（AbilityConfig→服务端检索库）")]
    public static void Export()
    {
        var seen = new HashSet<AbilityConfig>();
        var skills = new List<AbilityConfigDto>();
        int skipped = 0;

        // 来源①：SubJobs 特性类技能资产
        int subJobs = Collect(Resources.LoadAll<AbilityConfig>("Abilities"), seen, skills, ref skipped);

        // 来源②：EntityDataCollection 各实体的 Skills/Talents 引用
        int fromEntities = 0;
        var collection = Resources.Load<EntityDataCollection>(EntityCollectionPath);
        if (collection == null)
        {
            Debug.LogError($"[Corpus] 找不到 {EntityCollectionPath}，实体 Skills/Talents 来源缺失（仅导出 SubJobs）");
        }
        else
        {
            var referenced = new List<AbilityConfig>();
            foreach (EntityData data in collection.EntityBasicDatas)
            {
                if (data == null) continue;
                referenced.AddRange(data.Skills);
                referenced.AddRange(data.Talents);
            }
            fromEntities = Collect(referenced, seen, skills, ref skipped);
        }

        if (skills.Count == 0)
        {
            Debug.LogError("[Corpus] 两个来源都没有收集到可用技能，未写出语料文件");
            return;
        }

        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ServerDataDir);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "skills.json");
        var payload = new
        {
            protocolVersion = AgentGenerateRequest.ProtocolVersion,
            exportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            skills,
        };
        File.WriteAllText(path, JsonConvert.SerializeObject(payload, Formatting.Indented, settings: new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() },
        }));
        Debug.Log($"[Corpus] 导出 {skills.Count} 个技能（SubJobs {subJobs} + 实体引用 {fromEntities}，跳过 {skipped} 个）→ {path}");
    }

    /// <summary>逐个转 DTO + 校验过滤。seen 按资产引用去重——同一 AbilityConfig 被
    /// 多个实体的 Skills/Talents 共享时只导一次。返回本次新收进语料的数量。</summary>
    private static int Collect(IEnumerable<AbilityConfig> configs, HashSet<AbilityConfig> seen,
                               List<AbilityConfigDto> skills, ref int skipped)
    {
        int added = 0;
        foreach (AbilityConfig cfg in configs)
        {
            if (cfg == null || !seen.Add(cfg)) continue;
            AbilityConfigDto dto = ToDto(cfg);
            // FromDto 严格反序列化当闸：DTO 缺字段/类型不符在此抛出，挡下来人工看。
            try
            {
                AbilityConfigBuilder.FromDto(dto);
            }
            catch (System.Exception exc)
            {
                skipped++;
                Debug.LogWarning($"[Corpus] 跳过无法反序列化的技能 {dto.abilityId}（{exc.Message}；请检查对应资产）");
                continue;
            }
            skills.Add(dto);
            added++;
        }
        return added;
    }

    /// <summary>SO → DTO（与服务端/LLM 的传输契约同形）。rules 走 GetRules()
    /// 以兼容仍带 legacyComponents 的旧资产；iconKey 用 Sprite 名对齐 AbilityIconPool。</summary>
    public static AbilityConfigDto ToDto(AbilityConfig cfg)
    {
        return new AbilityConfigDto
        {
            abilityId = cfg.abilityId,
            abilityName = cfg.abilityName,
            description = cfg.description,
            iconKey = cfg.icon != null ? cfg.icon.name : null,
            sp = cfg.sp,
            rules = cfg.GetRules(),
        };
    }
}
