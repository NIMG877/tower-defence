using Cysharp.Threading.Tasks;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MyUI
{
    public class MonsterHandbookPanel : BasePanel
    {
        private static MonsterHandbookPanel _instance;
        public static MonsterHandbookPanel Panel
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MonsterHandbookPanel();
                }
                return _instance;
            }
        }
        private ScrollRect _monsterList;
        private Image[] _images;
        private IReadOnlyList<EntityData> _monsterDatas;
        private float _cellHeight, _cellSpace, _viewHeight, _canScrollHeight;
        private int _totalLineNum, _currentMaxLineNum, _onePageLineNum, _colNum, _currentSelectSerial;
        private TextMeshProUGUI _labelText, _nameText, _idText, _massText, _descriptionText, _skillDescriptionText, _talentDescription;
        private Image _headImg, _elitorbossImg, _content;
        private Sprite _elitSprite, _bossSprite;
        private RectTransform _skillImg, _talentImg, _details, _selectMask;
        private RadarDataController _radarDataController;
        private MonsterHandbookPanel() : base(new UIType("Prefabs/UI/MyUIs/MonsterHandbookPanel"))
        {
            _currentSelectSerial = -1;
            _monsterList = GetComponentInChildrenByPath<ScrollRect>("monsterListArea");
            _images = _monsterList.transform.Find("content").GetComponentsInChildren<Image>();
            _labelText = GetComponentInChildrenByPath<TextMeshProUGUI>("monsterDetail/label");
            _nameText = GetComponentInChildrenByPath<TextMeshProUGUI>("monsterDetail/name");
            _idText = GetComponentInChildrenByPath<TextMeshProUGUI>("monsterDetail/id");
            _massText = GetComponentInChildrenByPath<TextMeshProUGUI>("monsterDetail/massText");
            _descriptionText = GetComponentInChildrenByPath<TextMeshProUGUI>("monsterDetail/details/content/description");
            _skillDescriptionText = GetComponentInChildrenByPath<TextMeshProUGUI>("monsterDetail/details/content/skillDescription");
            _talentDescription = GetComponentInChildrenByPath<TextMeshProUGUI>("monsterDetail/details/content/talentDescription");
            _headImg = GetComponentInChildrenByPath<Image>("monsterDetail/headimg");
            _elitorbossImg = GetComponentInChildrenByPath<Image>("monsterDetail/elitorboss");
            Sprite[] sprites = Resources.LoadAll<Sprite>("Prefabs/UI/MyUIs/UISprites/MonsterHandbook/enemy_handbook");
            _elitSprite = sprites[43];
            _bossSprite = sprites[40];
            _skillImg = GetComponentInChildrenByPath<RectTransform>("monsterDetail/details/content/skillImg");
            _talentImg = GetComponentInChildrenByPath<RectTransform>("monsterDetail/details/content/talentImg");
            _content = GetComponentInChildrenByPath<Image>("monsterDetail/details/content");
            _radarDataController = GetComponentInChildrenByPath<RadarDataController>("monsterDetail/radarBackImg/radarData");
            _details = GetComponentInChildrenByPath<RectTransform>("monsterDetail/details");
            _selectMask = GetComponentInChildrenByPath<RectTransform>("monsterListArea/selectMask");
            _monsterDatas = GameDataService.EntityRepository.GetByCategory("m");
            for (int i = 0; i < _images.Length; i++)
            {
                int index = i;
                _images[i].GetComponent<Button>().onClick.AddListener(() =>
                {
                    int serialNum = (_currentMaxLineNum - _onePageLineNum + (index / _colNum - (_currentMaxLineNum + 1) % (_onePageLineNum + 1) + _onePageLineNum + 1) % (_onePageLineNum + 1)) * _onePageLineNum + index % _onePageLineNum;
                    SetMonsterDataToPanel(serialNum);
                    SetSelectMask(index);
                });
            }
            _onePageLineNum = 4;
            _colNum = 4;
            _cellHeight = 82.5f;
            _cellSpace = 10;

            ResetMonsterList();
            SetMonsterDataToPanel(0);
            SetSelectMask(0);
        }
        private void ResetMonsterList()
        {
            _currentMaxLineNum = _onePageLineNum;
            int totalNum = _monsterDatas.Count;
            _totalLineNum = (int)Math.Ceiling(totalNum / (float)_colNum);
            Vector2 size = _monsterList.content.sizeDelta;
            size.y = _cellHeight * _totalLineNum + _cellSpace * (_totalLineNum - 1);
            _monsterList.content.sizeDelta = size;
            int minttH = Math.Min(totalNum, _images.Length);
            for (int i = 0; i < minttH; i++)
            {
                _images[i].sprite = _monsterDatas[i].HeadImage;
                _images[i].gameObject.SetActive(true);
            }
            for (int i = minttH; i < _images.Length; i++)
            {
                _images[i].gameObject.SetActive(false);
            }
            if (_totalLineNum > _onePageLineNum + 1)
            {
                _viewHeight = _monsterList.viewport.sizeDelta.y;
                _canScrollHeight = size.y - _viewHeight;
                _monsterList.onValueChanged.AddListener((value) =>
                {
                    float haveDownHeight = (1 - value.y) * _canScrollHeight;
                    int shouldMax = (int)((haveDownHeight + _cellSpace) / (_cellHeight + _cellSpace)) + _onePageLineNum;
                    if (shouldMax > _currentMaxLineNum && shouldMax < _totalLineNum)
                    {
                        for (int i = 0; i < shouldMax - _currentMaxLineNum; i++)
                        {
                            LineDown();
                        }
                    }
                    else if (shouldMax < _currentMaxLineNum && shouldMax >= _onePageLineNum)
                    {
                        for (int i = 0; i < _currentMaxLineNum - shouldMax; i++)
                        {
                            LineUp();
                        }
                    }
                });
            }
        }
        /// <summary>
        /// ������ʱ�����������ڵײ�
        /// </summary>
        private void LineDown()
        {
            ++_currentMaxLineNum;
            if ((_currentMaxLineNum + 1) * _colNum - 1 < _currentSelectSerial || (_currentMaxLineNum - _onePageLineNum) * _colNum > _currentSelectSerial)
            {
                if (_selectMask.gameObject.activeSelf)
                {
                    _selectMask.gameObject.SetActive(false);
                }
            }
            else if (!_selectMask.gameObject.activeSelf)
            {
                _selectMask.gameObject.SetActive(true);
            }
            Image[] images = new Image[_colNum];
            int _imagesL = _images.Length;
            for (int i = 0; i < _colNum; i++)
            {
                images[i] = _images[i];
                images[i].rectTransform.anchoredPosition += Vector2.down * 5 * (_cellHeight + _cellSpace);
            }
            for (int i = _colNum; i < _imagesL; i++)
            {
                _images[i - _colNum] = _images[i];
            }
            for (int i = 0; i < _colNum; i++)
            {
                _images[i + _imagesL - _colNum] = images[i];
            }
            int m = Math.Min(_currentMaxLineNum * _colNum + _colNum, _monsterDatas.Count);
            for (int i = _currentMaxLineNum * _colNum; i < m; i++)
            {
                images[i - _currentMaxLineNum * _colNum].sprite = _monsterDatas[i].HeadImage;
            }
            for (int i = m; i < _currentMaxLineNum * _colNum + _colNum; i++)
            {
                images[i - _currentMaxLineNum * _colNum].gameObject.SetActive(false);
            }
        }
        private void LineUp()
        {
            --_currentMaxLineNum;
            if ((_currentMaxLineNum + 1) * _colNum - 1 < _currentSelectSerial || (_currentMaxLineNum - _onePageLineNum) * _colNum > _currentSelectSerial)
            {
                if (_selectMask.gameObject.activeSelf)
                {
                    _selectMask.gameObject.SetActive(false);
                }
            }
            else if (!_selectMask.gameObject.activeSelf)
            {
                _selectMask.gameObject.SetActive(true);
            }
            Image[] images = new Image[_colNum];
            int _imagesL = _images.Length;
            for (int i = _colNum - 1; i >= 0; i--)
            {
                images[i] = _images[i + _imagesL - _colNum];
                images[i].rectTransform.anchoredPosition -= Vector2.down * 5 * (_cellHeight + _cellSpace);
            }
            for (int i = _imagesL - _colNum - 1; i >= 0; i--)
            {
                _images[i + _colNum] = _images[i];
            }
            for (int i = 0; i < _colNum; i++)
            {
                _images[i] = images[i];
            }
            for (int i = (_currentMaxLineNum - _onePageLineNum) * _colNum; i < (_currentMaxLineNum - _onePageLineNum) * _colNum + _colNum; i++)
            {
                images[i - (_currentMaxLineNum - _onePageLineNum) * _colNum].sprite = _monsterDatas[i].HeadImage;
                images[i - (_currentMaxLineNum - _onePageLineNum) * _colNum].gameObject.SetActive(true);
            }
        }
        private void SetMonsterDataToPanel(int monsterAttributeIndex)
        {
            if (monsterAttributeIndex != _currentSelectSerial)
            {
                _currentSelectSerial = monsterAttributeIndex;
                EntityData monsterData = _monsterDatas[monsterAttributeIndex];
                _radarDataController.Set6Data(new float[6] { monsterData.MaxHp, monsterData.Attack, 1 / monsterData.BaseAttackTime, monsterData.Defense, monsterData.MagicResistance, monsterData.MoveSpeed });
                _labelText.text = monsterData.MonsterLabel;
                _nameText.text = monsterData.ChineseName;
                _idText.text = $"{monsterData.ID.ID_C}-{monsterData.ID.ID_N}";
                _massText.text = "���� " + monsterData.MassLevel.ToString();
                _descriptionText.text = monsterData.Description;
                _headImg.sprite = monsterData.HeadImage;
                if (monsterData.MonsterStatus == 0)
                {
                    _elitorbossImg.enabled = false;
                }
                else if (monsterData.MonsterStatus == 1)
                {
                    _elitorbossImg.sprite = _elitSprite;
                    _elitorbossImg.enabled = true;
                }
                else
                {
                    _elitorbossImg.sprite = _bossSprite;
                    _elitorbossImg.enabled = true;
                }
                float desHeight = _descriptionText.preferredHeight;
                float imgh = _skillImg.sizeDelta.y;
                float space = 5;
                _descriptionText.rectTransform.sizeDelta = new Vector2(_descriptionText.rectTransform.sizeDelta.x, desHeight);
                float ch = desHeight + space;
                var skills = monsterData.Abilities;
                Talent[] talents = monsterData.Prefab.GetComponents<Talent>();
                if (skills != null && skills.Count > 0)
                {
                    _skillDescriptionText.text = "● " + skills[0].description;
                    for (int i = 1; i < skills.Count; i++)
                    {
                        _skillDescriptionText.text += "\n● " + skills[i].description;
                    }
                    float skillDesHeight = _skillDescriptionText.preferredHeight;
                    _skillDescriptionText.rectTransform.sizeDelta = new Vector2(_skillDescriptionText.rectTransform.sizeDelta.x, skillDesHeight);
                    _skillImg.anchoredPosition = new Vector2(0, -ch);
                    ch += imgh + space;
                    _skillDescriptionText.rectTransform.anchoredPosition = new Vector2(10, -ch);
                    ch += skillDesHeight + space;
                    _skillDescriptionText.gameObject.SetActive(true);
                    _skillImg.gameObject.SetActive(true);
                }
                else
                {
                    _skillDescriptionText.gameObject.SetActive(false);
                    _skillImg.gameObject.SetActive(false);
                }
                if (talents.Length > 0)
                {
                    _talentDescription.text = "�� " + talents[0].TalentDescription;
                    for (int i = 1; i < talents.Length; i++)
                    {
                        _talentDescription.text += "\n�� " + talents[i].TalentDescription;
                    }
                    float talentDesHeight = _talentDescription.preferredHeight;
                    _talentDescription.rectTransform.sizeDelta = new Vector2(_talentDescription.rectTransform.sizeDelta.x, talentDesHeight);
                    _talentImg.anchoredPosition = new Vector2(0, -ch);
                    ch += imgh + space;
                    _talentDescription.rectTransform.anchoredPosition = new Vector2(10, -ch);
                    ch += talentDesHeight + space;
                    _talentDescription.gameObject.SetActive(true);
                    _talentImg.gameObject.SetActive(true);
                }
                else
                {
                    _talentDescription.gameObject.SetActive(false);
                    _talentImg.gameObject.SetActive(false);
                }
                _content.rectTransform.sizeDelta = new Vector2(_content.rectTransform.sizeDelta.x, ch);
                if (ch <= _details.sizeDelta.y)
                {
                    _content.raycastTarget = false;
                }
                else
                {
                    _content.raycastTarget = true;
                }
            }

        }
        private void SetSelectMask(int selectIndex)
        {
            int index = ((_onePageLineNum + 1) * _colNum + selectIndex - _colNum * ((_currentMaxLineNum - _onePageLineNum) % (_onePageLineNum + 1))) % ((_onePageLineNum + 1) * _colNum);
            _selectMask.SetParent(_images[index].transform);
            _selectMask.anchoredPosition = Vector2.zero;
            if (!_selectMask.gameObject.activeSelf)
            {
                _selectMask.gameObject.SetActive(true);
            }
        }
    }
}

