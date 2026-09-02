using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HeadSeterSkill1 : Skill
{
    [SerializeField] private float _moveDis;
    [SerializeField] private string _begin, _begin_d, _end, _end_d;
    private AnimationMachine am;
    public bool IsDie;
    public override void PreWarm()
    {
        base.PreWarm();
        am = _thisEntity.entityAM;
    }
    public override void Initialize()
    {
        base.Initialize();
        IsDie = false;
    }
    public override bool SkillBegin()
    {
        if (_thisEntity.StateMachine.CurrentState >= EntityState.Attack || _thisEntity.Movement.ResistList.Count == 0 || !base.SkillBegin())
            return false;
        if (!IsDie)
        {
            am.AddOverride(this, new AnimationOverride { [AnimationSlot.Start] = _begin });
        }
        else
        {
            am.AddOverride(this, new AnimationOverride { [AnimationSlot.Start] = _begin_d });
        }
        _thisEntity.StateMachine.TrySetState(EntityState.Die, false);
        _thisEntity.buffController.AddAbnormalState(-10, 0);
        _thisEntity.buffController.AddAbnormalState(-10, 3);
        return true;
    }
    public override void SkillEnd()
    {
        base.SkillEnd();
        if (!IsDie)
        {
            am.AddOverride(this, new AnimationOverride { [AnimationSlot.Start] = _end });
        }
        else
        {
            am.AddOverride(this, new AnimationOverride { [AnimationSlot.Start] = _end_d });
        }
        _thisEntity.StateMachine.TrySetState(EntityState.Die, false);
        _thisEntity.buffController.TryRemoveAbnormalState(0);
        _thisEntity.buffController.TryRemoveAbnormalState(3);
        FlashMove(_moveDis);
    }
    private void FlashMove(float dis)
    {
        MoveParameters[] moveParameters = _thisEntity.MoveBase.CurrentSection;
        int currentPathSerial = _thisEntity.MoveBase.CurrentPathSerial;
        int currentSectionSerial = _thisEntity.MoveBase.CurrentSectionSerial;
        int currentPointSerial = _thisEntity.MoveBase.CurrentPointSerial;
        Vector2 pos = _thisEntity.Movement.Position;
        float currentDis = 0;

        for (int i = currentPointSerial; i < moveParameters.Length; i++)
        {
            currentDis += Vector2.Distance(pos, moveParameters[i].targetPosition);
            if (currentDis < dis)
            {
                pos = moveParameters[i].targetPosition;
            }
            else
            {
                _thisEntity.Movement.Position = moveParameters[i].targetPosition + (currentDis - dis) * (pos - moveParameters[i].targetPosition).normalized;
                _thisEntity.MoveBase.SetMoveParameters(currentPathSerial, currentSectionSerial, i);
                return;
            }
        }
        _thisEntity.Movement.Position = moveParameters[moveParameters.Length - 1].targetPosition;
        _thisEntity.MoveBase.SetMoveParameters(currentPathSerial, currentSectionSerial, 0);
        return;
    }
}
