using UnityEngine;
using UnityEngine.UI;

public class SpSliderController : SliderControllerBasic
{
    private Color _skillFillColor;
    private Color _normalFillColor;
    private EntitySkillRunner _skillRunner;

    public override void SliderInitialize(GameObject sliderObject)
    {
        base.SliderInitialize(sliderObject);
        _skillFillColor = _slider.transform.Find("SkillFill").GetComponent<Image>().color;
        _normalFillColor = _slider.transform.Find("Fill").GetComponent<Image>().color;
    }

    public override void SetHostEntity(Entity hostEntity, float smoothSpeed, int type,
        int positionLayer, bool hideWhenFull, bool moveSlider)
    {
        base.SetHostEntity(hostEntity, smoothSpeed, type, positionLayer, hideWhenFull, moveSlider);
        _skillRunner = hostEntity.SkillRunner;
    }

    protected override void SetRateOperations()
    {
        if (_skillRunner == null || _skillRunner.Abilities.Count == 0)
        {
            SetRate(0f);
            return;
        }
        var runtime = _skillRunner.Abilities[0];
        var sp = runtime.spEngine;
        if (sp == null)
        {
            SetRate(0f);
            return;
        }

        bool isActive = sp.IsActive;
        float rate;
        if (isActive)
        {
            var cfg = runtime.config != null ? runtime.config.sp : null;
            float skillAmount = cfg != null ? cfg.skillAmount : 0f;
            rate = skillAmount > 0f
                ? Mathf.Clamp01(sp.CurrentAmount / skillAmount)
                : 1f;
        }
        else
        {
            var cfg = runtime.config != null ? runtime.config.sp : null;
            if (cfg == null || cfg.totalSp <= 0)
            {
                SetRate(0f);
                return;
            }
            rate = Mathf.Clamp01(sp.CurrentSp / cfg.totalSp);
        }

        _fill.color = isActive ? _skillFillColor : _normalFillColor;
        SetRate(rate);
    }
}
