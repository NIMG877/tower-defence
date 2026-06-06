using System;
using System.Collections.Generic;

/// <summary>
/// 实体数据仓库：在 <see cref="EntityData"/> 数组上构建两个索引（精确查 + 切片查），提供 O(1) 查询。
/// 由 <see cref="GameDataService"/> 持有，调用方通过 <c>GameDataService.Instance.EntityRepository</c> 访问。
///
/// 设计要点：
/// - 数据形状（<see cref="EntityData"/>）与 SO（<see cref="EntityDataCollection"/>）分离后，Repository 是唯一的查询入口
/// - 懒构建：第一次访问 <c>EntityRepository</c> 时构建索引；之后 O(1) 查询
/// - 单次扫描同时建两个索引，构建复杂度 O(n)
/// </summary>
public class EntityDataRepository
{
    readonly EntityData[] _all;
    readonly Dictionary<string, (int left, int right)> _classRange;  // 闭区间 [left, right]
    readonly Dictionary<EntityID, int> _byId;                        // 精确查找

    /// <summary>从原始数组构建仓库。同时建立分类区间索引和精确 ID 索引。</summary>
    public static EntityDataRepository Build(EntityData[] source)
    {
        if (source == null) source = Array.Empty<EntityData>();
        var byId  = new Dictionary<EntityID, int>(source.Length);
        var range = new Dictionary<string, (int left, int right)>();
        for (int i = 0; i < source.Length; i++)
        {
            byId[source[i].ID] = i;
            if (range.TryGetValue(source[i].ID.ID_C, out var r))
                range[source[i].ID.ID_C] = (r.left, i);
            else
                range[source[i].ID.ID_C] = (i, i);
        }
        return new EntityDataRepository(source, range, byId);
    }

    private EntityDataRepository(
        EntityData[] all,
        Dictionary<string, (int left, int right)> classRange,
        Dictionary<EntityID, int> byId)
    {
        _all = all;
        _classRange = classRange;
        _byId = byId;
    }

    public int Count => _all.Length;

    /// <summary>按精确 ID 查询。找不到返回 <c>default(EntityData)</c>（所有字段为零值）。</summary>
    public EntityData Get(EntityID id)
        => _byId.TryGetValue(id, out var i) ? _all[i] : default;

    /// <summary>按分类查询（返回该分类下所有实体的副本数组）。找不到返回空数组。</summary>
    public EntityData[] GetByCategory(string idC)
    {
        if (string.IsNullOrEmpty(idC)) return Array.Empty<EntityData>();
        if (!_classRange.TryGetValue(idC, out var r)) return Array.Empty<EntityData>();
        var len = r.right - r.left + 1;
        var dst = new EntityData[len];
        Array.Copy(_all, r.left, dst, 0, len);
        return dst;
    }
}
