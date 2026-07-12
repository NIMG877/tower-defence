using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using DG.Tweening;

public class WitherTalent2 : Talent
{
    [SerializeField] private string _recoverLoop, _start2;
    [SerializeField] private float _boomRadius;
    [SerializeField] private GameObject _recoverEffect, _boomEffect;
    [SerializeField] private float _recoverTime;
    public override void Initialize()
    {
        _thisEntity.entityAM.AddOverride(this, new AnimationOverride { Idle = _recoverLoop });
        _thisEntity.Stats.CurrentHpRate = 0.001f;
        _thisEntity.buffController.AddAbnormalState(-10, 3);
        _thisEntity.buffController.AddAbnormalState(-10, 0);
        _thisEntity.buffController.AddAbnormalState(-10, 2);
        EnterRecoverMode();
    }
    private async void EnterRecoverMode()
    {
        await UniTask.WaitForSeconds(_thisEntity.entityAM.ResolveAnimationDuration(AnimationSlot.Start) * 0.9f);
        _recoverEffect.SetActive(true);
        _thisEntity.entityAM.TrySetState(EntityState.Idle, true);
        _thisEntity.entityAM.AddOverride(this, new AnimationOverride { Start = _start2 });
        DOTween.To((value) =>
        {
            _thisEntity.Stats.CurrentHpRate = value;
        }, 0.001f, 1, _recoverTime).OnComplete(async () =>
        {
            _thisEntity.entityAM.TrySetState(EntityState.Die, true);
            _thisEntity.entityAM.RemoveOverrides(this);
            _boomEffect.SetActive(true);
            _recoverEffect.SetActive(false);
            await UniTask.WaitForSeconds(_thisEntity.entityAM.ResolveNamedAnimationDuration(_start2) * 0.7f);
            List<Entity> targets = EntityManager.Manager.EntitySelector_Radius((_thisEntity.Movement.Position.x, _thisEntity.Movement.Position.y), 2, false, _boomRadius, false);
            targets.AddRange(EntityManager.Manager.EntitySelector_Radius((_thisEntity.Movement.Position.x, _thisEntity.Movement.Position.y), 1, false, _boomRadius, false));
            float eneityR = EntityManager.EntityR;
            for (int i = 0; i < targets.Count; i++)
            {
                float r = Vector2.Distance(targets[i].Movement.Position, _thisEntity.Movement.Position);
                if (r <= eneityR)
                {
                    targets[i].Stats.ApplyDamage(_thisEntity, _thisEntity.Stats.AttackS, 5, 0, 0, 0, 0, 0, 1);
                    if (targets[i].MoveBase)
                    {
                        targets[i].MoveBase.TryToAddImpulse((targets[i].Movement.Position - _thisEntity.Movement.Position).normalized, 5);
                    }
                }
                else if (r <= 2 * eneityR)
                {
                    targets[i].Stats.ApplyDamage(_thisEntity, _thisEntity.Stats.AttackS, 4, 0, 0, 0, 0, 0, 1);
                    if (targets[i].MoveBase)
                    {
                        targets[i].MoveBase.TryToAddImpulse((targets[i].Movement.Position - _thisEntity.Movement.Position).normalized, 4);
                    }
                }
                else if (r <= 1.414 + eneityR)
                {
                    targets[i].Stats.ApplyDamage(_thisEntity, _thisEntity.Stats.AttackS, 3, 0, 0, 0, 0, 0, 1);
                    if (targets[i].MoveBase)
                    {
                        targets[i].MoveBase.TryToAddImpulse((targets[i].Movement.Position - _thisEntity.Movement.Position).normalized, 3);
                    }
                }
                else
                {
                    targets[i].Stats.ApplyDamage(_thisEntity, _thisEntity.Stats.AttackS, 2, 0, 0, 0, 0, 0, 1);
                    if (targets[i].MoveBase)
                    {
                        targets[i].MoveBase.TryToAddImpulse((targets[i].Movement.Position - _thisEntity.Movement.Position).normalized, 2);
                    }
                }
            }
            await UniTask.WaitForSeconds(_thisEntity.entityAM.ResolveNamedAnimationDuration(_start2) * 0.3f);
            _boomEffect.SetActive(false);
            _thisEntity.buffController.TryRemoveAbnormalState(3);
            _thisEntity.buffController.TryRemoveAbnormalState(0);
            _thisEntity.buffController.TryRemoveAbnormalState(2);
            _thisEntity.GetComponent<WitherTalent3>().HpCheck();
        });
    }
}
