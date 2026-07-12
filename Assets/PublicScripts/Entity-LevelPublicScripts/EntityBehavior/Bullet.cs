using UnityEngine;
using Cysharp.Threading.Tasks;
using System;

[Serializable]
public struct BulletData
{
    public GameObject BulletPrefab;
    public GameObject BulletTrailPrefab;
    public GameObject BulletSpawnEffect;
    public GameObject BulletDestroyEffect;
    public float BulletSpeed;
    public int BulletType;
    public bool AllowNoTarget;
    [Header("BulletType=1")]
    public float DevitationXRate;
    public float DevitationYValue;
}
public class Bullet
{
    private GameObject _bulletObject;
    private GameObject _bulletTrailObject;
    private GameObject _bulletSpawnEffect;
    private GameObject _bulletDestroyEffect;
    private Entity _originEntity;
    private Entity _targetEntity;
    private Vector2 _targetPos;
    private float _damage;
    private float _multiplyer;
    private float _defPenetrate;
    private float _mgrPenetrate;
    private float _defPenetrate_value;
    private float _mgrPenetrate_value;
    private float _bulletSpeed;
    private float _devitationXRate;
    private float _devitationYValue;
    private int _damageType;
    private int _applyType;
    private int _bulletType;
    private bool _allowNoEntityTarget;
    private BulletData _bulletData;
    private event AttackBase.OperationsBeforeTakeDamage _onBeforeTakeDamage;
    private event AttackBase.OperationsAfterTakeDamage _onAfterTakeDamage;
    public delegate void OperationsOnBulletDestroy(Vector2 bulletPos);
    private event OperationsOnBulletDestroy _onBulletDestroy;
    public Bullet(AttackBase.OperationsBeforeTakeDamage operationsBeforeTakeDamage, AttackBase.OperationsAfterTakeDamage operationsAfterTakeDamage, OperationsOnBulletDestroy operationsOnBulletDestroy, BulletData bulletData, Entity originEntity, Entity targetEntity, Vector2 targetPos, Vector2 bulletSpawnPosition, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType)
    {
        _onBeforeTakeDamage = operationsBeforeTakeDamage;
        _onAfterTakeDamage = operationsAfterTakeDamage;
        _onBulletDestroy = operationsOnBulletDestroy;
        _originEntity = originEntity;
        _targetEntity = targetEntity;
        _targetPos = targetPos;
        _bulletSpeed = bulletData.BulletSpeed;
        _bulletType = bulletData.BulletType;
        _devitationXRate = bulletData.DevitationXRate;
        _devitationYValue = bulletData.DevitationYValue;
        _bulletDestroyEffect = bulletData.BulletDestroyEffect;
        _damage = damage;
        _multiplyer = multiplyer;
        _defPenetrate = defPenetrate;
        _mgrPenetrate = mgrPenetrate;
        _defPenetrate_value = defPenetrate_value;
        _mgrPenetrate_value = mgrPenetrate_value;
        _damageType = damageType;
        _applyType = applyType;
        _bulletData = bulletData;
        _allowNoEntityTarget = bulletData.AllowNoTarget;
        if (bulletData.BulletPrefab)
        {
            _bulletObject = EffectManager.Manager.CreateEffect(bulletData.BulletPrefab, bulletSpawnPosition, Quaternion.identity, LevelResourceSharing.LM, 1, false);
        }
        if (bulletData.BulletTrailPrefab)
        {
            _bulletTrailObject = EffectManager.Manager.CreateEffect(bulletData.BulletTrailPrefab, bulletSpawnPosition, Quaternion.identity, LevelResourceSharing.LM, 1, false);
        }
        if (bulletData.BulletSpawnEffect)
        {
            EffectManager.Manager.CreateEffect(bulletData.BulletSpawnEffect, bulletSpawnPosition, Quaternion.Euler(0, originEntity.entityAM.CurrentDirection.left ? 180 : 0, 0), LevelResourceSharing.LM, 1, true);
        }
        BulletFly();
    }
    async private void BulletFly()
    {
        Vector2 bulletStartPos = _bulletObject.transform.position;
        Transform bulletTransform = _bulletObject.transform;
        Transform bulletTrailTransform = _bulletTrailObject.transform;
        if (_targetEntity != null && _targetEntity.Stats.IsActive)
            _targetPos = _targetEntity.Movement.Position + 0.4f * Vector2.up;
        float distance = Vector2.Distance(bulletStartPos, _targetPos);
        if (distance == 0)
        {
            BulletArriveEnd();
            return;
        }
        float rateSpeed = _bulletSpeed / distance;
        float rate = 0;
        while (true)
        {
            if (!_allowNoEntityTarget && (_targetEntity == null || !_targetEntity.Stats.IsActive))
            {
                DestroyBullet();
                return;
            }
            rate += rateSpeed * Time.fixedDeltaTime;
            Vector2 targetPo;
            if (_targetEntity != null && _targetEntity.Stats.IsActive)
                _targetPos = _targetEntity.Movement.Position + 0.4f * Vector2.up;
            if (rate < 1)
            {
                switch (_bulletType)
                {
                    case 0: targetPo = (1 - rate) * bulletStartPos + rate * _targetPos; break;
                    case 1:
                        float rate2 = rate * rate;
                        targetPo =
                            (rate2 - 2 * _devitationXRate * rate2 + 2 * _devitationXRate * rate) * _targetPos +
                            (1 - 2 * rate + rate2 + (2 * rate - 2 * rate2) * (1 - _devitationXRate)) * bulletStartPos +
                            (2 * rate - 2 * rate2) * _devitationYValue * Vector2.up;
                        break;
                    default: targetPo = Vector3.zero; break;
                }
                Quaternion euler = Quaternion.Euler(0, 0, Vector2.SignedAngle(Vector2.right, targetPo - (Vector2)bulletTransform.position));
                bulletTransform.SetPositionAndRotation(targetPo, euler);
                bulletTrailTransform.SetPositionAndRotation(targetPo, euler);
                await UniTask.WaitForFixedUpdate(LevelResourceSharing.LevelCtk);
            }
            else
            {
                targetPo = _targetPos;
                Quaternion euler = Quaternion.Euler(0, 0, Vector2.SignedAngle(Vector2.right, targetPo - (Vector2)bulletTransform.position));
                bulletTransform.SetPositionAndRotation(targetPo, euler);
                bulletTrailTransform.SetPositionAndRotation(targetPo, euler);
                BulletArriveEnd();
                return;
            }
        }
    }
    private void BulletArriveEnd()
    {
        if (_targetEntity != null && _targetEntity.Stats.IsActive)
        {
            _onBeforeTakeDamage?.Invoke(_targetEntity, ref _multiplyer, ref _defPenetrate, ref _mgrPenetrate, ref _defPenetrate_value, ref _mgrPenetrate, ref _damageType, 0);
            bool isDeadly = _targetEntity.Stats.ApplyDamage(_originEntity, _damage, _multiplyer, _defPenetrate, _mgrPenetrate, _defPenetrate_value, _mgrPenetrate_value, _damageType, 0);
            _onAfterTakeDamage?.Invoke(_targetEntity, _multiplyer, _defPenetrate, _mgrPenetrate, _defPenetrate_value, _mgrPenetrate, _damageType, 0, isDeadly);
        }
        DestroyBullet();
    }
    private void DestroyBullet()
    {
        EffectManager.Manager.ReturnEffect(_bulletData.BulletPrefab, _bulletObject);
        _onBulletDestroy?.Invoke(_bulletObject.transform.position);
        if (_bulletDestroyEffect)
        {
            EffectManager.Manager.CreateEffect(_bulletDestroyEffect, _bulletObject.transform.position, Quaternion.identity, LevelResourceSharing.LM, 1, true);
        }
        if (_bulletTrailObject)
        {
            EffectManager.Manager.SetEffectAutoReturn(_bulletData.BulletTrailPrefab, _bulletTrailObject);
        }
    }
}
