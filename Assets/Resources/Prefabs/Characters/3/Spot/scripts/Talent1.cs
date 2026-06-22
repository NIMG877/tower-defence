using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Talent1 : Talent
{
    BuffType[] buffTypes = new BuffType[1] { BuffType.phdoge_delta_rate };
    float[] floats = new float[1] { 0.2f };
    private void OperationsAfterTakeDamage(Entity target, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly)
    {
        if (damageType == 3)
        {
            target.buffController.CreateBuff(buffTypes, null, "smoke", floats, 3, false);
        }
    }
    public override void Initialize()
    {
        base.Initialize();
        _thisEntity.AttackBase.OnAfterTakeDamage += OperationsAfterTakeDamage;
    }
}
