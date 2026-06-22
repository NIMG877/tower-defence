using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EbnhlzSkill3 : Skill
{
    [SerializeField] private string _skillAttack, _skillCharge;
    private ChargeAttack _chargeAttack;
    private Buff _skill3Buff;
    private EbnhlzTalent1 _ebnhlzTalent1;
    private AnimationOverrideHandle _animationOverride;
    public override void PreWarm()
    {
        base.PreWarm();
        _chargeAttack = _thisEntity.GetComponent<ChargeAttack>();
        _ebnhlzTalent1 = _thisEntity.GetComponent<EbnhlzTalent1>();
    }
    public override bool SkillBegin()
    {
        if (!base.SkillBegin())
            return false;
        _ebnhlzTalent1._chargeRate *= 1.4f;
        _skill3Buff = _thisEntity.buffController.CreateBuff(new BuffType[2] { BuffType.atkspd_delta_value, BuffType.atk_delta_percent }, null, "SoundOfSilence", new float[2] { 80, 0.65f }, -5, false);
        _animationOverride = _thisEntity.entityAM.AddOverride(this, new AnimationOverride
        {
            AttackClose = _skillAttack,
            AttackRemote = _skillAttack,
            Charge = _skillCharge,
        });
        _chargeAttack.ForceResetAttack();
        return true;
    }
    public override void SkillEnd()
    {
        base.SkillEnd();
        _ebnhlzTalent1._chargeRate /= 1.4f;
        _thisEntity.buffController.DestroyBuff(_skill3Buff);
        _thisEntity.entityAM.RemoveOverride(_animationOverride);
        _chargeAttack.ForceResetAttack();
    }
}
