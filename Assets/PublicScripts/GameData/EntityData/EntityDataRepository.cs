using System;
using System.Collections.Generic;

/// <summary>
/// 实体数据仓库：在 <see cref="EntityData"/> 集合上构建两个索引（精确查 + 分类查），提供 O(1) 查询。
/// 由 <see cref="GameDataService"/> 持有，调用方通过 <c>GameDataService.EntityRepository</c> 访问。
///
/// 设计要点：
/// - 数据形状（<see cref="EntityData"/>）与 SO（<see cref="EntityDataCollection"/>）分离后，Repository 是唯一的查询入口
/// - 懒构建：第一次访问 <c>EntityRepository</c> 时构建索引；之后 O(1) 查询
/// - 单次扫描同时建两个索引，构建复杂度 O(n)
/// - 分类索引不假设 DTO 排序：按 DTO 原序建 <c>List&lt;EntityData&gt;</c>，对上游导入工具的输出顺序无要求
/// </summary>
public class EntityDataRepository
{
    readonly Dictionary<EntityID, EntityData> _byId;
    readonly Dictionary<string, List<EntityData>> _byCategory;

    /// <summary>从原始数组构建仓库。同时建立分类索引和精确 ID 索引。</summary>
    public static EntityDataRepository Build(IReadOnlyList<EntityData> source)
    {
        var byId = new Dictionary<EntityID, EntityData>();
        var byCategory = new Dictionary<string, List<EntityData>>();
        if (source != null)
        {
            for (int i = 0; i < source.Count; i++)
            {
                var d = source[i];
                byId[d.ID] = d;
                if (!byCategory.TryGetValue(d.ID.ID_C, out var list))
                {
                    list = new List<EntityData>();
                    byCategory[d.ID.ID_C] = list;
                }
                list.Add(d);
            }
        }
        return new EntityDataRepository(byId, byCategory);
    }

    private EntityDataRepository(
        Dictionary<EntityID, EntityData> byId,
        Dictionary<string, List<EntityData>> byCategory)
    {
        _byId = byId;
        _byCategory = byCategory;
    }

    /// <summary>实体总数（精确 ID 索引的 size）。</summary>
    public int Count => _byId.Count;

    /// <summary>按精确 ID 查询。找不到返回 <c>null</c>（EntityData 为引用类型）。</summary>
    public EntityData Get(EntityID id)
        => _byId.TryGetValue(id, out var d) ? d : default;

    /// <summary>按分类查询（只读列表，避免外部 mutation 索引）。找不到返回空数组。</summary>
    public IReadOnlyList<EntityData> GetByCategory(string idC)
    {
        if (string.IsNullOrEmpty(idC)) return Array.Empty<EntityData>();
        return _byCategory.TryGetValue(idC, out var list) ? list : Array.Empty<EntityData>();
    }
}
