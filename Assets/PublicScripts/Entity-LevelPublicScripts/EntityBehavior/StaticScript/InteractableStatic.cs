using MyUI;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.EventSystems.EventTrigger;

public class InteractableStatic : MonoBehaviour, IPoolOperation
{
    private Entity _thisEntity;
    protected int _direction;
    private int _buildOrder;
    private int _blockOccupationNum;
    private int _currentSetCost;
    public int BuilderOrder { set { _buildOrder = value; CountPriority(); } }
    public int CurrentSetCost { set { _currentSetCost = value; } get { return _currentSetCost; } }
    public void CountPriority()
    {
        _thisEntity.Priority = _buildOrder;
    }
    public Vector2 TryAddToEntityResistList(Entity movableEntity)
    {
        if (_blockOccupationNum + movableEntity.BlockOccupation > _thisEntity.BlockOccupation)
        {
            return 100 * Vector2.left;
        }
        else
        {
            Vector2 thisP = _thisEntity.EntityPosition;
            Vector2 moveP = movableEntity.EntityPosition;
            Vector2 AC;
            float blockMinD = 0.495f;
            if (Vector2.Distance(moveP, thisP) - EntityManager.EntityR >= blockMinD)
            {
                AC = moveP - thisP;
            }
            else
            {
                AC = (moveP - thisP).normalized * (blockMinD + EntityManager.EntityR);
            }
            bool xFree = true, yFree = true;
            if (AC.x > 0)
            {
                BlockData bD = MapDataManager.Manager.GetPosBlock((int)thisP.y, (int)thisP.x + 1);
                if (bD == null || bD.PassableType > 0)
                {
                    xFree = false;
                }
            }
            else if (AC.x < 0)
            {
                BlockData bD = MapDataManager.Manager.GetPosBlock((int)thisP.y, (int)thisP.x - 1);
                if (bD == null || bD.PassableType > 0)
                {
                    xFree = false;
                }
            }
            if (AC.y > 0)
            {
                BlockData bD = MapDataManager.Manager.GetPosBlock((int)thisP.y + 1, (int)thisP.x);
                if (bD == null || bD.PassableType > 0)
                {
                    yFree = false;
                }
            }
            else if (AC.y < 0)
            {
                BlockData bD = MapDataManager.Manager.GetPosBlock((int)thisP.y - 1, (int)thisP.x);
                if (bD == null || bD.PassableType > 0)
                {
                    yFree = false;
                }
            }
            if (_thisEntity.entityResistList.Count > 0)
            {
                Vector2 DiC;
                Vector2 correctVector = new Vector2();
                bool needCorrect = false;
                for (int i = 0; i < _thisEntity.entityResistList.Count; i++)
                {
                    DiC = _thisEntity.entityResistList[i].transform.position;
                    DiC = moveP - DiC;
                    if (DiC.magnitude < 0.1)
                    {
                        Vector2 ADi = _thisEntity.entityResistList[i].transform.position;
                        ADi -= thisP;
                        correctVector += new Vector2(ADi.y, -ADi.x).normalized;
                        needCorrect = true;
                    }
                    else if (DiC.magnitude < 0.4)
                    {
                        correctVector += (0.4f - DiC.magnitude) * 50 * DiC.normalized;
                    }
                }
                if (needCorrect)
                {
                    AC = AC.magnitude * (0.2f * correctVector.normalized + AC).normalized;
                }
            }
            _thisEntity.entityResistList.Add(movableEntity);
            ++_blockOccupationNum;
            if (xFree && yFree)
            {
                return thisP + AC;
            }
            else if (xFree && !yFree)
            {
                float k = (blockMinD - EntityManager.EntityR) / Math.Abs(AC.y);
                return thisP + Math.Min(k, 1) * AC;
            }
            else if (!xFree && yFree)
            {
                float k = (blockMinD - EntityManager.EntityR) / Math.Abs(AC.x);
                return thisP + Math.Min(k, 1) * AC;
            }
            else
            {
                float k = Math.Min((blockMinD - EntityManager.EntityR) / Math.Abs(AC.y), (blockMinD - EntityManager.EntityR) / Math.Abs(AC.x));
                return thisP + Math.Min(k, 1) * AC;
            }
        }
    }

    private void FixedUpdate()
    {
        if (_thisEntity.participateIn)
        {
            UpdateBlockEntityListAndBlockOccupationNum();
        }
    }
    private void UpdateBlockEntityListAndBlockOccupationNum()
    {
        _blockOccupationNum = 0;
        for (int i = _thisEntity.entityResistList.Count - 1; i >= 0; i--)
        {
            if (_thisEntity.entityResistList[i].participateIn && Vector2.Distance(_thisEntity.EntityPosition, _thisEntity.entityResistList[i].EntityPosition) <= 0.5 + EntityManager.EntityR)
            {
                if (_blockOccupationNum + _thisEntity.entityResistList[i].BlockOccupation <= _thisEntity.BlockOccupation)
                {
                    _blockOccupationNum += _thisEntity.entityResistList[i].BlockOccupation;
                }
                else
                {
                    _thisEntity.entityResistList[i].MoveBase.RelieveBlock(_thisEntity);
                    _thisEntity.entityResistList.RemoveAt(i);
                }
            }
            else
            {
                _thisEntity.entityResistList[i].MoveBase.RelieveBlock(_thisEntity);
                _thisEntity.entityResistList.RemoveAt(i);
            }
        }
    }

    public void PreWarm()
    {
        _thisEntity = this.GetComponent<Entity>();
    }
    public void Initialize()
    {
        LevelRescurceManager.Manager.CanSetNumLeft -= _thisEntity.EntityData.MaxOccupyCount;
    }
    public void Dormancy()
    {
        EntityManager.Manager.RemoveEntityFromStaticList(_thisEntity);
        LevelMessagePanel.Panel.EntityBackToSelector(_thisEntity);
        LevelRescurceManager.Manager.CanSetNumLeft += _thisEntity.EntityData.MaxOccupyCount;
    }
}
