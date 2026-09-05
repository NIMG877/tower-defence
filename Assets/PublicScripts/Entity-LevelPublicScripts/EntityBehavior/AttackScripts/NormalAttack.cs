public class NormalAttack : AttackBase
{
    public override bool TryToAttack(Entity[] attackTargets, bool forceChange, bool canBeInterrupt)
    {
        if (attackTargets.Length > 0 || canBeInterrupt == false)
        {
            _thisEntity.entityAM.SetAttackBranch(AttackAnimationBranch.Normal);
            if (_thisEntity.StateMachine.TrySetAttackState(
                forceChange,
                () => { AttackByAnimation(attackTargets, canBeInterrupt); }))
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
            return HitTargetAndSplash(attackTarget, onBeforeTakeDamage, onAfterTakeDamage, _thisEntity.Stats.AttackS, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType);
        }
        else
        {
            FireBullet(onBeforeTakeDamage, onAfterTakeDamage, null, attackEffectData.BulletData, attackTarget, attackEffectData.BulletSpawnTransform.position, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType);
            return false;
        }
    }
}
