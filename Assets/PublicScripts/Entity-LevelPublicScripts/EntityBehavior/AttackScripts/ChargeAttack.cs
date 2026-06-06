using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Spine;
using Spine.Unity;

public class ChargeAttack : AttackBase
{
    public OperationsBeforeTakeDamage OnBeforeChargeTakeDamage;
    public OperationsAfterTakeDamage OnAfterChargeTakeDamage;
    public AnimationReferenceAsset[] ChargeAnimation;
    private AnimationReferenceAsset[] _attackRemoteP, _attackCloseP;
    private int _currentChargeNum;
    [SerializeField] private GameObject[] _chargeEffects;
    [SerializeField] private AttackEffectData _chargeEffectData;
    public override void Initialize()
    {
        base.Initialize();
        _currentChargeNum = 0;
    }
    public override void Dormancy()
    {
        base.Dormancy();
        OnBeforeChargeTakeDamage = null;
        OnAfterChargeTakeDamage = null;
        for (int i = 0; i < _chargeEffects.Length; i++)
            _chargeEffects[i].SetActive(false);
    }
    public override bool TryToAttack(Entity[] attackTargets, bool forceChange, bool canBeInterrupt)
    {
        if (attackTargets.Length > 0)
        {
            if (_thisEntity.entityAM.TrySetAttackState(forceChange, () => { AttackByAnimation(attackTargets, canBeInterrupt); }))
            {
                base.TryToAttack(attackTargets, forceChange, canBeInterrupt);
                return true;
            }
        }
        else if (_currentChargeNum < _chargeEffects.Length)
        {
            _attackRemoteP = _thisEntity.entityAM.Attack_Remote;
            _attackCloseP = _thisEntity.entityAM.Attack_Close;
            _thisEntity.entityAM.Attack_Remote = ChargeAnimation;
            _thisEntity.entityAM.Attack_Close = ChargeAnimation;
            if (_thisEntity.entityAM.TrySetAttackState(forceChange, () => { SpawnChargeEffect(); }))
            {
                _thisEntity.entityAM.Attack_Remote = _attackRemoteP;
                _thisEntity.entityAM.Attack_Close = _attackCloseP;
                return true;
            }
            _thisEntity.entityAM.Attack_Remote = _attackRemoteP;
            _thisEntity.entityAM.Attack_Close = _attackCloseP;
        }
        return false;
    }
    private void SpawnChargeEffect()
    {
        _chargeEffects[_currentChargeNum].SetActive(true);
        _currentChargeNum++;
    }
    protected override bool AttackSingleTargetOperation(OperationsBeforeTakeDamage onBeforeTakeDamage, OperationsAfterTakeDamage onAfterTakeDamage, AttackEffectData attackEffectData, Entity attackTarget, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType)
    {
        for (int i = 0; i < _currentChargeNum; i++)
        {
            new Bullet(onBeforeTakeDamage + OnBeforeChargeTakeDamage, onAfterTakeDamage + OnAfterChargeTakeDamage, null, _chargeEffectData.BulletData, _thisEntity, attackTarget, Vector2.zero, _chargeEffects[i].transform.position, AttackDamageS, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, 0);
            _chargeEffects[i].SetActive(false);
        }
        _currentChargeNum = 0;
        if (attackEffectData.BulletData.BulletPrefab == null)
        {
            onBeforeTakeDamage?.Invoke(attackTarget, ref multiplyer, ref defPenetrate, ref mgrPenetrate, ref defPenetrate_value, ref mgrPenetrate_value, ref damageType, 0);
            bool isDeadly = attackTarget.TakeDamage(_thisEntity, AttackDamageS, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, 0);
            onAfterTakeDamage?.Invoke(attackTarget, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, 0, isDeadly);
            return isDeadly;
        }
        else
        {
            new Bullet(onBeforeTakeDamage, onAfterTakeDamage, null, attackEffectData.BulletData, _thisEntity, attackTarget, Vector2.zero, attackEffectData.BulletSpawnTransform.position, AttackDamageS, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, 0);
            return false;
        }
    }
}
