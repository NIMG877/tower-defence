using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BeefSkill : Skill
{
    [SerializeField] private string _skill;
    public override bool SkillBegin()
    {
        if (!base.SkillBegin())
            return false;
        _thisEntity.entityAM.TrySetState(EntityState.Idle, true, new AnimationOverride { Idle = _skill });
        return true;
    }
    public override void SkillEnd()
    {
        base.SkillEnd();
        List<Entity> entityList = EntityManager.Manager.EntitySelector_Range(new (int x, int y)[1] { ((int)_thisEntity.Movement.Position.x, (int)_thisEntity.Movement.Position.y) }, _thisEntity.Camp, true, 0.5f, true);
        for (int i = 0; i < entityList.Count; i++)
        {
            if (entityList[i].TryGetComponent(out InteractableStatic staticEntity) && entityList[i].EntityData.CharacterJob != 8)
            {
                MCEnvironmentalDevice.MCED.EntityEatSomething(entityList[i], 40);
                break;
            }
        }
        _thisEntity.Die();
    }
}
