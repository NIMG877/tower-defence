using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 加载后的 Tile 缓存。
/// 直接从 <see cref="LevelData.MapData"/> 构建,不再需要 LoadPrefabContents;
/// <see cref="Dispose"/> 保留为空操作,供 <c>LevelDataEditor.OnDisable</c> 调用。
/// </summary>
public sealed class BlockMapCache : IDisposable
{
    public Tile[,] Blocks;
    public bool[,] HasEntry;
    public int ISize;
    public int JSize;
    public float EntityR;

    LevelData _levelDataRef;
    public LevelData LevelData => _levelDataRef;

    /// <summary>
    /// 从 <paramref name="levelData"/> 直接解析 Tile 矩阵。
    /// </summary>
    public static BlockMapCache Load(LevelData levelData)
    {
        var cache = new BlockMapCache();
        cache._levelDataRef = levelData;
        if (levelData == null) return cache;

        cache.ISize = levelData.iSize;
        cache.JSize = levelData.jSize;
        cache.EntityR = EntityManager.EntityR;
        if (cache.ISize <= 0 || cache.JSize <= 0) return cache;

        cache.Blocks = new Tile[cache.ISize, cache.JSize];
        cache.HasEntry = new bool[cache.ISize, cache.JSize];
        foreach (var entry in levelData.MapData)
        {
            if (entry.i < 0 || entry.i >= cache.ISize) continue;
            if (entry.j < 0 || entry.j >= cache.JSize) continue;
            cache.Blocks[entry.i, entry.j] = entry;
            cache.HasEntry[entry.i, entry.j] = true;
        }
        return cache;
    }

    /// <summary>
    /// 重新从 <see cref="LevelData.MapData"/> 解析缓存。
    /// 用于外部修改了 MapData(如 Clean)之后让 Blocks/HasEntry 与 SO 重新对齐。
    /// </summary>
    public void Reload()
    {
        if (_levelDataRef == null) return;
        var fresh = Load(_levelDataRef);
        Blocks = fresh.Blocks;
        HasEntry = fresh.HasEntry;
        ISize = fresh.ISize;
        JSize = fresh.JSize;
        EntityR = fresh.EntityR;
    }

    public enum WarningKind { OutOfRange, Duplicate, LegacyPrefab }

    /// <summary>
    /// 一条诊断警告。I/J 仅对 OutOfRange / Duplicate 有意义。
    /// </summary>
    public readonly struct CacheWarning
    {
        public readonly WarningKind Kind;
        public readonly int I, J;
        public readonly string Message;
        public CacheWarning(WarningKind kind, int i, int j, string message)
        {
            Kind = kind; I = i; J = j; Message = message;
        }
        public CacheWarning(WarningKind kind, string message)
        {
            Kind = kind; I = -1; J = -1; Message = message;
        }
    }

    /// <summary>
    /// 收集缓存层面的诊断信息:超出网格范围 / (i,j) 重复 / MapPrefab 上残留旧 BlockData 组件。
    /// 见 spec §7。
    /// </summary>
    public List<CacheWarning> GetWarnings()
    {
        var warnings = new List<CacheWarning>();
        if (Blocks == null) return warnings;

        var seen = new HashSet<(int, int)>();
        var mapData = _levelDataRef != null ? _levelDataRef.MapData : null;
        if (mapData != null)
        {
            foreach (var entry in mapData)
            {
                if (entry.i < 0 || entry.i >= ISize || entry.j < 0 || entry.j >= JSize)
                {
                    warnings.Add(new CacheWarning(
                        WarningKind.OutOfRange, entry.i, entry.j,
                        $"entry (i={entry.i}, j={entry.j}) is out of range (grid is {ISize}×{JSize})"));
                    continue;
                }
                if (!seen.Add((entry.i, entry.j)))
                {
                    warnings.Add(new CacheWarning(
                        WarningKind.Duplicate, entry.i, entry.j,
                        $"duplicate entry at (i={entry.i}, j={entry.j})"));
                }
            }
        }

        if (_levelDataRef != null && _levelDataRef.MapPrefab != null
            && HasLegacyBlockData(_levelDataRef.MapPrefab))
        {
            warnings.Add(new CacheWarning(
                WarningKind.LegacyPrefab,
                "MapPrefab still has legacy BlockData components — auto-clean on next save"));
        }

        return warnings;
    }

    /// <summary>
    /// 删除所有 (i, j) 超出当前 iSize × jSize 的 MapData 条目。
    /// 通过 SerializedProperty + Undo,完全可撤销。
    /// 返回被删除的条数(0 表示无可清理)。
    /// </summary>
    public static int CleanOutOfRangeEntries(SerializedObject so, BlockMapCache cache)
    {
        if (so == null || cache == null || cache.Blocks == null) return 0;
        Undo.RecordObject(so.targetObject, "Clean Out-of-Range MapData");
        var mapData = so.FindProperty("MapData");
        if (mapData == null) return 0;

        int removed = 0;
        // 反向遍历,删除时 index 不会前移
        for (int k = mapData.arraySize - 1; k >= 0; k--)
        {
            var e = mapData.GetArrayElementAtIndex(k);
            int i = e.FindPropertyRelative("i").intValue;
            int j = e.FindPropertyRelative("j").intValue;
            if (i < 0 || i >= cache.ISize || j < 0 || j >= cache.JSize)
            {
                mapData.DeleteArrayElementAtIndex(k);
                removed++;
            }
        }
        if (removed > 0) so.ApplyModifiedProperties();
        return removed;
    }

    static bool HasLegacyBlockData(GameObject prefab)
    {
        if (prefab == null) return false;
        foreach (var mb in prefab.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue; // missing-script entry
            if (mb.GetType().Name == "BlockData") return true;
        }
        return false;
    }

    public void Dispose() { }
}
