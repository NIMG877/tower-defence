using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SpSliderController : SliderControllerBasic
{
    private Color _skillFillColor;
    private Color _normalFillColor;
    public override void SliderInitialize(GameObject sliderObject)
    {
        base.SliderInitialize(sliderObject);
        _skillFillColor = _slider.transform.Find("SkillFill").GetComponent<Image>().color;
        _normalFillColor = _slider.transform.Find("Fill").GetComponent<Image>().color;
    }
    protected override void SetRateOperations()
    {
        (float currentRate, int currentChargrNum, bool isSkill) = _hostEntity.skill[0].SkillMessage;
        if (!isSkill)
        {
            _fill.color = _normalFillColor;
        }
        else
        {
            _fill.color = _skillFillColor;
        }
        SetRate(currentRate);
    }
}
