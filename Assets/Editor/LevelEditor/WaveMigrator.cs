using UnityEditor;
using UnityEngine;

/// <summary>
/// Wave v1 → v2 数据迁移。Phase 1a 接入;Phase 1b 之后 Wave.LegacyActions / Action.GapFromLastAction 删除,本类保留为日志钩子(空跑即可)。
/// 入口:<see cref="MigrateLevelData"/> (单关卡),<see cref="MigrateAllLegacyLevels"/> (全工程扫描)。
/// </summary>
public static class WaveMigrator
{
    static readonly Color[] Palette =
    {
        new Color(0.55f, 0.55f, 0.55f),
        new Color(0.30f, 0.80f, 0.60f),
        new Color(0.30f, 0.60f, 0.90f),
        new Color(0.86f, 0.80f, 0.66f),
        new Color(0.77f, 0.52f, 0.75f),
    };

    public static Color DefaultTrackColor(int trackIndex) => Palette[trackIndex % Palette.Length];

    public static void MigrateLevelData(LevelData ld)
    {
        if (ld == null) return;
        if (ld.SchemaVersion >= 2) return;
        if (ld.Waves == null)
        {
            ld.SchemaVersion = 2;
            return;
        }

        // Phase 1b+:LegacyActions 字段已删除。这里只兜底 v1 → v2 未在 Phase 1a 跑过迁移的关卡
        // (理论上不会发生,但 InitializeOnLoad 仍扫一遍以防 commit 跨分支合并时漏过)。
        Debug.LogWarning($"[WaveMigrator] 关卡 {ld.name} SchemaVersion={ld.SchemaVersion} 但 Tracks 为空,无法自动恢复 v1 数据,请手动重建或从 git 找回 Phase 1a 前的版本。");
        ld.SchemaVersion = 2;
    }

    [InitializeOnLoadMethod]
    static void AutoMigrateOnLoad()
    {
        // 扫描所有 LevelData 资产
        var guids = AssetDatabase.FindAssets("t:LevelData");
        int migrated = 0, failed = 0;
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var ld = AssetDatabase.LoadAssetAtPath<LevelData>(path);
            if (ld == null || ld.SchemaVersion >= 2) continue;
            try
            {
                MigrateLevelData(ld);
                EditorUtility.SetDirty(ld);
                migrated++;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[WaveMigrator] 迁移 {path} 失败: {e}");
                failed++;
            }
        }
        if (migrated > 0 || failed > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"[WaveMigrator] 迁移完成: {migrated} 成功, {failed} 失败");
        }
    }
}
