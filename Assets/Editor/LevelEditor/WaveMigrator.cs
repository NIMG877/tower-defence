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

        for (int w = 0; w < ld.Waves.Length; w++)
        {
            var wave = ld.Waves[w];
            var oldActions = wave.LegacyActions;
            if (oldActions == null)
            {
                // 已是新结构但 SchemaVersion 未更新(用户中途保存过),跳过 Actions 转换
                continue;
            }

            var newActions = new LevelActions.Action[oldActions.Length];
            float cumulative = 0f;
            for (int i = 0; i < oldActions.Length; i++)
            {
                var a = oldActions[i];
                if (i == 0) cumulative = 0f;
                else cumulative += Mathf.Max(0f, a.GapFromLastAction);
                a.TriggerTime = cumulative;
                newActions[i] = a;
            }

            wave.Tracks = new[]
            {
                new LevelActions.Track
                {
                    Name = "默认",
                    TrackColor = DefaultTrackColor(0),
                    Locked = false,
                    Actions = newActions,
                }
            };
            wave.LegacyActions = null;
            ld.Waves[w] = wave;
        }

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
