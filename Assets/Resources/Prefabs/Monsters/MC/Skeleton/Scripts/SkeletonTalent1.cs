using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static AttackBase;

public class SkeletonTalent1 : Talent
{
    private float _communicationRange = 2;
    private List<(Entity receiveFrom, Entity receiveTarget)> _fromsAndTargets;
    private bool _isExtraAttack;
    [SerializeField] private string _eAttack;
    [SerializeField] private BulletData _bulletData;
    public override void Initialize()
    {
        base.Initialize();
        _thisEntity.AttackBase.OnBeforeAttack += ShareTarget;
        _fromsAndTargets = new List<(Entity _receiveFrom, Entity _receiveTarget)>();
        _isExtraAttack = false;
        _thisEntity.AttackBase.OnAttackSuccessfully += new AttackBase.OperationsOnAttackSuccessfully(() =>
        {
            if (_isExtraAttack)
            {
                _isExtraAttack = false;
                _fromsAndTargets.RemoveAt(0);
            }
        });
        _thisEntity.AttackBase.OnAttackInterrupt += new AttackBase.OperationsOnAttackInterrupt(() =>
        {
            if (_isExtraAttack)
            {
                _isExtraAttack = false;
                //_fromsAndTargets.RemoveAt(0);
            }
        });
    }

    private void ShareTarget(Entity target, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int cumbo, ref int damageType, int applyType)
    {
        List<Entity> targetEntities = EntityManager.Manager.EntitySelector_Radius((this.transform.position.x, this.transform.position.y), _thisEntity.Camp, true, _communicationRange, true);
        for (int i = 0; i < targetEntities.Count; i++)
        {
            if (targetEntities[i] != _thisEntity && targetEntities[i].TryGetComponent(out SkeletonTalent1 sl) && (!_isExtraAttack || targetEntities[i] != _fromsAndTargets[0].receiveFrom))
            {
                new Bullet(null, new OperationsAfterTakeDamage((Entity target0, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly) =>
                {
                    sl.ReceiveTarget(target, _thisEntity);
                }), null, _bulletData, _thisEntity, targetEntities[i], Vector2.zero, _thisEntity.EntityPosition, 1, 0, 0, 0, 0, 0, 0, 0);

            }
        }
    }

    public void ReceiveTarget(Entity target, Entity from)
    {
        //await UniTask.WaitForSeconds(1);
        if (!_thisEntity.Vision.NearbyTurrets.Contains(target))
        {
            _fromsAndTargets.Add((from, target));
            if (_fromsAndTargets.Count == 1)
            {
                TryExtraAttackConstantly();
            }
        }
    }
    private async void TryExtraAttackConstantly()
    {
        _thisEntity.buffController.AddAbnormalState(-10, 0);
        while (_fromsAndTargets.Count > 0)
        {
            if (_thisEntity.entityAM.CurrentState != EntityState.Start)
            {
                for (int i = 0; i < _fromsAndTargets.Count;)
                {
                    if (_fromsAndTargets[0].receiveTarget.Stats.IsActive == false || _fromsAndTargets[0].receiveTarget.Stats.Selectable != 0)
                    {
                        _fromsAndTargets.RemoveAt(0);
                    }
                    else
                    {
                        if (_thisEntity.AttackBase.TryToAttackWithAnimation(
                            new Entity[1] { _fromsAndTargets[0].receiveTarget },
                            false,
                            true,
                            new AnimationOverride { AttackClose = _eAttack, AttackRemote = _eAttack }))
                            _isExtraAttack = true;
                        break;
                    }
                }
            }
            await UniTask.WaitForFixedUpdate();
        }
        _thisEntity.buffController.TryRemoveAbnormalState(0);
    }
}
