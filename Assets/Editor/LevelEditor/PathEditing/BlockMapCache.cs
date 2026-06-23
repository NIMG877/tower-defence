using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 加载后的 BlockData 缓存。
/// 对 prefab asset 用 LoadPrefabContents 复制到隐藏 hierarchy,必须由 Dispose() 释放;
/// cache 持有的 Blocks[,] 引用指向这些副本的 BlockData,Dispose 前应保证 root 还活着。
/// </summary>
public sealed class BlockMapCache : IDisposable
{
    public BlockData[,] Blocks;
    public int ISize;
    public int JSize;
    public float EntityR;

    GameObject _prefabContentsRoot; // null = scene 实例(不需要 dispose)
    bool _disposed;

    /// <summary>
    /// 加载 MapPrefab 解析 BlockData 矩阵。
    /// 接受 prefab asset 或 scene 中的 GameObject 实例。
    /// </summary>
    public static BlockMapCache Load(GameObject mapPrefab)
    {
        if (mapPrefab == null) throw new ArgumentNullException(nameof(mapPrefab));

        var cache = new BlockMapCache();
        GameObject root;

        // PrefabUtility.LoadPrefabContents 仅对 asset prefab 可用;对 scene 实例直接用
        if (PrefabUtility.IsPartOfPrefabAsset(mapPrefab))
        {
            var path = AssetDatabase.GetAssetPath(mapPrefab);
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("MapPrefab 缺少有效的 asset 路径,无法 LoadPrefabContents");
            root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
                throw new InvalidOperationException("MapPrefab 解析后 root 为空(prefab asset 加载失败?)");
            cache._prefabContentsRoot = root; // 持有 root 引用,防止 BlockData 被销毁
        }
        else
        {
            root = mapPrefab;
        }

        cache.ParseBlocks(root);
        cache.EntityR = EntityManager.EntityR;
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
        if (_disposed) return;
        _disposed = true;
        Blocks = null;
        if (_prefabContentsRoot != null)
        {
            PrefabUtility.UnloadPrefabContents(_prefabContentsRoot);
            _prefabContentsRoot = null;
        }
    }
}
