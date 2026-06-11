using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using SkillSystem;

namespace MyUI
{
    public class SkillCard
    {
        public RectTransform SkillRT;
        private Image skillImage, spRecoverModeImage, skillOpenModeImage, skillAmountImage;
        private TextMeshProUGUI abilityNameText, sp0Text, totalSpText, spRecoverModeText, skillOpenModeText, skillAmountText, description;
        public SkillCard(Vector2 skillCardAnchorPos, RectTransform parent, Color textColor, float outerWidth)
        {
            RectTransform skillCard = Resources.Load<RectTransform>("Prefabs/UI/MyUIs/Components/skillCard");
            SkillRT = Object.Instantiate(skillCard.gameObject, parent).GetComponent<RectTransform>();
            SkillRT.sizeDelta = new Vector2(outerWidth, 0);
            SkillRT.Find("content1").GetComponent<RectTransform>().sizeDelta = new Vector2(outerWidth - 50, 0);
            abilityNameText = SkillRT.Find("content1/skillName").GetComponent<TextMeshProUGUI>();
            Transform skillImgArea = SkillRT.Find("skillImgArea");
            skillImage = skillImgArea.Find("skillImg").GetComponent<Image>();
            sp0Text = skillImage.transform.Find("sp0/text").GetComponent<TextMeshProUGUI>();
            totalSpText = skillImage.transform.Find("totalSp/text").GetComponent<TextMeshProUGUI>();
            description = SkillRT.Find("content1/description").GetComponent<TextMeshProUGUI>();
            description.rectTransform.sizeDelta = new Vector2(outerWidth - 50, 0);
            Transform content2 = SkillRT.Find("content1/content2");
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
            float skillAmount = sp != null ? sp.skillAmount : 0f;
            if (skillAmount > 0f)
            {
                skillAmountText.text = skillAmount.ToString("0.#");
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
                    case SkillOpenMode.Auto: skillOpenModeText.text = "自动触发"; break;
                    case SkillOpenMode.OnAttackAnimBegin: skillOpenModeText.text = "攻击时触发"; break;
                    case SkillOpenMode.OnBeforeHurt: skillOpenModeText.text = "受击时触发"; break;
                    case SkillOpenMode.Manual: skillOpenModeText.text = "手动触发"; break;
                    case SkillOpenMode.OnAttackSuccessfully: skillOpenModeText.text = "命中触发"; break;
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
        public TalentCard(Vector2 skillCardAnchorPos, RectTransform parent, Color textColor, float width)
        {
            RectTransform talentCard = Resources.Load<RectTransform>("Prefabs/UI/MyUIs/Components/talentCard");
            TalentRT = Object.Instantiate(talentCard.gameObject, parent).GetComponent<RectTransform>();
            TalentRT.sizeDelta = new Vector2(width, 0);
            description = TalentRT.Find("description").GetComponent<TextMeshProUGUI>();
            description.GetComponent<RectTransform>().sizeDelta = new Vector2(width, 0);
            talentName = TalentRT.Find("talentName/name").GetComponent<TextMeshProUGUI>();
            description.color = textColor;
        }
        public void UpdateTalentCardMessage(Talent talent)
        {
            talentName.text = talent.TalentName;
            description.text = talent.TalentDescription;
            description.rectTransform.sizeDelta = new Vector2(description.rectTransform.rect.width, description.preferredHeight);
            TalentRT.sizeDelta = new Vector2(TalentRT.rect.width, 17 + description.preferredHeight);
        }
    }
    public class SubpCard
    {
        public RectTransform SubpRT;
        private TextMeshProUGUI subpName, description;
        private Image subpImage;
        public SubpCard(Vector2 skillCardAnchorPos, RectTransform parent, Color textColor, float width)
        {
            RectTransform subpCard = Resources.Load<RectTransform>("Prefabs/UI/MyUIs/Components/subpCard");
            SubpRT = Object.Instantiate(subpCard.gameObject, parent).GetComponent<RectTransform>();
            SubpRT.sizeDelta = new Vector2(width, 0);
            subpImage = SubpRT.Find("logo").GetComponent<Image>();
            subpName = SubpRT.Find("content/subpName").GetComponent<TextMeshProUGUI>();
            description = SubpRT.Find("content/description").GetComponent<TextMeshProUGUI>();
            SubpRT.Find("content").GetComponent<RectTransform>().sizeDelta = new Vector2(width - 50, 0);
            description.rectTransform.sizeDelta = new Vector2(width - 50, 0);
            subpName.color = textColor;
            description.color = textColor;
        }
        public void UpdateSubpCardMessage(EntityData entityData)
        {

        }
    }
    public class BuffCard
    {
        public RectTransform BuffRT;
        private TextMeshProUGUI buffName, buffEffectName, buffEffectValue;
        private float rate = 0.5625f;
        private Dictionary<BuffType, string> buffs = new Dictionary<BuffType, string>()
        {
            {BuffType.atkminn_delta_value,"��С������" }
        };
        public BuffCard(Vector2 skillCardAnchorPos, RectTransform parent, Color textColor, float width)
        {
            RectTransform buffCard = Resources.Load<RectTransform>("Prefabs/UI/MyUIs/Components/buffCard");
            BuffRT = Object.Instantiate(buffCard.gameObject, parent).GetComponent<RectTransform>();
            BuffRT.sizeDelta = new Vector2(width, 0);
            buffName = BuffRT.Find("buffName/name").GetComponent<TextMeshProUGUI>();
            buffEffectName = BuffRT.Find("buffEffectName").GetComponent<TextMeshProUGUI>();
            buffEffectValue = BuffRT.Find("buffEffectValue").GetComponent<TextMeshProUGUI>();
            buffEffectName.rectTransform.sizeDelta = new Vector2(width * rate, 0);
            buffEffectValue.rectTransform.sizeDelta = new Vector2(width * (1 - rate), 0);
            buffEffectName.color = textColor;
            buffEffectValue.color = textColor;
        }
        public void UpdateBuffCardMessage(Buff buff)
        {
            buffName.text = buff.buff_name;
            float[] v = buff.buff_values;
            BuffType[] t = buff.buff_types;
            buffEffectName.text = null;
            buffEffectValue.text = null;
            for (int i = 0; i < v.Length; i++)
            {
                buffEffectName.text += t[i].ToString();
                buffEffectValue.text += v[i].ToString("0.000");
                if (i < v.Length - 1)
                {
                    buffEffectName.text += '\n';
                    buffEffectValue.text += '\n';
                }
            }
            buffEffectName.rectTransform.sizeDelta = new Vector2(buffEffectName.rectTransform.rect.width, buffEffectName.preferredHeight);
            buffEffectValue.rectTransform.sizeDelta = new Vector2(buffEffectValue.rectTransform.rect.width, buffEffectValue.preferredHeight);
            BuffRT.sizeDelta = new Vector2(BuffRT.rect.width, 17 + buffEffectName.preferredHeight);
        }
    }
}
