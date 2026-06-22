using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class EditorPathMigrationTool
{
    [MenuItem("Tools/Level Editor/Migrate CheckPoint Prefabs to PathData")]
    public static void MigrateAll()
    {
        var guids = AssetDatabase.FindAssets("t:LevelData");
        int migrated = 0;
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var ld = AssetDatabase.LoadAssetAtPath<LevelData>(path);
            if (ld == null) continue;
            if (MigrateAsset(ld)) migrated++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[EditorPathMigrationTool] 迁移完成,共迁移 {migrated} 个 LevelData 资产。");
    }

    public static bool MigrateAsset(LevelData ld)
    {
#pragma warning disable CS0618 // [Obsolete] CheckPoints
        if (ld.Paths != null && ld.Paths.Length > 0) return false;
        if (ld.CheckPoints == null || ld.CheckPoints.Length == 0) return false;

        var newPaths = new List<PathData>(ld.CheckPoints.Length);
        var warnings = new List<string>();

        for (int k = 0; k < ld.CheckPoints.Length; k++)
        {
            var prefab = ld.CheckPoints[k];
            if (prefab == null)
            {
                warnings.Add($"Path {k}: prefab 为空");
                newPaths.Add(new PathData { CheckPoints = new Vector2[0], WaitTimes = new float[0] });
                continue;
            }

            int childCount = prefab.transform.childCount;
            var cps = new Vector2[childCount];
            var wts = new float[childCount];
            for (int c = 0; c < childCount; c++)
            {
                var child = prefab.transform.GetChild(c);
                cps[c] = child.position;
                if (!float.TryParse(child.name, out wts[c]))
                {
                    wts[c] = 0f;
                    warnings.Add($"Path {k} child #{c}: 无法解析 name '{child.name}' 为 float,使用 0");
                }
            }
            newPaths.Add(new PathData { CheckPoints = cps, WaitTimes = wts });
        }

        ld.Paths = newPaths.ToArray();
        ld.CheckPoints = new GameObject[0];
        EditorUtility.SetDirty(ld);

        if (warnings.Count > 0)
            Debug.LogWarning($"[EditorPathMigrationTool] {ld.name} 迁移完成,带 {warnings.Count} 个警告:\n - " +
                string.Join("\n - ", warnings));
        else
            Debug.Log($"[EditorPathMigrationTool] {ld.name} 迁移完成,共 {ld.Paths.Length} 条路径。");

        return true;
#pragma warning restore CS0618
    }
}
