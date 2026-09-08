using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using AbilitySystem;

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

        // 消息页切换:枚举序 = skillTalentSwitch 下按钮子节点序(skill/talent/subp)
        private enum MessagePage
        {
            Skill,
            Talent,
            Subp,
        }
        private MessagePage _page = MessagePage.Skill;
        private Image _skillImage, _talentImage, _subpImage;
        private Color _white1 = new Color(0.7686f, 0.7686f, 0.7686f);
        private Color _black1 = new Color(0.2196f, 0.2196f, 0.2196f);
        private TextMeshProUGUI _skillText, _talentText, _subpText;
        private RectTransform _skillSelectRT;
        private ScrollRect _skillContent;
        private RectTransform _skillArea, _content;
        private RectTransform _talentArea, _talentContent;
        private AbilityCard[] _abilitySelectorCards;
        private List<TalentCard> _talentCardList;
        private ScrollRect _subpArea;
        private RectTransform _subpContent;
        private SubpCard _subpCard;
        private UnityAction[] _skillSelectorActions;
        private GameObject[] _noneSkillInfo;

        private TextMeshProUGUI _englishName, _name, _hpText, _atkText, _phdText, _mgrText, _reStartText, _costText, _occupyText, _atkBTText;
        private GameObject _nullMask;
        private AttackRangeTiles _attackRangeTiles;

        private CharacterSelectPanel() : base(new UIType("Prefabs/UI/MyUIs/CharacterSelectPanel"))
        {
            _frame = GetComponentInChildrenByPath<Transform>("container/characters/frame");
            RectTransform sp = GetComponentInChildrenByPath<RectTransform>("container/characters/frame/selectmark");
            _skillSelectRT = GetComponentInChildrenByPath<RectTransform>("container/characterMessage/skillArea/view/content/skillSelectMark");
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
            _attackRangeTiles = new AttackRangeTiles(
                GetComponentInChildrenByPath<RectTransform>("container/characterMessage/messageArea/attackRange"),
                GetComponentInChildrenByPath<RectTransform>("container/characterMessage/messageArea/attackRange/self"),
                GetComponentInChildrenByPath<RectTransform>("container/characterMessage/messageArea/attackRange/range"),
                leftAlign: true);
            _skillImage = GetComponentInChildrenByPath<Image>("container/characterMessage/skillTalentSwitch/skill");
            _skillText = GetComponentInChildrenByPath<TextMeshProUGUI>("container/characterMessage/skillTalentSwitch/skill/text");
            _talentImage = GetComponentInChildrenByPath<Image>("container/characterMessage/skillTalentSwitch/talent");
            _talentText = GetComponentInChildrenByPath<TextMeshProUGUI>("container/characterMessage/skillTalentSwitch/talent/text");
            _subpImage = GetComponentInChildrenByPath<Image>("container/characterMessage/skillTalentSwitch/subp");
            _subpText = GetComponentInChildrenByPath<TextMeshProUGUI>("container/characterMessage/skillTalentSwitch/subp/text");
            BindPageSwitch(_skillImage, MessagePage.Skill);
            BindPageSwitch(_talentImage, MessagePage.Talent);
            BindPageSwitch(_subpImage, MessagePage.Subp);
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
            GetComponentInChildrenByPath<Button>("clear").onClick.AddListener(() =>
            {
                ClearSelection();
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
                        }
                        else if (_selectedCharacters.Count < TeamFrame.MaxMembers)
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
                    RefreshMemberSkillIcons();
                });
            }

            _content = GetComponentInChildrenByPath<RectTransform>("container/characterMessage/skillArea/view/content");
            _skillContent = GetComponentInChildrenByPath<ScrollRect>("container/characterMessage/skillArea");
            _skillArea = _skillContent.GetComponent<RectTransform>();
            _noneSkillInfo = new GameObject[3] { _content.GetChild(0).gameObject, _content.GetChild(1).gameObject, _content.GetChild(2).gameObject };
            _abilitySelectorCards = new AbilityCard[3];
            _skillSelectorActions = new UnityAction[3];
            for (int i = 2; i >= 0; i--)
            {
                _abilitySelectorCards[i] = new AbilityCard(_content, Color.black);
                Image ii = _abilitySelectorCards[i].AbilityRT.gameObject.AddComponent<Image>();
                ii.color = new Color(1, 1, 1, 0);
                Button bi = _abilitySelectorCards[i].AbilityRT.gameObject.AddComponent<Button>();
                int index = i;
                bi.onClick.AddListener(() => _skillSelectorActions[index]());
                _abilitySelectorCards[i].AbilityRT.SetAsFirstSibling();
            }
            _talentArea = GetComponentInChildrenByPath<RectTransform>("container/characterMessage/talentArea");
            _talentContent = GetComponentInChildrenByPath<RectTransform>("container/characterMessage/talentArea/view/content");
            _talentCardList = new List<TalentCard>();
            _subpArea = GetComponentInChildrenByPath<ScrollRect>("container/characterMessage/subpArea");
            _subpContent = GetComponentInChildrenByPath<RectTransform>("container/characterMessage/subpArea/view/content");
            _subpCard = new SubpCard(_subpContent, Color.black);
            _subpCard.SubpRT.gameObject.SetActive(false);
        }
        private void BindPageSwitch(Image button, MessagePage page)
        {
            EventTrigger.Entry click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            click.callback.AddListener((data) =>
            {
                if (_page != page)
                {
                    SwitchPage(page);
                }
            });
            button.GetComponent<EventTrigger>().triggers.Add(click);
        }
        /// <summary>切换消息页：按钮选中态白底黑字、未选中黑底白字；只显示当前页内容区，
        /// 并对新显示区的 content 强制重建布局。</summary>
        private void SwitchPage(MessagePage page)
        {
            _page = page;
            SetSwitchButton(_skillImage, _skillText, page == MessagePage.Skill);
            SetSwitchButton(_talentImage, _talentText, page == MessagePage.Talent);
            SetSwitchButton(_subpImage, _subpText, page == MessagePage.Subp);
            _skillArea.gameObject.SetActive(page == MessagePage.Skill);
            _talentArea.gameObject.SetActive(page == MessagePage.Talent);
            _subpArea.gameObject.SetActive(page == MessagePage.Subp);
            switch (page)
            {
                case MessagePage.Skill:
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
                    break;
                case MessagePage.Talent:
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_talentContent);
                    break;
                case MessagePage.Subp:
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_subpContent);
                    break;
            }
        }
        private void SetSwitchButton(Image button, TextMeshProUGUI text, bool selected)
        {
            button.color = selected ? _white1 : _black1;
            text.color = selected ? _black1 : _white1;
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
        /// <summary>
        /// 刷新全部角色卡的技能图标:队伍内成员显示其选择的技能,不在队伍的回到默认技能 0。
        /// 覆盖加入/移除/替换成员时选择被重置为 0 的情形。
        /// </summary>
        private void RefreshMemberSkillIcons()
        {
            HashSet<EntityID> inTeam = new HashSet<EntityID>();
            for (int i = 0; i < _selectedCharacters.Count; i++)
            {
                if (_selectedCharacters[i].ID_C == null) continue;
                inTeam.Add(_selectedCharacters[i]);
                CharacterCardManager.cardManager.ResetCardForbidNullSkill(_selectedCharacters[i], _selectedCharacterSkill[i]);
            }
            EntityID[] allCharacters = SaveSystem.Current.charactersOwn.ToArray();
            for (int i = 0; i < allCharacters.Length; i++)
            {
                if (!inTeam.Contains(allCharacters[i]))
                    CharacterCardManager.cardManager.ResetCardForbidNullSkill(allCharacters[i], 0);
            }
        }
        /// <summary>
        /// 清空当前选择：自由编队模式清空全部成员；槽位模式只把当前槽位置空（技能选择重置为 0）。
        /// 只改面板本地副本，不写存档，点 done 才保存。
        /// </summary>
        private void ClearSelection()
        {
            if (_selectIndex >= 0)
            {
                _selectedCharacters[_selectIndex] = EntityID.Null;
                _selectedCharacterSkill[_selectIndex] = 0;
            }
            else
            {
                _selectedCharacters.Clear();
                _selectedCharacterSkill.Clear();
            }
            UpDateSelectMask();
            RefreshMemberSkillIcons();
            UpdateCharacterMessage(EntityID.Null);
        }
        private void UpDateSelectCharacter()
        {
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
            _subpArea.gameObject.SetActive(true);
            if (characterId.ID_C == null)
            {
                for (int i = 0; i < 3; i++)
                {
                    _abilitySelectorCards[i].AbilityRT.gameObject.SetActive(false);
                    _noneSkillInfo[i].SetActive(true);
                }
                _skillContent.vertical = false;
                _skillContent.content.anchoredPosition = Vector2.zero;
                _nullMask.SetActive(true);
                _attackRangeTiles.Clear();
                _skillSelectRT.gameObject.SetActive(false);
                // 区常驻,只藏上一角色的残留卡片
                _subpCard.SubpRT.gameObject.SetActive(false);
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
            _attackRangeTiles.Show(entityData.VisionRange);
            _nullMask.SetActive(false);
            int characterIndex = _selectedCharacters.IndexOf(characterId);
            var abilities = entityData.Skills;
            if (abilities.Count == 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    _abilitySelectorCards[i].AbilityRT.gameObject.SetActive(false);
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
                        _skillSelectRT.SetParent(_abilitySelectorCards[index].AbilityRT, false);
                        _skillSelectRT.gameObject.SetActive(true);
                        _selectedCharacterSkill[characterIndex] = index;
                        CharacterCardManager.cardManager.ResetCardForbidNullSkill(characterId, index);
                    };
                    _abilitySelectorCards[i].UpdateAbilityCardMessage(abilities[i]);
                    _abilitySelectorCards[i].AbilityRT.gameObject.SetActive(true);
                    _noneSkillInfo[i].SetActive(false);
                }
                for (int i = abilities.Count; i < 3; i++)
                {
                    _abilitySelectorCards[i].AbilityRT.gameObject.SetActive(false);
                    _noneSkillInfo[i].SetActive(true);
                }
                int skillSelect = _selectedCharacterSkill[characterIndex];
                _skillSelectRT.SetParent(_abilitySelectorCards[skillSelect].AbilityRT, false);
                _skillSelectRT.gameObject.SetActive(true);
                _skillContent.vertical = _skillArea.rect.height < _content.rect.height;
            }
            List<AbilityConfig> talents = entityData.Talents;
            if (talents == null) talents = new List<AbilityConfig>();
            if (talents.Count <= _talentCardList.Count)
            {
                for (int i = 0; i < talents.Count; i++)
                {
                    _talentCardList[i].TalentRT.gameObject.SetActive(true);
                    _talentCardList[i].UpdateTalentCardMessage(talents[i]);
                }
                for (int i = talents.Count; i < _talentCardList.Count; i++)
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
                for (int i = _talentCardList.Count; i < talents.Count; i++)
                {
                    TalentCard card = new TalentCard(_talentContent, Color.black);
                    card.UpdateTalentCardMessage(talents[i]);
                    _talentCardList.Add(card);
                }
            }
            // 特性卡:按有无特性自显隐(数据源 SubJobTrait 资产),单卡无选择逻辑;
            // 可见时的布局重建由 SwitchPage 统一处理
            _subpCard.UpdateSubpCardMessage(entityData);
            SwitchPage(_page);
        }
        public override void OnEnter()
        {
            base.OnEnter();
            UpDateSelectCharacter();
            UpDateSelectMask();
            RefreshMemberSkillIcons();
            if (_selectIndex >= 0)
            {
                UpdateCharacterMessage(_selectedCharacters[_selectIndex]);
            }
            else
            {
                UpdateCharacterMessage(EntityID.Null);
            }
            SwitchPage(MessagePage.Skill);
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

