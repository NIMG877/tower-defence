using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Skill1 : Skill
{
    public GameObject Skill1Effect;
    private Talent1 _talent1;
    private Buff _skill1Buff;
    private Modifier[] _modifiers = new Modifier[1] { new Modifier("AttackSpeed", ModifierOp.AddFlat, 120f) };
    public override void Initialize()
    {
        base.Initialize();
        _talent1 = _thisEntity.GetComponent<Talent1>();
    }
    public override bool SkillBegin()
    {
        if (!base.SkillBegin())
            return false;
        _skill1Buff = _thisEntity.buffController.CreateBuff(_modifiers, Skill1Effect, "EyjafjallaSkill1", -5, false);
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
