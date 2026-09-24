using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 实体移动子系统（POCO）。
/// 持有：
///   1. 位置（LocalPosition = transform.position）
///   2. 格位（InBlocks 4 元素数组）
///   3. 阻挡列表（ResistList）
///   4. 朝向镜像（mirror of Entity._orientation；Task 2.2 视野子系统已持有，本系统不必再持）
///   5. 优先级（Priority = raw priority + 1000 * TauntLevel）
///
/// 设计要点：
///   - POCO，构造接受 Entity 引用以便访问 transform / Camp / Stats。
///   - SetPosition 包含 FindSelfInBlocks 副作用。
///   - Camp getter 镜像 Entity._camp；setter 因有 EntityManager 副作用留 Entity 根（不在本子系统内）。
///   - 阵营参与（participateIn）读取自 Stats.IsActive，不在本子系统内重新存储。
/// </summary>
public class EntityMovement
{
    private readonly Entity _entity;

    // === 数据 ===
    private (int i, int j)[] _inBlocks;
    private List<Entity> _resistList;
    private float _rawPriority;

    public EntityMovement(Entity entity)
    {
        _entity = entity;
    }

    // === 位置 ===
    public Vector2 LocalPosition => _entity.transform.position;
    /// <summary>
    /// 位置（get/set 一体，set 隐含 FindSelfInBlocks 刷新格位）。
    /// </summary>
    public Vector2 Position
    {
        get => _entity.transform.position;
        set => SetPosition(value);
    }
    /// <summary>
    /// 设置 transform.position 并刷新所在格位（FindSelfInBlocks）。
    /// </summary>
    public void SetPosition(Vector2 value)
    {
        _entity.transform.position = value;
        FindSelfInBlocks(value);
    }

    // === 格位 ===
    public (int i, int j)[] InBlocks => _inBlocks;

    // === 阻挡列表 ===
    public List<Entity> ResistList => _resistList;

    // === 稳定阻挡位置（明日方舟 F 点语义：吸附时锁定并覆写，作为同干员后续多敌排开基准） ===
    public Vector2 StableBlockPosition { get; private set; }
    public void SetStableBlockPosition(Vector2 pos)
    {
        StableBlockPosition = pos;
    }

    // === 阵营（只读镜像；setter 留 Entity 根） ===
    public int Camp => _entity.Camp;

    // === 优先级（getter 加 1000 * TauntLevel，setter 存 raw） ===
    public float Priority
    {
        get => _rawPriority + 1000 * _entity.Stats.TauntLevel;
        set => _rawPriority = value;
    }

    // === 参与标志（只读镜像 Stats.IsActive） ===
    public bool IsActive => _entity.Stats.IsActive;

    // === 初始化 ===
    public void Initialize()
    {
        _resistList = new List<Entity>();
        _inBlocks = new (int i, int j)[4];
    }

    // === 还池时清空格位记录（Dormancy 先经 RemoveEntityFromBlock 退出格位表），防止跨部署复用残留旧坐标误操作格位表 ===
    public void ClearInBlocks()
    {
        for (int i = 0; i < _inBlocks.Length; i++)
            _inBlocks[i] = (-1, -1);
    }

    // === 格位刷新 ===
    public void FindSelfInBlocks(Vector2 point)
    {
        // 静态实体（干员/召唤物）与可移动实体（敌人）碰撞半径不同，按组件互斥绑定取值
        float entityR = _entity.MoveBase != null ? EntityManager.MovableEntityR : EntityManager.StaticEntityR;
        EntityManager.Manager.RemoveEntityFromBlock(_inBlocks, _entity, Camp);
        (int i, int j) ij0 = ((int)(point.y + 0.5), (int)(point.x + 0.5));
        _inBlocks[0] = ij0;
        float k = 0.5f - entityR;
        if (Mathf.Abs(point.x - ij0.j) < k && Mathf.Abs(point.y - ij0.i) < k)
        {
            _inBlocks[1] = (-1, -1);
            _inBlocks[2] = (-1, -1);
            _inBlocks[3] = (-1, -1);
        }
        else
        {
            int dx = (point.x - ij0.j) > 0 ? 1 : -1;
            int dy = (point.y - ij0.i) > 0 ? 1 : -1;
            Vector2 pV = new Vector2(ij0.j + 0.5f * dx, ij0.i + 0.5f * dy);
            if (Vector2.Distance(pV, point) <= entityR)
            {
                _inBlocks[1] = (ij0.i + dy, ij0.j);
                _inBlocks[2] = (ij0.i, ij0.j + dx);
                _inBlocks[3] = (ij0.i + dy, ij0.j + dx);
            }
            else if (Mathf.Abs(point.x - ij0.j) >= k && Mathf.Abs(point.y - ij0.i) >= k)
            {
                _inBlocks[1] = (ij0.i + dy, ij0.j);
                _inBlocks[2] = (ij0.i, ij0.j + dx);
                _inBlocks[3] = (-1, -1);
            }
            else if (Mathf.Abs(point.x - ij0.j) >= k && Mathf.Abs(point.y - ij0.i) < k)
            {
                _inBlocks[1] = (ij0.i, ij0.j + dx);
                _inBlocks[2] = (-1, -1);
                _inBlocks[3] = (-1, -1);
            }
            else if (Mathf.Abs(point.x - ij0.j) < k && Mathf.Abs(point.y - ij0.i) >= k)
            {
                _inBlocks[1] = (ij0.i + dy, ij0.j);
                _inBlocks[2] = (-1, -1);
                _inBlocks[3] = (-1, -1);
            }
        }
        EntityManager.Manager.AddEntityToBlock(_inBlocks, _entity, Camp);
    }
}
