public class NormalAttack : AttackBase
{
    public override bool TryToAttack(Entity[] attackTargets, bool forceChange, bool canBeInterrupt)
    {
        if (attackTargets.Length > 0 || canBeInterrupt == false)
        {
            if (_thisEntity.entityAM.TrySetAttackState(forceChange, () => { AttackByAnimation(attackTargets, canBeInterrupt); }, PendingAnimationOverride))
            {
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
            onBeforeTakeDamage?.Invoke(attackTarget, ref multiplyer, ref defPenetrate, ref mgrPenetrate, ref defPenetrate_value, ref mgrPenetrate_value, ref damageType, 0);
            bool isDeadly = attackTarget.TakeDamage(_thisEntity, AttackDamageS, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, 0);
            onAfterTakeDamage?.Invoke(attackTarget, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, 0, isDeadly);
            return isDeadly;
        }
        else
        {
            new Bullet(onBeforeTakeDamage, onAfterTakeDamage, null, attackEffectData.BulletData, _thisEntity, attackTarget, UnityEngine.Vector2.zero, attackEffectData.BulletSpawnTransform.position, AttackDamageS, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, 0);
            return false;
        }
    }
}
