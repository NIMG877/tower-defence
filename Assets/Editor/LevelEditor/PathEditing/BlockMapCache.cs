using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 加载后的 BlockDataEntry 缓存。
/// 直接从 <see cref="LevelData.MapData"/> 构建,不再需要 LoadPrefabContents;
/// <see cref="Dispose"/> 保留为空操作,供 <c>LevelDataEditor.OnDisable</c> 调用。
/// </summary>
public sealed class BlockMapCache : IDisposable
{
    public BlockDataEntry[,] Blocks;
    public int ISize;
    public int JSize;
    public float EntityR;

    LevelData _levelDataRef;
    public LevelData LevelData => _levelDataRef;

    /// <summary>
    /// 从 <paramref name="levelData"/> 直接解析 BlockDataEntry 矩阵。
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

        cache.Blocks = new BlockDataEntry[cache.ISize, cache.JSize];
        foreach (var entry in levelData.MapData)
        {
            if (entry.i < 0 || entry.i >= cache.ISize) continue;
            if (entry.j < 0 || entry.j >= cache.JSize) continue;
            cache.Blocks[entry.i, entry.j] = entry;
        }
        return cache;
    }

    /// <summary>
    /// 收集缓存层面的诊断信息:超出网格范围 / (i,j) 重复 / MapPrefab 上残留旧 BlockData 组件。
    /// 见 spec §7。
    /// </summary>
    public List<string> GetWarnings()
    {
        var warnings = new List<string>();
        if (Blocks == null) return warnings;

        var seen = new HashSet<(int, int)>();
        var mapData = _levelDataRef != null ? _levelDataRef.MapData : null;
        if (mapData != null)
        {
            foreach (var entry in mapData)
            {
                if (entry.i < 0 || entry.i >= ISize || entry.j < 0 || entry.j >= JSize)
                {
                    warnings.Add($"entry (i={entry.i}, j={entry.j}) is out of range (grid is {ISize}×{JSize})");
                    continue;
                }
                if (!seen.Add((entry.i, entry.j)))
                {
                    warnings.Add($"duplicate entry at (i={entry.i}, j={entry.j})");
                }
            }
        }

        if (_levelDataRef != null && _levelDataRef.MapPrefab != null
            && HasLegacyBlockData(_levelDataRef.MapPrefab))
        {
            warnings.Add("MapPrefab still has legacy BlockData components — auto-clean on next save");
        }

        return warnings;
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