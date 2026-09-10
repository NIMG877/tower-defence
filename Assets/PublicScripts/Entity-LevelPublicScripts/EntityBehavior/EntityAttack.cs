using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime-only attack subsystem. Entity owns its lifecycle and ticks it after vision refresh.
/// Bullet settings are sourced directly from <see cref="EntityData.Bullets"/>.
/// </summary>
public sealed class EntityAttack
{
    [Serializable]
    public struct AttackBulletData
    {
        public BulletData BulletData;
        public Transform BulletSpawnTransform;
    }

    public delegate void OperationsOnAttackInterrupt();
    public delegate void OperationsOnAttackSuccessfully();
    public delegate void OperationsOnAttackIdle();
    public delegate void OperationsBeforeAttack(Entity target, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrateValue, ref float mgrPenetrateValue, ref int cumbo, ref int damageType, int applyType);
    public delegate void OperationsBeforeTakeDamage(Entity target, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrateValue, ref float mgrPenetrateValue, ref int damageType, int applyType);
    public delegate void OperationsAfterAttack(Entity target, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrateValue, float mgrPenetrateValue, int damageType, int applyType, bool isDeadly);
    public delegate void OperationsAfterTakeDamage(Entity target, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrateValue, float mgrPenetrateValue, int damageType, int applyType, bool isDeadly);
    public delegate void OperationsOnBeforeTargetSelect(List<Entity> targets, ref int selectMaxNum, ref int selectMinNum, ref bool sameComp);
    public delegate void OperationsOnAfterTargetSelect(List<Entity> targets, int selectMaxNum, int selectMinNum, bool sameComp);

    public event OperationsOnAttackInterrupt OnAttackInterrupt;
    public event OperationsOnAttackSuccessfully OnAttackSuccessfully;
    public event OperationsOnAttackIdle OnAttackIdle;
    public event OperationsBeforeAttack OnBeforeAttack;
    public event OperationsBeforeTakeDamage OnBeforeTakeDamage;
    public event OperationsAfterAttack OnAfterAttack;
    public event OperationsAfterTakeDamage OnAfterTakeDamage;
    public event OperationsOnBeforeTargetSelect OnBeforeTargetSelect;
    public event OperationsOnAfterTargetSelect OnAfterTargetSelect;

    [NonSerialized] public OrderLogic TargetPriority;
    [NonSerialized] public int DamageType;

    public (int x, int y)[] AttackRangeS => _attackRangeF;
    public float AttackRadiusS => _attackRadiusF;

    private readonly Entity _entity;
    private readonly Transform _root;
    private AttackBulletData _defaultBulletData;
    private List<AttackBulletData> _bulletDatas = new List<AttackBulletData>();
    private (int x, int y)[] _attackRangeF;
    private float _attackRadiusF;
    private float _attackTimer;

    private const string BulletMuzzleName = "Muzzle";

    public EntityAttack(Entity entity)
    {
        _entity = entity ?? throw new ArgumentNullException(nameof(entity));
        _root = entity.transform;
    }

    public void PreWarm()
    {
        AttributesCaculateFirst();
        LoadBulletConfiguration(_entity.EntityData != null ? _entity.EntityData.Bullets : null);
    }


    public void Initialize()
    {
        _attackTimer = 0;
        DamageType = _entity.EntityData.DamageType;
        TargetPriority = _entity.Stats.TargetPriorityS;
    }

    public void Dormancy()
    {
        OnAttackInterrupt = null;
        OnAttackSuccessfully = null;
        OnAttackIdle = null;
        OnBeforeAttack = null;
        OnAfterAttack = null;
        OnBeforeTakeDamage = null;
        OnAfterTakeDamage = null;
        OnBeforeTargetSelect = null;
        OnAfterTargetSelect = null;
    }

    public void Tick(float dt)
    {
        if (!_entity.Stats.IsActive) return;
        if (_attackTimer > 0)
        {
            _attackTimer -= dt * _entity.Stats.BaseAttackTimeBase / _entity.Stats.BaseAttackTimeS;
            return;
        }

        EntityState state = _entity.StateMachine.CurrentState;
        if (state == EntityState.Start || state == EntityState.Cast || state == EntityState.Die) return;

        Entity[] targets = AttackTargetSelect(_entity.Stats.AttackNumS, _entity.Stats.AttackMinNumS);
        if (targets.Length == 0) OnAttackIdle?.Invoke();
        if (TryToAttack(targets, false, true))
            _attackTimer = _entity.Stats.BaseAttackTimeBase;
    }

    public bool ForceResetAttack()
    {
        _attackTimer = _entity.Stats.BaseAttackTimeBase;
        return TryToAttack(AttackTargetSelect(_entity.Stats.AttackNumS, _entity.Stats.AttackMinNumS), true, true);
    }


    public bool TryGetBulletData(int index, out AttackBulletData data)
    {
        if (index < 0 || index >= _bulletDatas.Count)
        {
            Debug.LogError($"[EntityAttack] Bullet data index {index} out of range ({_bulletDatas.Count} entries on {_entity.name}).");
            data = default;
            return false;
        }
        data = _bulletDatas[index];
        return true;
    }

    /// <summary>Default implementation is the former NormalAttack behavior.</summary>
    public bool TryToAttack(Entity[] attackTargets, bool forceChange, bool canBeInterrupt)
    {
        if (attackTargets.Length == 0 && canBeInterrupt) return false;

        _entity.entityAM.SetAttackBranch(AttackAnimationBranch.Normal);
        if (!_entity.StateMachine.TrySetAttackState(forceChange, () => AttackByAnimation(attackTargets, canBeInterrupt)))
            return false;

        if (attackTargets.Length > 0)
        {
            Vector3 centerPos = Vector3.zero;
            for (int i = 0; i < attackTargets.Length; i++) centerPos += attackTargets[i].transform.position;
            _entity.facing.SetDirection(centerPos / attackTargets.Length);
        }
        return true;
    }

    private async void AttackByAnimation(Entity[] attackTargets, bool canInterrupt)
    {
        if (canInterrupt)
        {
            attackTargets = _entity.Combat.EntityUpdate(attackTargets);
            if (attackTargets.Length == 0)
            {
                attackTargets = AttackTargetSelect(_entity.Stats.AttackNumS, _entity.Stats.AttackMinNumS);
                if (attackTargets.Length == 0)
                {
                    OnAttackInterrupt?.Invoke();
                    _entity.StateMachine.TrySetState(EntityState.Idle, true);
                    _attackTimer = 0;
                    return;
                }
            }
        }

        for (int i = 0; i < attackTargets.Length; i++)
        {
            float multiplyer = 1, defPenetrate = 0, mgrPenetrate = 0, defPenetrateValue = 0, mgrPenetrateValue = 0;
            int cumbo = 1;
            int damageType = DamageType;
            OnBeforeAttack?.Invoke(attackTargets[i], ref multiplyer, ref defPenetrate, ref mgrPenetrate, ref defPenetrateValue, ref mgrPenetrateValue, ref cumbo, ref damageType, 0);
            bool isDeadly = AttackSingleTargetOperation(OnBeforeTakeDamage, OnAfterTakeDamage, _defaultBulletData, attackTargets[i], multiplyer, defPenetrate, mgrPenetrate, defPenetrateValue, mgrPenetrateValue, damageType);
            for (int j = 1; j < cumbo; j++)
            {
                await UniTask.WaitForSeconds(0.1f);
                isDeadly = AttackSingleTargetOperation(OnBeforeTakeDamage, OnAfterTakeDamage, _defaultBulletData, attackTargets[i], multiplyer, defPenetrate, mgrPenetrate, defPenetrateValue, mgrPenetrateValue, damageType);
            }
            OnAfterAttack?.Invoke(attackTargets[i], multiplyer, defPenetrate, mgrPenetrate, defPenetrateValue, mgrPenetrateValue, damageType, 0, isDeadly);
        }
        OnAttackSuccessfully?.Invoke();
    }

    private bool AttackSingleTargetOperation(OperationsBeforeTakeDamage onBeforeTakeDamage, OperationsAfterTakeDamage onAfterTakeDamage, AttackBulletData bulletData, Entity target, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrateValue, float mgrPenetrateValue, int damageType)
    {
        if (bulletData.BulletData == null || bulletData.BulletData.BulletPrefab == null)
            return HitTargetAndSplash(target, onBeforeTakeDamage, onAfterTakeDamage, _entity.Stats.AttackS, multiplyer, defPenetrate, mgrPenetrate, defPenetrateValue, mgrPenetrateValue, damageType);

        if (bulletData.BulletSpawnTransform == null)
        {
            Debug.LogWarning($"[EntityAttack] {_entity.name} has a projectile but no resolved BulletSpawnTransform; attack skipped.");
            return false;
        }
        FireBullet(onBeforeTakeDamage, onAfterTakeDamage, null, bulletData.BulletData, target, bulletData.BulletSpawnTransform.position, multiplyer, defPenetrate, mgrPenetrate, defPenetrateValue, mgrPenetrateValue, damageType);
        return false;
    }

    private bool HitTargetAndSplash(Entity target, OperationsBeforeTakeDamage before, OperationsAfterTakeDamage after, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrateValue, float mgrPenetrateValue, int damageType)
    {
        before?.Invoke(target, ref multiplyer, ref defPenetrate, ref mgrPenetrate, ref defPenetrateValue, ref mgrPenetrateValue, ref damageType, 0);
        bool deadly = target.Stats.ApplyDamage(_entity, damage, multiplyer, defPenetrate, mgrPenetrate, defPenetrateValue, mgrPenetrateValue, damageType, 0);
        after?.Invoke(target, multiplyer, defPenetrate, mgrPenetrate, defPenetrateValue, mgrPenetrateValue, damageType, 0, deadly);
        SplashAroundTarget(target, damage, multiplyer, defPenetrate, mgrPenetrate, defPenetrateValue, mgrPenetrateValue, damageType, before, after);
        return deadly;
    }

    private void SplashAroundTarget(Entity center, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrateValue, float mgrPenetrateValue, int damageType, OperationsBeforeTakeDamage before, OperationsAfterTakeDamage after)
    {
        float radius = _entity.Stats.SplashRadiusS;
        if (radius <= 0) return;
        List<Entity> victims = EntityManager.Manager.EntitySelector_Radius((center.transform.position.x, center.transform.position.y), _entity.Movement.Camp, damageType == 3, radius, false);
        for (int i = 0; i < victims.Count; i++)
        {
            if (victims[i] == center) continue;
            HitTarget(victims[i], before, after, damage, multiplyer, defPenetrate, mgrPenetrate, defPenetrateValue, mgrPenetrateValue, damageType, 1);
        }
    }

    private bool HitTarget(Entity target, OperationsBeforeTakeDamage before, OperationsAfterTakeDamage after, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrateValue, float mgrPenetrateValue, int damageType, int applyType)
    {
        before?.Invoke(target, ref multiplyer, ref defPenetrate, ref mgrPenetrate, ref defPenetrateValue, ref mgrPenetrateValue, ref damageType, applyType);
        bool deadly = target.Stats.ApplyDamage(_entity, damage, multiplyer, defPenetrate, mgrPenetrate, defPenetrateValue, mgrPenetrateValue, damageType, applyType);
        after?.Invoke(target, multiplyer, defPenetrate, mgrPenetrate, defPenetrateValue, mgrPenetrateValue, damageType, applyType, deadly);
        return deadly;
    }

    private void FireBullet(OperationsBeforeTakeDamage before, OperationsAfterTakeDamage after, Bullet.OperationsOnBulletDestroy destroy, BulletData bulletData, Entity target, Vector2 spawnPosition, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrateValue, float mgrPenetrateValue, int damageType)
    {
        float damage = _entity.Stats.AttackS;
        OperationsAfterTakeDamage afterChain = after;
        afterChain += (hitTarget, m, dp, mp, dpv, mpv, dt, at, deadly) => SplashAroundTarget(hitTarget, damage, m, dp, mp, dpv, mpv, dt, before, after);
        new Bullet(before, afterChain, destroy, bulletData, _entity, target, Vector2.zero, spawnPosition, damage, multiplyer, defPenetrate, mgrPenetrate, defPenetrateValue, mgrPenetrateValue, damageType, 0,
            landCenter => SplashAroundTarget(landCenter, damage, multiplyer, defPenetrate, mgrPenetrate, defPenetrateValue, mgrPenetrateValue, damageType, before, after));
    }

    private Entity[] AttackTargetSelect(int selectMaxNum, int selectMinNum)
    {
        List<Entity> targets;
        bool sameComp;
        if (_entity.Movement.Camp == 1 && DamageType != 3) { targets = new List<Entity>(_entity.Vision.NearbyMonsters); sameComp = false; }
        else if (_entity.Movement.Camp == 1) { targets = new List<Entity>(_entity.Vision.NearbyTurrets); sameComp = true; }
        else if (_entity.Movement.Camp == 2 && DamageType != 3) { targets = new List<Entity>(_entity.Vision.NearbyTurrets); sameComp = false; }
        else if (_entity.Movement.Camp == 2) { targets = new List<Entity>(_entity.Vision.NearbyMonsters); sameComp = true; }
        else { targets = new List<Entity>(_entity.Vision.NearbyMonsters); targets.AddRange(_entity.Vision.NearbyTurrets); sameComp = false; }

        targets = _entity.Combat.PriorityOrder(targets, TargetPriority);
        OnBeforeTargetSelect?.Invoke(targets, ref selectMaxNum, ref selectMinNum, ref sameComp);
        if (targets.Count > 0 && selectMaxNum >= 0)
        {
            if (targets.Count > selectMaxNum) targets.RemoveRange(selectMaxNum, targets.Count - selectMaxNum);
            else if (targets.Count < selectMinNum)
            {
                int count = targets.Count;
                for (int i = 0; i < selectMinNum - count; i++) targets.Add(targets[i % count]);
            }
        }
        OnAfterTargetSelect?.Invoke(targets, selectMaxNum, selectMinNum, sameComp);
        return targets.ToArray();
    }

    private void AttributesCaculateFirst()
    {
        EntityData data = _entity.EntityData;
        var source = data.VisionRange;
        _attackRangeF = new (int x, int y)[source.Count];
        for (int i = 0; i < source.Count; i++) _attackRangeF[i] = (source[i].x, source[i].y);
        _attackRadiusF = data.VisionRadius;
    }

    private void LoadBulletConfiguration(IReadOnlyList<BulletData> bullets)
    {
        _bulletDatas = new List<AttackBulletData>();
        if (bullets != null)
        {
            for (int i = 0; i < bullets.Count; i++)
                _bulletDatas.Add(CreateRuntimeBulletData(bullets[i], $"Bullets[{i}]"));
        }
        _defaultBulletData = _bulletDatas.Count > 0 ? _bulletDatas[0] : default;
    }

    private AttackBulletData CreateRuntimeBulletData(BulletData source, string label)
    {
        return new AttackBulletData
        {
            BulletData = source,
            BulletSpawnTransform = FindBulletMuzzle(source, label),
        };
    }

    private Transform FindBulletMuzzle(BulletData bulletData, string label)
    {
        if (bulletData == null || bulletData.BulletPrefab == null) return null;

        Transform muzzle = null;
        Transform[] transforms = _root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] == _root || transforms[i].name != BulletMuzzleName) continue;
            if (muzzle != null)
            {
                Debug.LogWarning($"[EntityAttack] {_entity.name}: {label} found multiple '{BulletMuzzleName}' transforms. A projectile entity must have exactly one.");
                return null;
            }
            muzzle = transforms[i];
        }


        return muzzle;
    }
}
