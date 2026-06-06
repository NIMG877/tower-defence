using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HpSliderController : SliderControllerBasic
{
    protected override void SetRateOperations()
    {
        SetRate(_hostEntity.CurrentHpRate);
    }
}
