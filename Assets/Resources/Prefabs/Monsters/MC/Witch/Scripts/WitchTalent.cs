using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WitchTalent : Talent
{
    [SerializeField] private ParticleSystem _medicalLiquidLight;
    private MedicalEffect _medicalEffectLauncher;
    private List<Entity> _effectTargets;
    private (float poison, float damage) _pd;
    private float _r;
    private ParticleSystem _bulletLiquit, _bulletTrialP;
    private TrailRenderer _bulletTrial;
    public override void Initialize()
    {
        base.Initialize();
        _medicalLiquidLight.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _medicalEffectLauncher = MedicalEffect.MedicalEffectLauncher;
        _effectTargets = new List<Entity>();
        _r = 1.414f;
        GameObject bulletPrefab = Instantiate(_thisEntity.AttackBase._attackEffectData.BulletData.BulletPrefab, new Vector3(0, 0, 10), Quaternion.identity, _thisEntity.TempContainer);
        GameObject bulletTrailPrefab = Instantiate(_thisEntity.AttackBase._attackEffectData.BulletData.BulletTrailPrefab, new Vector3(0, 0, 10), Quaternion.identity, _thisEntity.TempContainer);
        _thisEntity.AttackBase._attackEffectData.BulletData.BulletPrefab = bulletPrefab;
        _thisEntity.AttackBase._attackEffectData.BulletData.BulletTrailPrefab = bulletTrailPrefab;
        _bulletLiquit = bulletPrefab.GetComponentInChildren<ParticleSystem>();
        _bulletTrialP = bulletTrailPrefab.GetComponent<ParticleSystem>();
        _bulletTrial = bulletTrailPrefab.GetComponentInChildren<TrailRenderer>();
        _thisEntity.AttackBase.OnAfterTargetSelect += new AttackBase.OperationsOnAfterTargetSelect((List<Entity> targets, int selectMaxNUm, int selectMinNum, bool sameComp) =>
        {
            if (targets.Count > 0)
            {
                Vector2 pos = targets[0].EntityPosition;
                _effectTargets = EntityManager.Manager.EntitySelector_Radius((pos.x, pos.y), _thisEntity.Camp, false, _r, false);
                _pd = CaculateValue(_effectTargets);
            }
        });
        _thisEntity.entityAM.OnAttackAnimationBegin += new AnimationMachine.OperationsOnAttackAnimationBegin(() =>
        {
            _medicalLiquidLight.Play(true);
            if (_pd.poison >= _pd.damage)
            {
                SetMainColor(_medicalEffectLauncher.GetMedicalEffectColor(2));
            }
            else
            {
                SetMainColor(_medicalEffectLauncher.GetMedicalEffectColor(3));
            }

        });
        _thisEntity.AttackBase.OnAttackSuccessfully += new AttackBase.OperationsOnAttackSuccessfully(() =>
        {
            _medicalLiquidLight.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        });
        _thisEntity.AttackBase.OnAttackInterrupt += new AttackBase.OperationsOnAttackInterrupt(() =>
        {
            _medicalLiquidLight.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        });
        _thisEntity.AttackBase.OnBeforeTakeDamage += new AttackBase.OperationsBeforeTakeDamage((Entity target, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int damageType, int applyType) =>
        {
            multiplyer = 0;
            if (_pd.poison >= _pd.damage)
            {
                _medicalEffectLauncher.TakeEffect_Radius((target.transform.position.x, target.transform.position.y), _thisEntity, _r, 2, 2, 5);
            }
            else
            {
                _medicalEffectLauncher.TakeEffect_Radius((target.transform.position.x, target.transform.position.y), _thisEntity, _r, 1, 3, 2);
            }

        });
    }
    private void SetMainColor(Color mainColor)
    {
        ParticleSystem.MainModule main = _medicalLiquidLight.main;
        ParticleSystem.MainModule liquidMain = _bulletLiquit.main;
        ParticleSystem.MainModule trailP = _bulletTrialP.main;
        Gradient trailColor = new Gradient();
        trailColor.SetKeys(
            new GradientColorKey[] { new GradientColorKey(mainColor * 0.5f, 0), new GradientColorKey(mainColor, 1) },
            new GradientAlphaKey[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(1, 0.65f), new GradientAlphaKey(0, 1) }
        );
        main.startColor = new ParticleSystem.MinMaxGradient(mainColor);
        liquidMain.startColor = new ParticleSystem.MinMaxGradient(mainColor);
        trailP.startColor = new ParticleSystem.MinMaxGradient(mainColor);
        _bulletTrial.colorGradient = trailColor;
    }
    private (float poisonValue, float damageValue) CaculateValue(List<Entity> entities)
    {
        //poison or damage
        float poisonValue = 0;
        float damageValue = 0;
        int effectCount = entities.Count;
        float yuzhi = 425;
        entities = _thisEntity.Combat.PriorityOrder(entities, OrderLogic.Priority_Des);
        for (int i = 0; i < effectCount; i++)
        {
            float hp = entities[i].Stats.CurrentHp;
            if (hp > yuzhi)
            {
                poisonValue += (effectCount - i) * (hp - yuzhi);
            }
            else
            {
                damageValue += (effectCount - i) * (yuzhi - hp);
            }
        }
        return (poisonValue, damageValue);
    }
}
