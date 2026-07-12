using Codice.CM.Client.Differences.Merge;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MedicalEffect
{
    public static MedicalEffect MedicalEffectLauncher
    {
        get
        {
            if (_medicalEffectLauncher == null)
            {
                _medicalEffectLauncher = new MedicalEffect();
            }
            return _medicalEffectLauncher;
        }
    }
    public MedicalEffect()
    {
        _medicalEffect_stay = Resources.Load<GameObject>("Prefabs/Monsters/MC/MedicalEffect/Effects/MedicalEffect_stay");
        _medicalEffect_burst = Resources.Load<GameObject>("Prefabs/Monsters/MC/MedicalEffect/Effects/MedicalEffect_burst");
        _medicalEffect_abhere_stay = Resources.Load<GameObject>("Prefabs/Monsters/MC/MedicalEffect/Effects/MedicalEffect_adhere_stay");
        _medicalEffect_abhere_burst = Resources.Load<GameObject>("Prefabs/Monsters/MC/MedicalEffect/Effects/MedicalEffect_adhere_burst");
    }

    private static MedicalEffect _medicalEffectLauncher;
    // 0-生命回复 1-瞬间治疗 2-中毒 3-瞬间伤害 4-饥饿 5-饱腹 ...
    private GameObject _medicalEffect_stay, _medicalEffect_burst, _medicalEffect_abhere_stay, _medicalEffect_abhere_burst;
    /// <summary>
    /// 创建药水效果
    /// </summary>
    /// <param name="medicalEffectOrigin">药水效果来源</param>
    /// <param name="medicalEffectTarget">药水效果目标(置空则为喷溅型药水，填写则为饮用型药水)</param>
    /// <param name="medicalEffectRadius">喷溅型药水溅射半径</param>
    /// <param name="medicalEffectDuration">药水持续时间</param>
    /// <param name="medicalEffectType">药水类型0-生命回复 1-瞬间治疗 2-中毒 3-瞬间伤害 4-饥饿 5-饱腹 ...</param>
    /// <param name="medicalEffectLevel">药水效果等级</param>
    public Color GetMedicalEffectColor(int type)
    {
        return type switch
        {
            0 => Color.green,
            1 => Color.green,
            2 => new Color(0.3137f, 0.5098f, 0.0392f),
            3 => new Color(0.1961f, 0.0588f, 0.3725f),
            _ => Color.white,
        };
    }
    public void TakeEffect_Radius((float x, float y) centerPos, Entity originEntity, float radius, float duration, int type, int level)
    {
        List<Entity> tmp = EntityManager.Manager.EntitySelector_Radius(centerPos, 2, false, radius, false);
        tmp.AddRange(EntityManager.Manager.EntitySelector_Radius(centerPos,1, false, radius, false));
        ParticleSystem[] particles = _medicalEffect_burst.GetComponentsInChildren<ParticleSystem>();
        ParticleSystem.MainModule[] mainModules = new ParticleSystem.MainModule[particles.Length];
        for (int i = 0; i < particles.Length; i++)
        {
            mainModules[i] = particles[i].main;
            mainModules[i].startColor = GetMedicalEffectColor(type);
        }
        mainModules[0].startSize = radius * 2;
        Object.Destroy(Object.Instantiate(_medicalEffect_burst, new Vector2(centerPos.x, centerPos.y), Quaternion.identity), 1.2f);
        for (int i = 0; i < tmp.Count; i++)
        {
            TakeEffect_Single(originEntity, tmp[i], duration, type, level);
        }
    }
    public void TakeEffect_Single(Entity originEntity, Entity targetEntity, float duration, int type, int level)
    {
        ParticleSystem.MainModule mainModule;
        switch (type)
        {
            case 0:
                mainModule = _medicalEffect_abhere_stay.GetComponent<ParticleSystem>().main;
                mainModule.startColor = new ParticleSystem.MinMaxGradient(GetMedicalEffectColor(0));
                targetEntity.buffController.CreateDOT(originEntity, _medicalEffect_abhere_stay, "recover", duration, 1 / (float)level, 0, 45, 3, false);
                break;
            case 1:
                mainModule = _medicalEffect_abhere_burst.GetComponent<ParticleSystem>().main;
                mainModule.startColor = new ParticleSystem.MinMaxGradient(GetMedicalEffectColor(1));
                Object.Destroy(Object.Instantiate(_medicalEffect_abhere_burst, targetEntity.transform.position, Quaternion.identity), 1.2f);
                targetEntity.Stats.ApplyDamage(originEntity, level * 200, 1, 0, 0, 0, 0, 3, 1);
                break;
            case 2:
                mainModule = _medicalEffect_abhere_stay.GetComponent<ParticleSystem>().main;
                mainModule.startColor = new ParticleSystem.MinMaxGradient(GetMedicalEffectColor(2));
                targetEntity.buffController.CreateDOT(originEntity, _medicalEffect_abhere_stay, "poison", duration, 1 / (float)level, 0, 45, 1, false);
                break;
            case 3:
                mainModule = _medicalEffect_abhere_burst.GetComponent<ParticleSystem>().main;
                mainModule.startColor = new ParticleSystem.MinMaxGradient(GetMedicalEffectColor(3));
                Object.Destroy(Object.Instantiate(_medicalEffect_abhere_burst, targetEntity.transform.position, Quaternion.identity), 1.2f);
                targetEntity.Stats.ApplyDamage(originEntity, level * 200, 1, 0, 0, 0, 0, 1, 1);
                break;
            case 4:
                break;
            case 5:
                break;
            default:
                break;
        }
    }
}
