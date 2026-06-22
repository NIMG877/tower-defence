using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class WdslmSkill2 : Skill
{
    [SerializeField] private string _inciteDefectionAnimation;
    [SerializeField] private GameObject _skillEffect;
    private List<Entity> _privateTarget;
    private BuffType[] _buffTypes = new BuffType[2] { BuffType.atk_delta_percent, BuffType.mhp_delta_percent };
    private float[] _buffValues = new float[2] { -0.45f, 6.5f };
    private WdslmSkill3 _skill3;
    private AnimationOverrideHandle _animationOverride;
    public override void Initialize()
    {
        base.Initialize();
        _skill3 = GetComponent<WdslmSkill3>();
        _skill3.SkilllRecoverForbid(true);
    }
    private void OnBeforeSkillAttack(Entity target, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int cumbo, ref int damageType, int applyType)
    {
        multiplyer = 0;
        InciteDefect(target);
        _thisEntity.AttackBase.OnBeforeAttack -= OnBeforeSkillAttack;
    }
    private void InciteDefect(Entity entity)
    {
        entity.Camp = _thisEntity.Camp;
        entity.buffController.CreateBuff(_buffTypes, null, "incite", _buffValues, -5, true);
        entity.buffController.AddAbnormalState(11, 2);
        entity.buffController.AddAbnormalState(11, 3);
        EntityManager.Manager.RemoveEntityFromStaticList(entity);
        Destroy(entity.TempContainer.Find("facing(Clone)").gameObject);
        _skill3.TargetEntity = entity;
    }
    public override bool SkillBegin()
    {
        _privateTarget = EntityManager.Manager.EntitySelector_Radius((0, 0), _thisEntity.Camp, false, -1, false);
        if (_privateTarget.Count == 0)
            return false;
        if (!base.SkillBegin())
            return false;
        _animationOverride = _thisEntity.entityAM.AddOverride(this, new AnimationOverride
        {
            AttackClose = _inciteDefectionAnimation,
            AttackRemote = _inciteDefectionAnimation,
        });
        int maxIndex = 0;
        float countPriority(Entity entity, int type)
        {
            //0-�������ȣ�1-�������ȣ�2-��������
            //�����ҷ�ƽ��Ѫ��Ϊ1500������Ϊ300������Ϊ5��ȫͼ�������ҷ���λ����Χ���ҷ���Χ��ɱ�����Ҫ�ƶ�����һ�����ŵ��ƽ��ʱ��Ϊ8s

            //Ŀ��Ϊ�����ʱ���ڻ�ɱ����ҷ���λ/�����������ʱ��/�ṩ�������������
            float damageCalculate(float dem, float def, float mgr, int t)
            {
                float minRate = 0.05f;
                return dem = t switch
                {
                    0 => Mathf.Max(dem * minRate, dem - def),
                    1 => Mathf.Max(dem * minRate, dem * (1 - mgr / 100)),
                    2 => dem,
                    3 => dem,
                    _ => 0,
                };
            }
            float Thp = 1500;
            float TDamage_P = 800;
            float TDamage_M = 650;
            float TATK_T = 1;
            float Tdef = 300;
            float Tmgr = 5;
            float TmoveTime = 10;
            int damageType = entity.AttackBase.DamageType;
            float damage = damageCalculate(entity.AttackBase.AttackDamageS * (1 + _buffValues[0]), Tdef, Tmgr, damageType);
            float dps = damage / entity.AttackBase.BaseAttackTimeS;
            float R_damage = Mathf.Max(damageCalculate(TDamage_P, entity.Stats.DefS, 0, 0), damageCalculate(TDamage_M, 0, entity.Stats.MagicResistanceS, 1));
            float R_dps = R_damage / TATK_T;
            float atkp = entity.Vision.Range.Length / (Thp * entity.Vision.Range.Length / dps + TmoveTime);
            float surp = R_dps / entity.Stats.MaxHpS;
            float medp = damageType == 3 ? dps / 800 : 0;
            switch (type)
            {
                case 0: return atkp * 1000 + surp + medp / 1000;
                case 1: return surp * 1000 + atkp + medp / 1000;
                case 2: return medp * 1000 + surp + atkp / 1000;
                default: return 0;
            }

        }
        int p_type = 0;
        for (int i = 1; i < _privateTarget.Count; i++)
        {
            if (countPriority(_privateTarget[maxIndex], p_type) < countPriority(_privateTarget[i], p_type))
                maxIndex = i;
        }
        _thisEntity.AttackBase.TryToAttack(new Entity[1] { _privateTarget[maxIndex] }, true, false);
        Destroy(Instantiate(_skillEffect, _privateTarget[maxIndex].EntityPosition, Quaternion.identity, _privateTarget[maxIndex].transform), 5);
        _skill3.SkilllRecoverForbid(false);
        SkilllRecoverForbid(true);
        _thisEntity.AttackBase.OnBeforeAttack += OnBeforeSkillAttack;
        return true;
    }
    public override void SkillEnd()
    {
        base.SkillEnd();
        _thisEntity.entityAM.RemoveOverride(_animationOverride);
    }
}
