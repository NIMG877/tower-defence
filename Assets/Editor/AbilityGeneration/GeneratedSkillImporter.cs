using System;
using System.Collections.Generic;
using System.IO;
using AbilitySystem;
using UnityEditor;
using UnityEngine;

/// <summary>
/// CLI 生成物收件箱导入器。ability-server 侧生成 ok 后原子写 out/{abilityId}.json
/// （纯 AbilityConfigDto，见 app/service.py _export_out）；本类 InitializeOnLoad 轮询
/// 该目录，Parse 严格闸（幻觉字段/枚举在解析层炸）→ FromDto → 落为
/// Assets/Resources/GeneratedAbilities/{abilityId}.asset，成功即消费 json（案例全文
/// 仍在服务端 logs/gen-*.json）；解析/落盘失败移入 out/failed/ 并 LogError，不重试。
/// 同 abilityId 重生成时 CopySerialized 原地覆盖，资产 GUID 不变，既有引用不断。
/// 目标目录故意在 Resources.LoadAll("Abilities") 扫描根之外：不进菜单④语料导出与
/// 图标池，LLM 产物采纳（移入角色目录 / xlsx Talents 列配路径）是人工动作。
/// </summary>
[InitializeOnLoad]
public static class GeneratedSkillImporter
{
    // 与 app/config.py out_dir 默认值同源，锚定仓库内 ability-server/out。
    private const string InboxRelPath = "ability-server/out";
    private const string ImportRoot = "Assets/Resources/GeneratedAbilities";
    private const double PollIntervalSeconds = 1.0;

    private static double _nextScanAt;
    // 解析失败且移入 failed/ 也失败（如文件被占用）时记录路径，跳过后续扫描——
    // 否则收件箱里每秒重报一次同样的错。
    private static readonly HashSet<string> ReportedFailures = new HashSet<string>();

    static GeneratedSkillImporter()
    {
        EditorApplication.update += Tick;
    }

    private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < _nextScanAt) return;
        _nextScanAt = EditorApplication.timeSinceStartup + PollIntervalSeconds;
        string inbox = Path.Combine(ProjectRoot, InboxRelPath);
        if (!Directory.Exists(inbox)) return;
        foreach (string file in Directory.GetFiles(inbox, "*.json"))
        {
            if (ReportedFailures.Contains(file)) continue;
            ImportOne(file);
        }
    }

    private static void ImportOne(string file)
    {
        try
        {
            AbilityConfigDto dto = AbilityConfigBuilder.Parse(File.ReadAllText(file));
            AbilityConfig generated = AbilityConfigBuilder.FromDto(dto);
            WarnPlaceholderIndexes(generated);
            SaveAsset(generated, dto.abilityId);
            File.Delete(file);
            Debug.Log($"[GeneratedSkillImporter] 已导入 {dto.abilityId} → {ImportRoot}/{dto.abilityId}.asset");
        }
        catch (Exception e)
        {
            Debug.LogError(
                $"[GeneratedSkillImporter] 导入失败 {Path.GetFileName(file)}：{e.Message}" +
                "（文件移入 out/failed/，案例全文见服务端 logs/gen-*.json）");
            MoveToFailed(file);
            ReportedFailures.Add(file);
        }
    }

    private static void SaveAsset(AbilityConfig generated, string abilityId)
    {
        string assetPath = $"{ImportRoot}/{abilityId}.asset";
        AbilityConfig existing = AssetDatabase.LoadAssetAtPath<AbilityConfig>(assetPath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(generated, existing);
            EditorUtility.SetDirty(existing);
        }
        else
        {
            Directory.CreateDirectory(Path.Combine(ProjectRoot, ImportRoot));
            AssetDatabase.CreateAsset(generated, assetPath);
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// 描述模式产物无宿主资产：spawn_entity.spawnIndex / fire_bullets.bulletDataIndex
    /// 取 0 占位待宿主绑定（服务端无从校验）。快照模式合法取 0 也会命中——警告只提示
    /// 人工核对，不阻断导入。
    /// </summary>
    private static void WarnPlaceholderIndexes(AbilityConfig cfg)
    {
        List<string> hits = new List<string>();
        for (int i = 0; i < cfg.rules.Length; i++)
            ScanSteps(cfg.rules[i].steps, $"rule[{i}]", hits);
        if (hits.Count == 0) return;
        Debug.LogWarning(
            "[GeneratedSkillImporter] 占位索引待宿主绑定（=0 为描述模式占位；若为快照模式" +
            $"产物请忽略）：\n  {string.Join("\n  ", hits)}");
    }

    private static void ScanSteps(StepConfig[] steps, string where, List<string> hits)
    {
        for (int i = 0; i < steps.Length; i++)
        {
            StepConfig step = steps[i];
            if (step == null) continue;
            string at = $"{where}/steps[{i}]";
            if (step.op == "spawn_entity" && step.args.HasKey("spawnIndex")
                    && step.args.GetInt("spawnIndex") == 0)
                hits.Add($"{at} spawn_entity.spawnIndex=0");
            if (step.op == "fire_bullets" && step.args.HasKey("bulletDataIndex")
                    && step.args.GetInt("bulletDataIndex") == 0)
                hits.Add($"{at} fire_bullets.bulletDataIndex=0");
            ScanSteps(step.steps, at, hits);
            ScanSteps(step.elseSteps, at, hits);
        }
    }

    private static void MoveToFailed(string file)
    {
        try
        {
            string failedDir = Path.Combine(ProjectRoot, InboxRelPath, "failed");
            Directory.CreateDirectory(failedDir);
            string dest = Path.Combine(failedDir, Path.GetFileName(file));
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(file, dest);
        }
        catch (Exception e)
        {
            Debug.LogError($"[GeneratedSkillImporter] 移入 out/failed/ 失败：{e.Message}");
        }
    }
}
