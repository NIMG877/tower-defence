using System;

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

    /// <summary>
    /// 从 <paramref name="levelData"/> 直接解析 BlockDataEntry 矩阵。
    /// </summary>
    public static BlockMapCache Load(LevelData levelData)
    {
        var cache = new BlockMapCache();
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

    public void Dispose() { }
}