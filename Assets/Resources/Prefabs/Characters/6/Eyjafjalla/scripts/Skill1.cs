using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Skill1 : Skill
{
    public GameObject Skill1Effect;
    private Buff _skill1Buff;
    private Modifier[] _modifiers = new Modifier[1] { new Modifier("AttackSpeed", ModifierOp.AddFlat, 120f) };
    public override bool SkillBegin()
    {
        if (!base.SkillBegin())
            return false;
        _skill1Buff = _thisEntity.buffController.CreateBuff(_modifiers, Skill1Effect, "EyjafjallaSkill1", -5, false);
        // Talent1 已迁移为 eyjafjalla_t1 资产：技能1状态经黑板传递——skill1 flag
        // 控制"优先打泡泡"暂停与动画切换；泡泡触发概率由技能侧直接改写参数值
        // （RandomRoll 的 input 按字符串读黑板，所以存 string）。
        _thisEntity.AbilityRunner.sharedBlackboard.Set("eyjafjalla_t1_skill1", "True");
        _thisEntity.AbilityRunner.sharedBlackboard.Set("eyjafjalla_t1_roll_p", "0.6");
        return true;
    }
    public override void SkillEnd()
    {
        base.SkillEnd();
        _thisEntity.buffController.DestroyBuff(_skill1Buff);
        _thisEntity.AbilityRunner.sharedBlackboard.Set("eyjafjalla_t1_skill1", "False");
        _thisEntity.AbilityRunner.sharedBlackboard.Set("eyjafjalla_t1_roll_p", "0.2");
    }
}
