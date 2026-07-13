using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
public class WdslmSkill3 : Skill
{
    public EntityID FlyMachineID;
    [SerializeField] private string _skillStart, _skillLoop, _skillEnd;
    [HideInInspector] public Entity TargetEntity;
    private WdslmSkill2 _skill2;
    private Entity _machine;
    public override void Initialize()
    {
        base.Initialize();
        _skill2 = GetComponent<WdslmSkill2>();
        TargetEntity = null;
    }
    public override bool SkillBegin()
    {
        if (TargetEntity == null)
            return false;
        if (!base.SkillBegin())
            return false;
        _thisEntity.buffController.AddAbnormalState(-10, 3);
        _thisEntity.buffController.AddAbnormalState(-10, 2);
        _thisEntity.buffController.AddAbnormalState(-10, 0);
        _thisEntity.entityAM.AddOverride(this, new AnimationOverride
        {
            Start = _skillStart,
            Idle = _skillLoop,
        });
        _thisEntity.entityAM.TrySetState(EntityState.Die, true);
        SummonMachine();
        return true;
    }
    private async void SummonMachine()
    {
        await UniTask.WaitForSeconds(_thisEntity.entityAM.ResolveNamedAnimationDuration(_skillStart), false, PlayerLoopTiming.Update, LevelResourceSharing.LevelCtk);
        _machine = EntityManager.Manager.SetMovableEntity(FlyMachineID, _thisEntity.Movement.Position, _thisEntity.Camp, _thisEntity.MoveBase.CurrentPathSerial);
        _machine.MoveBase.SetMoveParameters(_thisEntity.MoveBase.CurrentPathSerial, _thisEntity.MoveBase.CurrentSectionSerial, _thisEntity.MoveBase.CurrentPointSerial);
        _machine.buffController.AddAbnormalState(-10, 3);
        _machine.GetComponent<MachineTalent1>().ProjectEntity(TargetEntity, this);
    }

    public override void SkillEnd()
    {
        base.SkillEnd();
        _machine.Die();
        if (TargetEntity.Stats.IsActive)
            TargetEntity.Die();
        TargetEntity = null;
        _thisEntity.entityAM.RemoveOverrides(this);
        _thisEntity.entityAM.AddOverride(this, new AnimationOverride { Start = _skillEnd });
        _thisEntity.entityAM.TrySetState(EntityState.Die, true);
        _thisEntity.buffController.TryRemoveAbnormalState(0);
        _thisEntity.buffController.TryRemoveAbnormalState(2);
        _thisEntity.buffController.TryRemoveAbnormalState(3);
        _skill2.SkilllRecoverForbid(false);
        SkilllRecoverForbid(true);
    }
}
