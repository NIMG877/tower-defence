using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BeefTalent : Talent
{
    public override void Initialize()
    {
        _thisEntity.buffController.AddAbnormalState(-5, 3);
    }
}
