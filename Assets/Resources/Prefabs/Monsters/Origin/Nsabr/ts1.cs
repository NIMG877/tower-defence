using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ts1 : Skill
{
    Buff buff;
    public override bool SkillBegin()
    {
        if ((buff == null || buff.modifiers[0].magnitude < 80) && base.SkillBegin())
        {
            _thisEntity.TakeDamage(_thisEntity, 200, 1, 0, 0, 0, 0, 1, 2);
            if (buff == null)
            {
                buff = _thisEntity.GetComponent<BuffController>().CreateBuff(new Modifier[2] { new Modifier(Attributes.AttackSpeed, ModifierOp.AddFlat, -95f), new Modifier(Attributes.Attack, ModifierOp.AddFlat, -95f) }, null, "�����ٶ�����", new float[2] { -95, -95 }, -5, false);
            }
            else
            {
                _thisEntity.GetComponent<BuffController>().SetBuffValues(new Modifier[2] { new Modifier(Attributes.AttackSpeed, ModifierOp.AddFlat, 95f), new Modifier(Attributes.Attack, ModifierOp.AddFlat, 95f) }, buff);
            }
            return true;
        }
        return false;
    }
    public override void SkillEnd()
    {
        base.SkillEnd();
        _thisEntity.GetComponent<BuffController>().SetBuffValues(new Modifier[2] { new Modifier(Attributes.AttackSpeed, ModifierOp.AddFlat, 0f), new Modifier(Attributes.Attack, ModifierOp.AddFlat, 0f) }, buff);
    }
}
