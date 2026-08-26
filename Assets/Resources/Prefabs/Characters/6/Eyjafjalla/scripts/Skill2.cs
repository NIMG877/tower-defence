using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Bullet;

public class Skill2 : Skill
{
    [SerializeField] private string _skill2Attack;
    private AnimationMachine _thisAM;
    private AttackBase _thisAB;
    [SerializeField] private AttackBase.AttackEffectData _skill2AttackEffectData;
    private List<Vector2> _targetPos;
    private event OperationsOnBulletDestroy _onBulletDestroy;
    private void OperationsOnAttackSuccessfully()
    {
        for (int i = 0; i < _targetPos.Count; i++)
        {
            new Bullet(null, null, _onBulletDestroy, _skill2AttackEffectData.BulletData, _thisEntity, null, _targetPos[i], _skill2AttackEffectData.BulletSpawnTransform.position, 0, 1, 0, 0, 0, 0, 0, 0);
        }
        _thisAB.OnAttackSuccessfully -= OperationsOnAttackSuccessfully;
    }
    private void OnBulletDestroy(Vector2 pos)
    {
        // Talent1 已迁移为 eyjafjalla_t1 资产；"子弹落点生成泡泡"与技能2期间暂停
        // 优先索敌待 Skill2 本身迁移为资产后由 spawn_entity / inject_attack_targets
        // 步骤恢复——旧脚本无法触发 ability 步骤，先留空。
    }
    public override bool SkillBegin()
    {
        if (!base.SkillBegin())
            return false;
        _thisAB.TryToAttackWithAnimation(new Entity[0] { }, true, false, new AnimationOverride
        {
            AttackRemote = _skill2Attack,
            AttackClose = _skill2Attack,
        });
        _thisAB.OnAttackSuccessfully += OperationsOnAttackSuccessfully;
        _targetPos = new List<Vector2>() { new Vector2(1, 1), new Vector2(1, 2), new Vector2(2, 2), new Vector2(2, 1) };
        return true;
    }
    public override void SkillEnd()
    {
        base.SkillEnd();
    }

    public override void Initialize()
    {
        base.Initialize();
        _onBulletDestroy = null;
        _onBulletDestroy += OnBulletDestroy;
        _thisAB = _thisEntity.AttackBase;
        _thisAM = _thisEntity.entityAM;
    }
}
