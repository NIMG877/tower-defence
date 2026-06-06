// 启动时校验静态数据完整性。
// 触发时机：Editor 启动 / Domain Reload / Asset 重新导入。
// 行为：只读 + Debug.LogWarning，不修改任何资产（避免循环）。

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class StaticDataValidator
{
    const string EntityCollectionPath = "Assets/Resources/GameDatas/EntityDataCollection.asset";

    static StaticDataValidator()
    {
        // 延迟到 Editor 完全启动后再跑（避免 domain reload 中段触发）
        EditorApplication.delayCall += ValidateOnce;
    }

    static void ValidateOnce()
    {
        var so = AssetDatabase.LoadAssetAtPath<EntityDataCollection>(EntityCollectionPath);
        if (so == null)
        {
            Debug.LogWarning($"[StaticDataValidator] {EntityCollectionPath} not found.");
            return;
        }

        var data = so.EntityBasicDatas;
        if (data == null || data.Length == 0)
        {
            Debug.LogWarning($"[StaticDataValidator] {EntityCollectionPath} is empty.");
            return;
        }

        var issues = new List<string>();
        var seenIds = new HashSet<string>();
        int nullPrefabCount = 0;

        for (int i = 0; i < data.Length; i++)
        {
            var d = data[i];

            // 1. ID_C 必填
            if (string.IsNullOrEmpty(d.ID.ID_C))
                issues.Add($"index {i}: ID.ID_C is empty");

            // 2. ID 唯一
            var key = $"{d.ID.ID_C}-{d.ID.ID_N}";
            if (!seenIds.Add(key))
                issues.Add($"index {i}: duplicate ID {key}");

            // 3. prefab 引用（角色/怪物必须有 prefab）
            if (d.Prefab == null)
                nullPrefabCount++;
        }

        // 4. 召唤引用必须存在
        var allIds = data.Select(d => $"{d.ID.ID_C}-{d.ID.ID_N}").ToHashSet();
        for (int i = 0; i < data.Length; i++)
        {
            var d = data[i];
            if (d.CanSpawnEntityIds == null) continue;
            foreach (var spawnId in d.CanSpawnEntityIds)
            {
                if (string.IsNullOrEmpty(spawnId.ID_C)) continue;
                var spawnKey = $"{spawnId.ID_C}-{spawnId.ID_N}";
                if (!allIds.Contains(spawnKey))
                    issues.Add($"index {i} ({d.ID.ID_C}-{d.ID.ID_N}): canSpawnEntityID_L references missing {spawnKey}");
            }
        }

        if (issues.Count == 0 && nullPrefabCount == 0)
        {
            Debug.Log($"[StaticDataValidator] EntityDataCollection.asset OK, {data.Length} entries.");
        }
        else
        {
            var msg = $"[StaticDataValidator] {EntityCollectionPath} has {issues.Count} issue(s), {nullPrefabCount} null prefab(s):\n  - " +
                      string.Join("\n  - ", issues.Take(20));
            if (issues.Count > 20) msg += $"\n  ... ({issues.Count - 20} more)";
            if (nullPrefabCount > 0) msg += $"\n  (and {nullPrefabCount} entries with null prefab)";
            msg += "\n  → 跑 Tools > Static Data > Rebuild Entity Collection 修复";
            Debug.LogWarning(msg);
        }
    }
}
