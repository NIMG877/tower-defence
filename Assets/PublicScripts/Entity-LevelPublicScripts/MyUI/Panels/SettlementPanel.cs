using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;
using UnityEngine.UIElements.Experimental;

namespace MyUI
{
    public class SettlementPanel : BasePanel
    {
        private static SettlementPanel _instance;
        public static SettlementPanel Panel
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new SettlementPanel();
                }
                return _instance;
            }
        }
        private string _levelName;
        private bool _win;
        private float _timer;
        private DamageStatisticData[] _damageStatisticDatas;
        private float _td, _th, _tr;
        private Transform normalState, pullState;
        private RectTransform[] ttData;
        private TextMeshProUGUI levelName, accomplishState, timer, characterName;
        private GameObject[] ses;
        private Image tImg, classImg;
        private Image[] characterImages;
        private ColorfulTape[] colorfulTapes;
        private SettlementPanel() : base(new UIType("Prefabs/UI/MyUIs/SettlementPanel"))
        {
            normalState = GetComponentInChildrenByPath<Transform>("normalState");
            levelName = GetComponentInChildrenByPath<TextMeshProUGUI>("levelName");
            accomplishState = GetComponentInChildrenByPath<TextMeshProUGUI>("normalState/accomplishState");
            timer = GetComponentInChildrenByPath<TextMeshProUGUI>("normalState/timer/timer");
            Transform characterFrame = GetComponentInChildrenByPath<Transform>("normalState/characterFrame");
            int num = characterFrame.childCount;
            characterImages = new Image[num];
            for (int i = 0; i < num; i++)
            {
                Transform photo = characterFrame.GetChild(i).Find("photo");
                characterImages[i] = photo.GetComponent<Image>();
                EventTrigger.Entry point = new EventTrigger.Entry();
                point.eventID = EventTriggerType.PointerClick;
                int index = i;
                point.callback.AddListener((data) =>
                {
                    ShowCharacterData(_damageStatisticDatas[index]);
                });
                photo.GetComponent<EventTrigger>().triggers.Add(point);
            }
            EventTrigger.Entry pointBK = new EventTrigger.Entry();
            pointBK.eventID = EventTriggerType.PointerClick;
            pointBK.callback.AddListener((data) =>
            {
                if (normalState.gameObject.activeSelf)
                {
                    PanelManager.PopTo(LevelSelectorPanel.Panel);
                }
                else
                {
                    normalState.gameObject.SetActive(true);
                    pullState.gameObject.SetActive(false);
                }
            });
            UIObject.GetComponent<EventTrigger>().triggers.Add(pointBK);
            pullState = GetComponentInChildrenByPath<Transform>("pullState");
            tImg = GetComponentInChildrenByPath<Image>("pullState/tImg");
            classImg = GetComponentInChildrenByPath<Image>("pullState/class");
            characterName = GetComponentInChildrenByPath<TextMeshProUGUI>("pullState/characterName");
            ttData = new RectTransform[3]
            {
                GetComponentInChildrenByPath<RectTransform>("pullState/messageArea/ttd"),
                GetComponentInChildrenByPath<RectTransform>("pullState/messageArea/tth"),
                GetComponentInChildrenByPath<RectTransform>("pullState/messageArea/ttr")
            };
            colorfulTapes = new ColorfulTape[3]
            {
                GetComponentInChildrenByPath<ColorfulTape>("pullState/messageArea/ttd/ds"),
                GetComponentInChildrenByPath<ColorfulTape>("pullState/messageArea/tth/ds"),
                GetComponentInChildrenByPath<ColorfulTape>("pullState/messageArea/ttr/ds"),
            };
        }
        public override void OnEnter()
        {
            base.OnEnter();
            normalState.gameObject.SetActive(true);
            pullState.gameObject.SetActive(false);
            levelName.text = _levelName;
            accomplishState.text = _win ? "行动成功" : "行动失败";
            timer.text = _timer.ToString("0.0");
            if (_damageStatisticDatas != null)
            {
                for (int i = 0; i < _damageStatisticDatas.Length; i++)
                {
                    characterImages[i].sprite = CharacterCardManager.cardManager.GetCharacterAttribute(_damageStatisticDatas[i].CharacterID).headImg;
                    characterImages[i].transform.parent.gameObject.SetActive(true);
                }
                for (int i = _damageStatisticDatas.Length; i < characterImages.Length; i++)
                {
                    characterImages[i].transform.parent.gameObject.SetActive(false);
                }
            }

        }
        private void ShowCharacterData(DamageStatisticData damageStatisticData)
        {
            EntityData entityAttributes = CharacterCardManager.cardManager.GetCharacterAttribute(damageStatisticData.CharacterID);
            normalState.gameObject.SetActive(false);
            pullState.gameObject.SetActive(true);
            tImg.sprite = entityAttributes.wholeImg;
            characterName.text = entityAttributes.ChineseName;
            if (damageStatisticData.Td > 0)
            {
                float rate = damageStatisticData.Td / _td;
                colorfulTapes[0].SetValues(damageStatisticData.Damage, damageStatisticData.Td, rate);
                ttData[0].gameObject.SetActive(true);
            }
            else
            {
                ttData[0].gameObject.SetActive(false);
            }
            if (damageStatisticData.Th > 0)
            {
                float rate = damageStatisticData.Th / _th;
                colorfulTapes[1].SetValues(damageStatisticData.Healing, damageStatisticData.Th, rate);
                ttData[1].gameObject.SetActive(true);
            }
            else
            {
                ttData[1].gameObject.SetActive(false);
            }
            if (damageStatisticData.Tr > 0)
            {
                float rate = damageStatisticData.Tr / _tr;
                colorfulTapes[2].SetValues(damageStatisticData.DamageReceive, damageStatisticData.Tr, rate);
                ttData[2].gameObject.SetActive(true);
            }
            else
            {
                ttData[2].gameObject.SetActive(false);
            }

        }
        public void SetDatas(string levelName, bool win, float timer, DamageStatisticData[] damageStatisticDatas)
        {
            _levelName = levelName;
            _win = win;
            _timer = timer;
            _damageStatisticDatas = damageStatisticDatas;
            _td = 0;
            _th = 0;
            _tr = 0;
            for (int i = 0; i < damageStatisticDatas.Length; i++)
            {
                damageStatisticDatas[i].CaculateT();
                _td += damageStatisticDatas[i].Td;
                _th += damageStatisticDatas[i].Th;
                _tr += damageStatisticDatas[i].Tr;
            }
        }
    }
    public struct DamageStatisticData
    {
        public EntityID CharacterID;
        public float[] Damage;
        public float Td;
        public float[] Healing;
        public float Th;
        public float[] DamageReceive;
        public float Tr;
        public DamageStatisticData(EntityID characterId, float[] damage, float[] healing, float[] damageReceive)
        {
            CharacterID = characterId;
            Damage = damage;
            Healing = healing;
            DamageReceive = damageReceive;
            Td = 0;
            Th = 0;
            Tr = 0;
        }
        public void CaculateT()
        {
            for (int i = 0; i < Damage.Length; i++)
            {
                Td += Damage[i];
            }
            for (int i = 0; i < Healing.Length; i++)
            {
                Th += Healing[i];
            }
            for (int i = 0; i < DamageReceive.Length; i++)
            {
                Tr += DamageReceive[i];
            }
        }
    }
}

