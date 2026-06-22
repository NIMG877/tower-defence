using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EbnhlzTalent1 : Talent
{
    [HideInInspector] public float _chargeRate;
    private ChargeAttack _chargeAttack;
    public override void Initialize()
    {
        _chargeRate = 1.43f;
        _chargeAttack.OnBeforeChargeTakeDamage += ((Entity target, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int damageType, int applyType) =>
        {
            multiplyer *= _chargeRate;
        });
    }
    public override void PreWarm()
    {
        base.PreWarm();
        _chargeAttack = _thisEntity.GetComponent<ChargeAttack>();
    }
}
