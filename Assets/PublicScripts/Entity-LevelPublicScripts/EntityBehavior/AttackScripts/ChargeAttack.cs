using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Spine;

public sealed class ChargeReserveHandle
{
    internal int Id;
    internal ChargeAttack Attack;
}

public class ChargeAttack : AttackBase
{
    public OperationsBeforeTakeDamage OnBeforeChargeTakeDamage;
    public OperationsAfterTakeDamage OnAfterChargeTakeDamage;
    private int _defaultChargeCount;
    private int _nextReserveId;
    private readonly List<ChargeReservePool> _reservePools = new List<ChargeReservePool>();
    [SerializeField] private int _defaultChargeCapacity = 4;
    [SerializeField] private GameObject[] _chargeEffects;
    [SerializeField] private AttackEffectData _chargeEffectData;

    private sealed class ChargeReservePool
    {
        public readonly int Id;
        public readonly object Owner;
        public readonly int Capacity;
        public readonly Func<Entity, bool> CanUse;
        public int Count;

        public ChargeReservePool(int id, object owner, int capacity, Func<Entity, bool> canUse)
        {
            Id = id;
            Owner = owner;
            Capacity = capacity;
            CanUse = canUse;
        }
    }

    public override void Initialize()
    {
        base.Initialize();
        ClearStoredCharges();
    }
    public override void Dormancy()
    {
        base.Dormancy();
        OnBeforeChargeTakeDamage = null;
        OnAfterChargeTakeDamage = null;
        _reservePools.Clear();
        ClearStoredCharges();
    }

    public ChargeReserveHandle AddReservePool(object owner, int capacity, Func<Entity, bool> canUse)
    {
        if (capacity <= 0) return null;
        var handle = new ChargeReserveHandle { Id = ++_nextReserveId, Attack = this };
        _reservePools.Add(new ChargeReservePool(handle.Id, owner, capacity, canUse));
        return handle;
    }

    public void RemoveReservePool(ChargeReserveHandle handle)
    {
        if (handle == null || handle.Attack != this) return;
        _reservePools.RemoveAll(pool => pool.Id == handle.Id);
        RefreshChargeEffects();
    }

    public void RemoveReservePools(object owner)
    {
        _reservePools.RemoveAll(pool => ReferenceEquals(pool.Owner, owner));
        RefreshChargeEffects();
    }

    public override bool TryToAttack(Entity[] attackTargets, bool forceChange, bool canBeInterrupt)
    {
        if (attackTargets.Length > 0)
        {
            if (_thisEntity.entityAM.TrySetAttackState(
                forceChange,
                () => { AttackByAnimation(attackTargets, canBeInterrupt); },
                AttackAnimationBranch.Normal,
                PendingAnimationOverride))
            {
                base.TryToAttack(attackTargets, forceChange, canBeInterrupt);
                return true;
            }
        }
        else if (CanStoreCharge())
        {
            if (_thisEntity.entityAM.TrySetAttackState(
                forceChange,
                () => { SpawnChargeEffect(); },
                AttackAnimationBranch.Charge))
            {
                return true;
            }
        }
        return false;
    }

    private bool CanStoreCharge()
    {
        if (_defaultChargeCount < Math.Max(0, _defaultChargeCapacity)) return true;
        for (int i = 0; i < _reservePools.Count; i++)
        {
            if (_reservePools[i].Count < _reservePools[i].Capacity) return true;
        }
        return false;
    }

    private void SpawnChargeEffect()
    {
        if (_defaultChargeCount < Math.Max(0, _defaultChargeCapacity))
        {
            _defaultChargeCount++;
        }
        else
        {
            for (int i = 0; i < _reservePools.Count; i++)
            {
                ChargeReservePool pool = _reservePools[i];
                if (pool.Count >= pool.Capacity) continue;
                pool.Count++;
                break;
            }
        }
        RefreshChargeEffects();
    }

    protected override bool AttackSingleTargetOperation(OperationsBeforeTakeDamage onBeforeTakeDamage, OperationsAfterTakeDamage onAfterTakeDamage, AttackEffectData attackEffectData, Entity attackTarget, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType)
    {
        int consumedChargeCount = _defaultChargeCount;
        _defaultChargeCount = 0;
        for (int i = 0; i < _reservePools.Count; i++)
        {
            ChargeReservePool pool = _reservePools[i];
            if (pool.CanUse != null && !pool.CanUse(attackTarget)) continue;
            consumedChargeCount += pool.Count;
            pool.Count = 0;
        }

        for (int i = 0; i < consumedChargeCount; i++)
        {
            new Bullet(onBeforeTakeDamage + OnBeforeChargeTakeDamage, onAfterTakeDamage + OnAfterChargeTakeDamage, null, _chargeEffectData.BulletData, _thisEntity, attackTarget, Vector2.zero, GetChargeSpawnPosition(i), _thisEntity.Stats.AttackS, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, 0);
        }
        RefreshChargeEffects();

        if (attackEffectData.BulletData.BulletPrefab == null)
        {
            onBeforeTakeDamage?.Invoke(attackTarget, ref multiplyer, ref defPenetrate, ref mgrPenetrate, ref defPenetrate_value, ref mgrPenetrate_value, ref damageType, 0);
            bool isDeadly = attackTarget.Stats.ApplyDamage(_thisEntity, _thisEntity.Stats.AttackS, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, 0);
            onAfterTakeDamage?.Invoke(attackTarget, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, 0, isDeadly);
            return isDeadly;
        }
        else
        {
            new Bullet(onBeforeTakeDamage, onAfterTakeDamage, null, attackEffectData.BulletData, _thisEntity, attackTarget, Vector2.zero, attackEffectData.BulletSpawnTransform.position, _thisEntity.Stats.AttackS, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, 0);
            return false;
        }
    }

    private Vector3 GetChargeSpawnPosition(int index)
    {
        if (_chargeEffects != null && _chargeEffects.Length > 0)
        {
            return _chargeEffects[Math.Min(index, _chargeEffects.Length - 1)].transform.position;
        }
        if (_chargeEffectData.BulletSpawnTransform != null)
        {
            return _chargeEffectData.BulletSpawnTransform.position;
        }
        return transform.position;
    }

    private void ClearStoredCharges()
    {
        _defaultChargeCount = 0;
        for (int i = 0; i < _reservePools.Count; i++) _reservePools[i].Count = 0;
        RefreshChargeEffects();
    }

    private void RefreshChargeEffects()
    {
        if (_chargeEffects == null) return;
        int visibleChargeCount = _defaultChargeCount;
        for (int i = 0; i < _reservePools.Count; i++) visibleChargeCount += _reservePools[i].Count;
        for (int i = 0; i < _chargeEffects.Length; i++)
        {
            if (_chargeEffects[i] != null) _chargeEffects[i].SetActive(i < visibleChargeCount);
        }
    }
}
