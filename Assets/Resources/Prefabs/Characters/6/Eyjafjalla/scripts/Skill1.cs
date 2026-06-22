using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Skill1 : Skill
{
    public GameObject Skill1Effect;
    private Talent1 _talent1;
    private Buff _skill1Buff;
    private BuffType[] _buffTypes = new BuffType[1] { BuffType.atkspd_delta_value };
    private float[] _buffValues = new float[1] { 120 };
    public override void Initialize()
    {
        base.Initialize();
        _talent1 = _thisEntity.GetComponent<Talent1>();
    }
    public override bool SkillBegin()
    {
        if (!base.SkillBegin())
            return false;
        _skill1Buff = _thisEntity.buffController.CreateBuff(_buffTypes, Skill1Effect, "EyjafjallaSkill1", _buffValues, -5, false);
        _talent1.Skill1Open();
        return true;
    }
    public override void SkillEnd()
    {
        base.SkillEnd();
        _thisEntity.buffController.DestroyBuff(_skill1Buff);
        _talent1.Skill1End();
    }
}
