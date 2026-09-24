using MyUI;
using System.Collections.Generic;
using UnityEngine;

public class InteractableStatic : MonoBehaviour, IPoolOperation
{
    /// <summary>地面阻挡半径：√2/2，边长 1 格正方形对角线的一半（对齐明日方舟0.70709997）。</summary>
    public const float BlockRadius =  0.70709997f;
    /// <summary>地面阻挡半径平方（对齐明日方舟0.49999037，判定免开方）。</summary>
    public const float BlockRadiusSqr =  BlockRadius*BlockRadius;
    /// <summary>稳定阻挡位置到干员中心的距离：干员格边缘（对齐明日方舟阻挡偏移落点 0.5）。</summary>
    private const float BlockOffsetDistance = 0.5f;
    /// <summary>多敌排开（明日方舟修正向量）：DnC 不足旋转阈值贡献旋转分量、不足挤压阈值贡献挤压分量，≥挤压阈值不贡献。</summary>
    private const float BlockRotateThreshold = 0.1f;
    private const float BlockRepelThreshold = 0.4f;
    /// <summary>修正向量累积后长度设为该值，与 AC 相加、结果长度收回 |AC|。</summary>
    private const float BlockCorrectionWeight = 0.2f;

    private Entity _thisEntity;
    private int _buildOrder;
    private int _blockOccupationNum;
    private int _currentSetCost;
    public int BuilderOrder { set { _buildOrder = value; CountPriority(); } }
    public int CurrentSetCost { set { _currentSetCost = value; } get { return _currentSetCost; } }
    public void CountPriority()
    {
        _thisEntity.Movement.Priority = _buildOrder;
    }

    private void FixedUpdate()
    {
        if (_thisEntity.Stats.IsActive)
        {
            UpdateBlockEntityListAndBlockOccupationNum();
            TryBlockEntitiesAround();
        }
    }

    /// <summary>
    /// 吸附（明日方舟"优先阻挡离自身中心最近的敌人"）：按到干员中心的距离升序逐个注册，
    /// 直到剩余容量放不下为止。高台层级约束：高台格上的干员不吸附地面敌人
    /// （空中阻挡模式待飞行单位实装再议）。容量 0 的阻挡者（如"凋零"王座）直接不吸附。
    /// </summary>
    private void TryBlockEntitiesAround()
    {
        if (_thisEntity.Stats.BlockOccupationS <= 0)
        {
            return;
        }
        Vector2 thisP = _thisEntity.Movement.Position;
        if (MapDataManager.Manager.GetPosBlock((int)thisP.y, (int)thisP.x).highland)
        {
            return;
        }
        List<Entity> candidates = EntityManager.Manager.EntitySelector_Radius(
            (thisP.x, thisP.y), _thisEntity.Movement.Camp, false, BlockRadius, true);
        candidates.Sort((a, b) =>
        {
            float da = (a.Movement.Position - thisP).sqrMagnitude;
            float db = (b.Movement.Position - thisP).sqrMagnitude;
            return da.CompareTo(db);
        });
        for (int i = 0; i < candidates.Count; i++)
        {
            Entity candidate = candidates[i];
            if (candidate.MoveBase == null
                || candidate.Movement.ResistList.Count > 0
                || candidate.buffController.FetchAbnormalState(1))
            {
                continue;   // 不可移动/已被阻挡/失衡滑行中
            }
            if (_blockOccupationNum + candidate.Stats.BlockOccupationS > _thisEntity.Stats.BlockOccupationS)
            {
                continue;   // 剩余容量放不下该敌，按距离顺序尝试下一名候选
            }
            BlockEntity(candidate);
        }
    }

    private void BlockEntity(Entity movableEntity)
    {
        Vector2 targetPos = CalculateBlockPosition(movableEntity.Movement.Position);
        _thisEntity.Movement.ResistList.Add(movableEntity);
        _blockOccupationNum += movableEntity.Stats.BlockOccupationS;
        movableEntity.Movement.ResistList.Add(_thisEntity);
        movableEntity.Movement.SetStableBlockPosition(targetPos);
        movableEntity.MoveBase.BeginBlockOffset(targetPos);
        if (movableEntity.StateMachine.CurrentState == EntityState.Move)
        {
            movableEntity.StateMachine.TrySetState(EntityState.Idle, true);
        }
    }

    /// <summary>
    /// 计算稳定阻挡位置（明日方舟阻挡偏移，森空岛 50162 原文算法）：
    /// 1. 前提条件：敌我中点不重合（距离&gt;0.00001），重合不偏移；
    /// 2. 长度延长：AB 不足 0.5 沿方向延长到 0.5（干员格边缘），否则保持原距离，得到向量 AC；
    /// 3. 修正门：C 点 0.1 范围内存在已被本干员阻挡的敌人时，遍历全部已挡敌人（取稳定阻挡位置 F）累积修正向量——
    ///    DnC&lt;0.1：ADn 绕干员顺时针旋转 90° 后取单位向量；0.1≤DnC&lt;0.4：(0.4-DnC)×50×DnC 方向；≥0.4 不贡献；
    /// 4. 修正向量长度改为 0.2 与 AC 相加，结果长度收回 |AC| 得 AF；
    /// 5. F 即敌人本次的稳定阻挡位置，0.2s 内强制移动过去（BeginBlockOffset）。
    /// </summary>
    private Vector2 CalculateBlockPosition(Vector2 moveP)
    {
        Vector2 thisP = _thisEntity.Movement.Position;
        Vector2 AC = moveP - thisP;
        if (AC.sqrMagnitude < 1e-10f)
        {
            return moveP;
        }
        if (AC.magnitude < BlockOffsetDistance)
        {
            AC = AC.normalized * BlockOffsetDistance;
        }
        Vector2 pointC = thisP + AC;
        List<Entity> resistList = _thisEntity.Movement.ResistList;
        bool hasNearby = false;
        for (int i = 0; i < resistList.Count; i++)
        {
            if ((pointC - resistList[i].Movement.StableBlockPosition).sqrMagnitude < BlockRotateThreshold * BlockRotateThreshold)
            {
                hasNearby = true;
                break;
            }
        }
        if (hasNearby)
        {
            Vector2 correctVector = new Vector2();
            for (int i = 0; i < resistList.Count; i++)
            {
                Vector2 stable = resistList[i].Movement.StableBlockPosition;
                Vector2 DnC = pointC - stable;
                float distance = DnC.magnitude;
                if (distance < BlockRotateThreshold)
                {
                    Vector2 ADn = stable - thisP;
                    correctVector += new Vector2(ADn.y, -ADn.x).normalized;
                }
                else if (distance < BlockRepelThreshold)
                {
                    correctVector += (BlockRepelThreshold - distance) * 50 * DnC.normalized;
                }
            }
            AC = AC.magnitude * (BlockCorrectionWeight * correctVector.normalized + AC).normalized;
        }
        return thisP + AC;
    }

    private void UpdateBlockEntityListAndBlockOccupationNum()
    {
        _blockOccupationNum = 0;
        for (int i = 0; i < _thisEntity.Movement.ResistList.Count; )
        {
            Entity blocked = _thisEntity.Movement.ResistList[i];
            if (blocked.Stats.IsActive
                && (blocked.Movement.Position - _thisEntity.Movement.Position).sqrMagnitude <= BlockRadiusSqr
                && _blockOccupationNum + blocked.Stats.BlockOccupationS <= _thisEntity.Stats.BlockOccupationS)
            {
                _blockOccupationNum += blocked.Stats.BlockOccupationS;
                i++;
            }
            else
            {
                // 失效/移出阻挡圈/容量不足时解除；循环自表头遍历且 ResistList 按吸附先后排列，
                // 容量缩减时先解除的是最早吸附的（保留最晚）
                blocked.MoveBase.RelieveBlock(_thisEntity);
                _thisEntity.Movement.ResistList.RemoveAt(i);
            }
        }
    }

    public void PreWarm()
    {
        _thisEntity = this.GetComponent<Entity>();
    }
    public void Initialize()
    {
        LevelResourceManager.Manager.CanSetNumLeft -= _thisEntity.EntityData.MaxOccupyCount;
    }
    public void Dormancy()
    {
        // 被挡敌人的解除通知由 Entity.Dormancy 在清空 ResistList 前统一派发（见其注释）
        EntityManager.Manager.RemoveEntityFromStaticList(_thisEntity);
        LevelMessagePanel.Panel.EntityBackToSelector(_thisEntity);
        LevelResourceManager.Manager.CanSetNumLeft += _thisEntity.EntityData.MaxOccupyCount;
    }
}
