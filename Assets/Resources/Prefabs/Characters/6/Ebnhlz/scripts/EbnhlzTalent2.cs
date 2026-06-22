using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EbnhlzTalent2 : Talent
{
    private float _radius = 1.1f;
    private ChargeAttack _chargeAttack;
    private List<Entity> entitiesAround;
    public override void PreWarm()
    {
        base.PreWarm();
        _chargeAttack = _thisEntity.GetComponent<ChargeAttack>();
    }
    public override void Initialize()
    {
        _chargeAttack.OnAfterTakeDamage += ((Entity target, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly) =>
        {
            Vector3 pos = target.gameObject.transform.position;
            entitiesAround = EntityManager.Manager.EntitySelector_Radius((pos.x, pos.y), _thisEntity.Camp, false, _radius, true);
            if (entitiesAround.Count <= 1)
            {
                target.TakeDamage(_thisEntity, _chargeAttack.AttackDamageS, 0.17f, 0, 0, 0, 0, 1, 2);
            }
        });
    }
}
