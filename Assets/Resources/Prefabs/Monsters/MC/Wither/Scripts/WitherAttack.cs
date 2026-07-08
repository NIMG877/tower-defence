using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WitherAttack : AttackBase
{
    [SerializeField] private Transform[] _heads;
    private int _currentNum;
    public override void Initialize()
    {
        base.Initialize();
        _thisEntity.buffController.CreateBuff(new Modifier[]{ new Modifier("AttackMinNum", ModifierOp.AddFlat, 3f) }, null, "threeheadattack", -5, true);
    }
    public override bool TryToAttack(Entity[] attackTargets, bool forceChange, bool canBeInterrupt)
    {
        List<Entity> tmpTarget = _thisEntity.Combat.PriorityOrder(_thisEntity.Vision.NearbyTurrets, _thisEntity.AttackBase.TargetPriority);
        tmpTarget.AddRange(_thisEntity.Combat.PriorityOrder(_thisEntity.Vision.NearbyMonsters, _thisEntity.AttackBase.TargetPriority));
        tmpTarget.Remove(_thisEntity);
        if (tmpTarget.Count == 0)
            return false;
        int num = _heads.Length;
        if (tmpTarget.Count > num)
        {
            tmpTarget.RemoveRange(num, tmpTarget.Count - num);
        }
        else if (tmpTarget.Count < num)
        {
            int currentNum = tmpTarget.Count;
            for (int i = 0; i < num - currentNum; i++)
            {
                tmpTarget.Add(tmpTarget[i % currentNum]);
            }
        }
        attackTargets = tmpTarget.ToArray();
        if (attackTargets.Length > 0)
        {
            if (_thisEntity.entityAM.TrySetAttackState(
                forceChange,
                () => { AttackByAnimation(attackTargets, canBeInterrupt); },
                AttackAnimationBranch.Normal,
                PendingAnimationOverride))
            {
                _currentNum = 0;
                base.TryToAttack(attackTargets, forceChange, canBeInterrupt);
                return true;
            }
        }
        return false;
    }
    protected override bool AttackSingleTargetOperation(OperationsBeforeTakeDamage onBeforeTakeDamage, OperationsAfterTakeDamage onAfterTakeDamage, AttackEffectData attackEffectData, Entity attackTarget, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType)
    {
        if (attackEffectData.BulletData.BulletPrefab == null)
        {
            onBeforeTakeDamage?.Invoke(_thisEntity, ref multiplyer, ref defPenetrate, ref mgrPenetrate, ref defPenetrate_value, ref mgrPenetrate_value, ref damageType, 0);
            bool isDeadly = attackTarget.TakeDamage(_thisEntity, AttackDamageS, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, 0);
            onAfterTakeDamage?.Invoke(_thisEntity, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, 0, isDeadly);
            return isDeadly;
        }
        else
        {
            new Bullet(onBeforeTakeDamage, onAfterTakeDamage, null, attackEffectData.BulletData, _thisEntity, attackTarget, Vector2.zero, _heads[_currentNum++].position, AttackDamageS, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, 0);
            return false;
        }
    }
}
