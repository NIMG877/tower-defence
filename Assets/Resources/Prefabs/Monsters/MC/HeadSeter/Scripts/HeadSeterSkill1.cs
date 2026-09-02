using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

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
        am.AddOverride(this, new AnimationOverride { [AnimationSlot.Cast] = IsDie ? _begin_d : _begin });
        _thisEntity.StateMachine.TrySetState(EntityState.Cast, true);
        _thisEntity.buffController.AddAbnormalState(-10, 0);
        _thisEntity.buffController.AddAbnormalState(-10, 3);
        return true;
    }
    public override void SkillEnd()
    {
        base.SkillEnd();
        am.AddOverride(this, new AnimationOverride { [AnimationSlot.Cast] = IsDie ? _end_d : _end });
        _thisEntity.StateMachine.TrySetState(EntityState.Cast, true);
        _thisEntity.buffController.TryRemoveAbnormalState(0);
        _thisEntity.buffController.TryRemoveAbnormalState(3);
        FlashMove(_moveDis);
        EmergeResume();
    }

    // 钻地/钻出演出：覆盖 Cast 槽并转入 Cast（粘性，播完保持末帧=潜伏姿态）。
    // 旧 Die 后门播的是 SO Die 槽资产且钻出后无恢复通路；此处播真实技能动画（Skill_Begin/Skill_End，
    // headSeter.asset 已登记）并由钻出动画播完后显式回 Idle 恢复行走。
    private async void EmergeResume()
    {
        await UniTask.WaitForSeconds(am.ResolveNamedAnimationDuration(IsDie ? _end_d : _end), false, PlayerLoopTiming.Update, LevelResourceSharing.LevelCtk);
        _thisEntity.StateMachine.TrySetState(EntityState.Idle, true);
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
