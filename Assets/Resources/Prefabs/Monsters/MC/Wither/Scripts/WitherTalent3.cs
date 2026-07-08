using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Spine.Unity;
using DG.Tweening;

public class WitherTalent3 : Talent
{
    [SerializeField] private float _defUp, _mgrUp, _atkUp, _atkspdUp, _speedown;
    [SerializeField] private GameObject _talentEffect;

    public async void HpCheck()
    {
        while (_thisEntity.Stats.CurrentHpRate > 0.5f)
        {
            await UniTask.WaitForFixedUpdate();
        }
        _thisEntity.GetComponent<WitherTalent1>().HaveShield = true;
        _thisEntity.buffController.CreateBuff(new Modifier[5] { new Modifier("Defense", ModifierOp.AddFlat, _defUp), new Modifier("MagicResistance", ModifierOp.AddFlat, _mgrUp), new Modifier("Attack", ModifierOp.AddFlat, _atkUp), new Modifier("AttackSpeed", ModifierOp.AddFlat, _atkspdUp), new Modifier("MoveSpeed", ModifierOp.AddPercent, -_speedown) }, null, "witherShield", -5, true);
        _talentEffect.SetActive(true);
    }
}
