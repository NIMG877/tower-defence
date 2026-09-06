using Cysharp.Threading.Tasks;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class MoveBase : MonoBehaviour, IPoolOperation
{
    protected Entity _thisEntity;
    protected AnimationMachine _thisAM;
    protected MoveParameters[] _currentSection;
    protected Vector2 _unBalancedMoveSpeed;
    protected int _currentPathSerial;
    protected int _currentSectionSerial;
    protected int _currentPointSerial;
    protected int _moveMethod;
    protected int _massLevel;
    protected float _forceUnmoveTime;
    protected int _levelHpComsume;
    private float MIU_G = 2.45f;
    private bool _tempTargetExist;
    private float _tempWaitTime;
    private Action _reachTempTarget;
    public delegate void OnReachSectionEnd();
    public OnReachSectionEnd OperationsOnReachSectionEnd;
    /// <summary>移动速度统一出口：属性值 × 停顿(type 4)减速因子，寻路移动消费此处。</summary>
    public float MoveSpeedS { get { return _thisEntity.Stats.MoveSpeedS * _thisEntity.buffController.GetMoveSpeedFactor(); } }
    public int CurrentPathSerial { get { return _currentPathSerial; } }
    public int CurrentSectionSerial { get { return _currentSectionSerial; } }
    public int CurrentPointSerial { get { return _currentPointSerial; } }
    public int MoveMethod { get { return _moveMethod; } }
    public int LevelHpConsume { set { _levelHpComsume = value; } get { return _levelHpComsume; } }

    public MoveParameters[] CurrentSection { get { return _currentSection; } }
    protected void FixedUpdate()
    {
        if (!_thisEntity.Stats.IsActive)
            return;
        if (_thisEntity.Movement.ResistList.Count == 0)
        {
            FindEntitiesAroundAndTryToBeBlock();
        }
        else if (_thisEntity.Movement.ResistList[0].Stats.IsActive == false)
        {
            RelieveBlock(_thisEntity.Movement.ResistList[0]);
        }
        Move();
        UnBalancedMove();
    }
    private void OnDrawGizmosSelected()
    {
       Gizmos.color = Color.red;
       Gizmos.DrawLine(this.transform.position, _currentSection[_currentPointSerial].targetPosition);
       for (int i = _currentPointSerial; i < _currentSection.Length - 1; i++)
       {
           Gizmos.DrawLine(_currentSection[i].targetPosition, _currentSection[i + 1].targetPosition);
       }
       for (int i = 0; i < 4; i++)
       {
           if (_thisEntity.Movement.InBlocks[i].j != -1)
           {
               Gizmos.DrawWireCube(new Vector3(_thisEntity.Movement.InBlocks[i].j, _thisEntity.Movement.InBlocks[i].i, 0), new Vector3(1, 1, 1));
           }
       }
#if UNITY_EDITOR
       Handles.Label(this.transform.position-0.1f*Vector3.up, $"{PathDataManager.Manager.GetSection(_currentPathSerial, _currentSectionSerial, _moveMethod).Item1-_forceUnmoveTime:F5}",
           new GUIStyle { normal = { textColor = Color.red }, alignment = TextAnchor.MiddleCenter });
#endif
    }
    public void CountPriority()
    {
        float length = Vector2.Distance(this.transform.position, _currentSection[_currentPointSerial].targetPosition);
        for (int i = _currentPointSerial; i < _currentSection.Length - 1; i++)
        {
            length += Vector2.Distance(_currentSection[i].targetPosition, _currentSection[i + 1].targetPosition);
        }
        length += PathDataManager.Manager.GetLength(_currentPathSerial, _currentSectionSerial + 1, _moveMethod);
        _thisEntity.Movement.Priority = -length * 0.1f;
    }
    public void SetMoveParameters(int pathSerial, int sectionSerial, int pointSerial)
    {
        _currentPathSerial = pathSerial;
        _currentSectionSerial = sectionSerial;
        _currentPointSerial = pointSerial;
        (_forceUnmoveTime, _currentSection) = PathDataManager.Manager.GetSection(_currentPathSerial, _currentSectionSerial, _moveMethod);
        CountPriority();
    }
    private void FindEntitiesAroundAndTryToBeBlock()
    {
        if (_thisEntity.Stats.BlockOccupationS >= 0)
        {
            List<Entity> entitiesAround = new List<Entity>();
            entitiesAround = EntityManager.Manager.EntitySelector_Radius((this.transform.position.x, this.transform.position.y), _thisEntity.Movement.Camp, false, 0.5f + EntityManager.EntityR, true);

            for (int i = 0; i < entitiesAround.Count; i++)
            {
                if (entitiesAround[i].InteractableStatic)
                {
                    Vector2 targetPos = entitiesAround[i].InteractableStatic.TryAddToEntityResistList(_thisEntity);
                    if (targetPos.x != -100)
                    {
                        _thisEntity.Movement.ResistList.Add(entitiesAround[i]);
                        if (_thisEntity.StateMachine.CurrentState == EntityState.Move)
                        {
                            _thisEntity.StateMachine.TrySetState(EntityState.Idle, true);
                        }
                        _thisEntity.Movement.Position = targetPos;
                        return;
                    }
                }
            }
        }

    }
    public void RelieveBlock(Entity entity)
    {
        _thisEntity.Movement.ResistList.Remove(entity);
    }
    private void UnBalancedMove()
    {
        if (_thisEntity.buffController.FetchAbnormalState(1))
        {
            if (_unBalancedMoveSpeed.magnitude > 0.1f)
            {
                Vector2 originPos = _thisEntity.Movement.Position;
                _thisEntity.Movement.Position = _thisEntity.Movement.Position + (_unBalancedMoveSpeed - _unBalancedMoveSpeed.normalized * 0.5f * MIU_G * Time.fixedDeltaTime) * Time.fixedDeltaTime;
                _unBalancedMoveSpeed -= _unBalancedMoveSpeed.normalized * MIU_G * Time.fixedDeltaTime;
                int xConstrain = 0;
                int yConstrain = 0;
                (int x, int y) xyConstrain = (0, 0);
                (int i, int j) pos = ((int)(originPos.y + 0.5), (int)(originPos.x + 0.5));
                for (int i = 0; i < _thisEntity.Movement.InBlocks.Length; i++)
                {
                    int ii = _thisEntity.Movement.InBlocks[i].i;
                    int jj = _thisEntity.Movement.InBlocks[i].j;
                    if (ii != -1 && MapDataManager.Manager.GetPosBlock(ii, jj).highland)
                    {
                        if (ii != pos.i && jj != pos.j)
                        {
                            xyConstrain = (jj - pos.j, ii - pos.i);
                        }
                        else
                        {
                            xConstrain = jj - pos.j;
                            yConstrain = ii - pos.i;
                        }
                    }
                }
                float entityR = EntityManager.EntityR * 1.1f;
                if (xConstrain != 0 && yConstrain != 0)
                {
                    _unBalancedMoveSpeed = Vector2.zero;
                    _thisEntity.Movement.Position = new Vector2(pos.j + (0.5f - entityR) * xConstrain, pos.i + (0.5f - entityR) * yConstrain);
                }
                else if (xConstrain != 0)
                {
                    float x = pos.j + 0.5f * xConstrain - entityR * xConstrain;
                    float y = (x - originPos.x) * (_thisEntity.Movement.Position.y - originPos.y) / (_thisEntity.Movement.Position.x - originPos.x) + originPos.y;
                    _unBalancedMoveSpeed.x = 0;
                    _thisEntity.Movement.Position = new Vector2(x, y);
                }
                else if (yConstrain != 0)
                {
                    float y = pos.i + 0.5f * yConstrain - entityR * yConstrain;
                    float x = (y - originPos.y) * (_thisEntity.Movement.Position.x - originPos.x) / (_thisEntity.Movement.Position.y - originPos.y) + originPos.x;
                    _unBalancedMoveSpeed.y = 0;
                    _thisEntity.Movement.Position = new Vector2(x, y);
                }
                else if (xyConstrain != (0, 0))
                {
                    _unBalancedMoveSpeed = Vector2.zero;
                    _thisEntity.Movement.Position = new Vector2(pos.j + (0.5f - entityR) * xyConstrain.x, pos.i + (0.5f - entityR) * xyConstrain.y);
                }
            }
            else
            {
                _unBalancedMoveSpeed = Vector2.zero;
                _thisEntity.buffController.TryRemoveAbnormalState(1);
                FindPath();
            }
        }
    }
    public bool TryToAddImpulse(Vector2 direction, int strengthLevel)
    {
        if (_massLevel > strengthLevel)
            return false;
        _unBalancedMoveSpeed += direction * strengthLevel / _massLevel;
        _thisEntity.buffController.AddAbnormalState(-10, 1);
        return true;
    }
    private void ReachTempTarget()
    {
        _reachTempTarget?.Invoke();
        _forceUnmoveTime = _tempWaitTime;
        FindPath();
        _currentSectionSerial--;
        ReleaseTempTarget();
    }
    public void AddTempTarget(Action reachOperation, Vector2 targetPos, float waitTime)
    {
        _forceUnmoveTime = 0;
        _currentSection = new MoveParameters[1] { new MoveParameters(targetPos, false) };
        FindPath();
        _reachTempTarget = reachOperation;
        _tempWaitTime = waitTime;
        if (!_tempTargetExist)
        {
            OperationsOnReachSectionEnd += ReachTempTarget;
            _tempTargetExist = true;
            _currentSectionSerial--;
        }
    }
    public void ReleaseTempTarget()
    {
        if (_tempTargetExist)
        {
            _currentSectionSerial++;
            _tempTargetExist = false;
            OperationsOnReachSectionEnd -= ReachTempTarget;
            _currentSection = PathDataManager.Manager.GetSection(_currentPathSerial, _currentSectionSerial, _moveMethod).Item2;
            FindPath();
        }
    }
    protected virtual void MovePosition()
    {

    }
    private void Move()
    {
        if (_forceUnmoveTime <= 0)
        {
            if (_currentSection == null)
            {
                ArriveEnd();
                return;
            }
            if (_thisEntity.StateMachine.CurrentState != EntityState.Move && _thisEntity.Movement.ResistList.Count == 0)
            {
                _thisAM.SetMoveBranch(MoveAnimationBranch.Normal);
                _thisEntity.StateMachine.TrySetState(EntityState.Move, false);
            }
            if (_thisEntity.StateMachine.CurrentState == EntityState.Move)
            {
                MovePosition();
                if (_currentPointSerial == _currentSection.Length)
                {
                    _currentSectionSerial++;
                    (_forceUnmoveTime, _currentSection) = PathDataManager.Manager.GetSection(_currentPathSerial, _currentSectionSerial, _moveMethod);
                    _currentPointSerial = 0;
                    OperationsOnReachSectionEnd?.Invoke();
                    if (_forceUnmoveTime > 0)
                    {
                        _thisEntity.StateMachine.TrySetState(EntityState.Idle, true);
                    }
                }
            }
        }
        else
        {
            _forceUnmoveTime -= Time.fixedDeltaTime;
        }
    }
    protected virtual void ArriveEnd()
    {
        _thisEntity.ArriveEnd();
        if (_levelHpComsume > 0)
        {
            LevelResourceManager.Manager.LevelHpLeft -= LevelHpConsume;
            AudioManager.Manager.PlayAudio("alarm", 1, false, false);
        }
    }
    public virtual void FindPath()
    {

    }
    private void AttributesCaculateFirst()
    {
        // MoveSpeed 已迁出至 EntityStats（XxxBase 在 PreWarm 阶段 AttributesCaculateFirst 写入）
        _moveMethod = _thisEntity.EntityData.MoveMethod;
        _massLevel = _thisEntity.EntityData.MassLevel;
        _levelHpComsume = _thisEntity.EntityData.MonsterLevelHpConsume;
    }
    public void PreWarm()
    {
        _thisEntity = this.transform.GetComponent<Entity>();
        _thisAM = _thisEntity.entityAM;
        _unBalancedMoveSpeed = Vector2.zero;
        AttributesCaculateFirst();
    }

    public virtual void Initialize()
    {
        _tempTargetExist = false;
    }

    public void Dormancy()
    {
        _unBalancedMoveSpeed = Vector2.zero;
        if (_thisEntity.EntityData.MonsterCountOperated)
        {
            LevelResourceManager.Manager.CurrentOperateCount += 1;
        }
    }
}
