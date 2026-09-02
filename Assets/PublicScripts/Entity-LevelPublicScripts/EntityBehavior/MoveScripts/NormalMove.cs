using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NormalMove : MoveBase
{
    protected override void MovePosition()
    {
        float deltas = MoveSpeedS * Time.fixedDeltaTime;
        for (; _currentPointSerial < _currentSection.Length; _currentPointSerial++)
        {
            float d = Vector2.Distance(_currentSection[_currentPointSerial].targetPosition, this.transform.position);
            _thisEntity.facing.SetDirection(_currentSection[_currentPointSerial].targetPosition);
            if (deltas < d)
            {
                _thisEntity.Movement.Position += (_currentSection[_currentPointSerial].targetPosition - _thisEntity.Movement.Position).normalized * deltas;
                CountPriority();
                deltas = 0;
                break;
            }
            else
            {
                deltas -= d;
                if (_currentSection[_currentPointSerial].whetherToEnterPortal)
                {
                    this.transform.position = _currentSection[++_currentPointSerial].targetPosition;
                }
                else
                {
                    this.transform.position = _currentSection[_currentPointSerial].targetPosition;
                }
            }
        }
    }
    public override void FindPath()
    {
        Vector2 targetPos = _currentSection[_currentSection.Length - 1].targetPosition;
        _currentSection = MapDataManager.Manager.AStarWayFinding(this.transform.position, targetPos, _moveMethod);
        _currentPointSerial = 0;
    }
}
