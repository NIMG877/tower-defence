using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Spine;
using Spine.Unity;
using Cysharp.Threading.Tasks;

public class JumpMove : MoveBase
{
    [SerializeField] private Vector2Int[] _jumpRange;
    [SerializeField] private float _jumpGap;
    [SerializeField] private AnimationReferenceAsset _jumpBegin, _jumpEnd, _jump;
    private float _jumpGapTimer;
    bool _initialFind;
    protected override void MovePosition()
    {
        if (!_initialFind)
        {
            _initialFind = true;
            FindPath();
            print("Find");
        }
        if (_forceUnmoveTime <= 0 && _jumpGapTimer <= 0)
        {
            if (_currentSection == null)
            {
                ArriveEnd();
                return;
            }
            if (_thisAM.CurrentState != 2 && _thisEntity.entityResistList.Count == 0)
            {
                _thisAM.Move = _jumpBegin;
                if (_thisAM.TrySetState(2, false))
                {
                    Jump(this.transform.position, _currentSection[_currentPointSerial].targetPosition);
                }
            }
        }
        else
        {
            _forceUnmoveTime -= Time.fixedDeltaTime;
            _jumpGapTimer -= Time.fixedDeltaTime;
        }
    }


    private async void Jump(Vector2 from, Vector2 to)
    {
        float jumpBeginT, jumpEndT, jumpT;
        float k = 0.9f;
        jumpBeginT = _jumpBegin.Animation.Duration * k;
        jumpEndT = _jumpEnd.Animation.Duration * k;
        jumpT = _jump.Animation.Duration * k;
        Vector2 dir = to - from;
        await UniTask.WaitForSeconds(jumpBeginT);
        _thisAM.Move = _jump;
        _thisAM.TrySetState(2, true);
        float dt = 0;
        while (dt < jumpT)
        {
            _thisEntity.EntityPosition = from + dir * dt / jumpT;
            CountPriority();
            await UniTask.WaitForFixedUpdate();
            dt += Time.fixedDeltaTime;
        }
        this.transform.position = to;
        _thisAM.Move = _jumpEnd;
        _thisAM.TrySetState(2, true);
        await UniTask.WaitForSeconds(jumpEndT);
        if (_currentSection[_currentPointSerial].whetherToEnterPortal)
        {
            this.transform.position = _currentSection[++_currentPointSerial].targetPosition;
        }
        _currentPointSerial++;
        if (_currentPointSerial == _currentSection.Length)
        {
            _currentSectionSerial++;
            (_forceUnmoveTime, _currentSection) = PathDataManager.Manager.GetSection(_currentPathSerial, _currentSectionSerial, _moveMethod);
            FindPath();
            _currentPointSerial = 0;
        }
        _jumpGapTimer = _jumpGap;
        _thisAM.TrySetState(1, true);
    }
    public override void FindPath()
    {
        if (_currentSection != null)
        {
            Vector2 targetPos = _currentSection[_currentSection.Length - 1].targetPosition;
            _currentSection = MapDataManager.Manager.AStarWayFinding_Jump(this.transform.position, targetPos, _jumpRange);
            _currentPointSerial = 0;
        }
    }



    public override void Initialize()
    {
        base.Initialize();
        _initialFind = false;
    }
}
