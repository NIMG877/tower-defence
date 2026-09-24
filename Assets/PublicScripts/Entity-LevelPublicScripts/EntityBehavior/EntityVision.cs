using System.Collections.Generic;

/// <summary>
/// 实体视野子系统（POCO）。
/// 持有：
///   1. 基础视野（BaseRange/BaseRadius，来自 EntityData）
///   2. 计算视野（Range：BaseRange 经 MapDataManager.RangeCaculator 按实体位置+朝向偏移后的结果）
///   3. 当前半径（Radius）
///   4. 朝向（mirror of Entity._orientation，Range setter 与 SetOrientation 同步用）
///   5. 视野内实体列表（NearbyMonsters/NearbyTurrets）
///
/// 设计要点：
///   - POCO，构造接受 Entity 引用以便访问 transform.position / Camp。
///   - Range setter 有副作用（调 MapDataManager）。
///   - Refresh() 每帧经 EntitySelector_Range/Radius 重算视野内实体列表。
///   - SetOrientation 同时更新镜像朝向并重算 Range = BaseRange。
/// </summary>
public class EntityVision
{
    private readonly Entity _entity;

    // 基础（来自 EntityData）
    private (int x, int y)[] _baseRange;
    private float _baseRadius;

    // 计算（经 MapDataManager 偏移 + 当帧选择结果）
    private (int x, int y)[] _range;
    private float _radius;

    // 朝向镜像（由 Entity.SetOrientation 同步；Range setter 读取）
    private int _orientation;

    // 视野内实体列表
    private List<Entity> _monstersInRange;
    private List<Entity> _turretsInRange;

    public EntityVision(Entity entity)
    {
        _entity = entity;
    }

    // === 基础读 ===
    public (int x, int y)[] BaseRange => _baseRange;
    public float BaseRadius => _baseRadius;

    // === 计算读 + 写 ===
    /// <summary>
    /// 经 MapDataManager.RangeCaculator 按实体位置 + 朝向偏移后的视野格子集。
    /// 写入时立即重算。
    /// </summary>
    public (int x, int y)[] Range
    {
        get => _range;
        set => _range = MapDataManager.Manager.RangeCaculator(
            value,
            ((int)(_entity.Movement.Position.x + 0.5), (int)(_entity.Movement.Position.y + 0.5)),
            _orientation);
    }
    public float Radius => _radius;

    // === 视野内实体 ===
    public List<Entity> NearbyMonsters => _monstersInRange;
    public List<Entity> NearbyTurrets => _turretsInRange;

    /// <summary>
    /// 从 EntityData 装填基础视野。
    /// </summary>
    public void InitializeFromData(EntityData data)
    {
        _baseRange = new (int x, int y)[data.VisionRange.Count];
        for (int i = 0; i < _baseRange.Length; i++)
        {
            _baseRange[i] = (data.VisionRange[i].x, data.VisionRange[i].y);
        }
        _baseRadius = data.VisionRadius;
        // Radius 初始化为 base 的拷贝；Range 启动为 null（惰性初始化：Refresh 在其为 null 期间
        // 走 Radius 分支，直到 setter 被写入——SetOrientation/攻击距离覆盖等——才经 RangeCaculator 计算）
        _range = null;
        _radius = _baseRadius;
        _monstersInRange = new List<Entity>();
        _turretsInRange = new List<Entity>();
    }

    /// <summary>
    /// 更新朝向并重算 Range = BaseRange。
    /// </summary>
    public void SetOrientation(int orientation)
    {
        _orientation = orientation;
        Range = _baseRange;
    }

    /// <summary>
    /// 每帧重算视野内实体列表。
    /// </summary>
    public void Refresh()
    {
        if (Range != null)
        {
            _monstersInRange = EntityManager.Manager.EntitySelector_Range(Range, _entity.Camp, _entity.Camp == 2, 1, false);
            _turretsInRange = EntityManager.Manager.EntitySelector_Range(Range, _entity.Camp, _entity.Camp == 1, 1, false);
        }
        else
        {
            _monstersInRange = EntityManager.Manager.EntitySelector_Radius((_entity.transform.position.x, _entity.transform.position.y), _entity.Camp, _entity.Camp == 2, _radius, false);
            _turretsInRange = EntityManager.Manager.EntitySelector_Radius((_entity.transform.position.x, _entity.transform.position.y), _entity.Camp, _entity.Camp == 1, _radius, false);
        }
    }

    /// <summary>
    /// 池释放时清空列表（外部仍在引用旧 List 的情况下保留 List 实例可减少 GC churn，
    /// 但 Dormancy 周期内外部已无人持有，Clear() 即可）。
    /// </summary>
    public void ClearLists()
    {
        _monstersInRange?.Clear();
        _turretsInRange?.Clear();
    }
}
