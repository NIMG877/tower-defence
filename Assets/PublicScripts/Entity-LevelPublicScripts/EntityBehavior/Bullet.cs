using UnityEngine;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;

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
    private static readonly List<Bullet> _activeBullets = new List<Bullet>();

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
    private bool _destroyed;
    private BulletData _bulletData;
    private event AttackBase.OperationsBeforeTakeDamage _onBeforeTakeDamage;
    private event AttackBase.OperationsAfterTakeDamage _onAfterTakeDamage;
    public delegate void OperationsOnBulletDestroy(Vector2 bulletPos);
    private event OperationsOnBulletDestroy _onBulletDestroy;
    public delegate void OperationsOnBulletLandWithoutHit(Entity targetEntity);
    private event OperationsOnBulletLandWithoutHit _onLandWithoutHit;
    public Bullet(AttackBase.OperationsBeforeTakeDamage operationsBeforeTakeDamage, AttackBase.OperationsAfterTakeDamage operationsAfterTakeDamage, OperationsOnBulletDestroy operationsOnBulletDestroy, BulletData bulletData, Entity originEntity, Entity targetEntity, Vector2 targetPos, Vector2 bulletSpawnPosition, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, OperationsOnBulletLandWithoutHit onLandWithoutHit = null)
    {
        _onBeforeTakeDamage = operationsBeforeTakeDamage;
        _onAfterTakeDamage = operationsAfterTakeDamage;
        _onBulletDestroy = operationsOnBulletDestroy;
        _onLandWithoutHit = onLandWithoutHit;
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
            EffectManager.Manager.CreateEffect(bulletData.BulletSpawnEffect, bulletSpawnPosition, Quaternion.Euler(0, originEntity.facing.CurrentDirection.left ? 180 : 0, 0), LevelResourceSharing.LM, 1, true);
        }
        BulletFly();
        _activeBullets.Add(this);
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
        while (!_destroyed)
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
            _onBeforeTakeDamage?.Invoke(_targetEntity, ref _multiplyer, ref _defPenetrate, ref _mgrPenetrate, ref _defPenetrate_value, ref _mgrPenetrate_value, ref _damageType, 0);
            bool isDeadly = _targetEntity.Stats.ApplyDamage(_originEntity, _damage, _multiplyer, _defPenetrate, _mgrPenetrate, _defPenetrate_value, _mgrPenetrate_value, _damageType, 0);
            _onAfterTakeDamage?.Invoke(_targetEntity, _multiplyer, _defPenetrate, _mgrPenetrate, _defPenetrate_value, _mgrPenetrate_value, _damageType, 0, isDeadly);
        }
        else if (_targetEntity != null)
        {
            // AllowNoTarget 存活弹：主目标已死，落地不再对其结算，仍以其位置为圆心触发溅射
            _onLandWithoutHit?.Invoke(_targetEntity);
        }
        DestroyBullet();
    }
    private void DestroyBullet()
    {
        if (_destroyed) return;
        _destroyed = true;
        EffectManager.Manager.ReturnEffect(_bulletObject);
        _onBulletDestroy?.Invoke(_bulletObject.transform.position);
        if (_bulletDestroyEffect)
        {
            EffectManager.Manager.CreateEffect(_bulletDestroyEffect, _bulletObject.transform.position, Quaternion.identity, LevelResourceSharing.LM, 1, true);
        }
        if (_bulletTrailObject)
        {
            EffectManager.Manager.SetEffectAutoReturn(_bulletTrailObject);
        }
        _activeBullets.Remove(this);
    }

    /// <summary>退出关卡时主动回收所有飞行中的子弹，不依赖 async cancel 路径。</summary>
    public static void ReturnAllActive()
    {
        // DestroyBullet 会修改 _activeBullets，遍历副本避免迭代异常
        var snapshot = _activeBullets.ToArray();
        _activeBullets.Clear();
        foreach (var b in snapshot)
        {
            b.DestroyBullet();
        }
    }
}
