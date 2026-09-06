using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using AbilitySystem;

namespace MyUI
{
    public class AbilityCard
    {
        public RectTransform AbilityRT;
        private Image skillImage, spRecoverModeImage, skillOpenModeImage, skillAmountImage;
        private TextMeshProUGUI abilityNameText, sp0Text, totalSpText, spRecoverModeText, skillOpenModeText, skillAmountText, description;
        public AbilityCard(RectTransform parent, Color textColor)
        {
            RectTransform skillCard = Resources.Load<RectTransform>("Prefabs/UI/MyUIs/Components/skillCard");
            AbilityRT = Object.Instantiate(skillCard.gameObject, parent).GetComponent<RectTransform>();
            abilityNameText = AbilityRT.Find("content1/skillName").GetComponent<TextMeshProUGUI>();
            Transform skillImgArea = AbilityRT.Find("skillImgArea");
            skillImage = skillImgArea.Find("skillImg").GetComponent<Image>();
            sp0Text = skillImage.transform.Find("sp0/text").GetComponent<TextMeshProUGUI>();
            totalSpText = skillImage.transform.Find("totalSp/text").GetComponent<TextMeshProUGUI>();
            description = AbilityRT.Find("content1/description").GetComponent<TextMeshProUGUI>();
            Transform content2 = AbilityRT.Find("content1/content2");
            spRecoverModeImage = content2.Find("spRecoverMode").GetComponent<Image>();
            spRecoverModeText = spRecoverModeImage.GetComponentInChildren<TextMeshProUGUI>();
            skillOpenModeImage = content2.Find("skillOpenMode").GetComponent<Image>();
            skillOpenModeText = skillOpenModeImage.GetComponentInChildren<TextMeshProUGUI>();
            skillAmountImage = content2.Find("skillAmount").GetComponent<Image>();
            skillAmountText = skillAmountImage.GetComponentInChildren<TextMeshProUGUI>();
            abilityNameText.color = textColor;
            description.color = textColor;
        }
        public void UpdateAbilityCardMessage(AbilityConfig config, AbilityRuntime runtime = null)
        {
            if (config == null) return;
            if (config.icon != null) skillImage.sprite = config.icon;
            abilityNameText.text = config.abilityName;
            description.text = config.description;
            var sp = config.sp;
            float abilityAmount = sp != null ? sp.abilityAmount : 0f;
            if (abilityAmount > 0f)
            {
                skillAmountText.transform.parent.gameObject.SetActive(true);
                skillAmountText.text = abilityAmount.ToString("0.#");
            }
            else
            {
                skillAmountText.transform.parent.gameObject.SetActive(false);
            }
            if (sp != null)
            {
                switch (sp.recoverMode)
                {
                    case SpRecoverMode.Natural: spRecoverModeText.text = "自动回复"; break;
                    case SpRecoverMode.OnAttackSuccessfully: spRecoverModeText.text = "攻击回复"; break;
                    case SpRecoverMode.OnAfterHurt: spRecoverModeText.text = "受击回复"; break;
                }
                switch (sp.openMode)
                {
                    case AbilityOpenMode.Auto: skillOpenModeText.text = "自动触发"; break;
                    case AbilityOpenMode.OnAttackAnimBegin: skillOpenModeText.text = "攻击时触发"; break;
                    case AbilityOpenMode.OnBeforeHurt: skillOpenModeText.text = "受击时触发"; break;
                    case AbilityOpenMode.Manual: skillOpenModeText.text = "手动触发"; break;
                }
                if (sp.totalSp > 0)
                {
                    totalSpText.transform.parent.gameObject.SetActive(true);
                    totalSpText.text = sp.totalSp.ToString();
                }
                else
                {
                    totalSpText.transform.parent.gameObject.SetActive(false);
                }
                if (sp.initialSp > 0)
                {
                    sp0Text.transform.parent.gameObject.SetActive(true);
                    sp0Text.text = sp.initialSp.ToString();
                }
                else
                {
                    sp0Text.transform.parent.gameObject.SetActive(false);
                }
            }
        }
    }
    public class TalentCard
    {
        public RectTransform TalentRT;
        private TextMeshProUGUI talentName, description;
        public TalentCard(RectTransform parent, Color textColor)
        {
            RectTransform talentCard = Resources.Load<RectTransform>("Prefabs/UI/MyUIs/Components/talentCard");
            TalentRT = Object.Instantiate(talentCard.gameObject, parent).GetComponent<RectTransform>();
            description = TalentRT.Find("description").GetComponent<TextMeshProUGUI>();
            talentName = TalentRT.Find("talentName/name").GetComponent<TextMeshProUGUI>();
            description.color = textColor;
        }
        public void UpdateTalentCardMessage(AbilityConfig talent)
        {
            talentName.text = talent.abilityName;
            description.text = talent.description;
        }
    }
    public class SubpCard
    {
        public RectTransform SubpRT;
        private TextMeshProUGUI subpName, description;
        private Image subpImage;
        public SubpCard(RectTransform parent, Color textColor)
        {
            RectTransform subpCard = Resources.Load<RectTransform>("Prefabs/UI/MyUIs/Components/subpCard");
            SubpRT = Object.Instantiate(subpCard.gameObject, parent).GetComponent<RectTransform>();
            subpImage = SubpRT.Find("logo").GetComponent<Image>();
            subpName = SubpRT.Find("content/subpName").GetComponent<TextMeshProUGUI>();
            description = SubpRT.Find("content/description").GetComponent<TextMeshProUGUI>();
            subpName.color = textColor;
            description.color = textColor;
        }
        /// <summary>子职业特性卡:数据源 <c>EntityData.SubJobTrait</c> 资产(abilityName=子职业名,
        /// description=特性描述,icon=职业图标)。无子职业(0=无)时隐藏整卡。</summary>
        public void UpdateSubpCardMessage(EntityData entityData)
        {
            AbilityConfig trait = entityData.SubJobTrait;
            SubpRT.gameObject.SetActive(trait != null);
            if (trait == null) return;
            if (trait.icon != null) subpImage.sprite = trait.icon;
            subpName.text = trait.abilityName;
            description.text = trait.description;
        }
    }
    public class BuffCard
    {
        public RectTransform BuffRT;
        private TextMeshProUGUI buffName, buffDetail;
        public BuffCard(RectTransform parent, Color textColor)
        {
            RectTransform buffCard = Resources.Load<RectTransform>("Prefabs/UI/MyUIs/Components/buffCard");
            BuffRT = Object.Instantiate(buffCard.gameObject, parent).GetComponent<RectTransform>();
            buffName = BuffRT.Find("buffName/name").GetComponent<TextMeshProUGUI>();
            buffDetail = BuffRT.Find("buffDetail").GetComponent<TextMeshProUGUI>();
        }
        public void UpdateBuffCardMessage(Buff buff)
        {
            buffName.text = buff.buff_name;
            Modifier[] m = buff.modifiers;
            buffDetail.text = null;
            for (int i = 0; i < m.Length; i++)
            {
                string effectName = m[i].attribute;
                buffDetail.text += $"{effectName}: {FormatMagnitude(m[i])}";
                if (i < m.Length - 1)
                {
                    buffDetail.text += '\n';
                }
            }
        }

        /// <summary>按运算方式区分显示:直接加算 +300 / 直接百分比 +15% /
        /// 最终加算 +50(最终) / 最终乘算 ×1.5。语义见 ModifierOp。</summary>
        private static string FormatMagnitude(Modifier modifier)
        {
            switch (modifier.op)
            {
                case ModifierOp.AddPercent:
                    return (modifier.magnitude * 100).ToString("+0.#;-0.#") + "%";
                case ModifierOp.AddFlatFinal:
                    return modifier.magnitude.ToString("+0.#;-0.#");
                case ModifierOp.MulFinal:
                    return "×" + (modifier.magnitude * 100).ToString("0.#")+ "%";
                case ModifierOp.AddFlat:
                    return modifier.magnitude.ToString("+0.#;-0.#");
                default:
                    return "error_op";
            }
        }
    }
}
