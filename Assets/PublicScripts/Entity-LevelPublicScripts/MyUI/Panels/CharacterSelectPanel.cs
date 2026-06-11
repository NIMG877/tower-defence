using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace MyUI
{
    public class CharacterSelectPanel : BasePanel
    {
        private static CharacterSelectPanel _instance;
        public static CharacterSelectPanel Panel
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new CharacterSelectPanel();
                }
                return _instance;
            }
        }
        private Transform _frame;
        private List<(RectTransform trans, TextMeshProUGUI text)> _selectMarks;
        private List<EntityID> _selectedCharacters;
        private List<int> _selectedCharacterSkill;
        private string _teamName;
        public string TeamName
        {
            set
            {
                _teamName = value;
                _selectedCharacters    = new List<EntityID>(SaveSystem.GetTeamMembers(_teamName));
                _selectedCharacterSkill = new List<int>(SaveSystem.GetTeamSkillSelects(_teamName));
            }
        }
        private int _selectIndex;
        public int SelectIndex
        {
            set
            {
                if (value >= _selectedCharacters.Count)
                {
                    _selectIndex = _selectedCharacters.Count;
                    _selectedCharacters.Add(EntityID.Null);
                    _selectedCharacterSkill.Add(0);
                }
                else
                {
                    _selectIndex = value;
                }
            }
        }

        private bool _showSkill, _showTalent;
        private Image _skillImage, _talentImage;
        private Color _white1 = new Color(0.7686f, 0.7686f, 0.7686f);
        private Color _black1 = new Color(0.2196f, 0.2196f, 0.2196f);
        private TextMeshProUGUI _skillText, _talentText;
        private RectTransform _skillSelectRT;
        private ScrollRect _skillContent;
        private RectTransform _skillArea, _content;
        private RectTransform _talentArea, _talentContent;
        private SkillCard[] _skillSelectorCards;
        private List<TalentCard> _talentCardList;
        private UnityAction[] _skillSelectorActions;
        private GameObject[] _noneSkillInfo;

        private TextMeshProUGUI _englishName, _name, _hpText, _atkText, _phdText, _mgrText, _reStartText, _costText, _occupyText, _atkBTText;
        private GameObject _nullMask;

        private CharacterSelectPanel() : base(new UIType("Prefabs/UI/MyUIs/CharacterSelectPanel"))
        {
            _frame = GetComponentInChildrenByPath<Transform>("container/characters/frame");
            RectTransform sp = GetComponentInChildrenByPath<RectTransform>("container/characters/frame/selectmark");
            _skillSelectRT = GetComponentInChildrenByPath<RectTransform>("container/characterMessage/skillArea/content/skillSelectMark");
            _englishName = GetComponentInChildrenByPath<TextMeshProUGUI>("container/characterMessage/messageArea/englishName");
            _name = GetComponentInChildrenByPath<TextMeshProUGUI>("container/characterMessage/messageArea/name");
            _hpText = GetComponentInChildrenByPath<TextMeshProUGUI>("container/characterMessage/messageArea/hpText");
            _atkText = GetComponentInChildrenByPath<TextMeshProUGUI>("container/characterMessage/messageArea/atkText");
            _phdText = GetComponentInChildrenByPath<TextMeshProUGUI>("container/characterMessage/messageArea/phdText");
            _mgrText = GetComponentInChildrenByPath<TextMeshProUGUI>("container/characterMessage/messageArea/mgrText");
            _reStartText = GetComponentInChildrenByPath<TextMeshProUGUI>("container/characterMessage/messageArea/reStartTimeText");
            _costText = GetComponentInChildrenByPath<TextMeshProUGUI>("container/characterMessage/messageArea/costText");
            _occupyText = GetComponentInChildrenByPath<TextMeshProUGUI>("container/characterMessage/messageArea/occupyText");
            _atkBTText = GetComponentInChildrenByPath<TextMeshProUGUI>("container/characterMessage/messageArea/atkBTText");
            _nullMask = GetComponentInChildrenByPath<Transform>("container/characterMessage/messageArea/nullMask").gameObject;
            _skillImage = GetComponentInChildrenByPath<Image>("container/characterMessage/skillTalentSwitch/skill");
            _skillText = GetComponentInChildrenByPath<TextMeshProUGUI>("container/characterMessage/skillTalentSwitch/skill/text");
            _talentImage = GetComponentInChildrenByPath<Image>("container/characterMessage/skillTalentSwitch/talent");
            _talentText = GetComponentInChildrenByPath<TextMeshProUGUI>("container/characterMessage/skillTalentSwitch/talent/text");
            EventTrigger.Entry skillClick = new EventTrigger.Entry();
            skillClick.eventID = EventTriggerType.PointerClick;
            skillClick.callback.AddListener((data) =>
            {
                if (!_showSkill)
                {
                    SwitchToSkill_Talent(true);
                }
            });
            _skillImage.GetComponent<EventTrigger>().triggers.Add(skillClick);
            EventTrigger.Entry talentClick = new EventTrigger.Entry();
            talentClick.eventID = EventTriggerType.PointerClick;
            talentClick.callback.AddListener((data) =>
            {
                if (!_showTalent)
                {
                    SwitchToSkill_Talent(false);
                }
            });
            _talentImage.GetComponent<EventTrigger>().triggers.Add(talentClick);
            _selectMarks = new List<(RectTransform, TextMeshProUGUI)>() { (sp, sp.GetChild(0).GetComponent<TextMeshProUGUI>()) };
            GetComponentInChildrenByPath<Button>("done").onClick.AddListener(() =>
            {
                for (int i = _selectedCharacters.Count - 1; i >= 0; i--)
                {
                    if (_selectedCharacters[i].ID_C == null)
                    {
                        _selectedCharacters.RemoveAt(i);
                        _selectedCharacterSkill.RemoveAt(i);
                    }
                }
                SaveSystem.SetTeamMembers(_teamName, _selectedCharacters, _selectedCharacterSkill);
                PanelManager.Pop(1);
            });



            EntityID[] characters = SaveSystem.Current.charactersOwn.ToArray();
            for (int i = 0; i < characters.Length; i++)
            {
                int cindex = i;
                CharacterCardManager.cardManager.InstantiateCardForbidNull(_frame, characters[cindex]).onClick.AddListener(() =>
                {
                    if (_selectIndex < 0)
                    {
                        if (_selectedCharacters.Contains(characters[cindex]))
                        {
                            int index_s = _selectedCharacters.IndexOf(characters[cindex]);
                            _selectedCharacters.RemoveAt(index_s);
                            _selectedCharacterSkill.RemoveAt(index_s);
                            UpdateCharacterMessage(EntityID.Null);
                            Debug.Log(_selectedCharacters.Count);
                        }
                        else
                        {
                            _selectedCharacters.Add(characters[cindex]);
                            _selectedCharacterSkill.Add(0);
                            UpdateCharacterMessage(characters[cindex]);
                        }
                    }
                    else
                    {
                        if (_selectedCharacters[_selectIndex] == characters[cindex])
                        {
                            _selectedCharacters[_selectIndex] = EntityID.Null;
                            UpdateCharacterMessage(EntityID.Null);
                        }
                        else
                        {
                            _selectedCharacters[_selectIndex] = characters[cindex];
                            _selectedCharacterSkill[_selectIndex] = 0;
                            UpdateCharacterMessage(characters[cindex]);
                        }
                    }
                    UpDateSelectMask();
                });
            }

            _content = GetComponentInChildrenByPath<RectTransform>("container/characterMessage/skillArea/content");
            _skillContent = GetComponentInChildrenByPath<ScrollRect>("container/characterMessage/skillArea");
            _skillArea = _skillContent.GetComponent<RectTransform>();
            _noneSkillInfo = new GameObject[3] { _content.GetChild(0).gameObject, _content.GetChild(1).gameObject, _content.GetChild(2).gameObject };
            _skillSelectorCards = new SkillCard[3];
            _skillSelectorActions = new UnityAction[3];
            for (int i = 2; i >= 0; i--)
            {
                _skillSelectorCards[i] = new SkillCard(Vector2.zero, _content, Color.black, _content.rect.width - 6);
                Image ii = _skillSelectorCards[i].SkillRT.gameObject.AddComponent<Image>();
                ii.color = new Color(1, 1, 1, 0);
                Button bi = _skillSelectorCards[i].SkillRT.gameObject.AddComponent<Button>();
                int index = i;
                bi.onClick.AddListener(() => _skillSelectorActions[index]());
                _skillSelectorCards[i].SkillRT.SetAsFirstSibling();
            }
            _talentArea = GetComponentInChildrenByPath<RectTransform>("container/characterMessage/talentArea");
            _talentContent = GetComponentInChildrenByPath<RectTransform>("container/characterMessage/talentArea/content");
            _talentCardList = new List<TalentCard>();
        }
        private void SwitchToSkill_Talent(bool isSkill)
        {
            if (isSkill)
            {
                _showSkill = true;
                _showTalent = false;
                _skillImage.color = _white1;
                _skillText.color = _black1;
                _talentImage.color = _black1;
                _talentText.color = _white1;
                _skillArea.gameObject.SetActive(true);
                LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
                _talentArea.gameObject.SetActive(false);
            }
            else
            {
                _showTalent = true;
                _showSkill = false;
                _skillImage.color = _black1;
                _skillText.color = _white1;
                _talentImage.color = _white1;
                _talentText.color = _black1;
                _skillArea.gameObject.SetActive(false);
                _talentArea.gameObject.SetActive(true);
                LayoutRebuilder.ForceRebuildLayoutImmediate(_talentContent);
            }
        }
        private void UpDateSelectMask()
        {
            if (_selectIndex < 0)
            {
                int count = _selectMarks.Count;
                if (count >= _selectedCharacters.Count)
                {
                    for (int i = 0; i < _selectedCharacters.Count; i++)
                    {
                        _selectMarks[i].trans.SetParent(CharacterCardManager.cardManager.GetCardForbidNull(_selectedCharacters[i]).transform);
                        _selectMarks[i].trans.localPosition = Vector3.zero;
                        _selectMarks[i].text.text = (i + 1).ToString();
                        _selectMarks[i].trans.gameObject.SetActive(true);
                    }
                    for (int i = _selectedCharacters.Count; i < count; i++)
                    {
                        _selectMarks[i].trans.gameObject.SetActive(false);
                    }
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        _selectMarks[i].trans.transform.SetParent(CharacterCardManager.cardManager.GetCardForbidNull(_selectedCharacters[i]).transform);
                        _selectMarks[i].trans.localPosition = Vector3.zero;
                        _selectMarks[i].text.text = (i + 1).ToString();
                        _selectMarks[i].trans.gameObject.SetActive(true);
                    }
                    for (int i = count; i < _selectedCharacters.Count; i++)
                    {
                        RectTransform sp = Object.Instantiate(_selectMarks[0].trans, CharacterCardManager.cardManager.GetCardForbidNull(_selectedCharacters[i]).transform);
                        _selectMarks.Add((sp, sp.GetChild(0).GetComponent<TextMeshProUGUI>()));
                        _selectMarks[i].text.text = (i + 1).ToString();
                    }
                }
            }
            else
            {
                if (_selectedCharacters[_selectIndex].ID_C != null)
                {
                    _selectMarks[0].trans.SetParent(CharacterCardManager.cardManager.GetCardForbidNull(_selectedCharacters[_selectIndex]).transform);
                    _selectMarks[0].trans.localPosition = Vector3.zero;
                    _selectMarks[0].text.text = null;
                    _selectMarks[0].trans.gameObject.SetActive(true);
                }
                else
                {
                    _selectMarks[0].trans.gameObject.SetActive(false);
                }
            }
        }
        private void UpDateSelectCharacter()
        {
            Debug.Log(_selectedCharacters.Count);
            Debug.Log(_selectIndex);
            //����ĳ��˳������
            if (_selectIndex >= 0)
            {
                for (int i = 0; i < _selectedCharacters.Count; i++)
                {
                    if (i != _selectIndex)
                    {
                        CharacterCardManager.cardManager.GetCardForbidNull(_selectedCharacters[i]).gameObject.SetActive(false);
                    }
                    else if (_selectedCharacters[i].ID_C != null)
                    {
                        CharacterCardManager.cardManager.GetCardForbidNull(_selectedCharacters[i]).gameObject.SetActive(true);
                    }
                }
            }
            else
            {
                for (int i = _selectedCharacters.Count - 1; i >= 0; i--)
                {
                    Button button = CharacterCardManager.cardManager.GetCardForbidNull(_selectedCharacters[i]);
                    button.gameObject.SetActive(true);
                    button.transform.SetAsFirstSibling();
                }
            }
        }

        private void UpdateCharacterMessage(EntityID characterId)
        {
            _skillArea.gameObject.SetActive(true);
            _talentArea.gameObject.SetActive(true);
            if (characterId.ID_C == null)
            {
                for (int i = 0; i < 3; i++)
                {
                    _skillSelectorCards[i].SkillRT.gameObject.SetActive(false);
                    _noneSkillInfo[i].SetActive(true);
                }
                _skillContent.vertical = false;
                _skillContent.content.anchoredPosition = Vector2.zero;
                _nullMask.SetActive(true);
                _skillSelectRT.gameObject.SetActive(false);
                for (int i = 0; i < _talentCardList.Count; i++)
                {
                    _talentCardList[i].TalentRT.gameObject.SetActive(false);
                }
                return;
            }
            EntityData entityData = CharacterCardManager.cardManager.GetCharacterAttribute(characterId);
            _englishName.text = entityData.EnglishName;
            _name.text = entityData.ChineseName;
            _hpText.text = entityData.MaxHp.ToString();
            _atkText.text = entityData.Attack.ToString();
            _phdText.text = entityData.Defense.ToString();
            _mgrText.text = entityData.MagicResistance.ToString();
            _reStartText.text = entityData.RespawnTime.ToString();
            _costText.text = entityData.Cost.ToString();
            _occupyText.text = entityData.BlockOccupation.ToString();
            _atkBTText.text = entityData.BaseAttackTime.ToString();
            _nullMask.SetActive(false);
            int characterIndex = _selectedCharacters.IndexOf(characterId);
            var abilities = entityData.Abilities;
            if (abilities.Count == 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    _skillSelectorCards[i].SkillRT.gameObject.SetActive(false);
                    _noneSkillInfo[i].SetActive(true);
                }
                _skillContent.vertical = false;
                _skillContent.content.anchoredPosition = Vector2.zero;
            }
            else
            {
                for (int i = 0; i < abilities.Count; i++)
                {
                    int index = i;
                    _skillSelectorActions[i] = () =>
                    {
                        _skillSelectRT.SetParent(_skillSelectorCards[index].SkillRT);
                        _skillSelectRT.offsetMax = Vector2.zero;
                        _skillSelectRT.offsetMin = Vector2.zero;
                        _skillSelectRT.gameObject.SetActive(true);
                        _selectedCharacterSkill[characterIndex] = index;
                        CharacterCardManager.cardManager.ResetCardForbidNullSkill(characterId);
                    };
                    _skillSelectorCards[i].UpdateSkillCardMessage(abilities[i]);
                    _skillSelectorCards[i].SkillRT.gameObject.SetActive(true);
                    _noneSkillInfo[i].SetActive(false);
                }
                for (int i = abilities.Count; i < 3; i++)
                {
                    _skillSelectorCards[i].SkillRT.gameObject.SetActive(false);
                    _noneSkillInfo[i].SetActive(true);
                }
                int skillSelect = _selectedCharacterSkill[characterIndex];
                _skillSelectRT.SetParent(_skillSelectorCards[skillSelect].SkillRT);
                _skillSelectRT.offsetMax = Vector2.zero;
                _skillSelectRT.offsetMin = Vector2.zero;
                _skillSelectRT.gameObject.SetActive(true);
                _skillContent.vertical = _skillArea.rect.height < _content.rect.height;
            }
            Talent[] talents = entityData.Prefab.GetComponents<Talent>();
            if (talents.Length <= _talentCardList.Count)
            {
                for (int i = 0; i < talents.Length; i++)
                {
                    _talentCardList[i].TalentRT.gameObject.SetActive(true);
                    _talentCardList[i].UpdateTalentCardMessage(talents[i]);
                }
                for (int i = talents.Length; i < _talentCardList.Count; i++)
                {
                    _talentCardList[i].TalentRT.gameObject.SetActive(false);
                }
            }
            else
            {
                for (int i = 0; i < _talentCardList.Count; i++)
                {
                    _talentCardList[i].TalentRT.gameObject.SetActive(true);
                    _talentCardList[i].UpdateTalentCardMessage(talents[i]);
                }
                for (int i = _talentCardList.Count; i < talents.Length; i++)
                {
                    TalentCard card = new TalentCard(new Vector2(0, 0), _talentContent, Color.black, 210);
                    card.UpdateTalentCardMessage(talents[i]);
                    _talentCardList.Add(card);
                }
            }
            SwitchToSkill_Talent(_showSkill);
        }
        public override void OnEnter()
        {
            base.OnEnter();
            UpDateSelectCharacter();
            UpDateSelectMask();
            if (_selectIndex >= 0)
            {
                UpdateCharacterMessage(_selectedCharacters[_selectIndex]);
            }
            else
            {
                UpdateCharacterMessage(EntityID.Null);
            }
            SwitchToSkill_Talent(true);
        }
        public override void OnPause()
        {
            base.OnPause();
            for (int i = 0; i < _selectMarks.Count; i++)
            {
                _selectMarks[i].trans.gameObject.SetActive(false);
                _selectMarks[i].trans.SetParent(_frame);
            }
        }
        public override void OnExit()
        {
            base.OnExit();
            for (int i = 0; i < _selectMarks.Count; i++)
            {
                _selectMarks[i].trans.gameObject.SetActive(false);
                _selectMarks[i].trans.SetParent(_frame);
            }
            for (int i = _selectedCharacters.Count - 1; i >= 0; i--)
            {
                if (_selectedCharacters[i].ID_C == null)
                {
                    _selectedCharacters.RemoveAt(i);
                    _selectedCharacterSkill.RemoveAt(i);
                }
            }
        }
    }
}

