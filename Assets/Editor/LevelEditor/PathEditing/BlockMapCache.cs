using System;
using UnityEditor;
using UnityEngine;

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
            root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(mapPrefab));
            isPrefabContents = true;
        }
        else
        {
            root = mapPrefab;
        }

        try
        {
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
        int childCount = mapRoot.transform.childCount;
        int maxI = 0, maxJ = 0;
        for (int k = 0; k < childCount; k++)
        {
            var t = mapRoot.transform.GetChild(k);
            if (maxI < t.position.y) maxI = (int)t.position.y;
            if (maxJ < t.position.x) maxJ = (int)t.position.x;
        }
        ISize = maxI + 1;
        JSize = maxJ + 1;
        Blocks = new BlockData[ISize, JSize];

        for (int k = 0; k < childCount; k++)
        {
            var t = mapRoot.transform.GetChild(k);
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