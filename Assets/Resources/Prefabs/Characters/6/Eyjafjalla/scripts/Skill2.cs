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
    private Talent1 _talent1;
    private void OperationsOnAttackSuccessfully()
    {
        for (int i = 0; i < _targetPos.Count; i++)
        {
            print(_targetPos[i]);
            new Bullet(null, null, _onBulletDestroy, _skill2AttackEffectData.BulletData, _thisEntity, null, _targetPos[i], _skill2AttackEffectData.BulletSpawnTransform.position, 0, 1, 0, 0, 0, 0, 0, 0);
        }
        _thisAB.OnAttackSuccessfully -= OperationsOnAttackSuccessfully;
    }
    private void OnBulletDestroy(Vector2 pos)
    {
        _talent1.Skill2SetBubble(pos);
    }
    public override bool SkillBegin()
    {
        if (!base.SkillBegin())
            return false;
        _talent1.Skill2Open();
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
        _talent1.Skill2End();
    }

    public override void Initialize()
    {
        base.Initialize();
        _onBulletDestroy = null;
        _onBulletDestroy += OnBulletDestroy;
        _thisAB = _thisEntity.AttackBase;
        _thisAM = _thisEntity.entityAM;
        _talent1 = _thisEntity.GetComponent<Talent1>();
    }
}
