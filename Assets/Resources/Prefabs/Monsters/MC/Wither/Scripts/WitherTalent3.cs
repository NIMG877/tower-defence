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
        _thisEntity.buffController.CreateBuff(new BuffType[5] { BuffType.def_delta_value, BuffType.mgr_delta_value, BuffType.atk_delta_value, BuffType.atkspd_delta_value, BuffType.mspeed_delta_percent }, null, "witherShield", new float[5] { _defUp, _mgrUp, _atkUp, _atkspdUp, -_speedown }, -5, true);
        _talentEffect.SetActive(true);
    }
}
