using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ts1 : Skill
{
    Buff buff;
    public override bool SkillBegin()
    {
        if ((buff == null || buff.buff_values[0] < 80) && base.SkillBegin())
        {
            _thisEntity.TakeDamage(_thisEntity, 200, 1, 0, 0, 0, 0, 1, 2);
            if (buff == null)
            {
                buff = _thisEntity.GetComponent<BuffController>().CreateBuff(new BuffType[2] { BuffType.atkspd_delta_value, BuffType.atk_delta_value }, null, "¹¥»÷ËÙ¶ÈÌáÉý", new float[2] { -95, -95 }, -5, false);
            }
            else
            {
                buff.buff_values[0] = 95;
                buff.buff_values[1] = 95;
            }
            return true;
        }
        return false;
    }
    public override void SkillEnd()
    {
        base.SkillEnd();
        buff.buff_values[0] = 0;
        buff.buff_values[1] = 0;
    }
}
