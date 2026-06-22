using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

public class CreeperTalent : Talent
{
    [SerializeField] private string _chargeLoop, _backout, _die2, _attack;
    [SerializeField] private float _boomRadius;
    private float _chargeTime;
    private float _chargeTimer;

    public override void Initialize()
    {
        _chargeTimer = 0;
        _chargeTime = 1;
        _thisEntity.buffController.AddAbnormalState(-10, 2);
    }
    private void FixedUpdate()
    {
        if (!_thisEntity.Stats.IsActive)
            return;
        if (_thisEntity.Vision.NearbyTurrets.Count > 0 && _chargeTimer == 0)
        {
            ChargeAndBackOut();
        }
    }
    private async void ChargeAndBackOut()
    {
        _thisEntity.buffController.AddAbnormalState(-10, 0);
        _thisEntity.entityAM.TrySetState(EntityState.Idle, true, new AnimationOverride { Idle = _chargeLoop });
        while (_chargeTimer < _chargeTime)
        {
            _chargeTimer += Time.fixedDeltaTime;
            await UniTask.WaitForFixedUpdate();
            if (_thisEntity.Vision.NearbyTurrets.Count == 0)
            {
                _thisEntity.entityAM.TrySetState(EntityState.Idle, true, new AnimationOverride { Idle = _backout });
                await UniTask.WaitForSeconds(_thisEntity.entityAM.ResolveNamedAnimationDuration(_backout) * 0.9f);
                _thisEntity.entityAM.TrySetState(EntityState.Idle, true);
                _thisEntity.buffController.TryRemoveAbnormalState(0);
                _chargeTimer = 0;
                return;
            }
        }
        _thisEntity.entityAM.TrySetState(EntityState.Idle, true, new AnimationOverride { Idle = _attack });
        await UniTask.WaitForSeconds(_thisEntity.entityAM.ResolveNamedAnimationDuration(_attack) * 0.9f);
        _thisEntity.buffController.TryRemoveAbnormalState(0);
        List<Entity> targets = EntityManager.Manager.EntitySelector_Radius((_thisEntity.Movement.Position.x, _thisEntity.Movement.Position.y), 2, false, _boomRadius, false);
        targets.AddRange(EntityManager.Manager.EntitySelector_Radius((_thisEntity.Movement.Position.x, _thisEntity.Movement.Position.y), 1, false, _boomRadius, false));
        float eneityR = EntityManager.EntityR;
        for (int i = 0; i < targets.Count; i++)
        {
            float r = Vector2.Distance(targets[i].EntityPosition, _thisEntity.Movement.Position);
            if (r <= eneityR)
            {
                targets[i].TakeDamage(_thisEntity, _thisEntity.AttackBase.AttackDamageS, 3, 0, 0, 0, 0, 0, 1);
                if (targets[i].MoveBase)
                {
                    targets[i].MoveBase.TryToAddImpulse((targets[i].EntityPosition - _thisEntity.Movement.Position).normalized, 5);
                }
            }
            else if (r <= 2 * eneityR)
            {
                targets[i].TakeDamage(_thisEntity, _thisEntity.AttackBase.AttackDamageS, 2, 0, 0, 0, 0, 0, 1);
                if (targets[i].MoveBase)
                {
                    targets[i].MoveBase.TryToAddImpulse((targets[i].EntityPosition - _thisEntity.Movement.Position).normalized, 4);
                }
            }
            else if (r <= 1.414 + eneityR)
            {
                targets[i].TakeDamage(_thisEntity, _thisEntity.AttackBase.AttackDamageS, 1, 0, 0, 0, 0, 0, 1);
                if (targets[i].MoveBase)
                {
                    targets[i].MoveBase.TryToAddImpulse((targets[i].EntityPosition - _thisEntity.Movement.Position).normalized, 3);
                }
            }
            else
            {
                targets[i].TakeDamage(_thisEntity, _thisEntity.AttackBase.AttackDamageS, 0.5f, 0, 0, 0, 0, 0, 1);
                if (targets[i].MoveBase)
                {
                    targets[i].MoveBase.TryToAddImpulse((targets[i].EntityPosition - _thisEntity.Movement.Position).normalized, 2);
                }
            }
        }
        _thisEntity.entityAM.AddOverride(this, new AnimationOverride { Die = _die2 });
        _thisEntity.Die();
    }
}
