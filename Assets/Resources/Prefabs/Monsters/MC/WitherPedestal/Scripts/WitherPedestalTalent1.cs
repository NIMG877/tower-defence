using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WitherPedestalTalent1 : Talent
{
    private int _headCount;
    [SerializeField] private SpriteRenderer[] _heads;
    public override void Initialize()
    {
        _headCount = 0;
        for (int i = 0; i < _heads.Length; i++)
        {
            _heads[i].enabled = false;
        }
        _thisEntity.buffController.AddAbnormalState(-10, 3);
    }
    public void AddHead()
    {
        _heads[_headCount].enabled = true;
        _headCount++;
        if (_headCount == 3)
            _thisEntity.Die();
    }
}
