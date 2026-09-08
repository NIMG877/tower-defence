using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SpSliderController : SliderControllerBasic
{
    private Color _skillFillColor;
    private Color _normalFillColor;
    private EntityAbilityRunner _skillRunner;
    private Image _skillStatus;
    private Sprite _skillReadySprite;
    private Sprite _skillStopSprite;
    private GameObject _chargeCount;
    private TextMeshProUGUI _chargeCountText;

    public override void SliderInitialize(GameObject sliderObject)
    {
        base.SliderInitialize(sliderObject);
        _skillFillColor = _slider.transform.Find("SkillFill").GetComponent<Image>().color;
        _normalFillColor = _slider.transform.Find("Fill").GetComponent<Image>().color;
        _skillStatus = _slider.transform.Find("SkillStatus").GetComponent<Image>();
        _skillStatus.enabled = false;
        _skillReadySprite = Resources.Load<Sprite>("Prefabs/UI/MyUIs/UISprites/LevelMessagePanel/sprite_skill_ready");
        _skillStopSprite = Resources.Load<Sprite>("Prefabs/UI/MyUIs/UISprites/LevelMessagePanel/sprite_skill_stop");
        _chargeCount = _slider.transform.Find("ChargeCount").gameObject;
        _chargeCountText = _chargeCount.transform.Find("Text").GetComponent<TextMeshProUGUI>();
        _chargeCount.SetActive(false);
    }

    public override void SetHostEntity(Entity hostEntity, float smoothSpeed, int type,
        int positionLayer, bool hideWhenFull, bool moveSlider)
    {
        base.SetHostEntity(hostEntity, smoothSpeed, type, positionLayer, hideWhenFull, moveSlider);
        _skillRunner = hostEntity.AbilityRunner;
        // 池化复用时清除上一次使用残留的图标，避免重新激活到首次 FixedUpdate 之间闪现
        _skillStatus.enabled = false;
        _chargeCount.SetActive(false);
    }

    protected override void SetRateOperations()
    {
        AbilitySystem.SPEngine sp = null;
        AbilitySystem.SPConfig cfg = null;
        bool isActive = false;
        float rate = 0f;
        if (_skillRunner != null && _skillRunner.Skills.Count > 0)
        {
            var runtime = _skillRunner.Skills[0];
            sp = runtime.spEngine;
            if (sp != null)
            {
                isActive = sp.IsActive;
                cfg = runtime.config != null ? runtime.config.sp : null;
                if (isActive)
                {
                    float abilityAmount = cfg != null ? cfg.abilityAmount : 0f;
                    rate = abilityAmount > 0f
                        ? Mathf.Clamp01(sp.CurrentAmount / abilityAmount)
                        : 1f;
                }
                else if (cfg != null && cfg.totalSp > 0)
                {
                    rate = Mathf.Clamp01(sp.CurrentSp / cfg.totalSp);
                }
            }
        }

        _fill.color = isActive ? _skillFillColor : _normalFillColor;
        SetRate(rate);
        UpdateSkillStatus(sp, cfg, isActive);
        UpdateChargeCount(sp, cfg, isActive);
    }

    /// <summary>
    /// SkillStatus 仅在需要玩家手动操作时显示：未开启且手动开启已就绪 → ready 图，开启中且可手动关闭 → stop 图。
    /// </summary>
    private void UpdateSkillStatus(AbilitySystem.SPEngine sp, AbilitySystem.SPConfig cfg, bool isActive)
    {
        if (sp == null || cfg == null)
        {
            _skillStatus.enabled = false;
            return;
        }
        if (!isActive && cfg.openMode == AbilitySystem.AbilityOpenMode.Manual && sp.CanBegin())
        {
            _skillStatus.sprite = _skillReadySprite;
            _skillStatus.enabled = true;
        }
        else if (isActive && cfg.canManualClose)
        {
            _skillStatus.sprite = _skillStopSprite;
            _skillStatus.enabled = true;
        }
        else
        {
            _skillStatus.enabled = false;
        }
    }

    /// <summary>
    /// 充能层数仅展示非手动触发技能的待发充能：未开启、chargeNum>1 且已有充能时显示层数文本；手动触发由 SkillStatus ready 图标负责。
    /// </summary>
    private void UpdateChargeCount(AbilitySystem.SPEngine sp, AbilitySystem.SPConfig cfg, bool isActive)
    {
        bool show = sp != null && cfg != null && !isActive
                    && cfg.openMode != AbilitySystem.AbilityOpenMode.Manual
                    && cfg.chargeNum > 1 && sp.CurrentCharge >= 1;
        _chargeCount.SetActive(show);
        if (show)
        {
            _chargeCountText.text = sp.CurrentCharge.ToString();
        }
    }
}
