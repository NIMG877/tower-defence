using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Spine;

public class WitchSkill : Skill
{
    [SerializeField] private ParticleSystem _medicalLiquidLight;
    [SerializeField] private string _drink;
    private MedicalEffect _medicalEffectLauncher;
    private AnimationOverrideHandle _animationOverride;
    public override bool SkillBegin()
    {
        if (_thisEntity.Stats.CurrentHpRate >= 0.7f || !base.SkillBegin())
            return false;
        _animationOverride = _thisEntity.entityAM.AddOverride(this, new AnimationOverride
        {
            [AnimationSlot.AttackClose] = _drink,
            [AnimationSlot.AttackRemote] = _drink,
        });
        _thisEntity.AttackBase.TryToAttack(new Entity[1] { _thisEntity }, true, false);
        _medicalLiquidLight.Play(true);
        ParticleSystem.MainModule main = _medicalLiquidLight.main;
        main.startColor = new ParticleSystem.MinMaxGradient(_medicalEffectLauncher.GetMedicalEffectColor(1));
        return true;
    }
    public override void SkillEnd()
    {
        base.SkillEnd();
        _medicalLiquidLight.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _medicalEffectLauncher.TakeEffect_Single(_thisEntity, _thisEntity, 1, 1, 20);
        _thisEntity.entityAM.RemoveOverride(_animationOverride);
    }
    public override void Initialize()
    {
        base.Initialize();
        _medicalEffectLauncher = MedicalEffect.MedicalEffectLauncher;
    }
}
