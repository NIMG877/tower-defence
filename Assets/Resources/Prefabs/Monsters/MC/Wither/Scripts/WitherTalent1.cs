using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WitherTalent1 : Talent
{
    public bool HaveShield;
    public override void Initialize()
    {
        HaveShield = false;
        _thisEntity.AttackBase.OnBeforeTakeDamage += new AttackBase.OperationsBeforeTakeDamage((Entity target, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int damageType, int applyType) =>
        {
            if (target.Camp == 2)
            {
                multiplyer = 10;
            }
        });
        _thisEntity.AttackBase.OnAfterTakeDamage += new AttackBase.OperationsAfterTakeDamage((Entity target, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly) =>
        {
            if (isDeadly)
            {
                if (HaveShield)
                {
                    if (_thisEntity.Stats.CurrentHp < _thisEntity.Stats.MaxHpS / 2 - 2500)
                    {
                        _thisEntity.Stats.ApplyDamage(_thisEntity, 2500, 1, 0, 0, 0, 0, 3, 0);
                    }
                }
                else
                {
                    _thisEntity.Stats.ApplyDamage(_thisEntity, 5000, 1, 0, 0, 0, 0, 3, 0);
                }
            }
        });
    }
}
