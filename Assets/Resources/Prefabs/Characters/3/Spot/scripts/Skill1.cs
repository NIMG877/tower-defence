using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public class Skill1 : Skill
{
    BuffType[] buffTypes = new BuffType[2] { BuffType.atk_delta_percent, BuffType.batkt_delta_value };
    float[] values = new float[2] { 0.45f, 1.3f };
    Buff buff;
    private AnimationOverrideHandle _animationOverride;
    public string _skillAttack;
    public override bool SkillBegin()
    {
        if (!base.SkillBegin())
            return false;
        _thisEntity.AttackBase.EntityOrderLogic = OrderLogic.Hprate_NoFull_Asc;
        _thisEntity.AttackBase.DamageType = 3;
        buff = _thisEntity.buffController.CreateBuff(buffTypes, null, "subcure", values, -5, false);
        _animationOverride = _thisEntity.entityAM.AddOverride(this, new AnimationOverride
        {
            AttackClose = _skillAttack,
            AttackRemote = _skillAttack,
        });
        _thisEntity.AttackBase.ForceResetAttack();
        return true;
    }
    public override void SkillEnd()
    {
        base.SkillEnd();
        _thisEntity.AttackBase.EntityOrderLogic = OrderLogic.ResistFirst_Priority_Des;
        _thisEntity.AttackBase.DamageType = 0;
        _thisEntity.buffController.DestroyBuff(buff);
        _thisEntity.entityAM.RemoveOverride(_animationOverride);
        _thisEntity.AttackBase.ForceResetAttack();
    }
}
