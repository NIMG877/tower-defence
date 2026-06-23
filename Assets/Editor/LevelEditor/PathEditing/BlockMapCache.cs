using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 加载后的 BlockData 缓存。<see cref="Dispose"/> 仅释放 BlockData 引用,prefab contents
/// 的生命周期由 <see cref="Load"/> 内部管理(在 Load 的 finally 中已 unload)。
/// </summary>
public sealed class BlockMapCache : IDisposable
{
    public BlockData[,] Blocks;
    public int ISize;
    public int JSize;
    public float EntityR;

    /// <summary>
    /// 加载 MapPrefab 解析 BlockData 矩阵。
    /// 接受 prefab asset 或 scene 中的 GameObject 实例。
    /// </summary>
    public static BlockMapCache Load(GameObject mapPrefab)
    {
        if (mapPrefab == null) throw new ArgumentNullException(nameof(mapPrefab));

        var cache = new BlockMapCache();
        GameObject root = null;
        bool isPrefabContents = false;

        // PrefabUtility.LoadPrefabContents 仅对 asset prefab 可用;对 scene 实例退化为直接遍历
        if (PrefabUtility.IsPartOfPrefabAsset(mapPrefab))
        {
            var path = AssetDatabase.GetAssetPath(mapPrefab);
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("MapPrefab 缺少有效的 asset 路径,无法 LoadPrefabContents");
            root = PrefabUtility.LoadPrefabContents(path);
            isPrefabContents = true;
        }
        else
        {
            root = mapPrefab;
        }

        try
        {
            if (root == null)
                throw new InvalidOperationException("MapPrefab 解析后 root 为空(prefab asset 加载失败?)");
            cache.ParseBlocks(root);
            cache.EntityR = EntityManager.EntityR;
        }
        finally
        {
            if (isPrefabContents && root != null) PrefabUtility.UnloadPrefabContents(root);
        }

        return cache;
    }

    void ParseBlocks(GameObject mapRoot)
    {
        if (mapRoot == null)
            throw new InvalidOperationException("ParseBlocks 收到 null root");
        var mapT = mapRoot.transform;
        if (mapT == null)
            throw new InvalidOperationException("MapPrefab 缺少 Transform 组件");

        int childCount = mapT.childCount;
        int maxI = 0, maxJ = 0;
        for (int k = 0; k < childCount; k++)
        {
            var t = mapT.GetChild(k);
            if (t == null) continue;
            if (maxI < t.position.y) maxI = (int)t.position.y;
            if (maxJ < t.position.x) maxJ = (int)t.position.x;
        }
        ISize = maxI + 1;
        JSize = maxJ + 1;
        Blocks = new BlockData[ISize, JSize];

        for (int k = 0; k < childCount; k++)
        {
            var t = mapT.GetChild(k);
            if (t == null) continue;
            if (t.TryGetComponent<BlockData>(out var bd))
            {
                Blocks[(int)t.position.y, (int)t.position.x] = bd;
            }
        }
    }

    public void Dispose()
    {
        Blocks = null;
    }
}