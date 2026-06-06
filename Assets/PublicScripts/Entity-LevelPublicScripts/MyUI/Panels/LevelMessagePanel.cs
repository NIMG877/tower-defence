using Cysharp.Threading.Tasks;
using DG.Tweening;

using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MyUI
{
    public class LevelMessagePanel : BasePanel
    {
        #region Nested Types
        private class StaticEntityPlaceData
        {
            GameObject _selectorRoot;
            RectTransform _selectorRect;
            Image _photoImage, _classImage, _respawnRate;
            GameObject _respawn;
            TextMeshProUGUI _costText, _countText, _respawnRateText;
            EventTrigger _trigger;

            public EntityID EntityId;
            public EntityData EntityData;

            float _respawnTimer;
            float _selectorYAnchor;
            int _deployCount;
            int _remainingCount;
            bool _isAffordable;

            public StaticEntityPlaceData(GameObject selector)
            {
                _selectorRoot = selector;
                _selectorRect = _selectorRoot.GetComponent<RectTransform>();
                _photoImage = selector.transform.Find("photo").GetComponent<Image>();
                _classImage = selector.transform.Find("head/class").GetComponent<Image>();
                _costText = selector.transform.Find("head/cost").GetComponent<TextMeshProUGUI>();
                _countText = selector.transform.Find("count").GetComponent<TextMeshProUGUI>();
                _respawn = selector.transform.Find("respawn").gameObject;
                _respawnRate = _respawn.transform.Find("rate").GetComponent<Image>();
                _respawnRateText = _respawn.transform.Find("rateText").GetComponent<TextMeshProUGUI>();
                _trigger = _photoImage.transform.GetComponent<EventTrigger>();
            }
            public void InitializeSelectorData(EntityID staticId, int num)
            {
                EntityId = staticId;
                EntityData = GameDataService.EntityRepository.Get(staticId);

                _respawnTimer = 0;
                _deployCount = 0;
                _remainingCount = num;
                _selectorYAnchor = -60;
                _isAffordable = false;
                _respawn.SetActive(false);
                if (_remainingCount > 1)
                    _countText.text = $"��{_remainingCount}";
                else
                    _countText.text = "";
                _selectorRoot.gameObject.SetActive(true);
                _photoImage.sprite = EntityData.HeadImage;
                _classImage.sprite = Panel._professionsSmall[EntityData.CharacterJob];


                _trigger.triggers.Clear();
                EventTrigger.Entry point = new EventTrigger.Entry();
                point.eventID = EventTriggerType.PointerClick;
                point.callback.AddListener((data) =>
                {
                    if (!Panel._selectedStaticEntityID.HasValue)
                    {
                        Panel.UIStates_SwitchTo_ViewBeforeSet(this);
                        SelectorMove(true);
                    }
                    else if (Panel._selectedPlaceData == this)
                    {
                        Panel.UIStates_SwitchTo_Normal();
                        SelectorMove(false);
                    }
                    else
                    {
                        // 已选中的是别处：可能是 placeData（→下移），也可能是已部署 entity（→无 placeData 可下移）
                        Panel._selectedPlaceData?.SelectorMove(false);
                        Panel.UIStates_SwitchTo_ViewBeforeSet(this);
                        SelectorMove(true);
                    }
                });
                EventTrigger.Entry dragBegin = new EventTrigger.Entry();
                dragBegin.eventID = EventTriggerType.BeginDrag;
                dragBegin.callback.AddListener((data) =>
                {
                    if (!Panel._selectedStaticEntityID.HasValue)
                    {
                        SelectorMove(true);
                    }
                    else if (Panel._selectedPlaceData != this)
                    {
                        Panel._selectedPlaceData?.SelectorMove(false);
                        SelectorMove(true);
                    }
                    if (_isAffordable)
                    {
                        Panel.UIStates_SwitchTo_Setting(this);
                    }
                    else
                    {
                        Panel.UIStates_SwitchTo_ViewBeforeSet(this);
                    }
                });
                EventTrigger.Entry drag = new EventTrigger.Entry();
                drag.eventID = EventTriggerType.Drag;
                drag.callback.AddListener((data) =>
                {
                    Panel.UIStates_Update_Dragger();
                });
                EventTrigger.Entry dragEnd = new EventTrigger.Entry();
                dragEnd.eventID = EventTriggerType.EndDrag;
                dragEnd.callback.AddListener((data) =>
                {
                    Panel.HideTargetOrEnterNextStage(this);
                });
                _trigger.triggers.Add(point);
                _trigger.triggers.Add(dragBegin);
                _trigger.triggers.Add(drag);
                _trigger.triggers.Add(dragEnd);
            }
            public int CalculateCost()
            {
                if (EntityData.RespawnCostUp>0)
                {
                    if (_deployCount == 0)
                    {
                        return EntityData.Cost;
                    }
                    if (_deployCount == 1)
                    {
                        return (int)(EntityData.Cost * (1+EntityData.RespawnCostUp/100));
                    }
                    return (int)(EntityData.Cost * (1+EntityData.RespawnCostUp/100)*((1+EntityData.RespawnCostUp/100)));
                }
                return EntityData.Cost;
            }
            public void DeltaNum(int delta)
            {
                _remainingCount += delta;
                if (_remainingCount > 1)
                    _countText.text = $"��{_remainingCount}";
                else
                    _countText.text = "";
                if (_remainingCount > 0)
                {
                    _selectorRoot.SetActive(true);
                }
                else
                {
                    _remainingCount = 0;
                    _selectorRoot.SetActive(false);
                }
                //�޸���ʾ����������ʾ
            }
            public void SelectorMove(bool up)
            {
                if (up)
                {
                    Vector2 vector2 = _selectorRect.anchoredPosition;
                    DOTween.To((value) =>
                    {
                        vector2.y = value;
                        _selectorRect.anchoredPosition = vector2;
                    }, vector2.y, _selectorYAnchor + 10, 0.1f).SetUpdate(true);
                    //_selectorRoot.transform.DOMoveY(0.2f, 0.1f).SetRelative().SetUpdate(true);
                }
                else
                {
                    Vector2 vector2 = _selectorRect.anchoredPosition;
                    DOTween.To((value) =>
                    {
                        vector2.y = value;
                        _selectorRect.anchoredPosition = vector2;
                    }, vector2.y, _selectorYAnchor, 0.1f).SetUpdate(true);
                }
            }
            public void SetNum(int setNum)
            {
                DeltaNum(-setNum);
                _deployCount++;
                if (EntityData.RespawnStrategy == 0 || (EntityData.RespawnStrategy == 2 && _remainingCount == 0))
                {
                    RespawnTiming();
                }
            }
            public void CallBackNum(int callBackNum)
            {
                DeltaNum(callBackNum);
                if (EntityData.RespawnStrategy == 1)
                {
                    RespawnTiming();
                }
            }
            public async void RespawnTiming()
            {
                float tt = EntityData.RespawnTime;
                _respawnTimer = tt;
                _respawn.SetActive(true);
                while (_respawnTimer > 0)
                {
                    if (_respawnTimer < 0)
                        _respawnTimer = 0;
                    _respawnRateText.text = _respawnTimer.ToString("0.0");
                    _respawnRate.fillAmount = 1 - _respawnTimer / tt;
                    try
                    {
                        await UniTask.WaitForFixedUpdate(LevelResourceSharing.LevelCtk);
                        _respawnTimer -= Time.fixedDeltaTime;
                    }
                    catch (System.OperationCanceledException)
                    {
                        Debug.Log("RespawnTiming Canceled");
                        return;
                    }
                }
                _respawn.SetActive(false);
            }
            public void CanSetStateUpDate()
            {
                int cost = CalculateCost();
                _costText.text = cost.ToString();
                if (_respawnTimer <= 0 && cost <= LevelRescurceManager.Manager.CostMessage.currentCost && LevelRescurceManager.Manager.CanSetNumLeft - EntityData.MaxOccupyCount >= 0)
                {
                    _isAffordable = true;
                    _photoImage.color = Color.white;
                }
                else
                {
                    _isAffordable = false;
                    _photoImage.color = Color.gray;
                }
            }
        }
        #endregion

        #region UI Element References
        // ===== _selectorRoot Area =====
        private List<StaticEntityPlaceData> _placeDataList;
        private List<GameObject> _selectorObjects;
        private Transform _content;
        private GameObject _selectorSample;

        // ===== Time Control =====
        private GameObject _pauseMask;
        private Button _timeMultiple, _pause;

        // ===== Resource Display =====
        private Sprite _x1, _x2, _c, _p;
        private int _currentCost;
        private TextMeshProUGUI _cost;
        private Image _costSlider;

        // ===== Capacity Display =====
        private TextMeshProUGUI _isAffordableNumText;
        private int _isAffordableNum;

        // ===== Level Status =====
        private TextMeshProUGUI _currentNumAndTotalNum, _levelHpLeft;

        // ===== Left Message Panel =====
        private GameObject _leftMessage;
        private Image _chooser, _up, _down, _left, _right;
        private Image _class, _target;
        private RectTransform _rangeSelfTile, _rangeArea;
        private List<RectTransform> _rangeTiles;
        private TextMeshProUGUI _name, _statsText;
        private RectTransform _hpSlider, _hpBk;
        private (float width, float height) _hpSliderSize;
        private TextMeshProUGUI _hpText;
        private Sprite[] _professionsSmall, _professionsLighten;
        private RectTransform _skillTalentRect, _skillTalentRectParent;
        private SkillCard _skillCard;
        private SubpCard _subpCard;
        private List<TalentCard> _talentCards;
        private List<BuffCard> _buffCards;
        private Image[] _skillTalentSwitchButtons;
        private List<SpriteRenderer> _rangeImg;
        private GameObject _rangeImgCollection;

        // ===== Operator Panel =====
        private GameObject _operateArea;
        private Image _callBack, _skillOpen, _skillRange, _spBk, _spState, _spMask, _stop, _skillChargeNum;
        private TextMeshProUGUI _spText, _skillChargeNumText;
        private Sprite[] _spMessageAtlas, _skillRangeButton;
        private Skill _selectSkill;

        // ===== Floating Text Pool =====
        private Transform _text;
        private List<TextMeshProUGUI>[] _textsInPool;

        // ===== Misc UI =====
        private EventTrigger _levelMessageTrigger;
        #endregion

        #region Runtime State
        // ===== UI State Machine =====
        private enum UIState
        {
            normal,
            viewBeforeSet,
            setting,
            choosing,
            viewAfterSet,
        }
        private UIState _currentUIState;
        private bool _leftmessageOpen, _operaterOpen, _draggerOpen, _chooserOpen, _rangeOpen, _cansetOpen;

        // ===== Selection State (3-field) =====
        // 选中状态（三字段联合表达，由 UIState 状态机保证互斥）：
        //   viewBeforeSet/setting/choosing → _selectedStaticEntityID + _selectedPlaceData
        //   viewAfterSet                   → _selectedStaticEntityID + _selectedEntity
        //   normal/无选中                  → 三个全 null
        private StaticEntityPlaceData _selectedPlaceData;
        private EntityID? _selectedStaticEntityID;
        private Entity _selectedEntity;

        // ===== Map/Block Data =====
        private bool[,] _higherCanSetBlock;
        private bool[,] _lowerCanSetBlock;
        private bool[,] _staticEntityExistBlock;
        private int _isAffordableType;
        private List<(int i, int j)> _isAffordableBlockList;
        private BlockData[,] _blockDatas;
        private int _iSize, _jSize;

        // ===== Camera =====
        private Camera _camera;
        private Vector3 _cameraOriginalPos;
        private float _deltaX;

        // ===== Chooser / Orientation =====
        private bool _inChooser;
        private int _orientation;

        // ===== Damage Stats =====
        private string[] _characterChineseName;
        private DamageStatisticData[] _damageStatisticDatas;

        // ===== Time Control Flags =====
        private bool _isPause, _is2X, _isSlow;

        // ===== Skill State & Colors =====
        private int _currentShow;
        private Color _colorSelect = new Color(0, 0, 0, 0.5882f);
        private Color _colorUnSelect = new Color(0.3529f, 0.3529f, 0.3529f, 0.7843f);
        private Color _lightGreen = new Color(0.796f, 0.925f, 0.278f);
        private Color _lightGreen_half = new Color(0.796f, 0.925f, 0.278f, 0.5f);
        private Color _orange = new Color(1, 0.412f, 0);
        private Color _orange_half = new Color(1, 0.412f, 0, 0.5f);
        private Color _gray = new Color(0.259f, 0.259f, 0.259f);

        // ===== Legacy Unused Flags (kept to avoid breaking reflection/serialization) =====
        private bool _isShowMessage, _isShowCanSetBlock, _isShowOperate, _isShowAttackRange;
        #endregion

        #region Construction & Initialization
        private LevelMessagePanel() : base(new UIType("Prefabs/UI/MyUIs/LevelMessagePanel"))
        {
            InitCoreResources();
            InitTimeControl();
            InitTopStatusBar();
            InitLeftMessagePanel();
            InitOperatorPanel();
            InitSelectorArea();
            InitFloatingTextPool();
            InitEventTriggers();
        }

        private void InitCoreResources()
        {
            _camera = LevelResourceSharing.MainCamera;
            _rangeImgCollection = new GameObject("rangeImgCollection");
            _x1 = Resources.Load<Sprite>("Prefabs/UI/MyUIs/UISprites/LevelMessagePanel/1X");
            _x2 = Resources.Load<Sprite>("Prefabs/UI/MyUIs/UISprites/LevelMessagePanel/2X");
            _c = Resources.Load<Sprite>("Prefabs/UI/MyUIs/UISprites/LevelMessagePanel/continue_black");
            _p = Resources.Load<Sprite>("Prefabs/UI/MyUIs/UISprites/LevelMessagePanel/pause_black");
            _professionsSmall = Resources.LoadAll<Sprite>("Prefabs/UI/MyUIs/UISprites/CharacterHandSprites/atlas_profession_v2");
            _professionsLighten = Resources.LoadAll<Sprite>("Prefabs/UI/MyUIs/UISprites/CharacterHandSprites/atlas_profession_lighten");
            _spMessageAtlas = Resources.LoadAll<Sprite>("Prefabs/UI/MyUIs/UISprites/LevelMessagePanel/atlas_spState");
            _skillRangeButton = new Sprite[2] { Resources.Load<Sprite>("Prefabs/UI/MyUIs/UISprites/LevelMessagePanel/sprite_skill_range_off"), Resources.Load<Sprite>("Prefabs/UI/MyUIs/UISprites/LevelMessagePanel/sprite_skill_range_on") };
            _rangeImg = new List<SpriteRenderer>() { Object.Instantiate(Resources.Load<GameObject>("Prefabs/EffectPrefabs/rangeImg"), _rangeImgCollection.transform).GetComponent<SpriteRenderer>() };
            _rangeImgCollection.transform.SetParent(LevelResourceSharing.LM);
            _placeDataList = new List<StaticEntityPlaceData>();
            _selectorObjects = new List<GameObject>();
            _isAffordableBlockList = new List<(int i, int j)>();
        }

        private void InitTimeControl()
        {
            _timeMultiple = GetComponentInChildrenByPath<Button>("timeMultiple");
            _timeMultiple.onClick.AddListener(() =>
            {
                _is2X = !_is2X;
                if (_is2X)
                {
                    _timeMultiple.image.sprite = _x2;
                }
                else
                {
                    _timeMultiple.image.sprite = _x1;
                }
                SetTimeScale();
            });
            _pause = GetComponentInChildrenByPath<Button>("pause");
            _pause.onClick.AddListener(() =>
            {
                _isPause = !_isPause;
                if (_isPause)
                {
                    _pause.image.sprite = _p;
                    _pauseMask.SetActive(true);
                }
                else
                {
                    _pause.image.sprite = _c;
                    _pauseMask.SetActive(false);
                }
                SetTimeScale();
            });
            GetComponentInChildrenByPath<Button>("exit").onClick.AddListener(() =>
            {
                Time.timeScale = 0;
                NoticeManager.NM.LaunchMessageBox("ȷ���˳��ؿ���", () => LevelActionManager.Manager.MissionEnd(false), () => SetTimeScale());
            });
        }

        private void InitTopStatusBar()
        {
            _cost = GetComponentInChildrenByPath<TextMeshProUGUI>("staticEntityArea/resource/cost");
            _costSlider = GetComponentInChildrenByPath<Image>("staticEntityArea/resource/costSlider");
            _isAffordableNumText = GetComponentInChildrenByPath<TextMeshProUGUI>("staticEntityArea/numLeft/canSetNum");
            _currentNumAndTotalNum = GetComponentInChildrenByPath<TextMeshProUGUI>("count_total_healthleft/c_t");
            _levelHpLeft = GetComponentInChildrenByPath<TextMeshProUGUI>("count_total_healthleft/t_hp");
            _pauseMask = GetComponentInChildrenByPath<Transform>("pauseMask").gameObject;
        }

        private void InitLeftMessagePanel()
        {
            _leftMessage = GetComponentInChildrenByPath<Transform>("leftMessageArea").gameObject;
            _chooser = GetComponentInChildrenByPath<Image>("leftMessageArea/chooser");
            _up = GetComponentInChildrenByPath<Image>("leftMessageArea/target/up");
            _down = GetComponentInChildrenByPath<Image>("leftMessageArea/target/down");
            _left = GetComponentInChildrenByPath<Image>("leftMessageArea/target/left");
            _right = GetComponentInChildrenByPath<Image>("leftMessageArea/target/right");
            _class = GetComponentInChildrenByPath<Image>("leftMessageArea/class");
            _target = GetComponentInChildrenByPath<Image>("leftMessageArea/target");
            _rangeSelfTile = GetComponentInChildrenByPath<RectTransform>("leftMessageArea/attributes/atkRange/area/self");
            _rangeTiles = new List<RectTransform> { GetComponentInChildrenByPath<RectTransform>("leftMessageArea/attributes/atkRange/area/range") };
            _rangeArea = GetComponentInChildrenByPath<RectTransform>("leftMessageArea/attributes/atkRange/area");
            _name = GetComponentInChildrenByPath<TextMeshProUGUI>("leftMessageArea/name");
            _statsText = GetComponentInChildrenByPath<TextMeshProUGUI>("leftMessageArea/attributes/admb");
            _hpSlider = GetComponentInChildrenByPath<RectTransform>("leftMessageArea/attributes/hpSliderBk/hpSlider");
            _skillTalentRect = GetComponentInChildrenByPath<RectTransform>("leftMessageArea/skillTalent/content/content");
            _skillTalentRectParent = GetComponentInChildrenByPath<RectTransform>("leftMessageArea/skillTalent/content");
            _skillCard = new SkillCard(new Vector2(0, 0), _skillTalentRect, Color.white, _skillTalentRect.rect.width);
            _subpCard = new SubpCard(new Vector2(0, 0), _skillTalentRect, Color.white, _skillTalentRect.rect.width);
            _subpCard.SubpRT.gameObject.SetActive(false);
            _talentCards = new List<TalentCard>();
            _buffCards = new List<BuffCard>();
            Transform selectBar = GetComponentInChildrenByPath<Transform>("leftMessageArea/skillTalent/selectBar");
            _skillTalentSwitchButtons = new Image[4];
            for (int i = 0; i < _skillTalentSwitchButtons.Length; i++)
            {
                _skillTalentSwitchButtons[i] = selectBar.GetChild(i).GetComponent<Image>();
                EventTrigger.Entry click = new EventTrigger.Entry();
                click.eventID = EventTriggerType.PointerClick;
                int index = i;
                click.callback.AddListener((data) =>
                {
                    if (_currentShow != index)
                    {
                        EntityData entityData;
                        if (_selectedPlaceData != null && _selectedEntity == null)
                        {
                            entityData = _selectedPlaceData.EntityData.Prefab.GetComponent<Entity>().EntityData;
                        }
                        else if (_selectedPlaceData == null && _selectedEntity != null)
                        {
                            entityData = _selectedEntity.EntityData;
                        }
                        else
                        {
                            return;
                        }
                        SwitchShowSkillTalent(index, entityData);
                    }
                });
                _skillTalentSwitchButtons[i].GetComponent<EventTrigger>().triggers.Add(click);
            }
            _hpBk = GetComponentInChildrenByPath<RectTransform>("leftMessageArea/attributes/hpBk");
            _hpText = GetComponentInChildrenByPath<TextMeshProUGUI>("leftMessageArea/attributes/hpBk/hpText");
            _hpSliderSize = (_hpSlider.rect.width, _hpSlider.rect.height);
        }

        private void InitOperatorPanel()
        {
            _operateArea = GetComponentInChildrenByPath<Transform>("operateArea").gameObject;
            _callBack = GetComponentInChildrenByPath<Image>("operateArea/callback");
            _skillOpen = GetComponentInChildrenByPath<Image>("operateArea/skillOpen");
            _skillRange = GetComponentInChildrenByPath<Image>("operateArea/skillRange");
            _spBk = GetComponentInChildrenByPath<Image>("operateArea/skillOpen/sp/spBk");
            _spState = GetComponentInChildrenByPath<Image>("operateArea/skillOpen/sp/spBk/spState");
            _spText = GetComponentInChildrenByPath<TextMeshProUGUI>("operateArea/skillOpen/sp/spText");
            _spMask = GetComponentInChildrenByPath<Image>("operateArea/skillOpen/spmask");
            _stop = GetComponentInChildrenByPath<Image>("operateArea/skillOpen/stop");
            _skillChargeNum = GetComponentInChildrenByPath<Image>("operateArea/skillOpen/skillChargeNum");
            _skillChargeNumText = GetComponentInChildrenByPath<TextMeshProUGUI>("operateArea/skillOpen/skillChargeNum/skillChargeText");
            _callBackClick = new EventTrigger.Entry();
            _callBackClick.eventID = EventTriggerType.PointerClick;
            _callBack.GetComponent<EventTrigger>().triggers.Add(_callBackClick);
            EventTrigger.Entry skillOpenClick = new EventTrigger.Entry();
            skillOpenClick.eventID = EventTriggerType.PointerClick;
            skillOpenClick.callback.AddListener((data) =>
            {
                if (_selectSkill.SkillCanBegin())
                {
                    _selectSkill.SkillBegin();
                    UIStates_SwitchTo_Normal();
                }
            });
            _skillOpen.GetComponent<EventTrigger>().triggers.Add(skillOpenClick);
            _skillRangeClick = new EventTrigger.Entry();
            _skillRangeClick.eventID = EventTriggerType.PointerClick;
            _skillRange.GetComponent<EventTrigger>().triggers.Add(_skillRangeClick);
            EventTrigger.Entry skillstop = new EventTrigger.Entry();
            skillstop.eventID = EventTriggerType.PointerClick;
            skillstop.callback.AddListener((data) =>
            {
                _selectSkill.SkillEnd();
                UIStates_SwitchTo_Normal();
                AudioManager.Manager.PlayAudio("skill_boostclose", 1, false, false);
            });
            _stop.GetComponent<EventTrigger>().triggers.Add(skillstop);
        }

        private void InitSelectorArea()
        {
            _selectorSample = GetComponentInChildrenByPath<Transform>("staticEntityArea/content/ses0").gameObject;
            _content = GetComponentInChildrenByPath<Transform>("staticEntityArea/content");
        }

        private void InitFloatingTextPool()
        {
            _text = GetComponentInChildrenByPath<Transform>("texts");
            string[] textsName = new string[6] { "textDamage", "textHeal", "textAddCost", "textReduceCost", "spAdd", "miss" };
            _textsInPool = new List<TextMeshProUGUI>[textsName.Length];
            for (int i = 0; i < textsName.Length; i++)
            {
                _textsInPool[i] = new List<TextMeshProUGUI>() { GetComponentInChildrenByPath<TextMeshProUGUI>($"texts/{textsName[i]}") };
                for (int j = 0; j < 4; j++)
                {
                    _textsInPool[i].Add(Object.Instantiate(_textsInPool[i][0], _text));
                }
            }
        }

        private void InitEventTriggers()
        {
            _levelMessageTrigger = UIObject.GetComponent<EventTrigger>();
            EventTrigger.Entry blankClick = new EventTrigger.Entry();
            blankClick.eventID = EventTriggerType.PointerClick;
            blankClick.callback.AddListener((data) =>
            {
                if (_currentUIState == UIState.normal)
                {
                    Vector2 clickBlock = _camera.ScreenToWorldPoint(Input.mousePosition);
                    (int i, int j) = ((int)(clickBlock.y + 0.5), (int)(clickBlock.x + 0.5));
                    _selectedEntity = EntityManager.Manager.GetStaticEntityInBlock(i, j);
                    if (_selectedEntity != null && _selectedEntity.participateIn)
                    {
                        UIStates_SwitchTo_ViewAfterSet(_selectedEntity);
                    }
                }
                else
                {
                    UIStates_SwitchTo_Normal();
                }
            });
            _levelMessageTrigger.triggers.Add(blankClick);

            EventTrigger chooserTrigger = _chooser.GetComponent<EventTrigger>();
            EventTrigger.Entry chooserEnter = new EventTrigger.Entry();
            chooserEnter.eventID = EventTriggerType.PointerEnter;
            chooserEnter.callback.AddListener((data) =>
            {
                _inChooser = true;
            });
            chooserTrigger.triggers.Add(chooserEnter);
            EventTrigger.Entry chooserExit = new EventTrigger.Entry();
            chooserExit.eventID = EventTriggerType.PointerExit;
            chooserExit.callback.AddListener((data) =>
            {
                _inChooser = false;
            });
            chooserTrigger.triggers.Add(chooserExit);
            EventTrigger.Entry dir = new EventTrigger.Entry();
            dir.eventID = EventTriggerType.Drag;
            dir.callback.AddListener((data) =>
            {
                UIStates_Update_Chooser();
            });
            EventTrigger.Entry dend = new EventTrigger.Entry();
            dend.eventID = EventTriggerType.EndDrag;
            dend.callback.AddListener((data) =>
            {
                ReSelectOrSetStaticEntity();
            });
            chooserTrigger.triggers.Add(dir);
            chooserTrigger.triggers.Add(dend);
        }
        #endregion

        #region Public API
        private static LevelMessagePanel _instance;
        public static LevelMessagePanel Panel
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new LevelMessagePanel();
                }
                return _instance;
            }
        }

        public DamageStatisticData[] DamageStatisticDatas { get { return _damageStatisticDatas; } }

        public void InitializeStaticEntityPrefabToSelector(EntityID[] idList, int[] nums)
        {
            EntityPoolManager.Manager.CreateOrExpandEntityPool(idList, nums);
            int countL = _selectorObjects.Count;
            if (countL <= idList.Length)
            {
                for (int i = 0; i < countL; i++)
                {
                    StaticEntityPlaceData placeData = new StaticEntityPlaceData(_selectorObjects[i]);
                    placeData.InitializeSelectorData(idList[i], nums[i]);
                    _placeDataList.Add(placeData);
                }
                for (int i = countL; i < idList.Length; i++)
                {
                    _selectorObjects.Add(Object.Instantiate(_selectorSample, _content));
                    StaticEntityPlaceData placeData = new StaticEntityPlaceData(_selectorObjects[i]);
                    placeData.InitializeSelectorData(idList[i], nums[i]);
                    _placeDataList.Add(placeData);
                }
            }
            else
            {
                for (int i = 0; i < idList.Length; i++)
                {
                    StaticEntityPlaceData placeData = new StaticEntityPlaceData(_selectorObjects[i]);
                    placeData.InitializeSelectorData(idList[i], nums[i]);
                    _placeDataList.Add(placeData);
                }
                for (int i = idList.Length; i < countL; i++)
                {
                    _selectorObjects[i].SetActive(false);
                }
            }
        }
        public void AddStaticEntityPrefabTo_selectorRoot(EntityID[] idList, int[] nums)
        {
            for (int i = 0; i < idList.Length; i++)
            {
                for (int j = 0; j < _placeDataList.Count; j++)
                {
                    if (_placeDataList[j].EntityId == idList[i])
                    {
                        _placeDataList[j].DeltaNum(nums[i]);
                        return;
                    }
                }
                if (_placeDataList.Count < _selectorObjects.Count)
                {
                    StaticEntityPlaceData placeData = new StaticEntityPlaceData(_selectorObjects[_placeDataList.Count]);
                    placeData.InitializeSelectorData(idList[i], nums[i]);
                    _placeDataList.Add(placeData);
                }
                else
                {
                    _selectorObjects.Add(Object.Instantiate(_selectorSample, _content));
                    StaticEntityPlaceData placeData = new StaticEntityPlaceData(_selectorObjects[_placeDataList.Count]);
                    placeData.InitializeSelectorData(idList[i], nums[i]);
                    _placeDataList.Add(placeData);
                }
            }
        }
        /// <summary>
        /// ��ʾ��ֵ
        /// </summary>
        /// <param name="pos">��ʾλ��</param>
        /// <param name="textType">��ֵ����:0-Damage, 1-Heal, 2-AddCost, 3-ReduceCost, 4-spAdd, 5-miss</param>
        /// <param name="value">ֵ</param>
        public void ShowText(Vector2 entityPos, int textType, int value)
        {
            TextMeshProUGUI textMeshProUGUI;
            if (_textsInPool[textType].Count > 1)
            {
                textMeshProUGUI = _textsInPool[textType][1];
                _textsInPool[textType].RemoveAt(1);
            }
            else
            {
                textMeshProUGUI = Object.Instantiate(_textsInPool[textType][0], _text);
            }
            switch (textType)
            {
                case 0: textMeshProUGUI.text = value.ToString(); break;
                case 1: textMeshProUGUI.text = "+" + value.ToString(); break;
                case 2: textMeshProUGUI.text = "COST " + value.ToString(); break;
                case 3: textMeshProUGUI.text = "COST- " + value.ToString(); break;
                case 4: textMeshProUGUI.text = "SP " + value.ToString(); break;
                case 5: break;
                default: break;
            }
            textMeshProUGUI.gameObject.transform.position = entityPos + 1.2f * Vector2.up + 0.1f * Random.insideUnitCircle;
            textMeshProUGUI.gameObject.SetActive(true);
            textMeshProUGUI.gameObject.transform.DOScale(1, 0.2f).SetUpdate(true).OnComplete(async () =>
            {
                await UniTask.WaitForSeconds(0.3f, true, PlayerLoopTiming.Update, LevelResourceSharing.LevelCtk);
                textMeshProUGUI.gameObject.transform.DOScale(0, 0.2f).SetUpdate(true).OnComplete(() =>
                {
                    textMeshProUGUI.gameObject.SetActive(false);
                    _textsInPool[textType].Add(textMeshProUGUI);
                });
            });
        }
        public void EntityBackTo_selectorRoot(Entity entityToBack)
        {
            if (entityToBack.EntityData.CanRespawn)
            {
                for (int i = 0; i < _placeDataList.Count; i++)
                {
                    if (_placeDataList[i].EntityData.ChineseName == entityToBack.NAME)
                    {
                        _placeDataList[i].CallBackNum(1);
                        if (entityToBack.EntityData.RespawnStrategy == 1)
                        {
                            _placeDataList[i].RespawnTiming();
                        }
                        return;
                    }
                }
                AddStaticEntityPrefabTo_selectorRoot(new EntityID[1] { entityToBack.EntityData.ID }, new int[1] { 1 });
            }
        }
        public void AcceptDamageMessage(Entity target, Entity origin, float finalDamage, int damageType)
        {
            string targetName = "";
            string originName = "";
            if (target != null)
            {
                targetName = target.EntityData.ChineseName;
            }
            if (origin != null)
            {
                originName = origin.EntityData.ChineseName;
            }
            for (int i = 0; i < _damageStatisticDatas.Length; i++)
            {
                if (targetName == _characterChineseName[i] && damageType <= 2)
                {
                    _damageStatisticDatas[i].DamageReceive[damageType] += finalDamage;
                }
                if (originName == _characterChineseName[i])
                {
                    if (damageType <= 2)
                    {
                        _damageStatisticDatas[i].Damage[damageType] += finalDamage;
                    }
                    else
                    {
                        _damageStatisticDatas[i].Healing[0] += finalDamage;
                    }
                }
            }
        }
        public void CostTextUpDate()
        {
            (int currentCost, int maxCost, float costTimer) cm = LevelRescurceManager.Manager.CostMessage;
            _currentCost = cm.currentCost;
            _cost.text = _currentCost.ToString();
            for (int i = 0; i < _placeDataList.Count; i++)
            {
                _placeDataList[i].CanSetStateUpDate();
            }
        }
        public void CanSetNumUpDate()
        {
            _isAffordableNum = LevelRescurceManager.Manager.CanSetNumLeft;
            _isAffordableNumText.text = _isAffordableNum.ToString();
            for (int i = 0; i < _placeDataList.Count; i++)
            {
                _placeDataList[i].CanSetStateUpDate();
            }
        }
        public void LevelHpLeftTextUpdate()
        {
            _levelHpLeft.text = LevelRescurceManager.Manager.LevelHpLeft.ToString();

        }
        public void CurrentNumAndTotalNumUpdate()
        {
            _currentNumAndTotalNum.text = LevelRescurceManager.Manager.CurrentOperateCount.ToString() + '/' + LevelRescurceManager.Manager.NeedOperateCount.ToString();
        }
        public void ReSelectOrSetStaticEntity()
        {
            if (_orientation != -1)
            {
                int cost = EntityManager.Manager.SetStaticEntity(_selectedPlaceData.EntityId, _chooser.transform.position, 1, _orientation).GetComponent<InteractableStatic>().CurrentSetCost = _selectedPlaceData.CalculateCost();
                LevelRescurceManager.Manager.ChangeCost(-cost);
                _selectedPlaceData.SelectorMove(false);
                _selectedPlaceData.SetNum(1);
                _chooser.color = new Color(1, 1, 0, 0.4f);
                _selectedPlaceData = null;
                _orientation = -1;
                UIStates_SwitchTo_Normal();
            }
            else
            {
                _target.transform.DOMove(_chooser.transform.position, 0.2f).SetUpdate(true);
            }

        }
        public override void OnEnter()
        {
            base.OnEnter();
            var team1Members = SaveSystem.GetTeamMembers("Team1");
            EntityID[] characters = new EntityID[team1Members.Count];
            for (int i = 0; i < characters.Length; i++) characters[i] = team1Members[i];
            _characterChineseName = new string[characters.Length];
            _damageStatisticDatas = new DamageStatisticData[characters.Length];
            for (int i = 0; i < _damageStatisticDatas.Length; i++)
            {
                _damageStatisticDatas[i] = new DamageStatisticData(characters[i], new float[3] { 0, 0, 0 }, new float[1] { 0 }, new float[3] { 0, 0, 0 });
                _characterChineseName[i] = CharacterCardManager.cardManager.GetCharacterAttribute(characters[i]).ChineseName;
            }
            GameObject[] prefab = new GameObject[characters.Length];
            int[] num = new int[characters.Length];
            for (int i = 0; i < characters.Length; i++)
            {
                prefab[i] = CharacterCardManager.cardManager.GetCharacterAttribute(characters[i]).Prefab;
                num[i] = 1;
            }
            //=====================================================================================================================================
            InitializeStaticEntityPrefabToSelector(prefab, num);
            //=====================================================================================================================================
            _currentUIState = UIState.normal;
            _leftmessageOpen = false;
            _operaterOpen = false;
            _draggerOpen = false;
            _rangeOpen = false;
            _cansetOpen = false;
            _pause.image.sprite = _c;
            _pauseMask.SetActive(false);
            _timeMultiple.image.sprite = _x1;
            _blockDatas = MapDataManager.Manager.BlockDataMatrix;
            (_iSize, _jSize) = MapDataManager.Manager.MapSize;
            _rangeImgCollection.SetActive(false);
            CostSliderAndCanSetNumUpdate();
            _cameraOriginalPos = _camera.transform.position;
            _deltaX = _cameraOriginalPos.x - _operateArea.transform.position.x;
        }
        public override void OnExit()
        {
            base.OnExit();
            UIStates_SwitchTo_Normal();
            _isAffordableBlockList.Clear();
            //=====================================================================================================================================
            _selectedStaticEntityID = null;
            _selectedEntity = null;
            //=====================================================================================================================================
            _selectedPlaceData = null;
            _orientation = -1;
        }
        public override void OnPause()
        {
            base.OnPause();
            _isPause = false;
            _is2X = false;
            _isSlow = false;
            SetTimeScale();
            _placeDataList.Clear();
        }
        #endregion

        #region Time Control
        #endregion

        #region UI States
        #endregion

        #region Helpers
        #endregion
        //operator
        private EventTrigger.Entry _callBackClick, _skillRangeClick;

        private void SetTimeScale()
        {
            if (_isPause)
            {
                Time.timeScale = 0;
            }
            else if (_isSlow)
            {
                Time.timeScale = 0.1f;
            }
            else if (_is2X)
            {
                Time.timeScale = 2;
            }
            else
            {
                Time.timeScale = 1;
            }
        }
        
        private async void FixedUpdate()
        {
            while (true)
            {
                UIStateMachine();
                if (_currentUIState == UIState.normal)
                    return;
                await UniTask.WaitForFixedUpdate(LevelResourceSharing.LevelCtk);
            }
        }
        /// <summary>
        /// �жϵ��λ���Ƿ���Է���
        /// </summary>
        /// <param name="pos">���λ��</param>
        /// <returns>�Ƿ�ɷ���</returns>
        private void HideTargetOrEnterNextStage(StaticEntityPlaceData staticEntityPlaceData)
        {
            if (_currentUIState == UIState.setting)
            {
                if (_target.color == Color.yellow)
                {
                    UIStates_SwitchTo_Chooseing(staticEntityPlaceData);
                }
                else if (_target.color == Color.green)
                {
                    ReSelectOrSetStaticEntity();
                    UIStates_SwitchTo_Normal();
                }
                else
                {
                    UIStates_SwitchTo_ViewBeforeSet(staticEntityPlaceData);
                }
            }
        }
        private void FetchMapEntityData()
        {
            switch (_isAffordableType)
            {
                case 0:
                    _lowerCanSetBlock = MapDataManager.Manager.LowerCanSetBlock;
                    _staticEntityExistBlock = EntityManager.Manager.StaticEntityExistBlock;
                    break;
                case 1:
                    _higherCanSetBlock = MapDataManager.Manager.HigherCanSetBlock;
                    _staticEntityExistBlock = EntityManager.Manager.StaticEntityExistBlock;
                    break;
                case 2:
                    _lowerCanSetBlock = MapDataManager.Manager.LowerCanSetBlock;
                    _higherCanSetBlock = MapDataManager.Manager.HigherCanSetBlock;
                    _staticEntityExistBlock = EntityManager.Manager.StaticEntityExistBlock;
                    break;
                case 3:
                    _lowerCanSetBlock = MapDataManager.Manager.LowerCanSetBlock;
                    _higherCanSetBlock = MapDataManager.Manager.HigherCanSetBlock;
                    _staticEntityExistBlock = EntityManager.Manager.StaticEntityExistBlock;
                    break;
                default: break;
            }
        }
        private void MoveCamera(Vector2 targetPos, float duration)
        {
            Vector2 currentPos = _camera.transform.position;
            DOTween.To((value) =>
            {
                Vector2 tmp = (1 - value) * currentPos + value * targetPos;
                _camera.transform.position = new Vector3(tmp.x, tmp.y, _cameraOriginalPos.z);
                SlidersManager.Manager.TakeOverSliderMove();
            }, 0, 1, duration).SetUpdate(true);
        }
        private void SwitchShowSkillTalent(int show, EntityData entityData)
        {
            if (_currentShow != show)
            {
                _currentShow = show;
                _skillCard.SkillRT.gameObject.SetActive(false);
                _subpCard.SubpRT.gameObject.SetActive(false);
                for (int i = 0; i < _talentCards.Count; i++)
                {
                    _talentCards[i].TalentRT.gameObject.SetActive(false);
                }
                for (int i = 0; i < _buffCards.Count; i++)
                {
                    _buffCards[i].BuffRT.gameObject.SetActive(false);
                }
                for (int i = 0; i < 4; i++)
                {
                    if (i != show)
                    {
                        _skillTalentSwitchButtons[i].color = _colorUnSelect;
                    }
                    else
                    {
                        _skillTalentSwitchButtons[i].color = _colorSelect;
                    }
                }
            }
        
            // switch (_currentShow)
            // {
            //     case 0:
            //         if (entityData.skill != null && entity.skill.Length > 0)
            //         {
            //             _skillCard.SkillRT.gameObject.SetActive(true);
            //             _skillCard.UpdateSkillCardMessage(entity.skill[0]);
            //         }
            //         else
            //         {
            //             _skillCard.SkillRT.gameObject.SetActive(false);
            //         }
            //         break;
            //     case 1:
            //         _subpCard.SubpRT.gameObject.SetActive(true);
            //         _subpCard.UpdateSubpCardMessage(entityData);
            //         break;
            //     case 2:
            //         Talent[] ts = entity.Talents;
            //         if (ts.Length <= _talentCards.Count)
            //         {
            //             for (int i = 0; i < ts.Length; i++)
            //             {
            //                 _talentCards[i].TalentRT.gameObject.SetActive(true);
            //                 _talentCards[i].UpdateTalentCardMessage(ts[i]);
            //             }
            //             for (int i = ts.Length; i < _talentCards.Count; i++)
            //             {
            //                 _talentCards[i].TalentRT.gameObject.SetActive(false);
            //             }
            //         }
            //         else
            //         {
            //             for (int i = 0; i < _talentCards.Count; i++)
            //             {
            //                 _talentCards[i].TalentRT.gameObject.SetActive(true);
            //                 _talentCards[i].UpdateTalentCardMessage(ts[i]);
            //             }
            //             for (int i = _talentCards.Count; i < ts.Length; i++)
            //             {
            //                 TalentCard ti = new TalentCard(new Vector2(0, 0), _skillTalentRect, Color.white, _skillTalentRect.rect.width);
            //                 ti.UpdateTalentCardMessage(ts[i]);
            //                 _talentCards.Add(ti);
            //             }
            //         }
            //         break;
            //     case 3:
            //         List<Buff> bs = entity.buffController.Buffs;
            //         if (bs.Count <= _buffCards.Count)
            //         {
            //             for (int i = 0; i < bs.Count; i++)
            //             {
            //                 _buffCards[i].BuffRT.gameObject.SetActive(true);
            //                 _buffCards[i].UpdateBuffCardMessage(bs[i]);
            //             }
            //             for (int i = bs.Count; i < _buffCards.Count; i++)
            //             {
            //                 _buffCards[i].BuffRT.gameObject.SetActive(false);
            //             }
            //         }
            //         else
            //         {
            //             for (int i = 0; i < _buffCards.Count; i++)
            //             {
            //                 _buffCards[i].BuffRT.gameObject.SetActive(true);
            //                 _buffCards[i].UpdateBuffCardMessage(bs[i]);
            //             }
            //             for (int i = _buffCards.Count; i < bs.Count; i++)
            //             {
            //                 BuffCard bi = new BuffCard(new Vector2(0, 0), _skillTalentRect, Color.white, _skillTalentRect.rect.width);
            //                 bi.UpdateBuffCardMessage(bs[i]);
            //                 _buffCards.Add(bi);
            //             }
            //         }
            //         break;
            // }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_skillTalentRect);
            if (_skillTalentRect.rect.height > _skillTalentRectParent.rect.height)
            {
                _skillTalentRectParent.GetComponent<ScrollRect>().vertical = true;
            }
            else
            {
                _skillTalentRectParent.GetComponent<ScrollRect>().vertical = false;
            }
        }
        private async void CostSliderAndCanSetNumUpdate()
        {
            while (true)
            {
                (int currentCost, int maxCost, float costTimer) cm = LevelRescurceManager.Manager.CostMessage;
                _isAffordableNum = LevelRescurceManager.Manager.CanSetNumLeft;
                _currentCost = cm.currentCost;
                _cost.text = _currentCost.ToString();
                _costSlider.fillAmount = cm.costTimer;
                _isAffordableNumText.text = _isAffordableNum.ToString();
                for (int i = 0; i < _placeDataList.Count; i++)
                {
                    _placeDataList[i].CanSetStateUpDate();
                }
                await UniTask.WaitForFixedUpdate(LevelResourceSharing.LevelCtk);
            }
        }
        private void ShowAttackRangeAttributes((int x, int y)[] range0)
        {
            int maxX, minX, maxY, minY;
            maxX = -1000;
            maxY = -1000;
            minX = 1000;
            minY = 1000;
            (int x, int y)[] range = new (int x, int y)[range0.Length];
            for (int i = 0; i < range.Length; i++)
            {
                range[i] = (range0[i].y, -range0[i].x);
                if (maxX < range[i].x)
                    maxX = range[i].x;
                if (maxY < range[i].y)
                    maxY = range[i].y;
                if (minX > range[i].x)
                    minX = range[i].x;
                if (minY > range[i].y)
                    minY = range[i].y;
            }
            int dx = maxX - minX;
            int dy = maxY - minY;
            float gap = 0.05f;
            float dxl = _rangeArea.rect.width / (dx + 1);
            float dyl = _rangeArea.rect.height / (dy + 1);
            float l;
            if (dxl < dyl)
                l = dxl > 15 ? 15 : dxl;
            else
                l = dyl > 15 ? 15 : dyl;
            float centerX = (float)(minX + maxX) / 2;
            float centerY = (float)(minY + maxY) / 2;
            if (_rangeTiles.Count < range.Length)
            {
                int dc = range.Length - _rangeTiles.Count;
                for (int i = 0; i < dc; i++)
                {
                    _rangeTiles.Add(Object.Instantiate(_rangeTiles[0].gameObject, _rangeArea).GetComponent<RectTransform>());
                }
            }
            else if (_rangeTiles.Count > range.Length)
            {
                for (int i = range.Length; i < _rangeTiles.Count; i++)
                {
                    _rangeTiles[i].gameObject.SetActive(false);
                }
            }
            for (int i = 0; i < range.Length; i++)
            {
                _rangeTiles[i].sizeDelta = new Vector2(l * (1 - gap), l * (1 - gap));
                _rangeTiles[i].anchoredPosition = new Vector2(l * (range[i].x - centerX), l * (range[i].y - centerY));
                _rangeTiles[i].gameObject.SetActive(true);
            }
            _rangeSelfTile.sizeDelta = new Vector2(l * (1 - gap), l * (1 - gap));
            _rangeSelfTile.anchoredPosition = new Vector2(-l * centerX, -l * centerY);
        
        
        
        
        }
        private void UIStates_ShowClose_Leftmessage(bool show)
        {
            //=====================================================================================================================================
            if (show)
            {
                if (!_leftmessageOpen)
                {
                    _leftmessageOpen = true;
                    _leftMessage.SetActive(true);
                }
                // 选中态由 _selectedStaticEntityID 统一表达（placeData 与已部署 entity 两种场景都会设置它）
                if (!_selectedStaticEntityID.HasValue)
                {
                    return;
                }
                EntityID entityID = _selectedStaticEntityID.Value;

                EntityData entityData = GameDataService.EntityRepository.Get(entityID);

                SwitchShowSkillTalent(_currentShow, entityData);
                ShowAttackRangeAttributes(entityData.VisionRange);
                _name.text = entityData.ChineseName;
                _class.sprite = _professionsLighten[entityData.CharacterJob];
                UIStates_Update_Leftmessage();
            }
            else
            {
                if (_leftmessageOpen)
                {
                    _leftmessageOpen = false;
                    _leftMessage.SetActive(false);
                }
            }
            //=====================================================================================================================================
        }
        private void UIStates_Update_Leftmessage()
        {
            //=====================================================================================================================================
            Entity entity;
            bool isBefore;
            if (_selectedPlaceData != null && _selectedEntity == null)
            {
                entity = _selectedPlaceData.StaticEntity;
                isBefore = true;
            }
            else if (_selectedPlaceData == null && _selectedEntity != null)
            {
                entity = _selectedEntity;
                isBefore = false;
            }
            else
            {
                return;
            }
            float atk, def, mgr, currentHp, maxHp;
            int blo;
            if (isBefore)
            {
                if (entity.AttackBase != null)
                {
                    atk = entity.AttackBase.AttackDamageF;
                }
                else
                {
                    atk = 0;
                }
                def = entity.DEF_1;
                mgr = entity.MagicResistance_1;
                blo = entity.BlockOccupation_1;
                currentHp = entity.MaxHp_1;
                maxHp = entity.MaxHp_1;
            }
            else
            {
                if (entity.AttackBase != null)
                {
                    atk = entity.AttackBase.AttackDamageS;
                }
                else
                {
                    atk = 0;
                }
                def = entity.DEF_2;
                mgr = entity.MagicResistance_2;
                blo = entity.BlockOccupation;
                currentHp = entity.CurrentHp;
                maxHp = entity.MaxHpS;
            }
            _statsText.text = $"����  {(int)atk}\n����  {(int)def}\n����  {(int)mgr}\n�赲  {blo}";
            float leftLength = _hpSliderSize.width * currentHp / maxHp;
            _hpSlider.sizeDelta = new Vector2(leftLength - _hpSliderSize.width, _hpSliderSize.height);
            _hpText.text = $"{(int)currentHp}/{(int)maxHp}";
            _hpBk.anchoredPosition = new Vector2(leftLength > 81 ? leftLength : 81, 0);
            //=====================================================================================================================================

        }
        private void UIStates_ShowClose_Operator(bool show)
        {
            //=====================================================================================================================================
            if (show)
            {
                if (!_operaterOpen)
                {
                    _operaterOpen = true;
                    _operateArea.SetActive(true);
                }
                MoveCamera(_selectedEntity.EntityPosition + _deltaX * Vector2.right, 0.1f);
                if (sea.CanCallBack)
                {
                    _callBack.enabled = true;
                    _callBackClick.callback.RemoveAllListeners();
                    _callBackClick.callback.AddListener((data) =>
                    {
                        _selectedEntity.thisEntityPool.Return(_selectedEntity);
                        LevelRescurceManager.Manager.ChangeCost((int)(_selectedEntity.GetComponent<InteractableStatic>().CurrentSetCost * 0.5));
                        _selectedEntity.participateIn = false;
                        AudioManager.Manager.PlayAudio("escape", 1, false, false);
                        UIStates_SwitchTo_Normal();
                    });
                }
                else
                {
                    _callBack.enabled = false;
                }
                if (_selectedEntity.skill != null && _selectedEntity.skill.Length > 0)
                {
                    _skillOpen.gameObject.SetActive(true);
                    _selectSkill = _selectedEntity.skill[0];
                    _skillOpen.sprite = _selectSkill.SkillImg;
                    if (_selectSkill.SkillAttackRange != null)
                    {
                        _skillRange.gameObject.SetActive(true);
                        _skillRangeClick.callback.RemoveAllListeners();
                        _skillRangeClick.callback.AddListener((data) =>
                        {

                        });
                    }
                    else
                    {
                        _skillRange.gameObject.SetActive(false);
                    }
                }
                else
                {
                    _skillOpen.gameObject.SetActive(false);
                    _selectSkill = null;
                }
                UIStates_Update_Operator();
            }
            else
            {
                if (_operaterOpen)
                {
                    _operaterOpen = false;
                    _operateArea.SetActive(false);
                    MoveCamera(_cameraOriginalPos, 0.1f);
                }
            }
            //=====================================================================================================================================
        }
        private void UIStates_Update_Operator()
        {
            //=====================================================================================================================================
            if (_selectedEntity == null || !_selectedEntity.participateIn)
                UIStates_SwitchTo_Normal();
            //=====================================================================================================================================
            if (_selectSkill)
            {
                if (!_selectSkill.SkillCanBegin())
                {
                    _skillOpen.raycastTarget = false;
                    _skillOpen.color = Color.gray;
                }
                else
                {
                    if (_selectSkill.SkillOpenMode == 3)
                    {
                        _skillOpen.raycastTarget = true;
                    }
                    else
                    {
                        _skillOpen.raycastTarget = false;
                    }
                    _skillOpen.color = Color.white;
                }
        
                (float currentSpRate, int currentChargeNum, bool isSkill) = _selectSkill.SkillMessage;
                _spMask.fillAmount = currentSpRate;
                if (currentChargeNum < 1)
                {
                    _skillChargeNum.gameObject.SetActive(false);
                }
                else
                {
                    _skillChargeNum.gameObject.SetActive(true);
                    _skillChargeNumText.text = currentChargeNum.ToString();
                }
                if (!isSkill)
                {
                    float tsp;
                    tsp = _selectSkill.TotalSp;
                    _spState.sprite = _spMessageAtlas[0];
                    _stop.enabled = false;
                    if (!_selectSkill.SPFull())
                    {
                        _spMask.enabled = true;
                        _spMask.color = _lightGreen_half;
                        _spText.text = $"{(int)(tsp * currentSpRate)}/{(int)tsp}";
                        if (currentChargeNum == 0 && currentSpRate < 1)
                        {
                            _spBk.color = _gray;
                            _spState.color = _lightGreen;
                            _spText.color = Color.white;
                        }
                        else
                        {
                            _spBk.color = _lightGreen;
                            _spState.color = Color.white;
                            _spText.color = Color.black;
                        }
                    }
                    else
                    {
                        _spMask.enabled = false;
                        _spText.text = "READY";
                        _spBk.color = _lightGreen;
                        _spState.color = Color.white;
                        _spText.color = Color.black;
                    }
                }
                else
                {
                    int consumeType = _selectSkill.SpComsumeMode;
                    _spBk.color = _orange;
                    _spState.color = Color.white;
                    if (consumeType < 3)
                    {
                        float tsa = _selectSkill.SkillAmount;
                        _spMask.enabled = true;
                        _spMask.color = _orange_half;
                        _spState.sprite = _spMessageAtlas[consumeType + 1];
                        _spText.color = Color.white;
                        if (consumeType == 0)
                        {
                            _spText.text = (tsa * currentSpRate).ToString("0.0") + 's';
                        }
                        else
                        {
                            _spText.text = (tsa * currentSpRate).ToString() + '/' + tsa.ToString();
                        }
                    }
                    else
                    {
                        _spMask.enabled = false;
                        _spState.sprite = _spMessageAtlas[4];
                        _spText.text = null;
                    }
                    if (_selectSkill.CanCloseSkill)
                    {
                        _stop.enabled = true;
                    }
                    else
                    {
                        _stop.enabled = false;
                    }
                }
            }
        }
        private void UIStates_ShowClose_Dragger(bool show)
        {
            if (show)
            {
                if (!_draggerOpen)
                {
                    _draggerOpen = true;
                    _target.gameObject.SetActive(true);
                }
            }
            else
            {
                if (_draggerOpen)
                {
                    _draggerOpen = false;
                    _target.gameObject.SetActive(false);
                    _up.enabled = false;
                    _right.enabled = false;
                    _left.enabled = false;
                    _down.enabled = false;
                }
            }
        }
        private void UIStates_Update_Dragger()
        {
            if (_currentUIState == UIState.setting)
            {
                Vector3 p = _camera.ScreenToWorldPoint(Input.mousePosition);
                (int i, int j) = ((int)(p.y + 0.5), (int)(p.x + 0.5));
                if (_isAffordableBlockList.Contains((i, j)))
                {
                    _target.transform.position = new Vector2(j, i);
                    if (true)
                    {
                        _target.color = Color.yellow;
                    }
                    else
                    {
                        _target.color = Color.green;
                        _orientation = 0;
                        UIStates_ShowClose_Chooser(true);
                        UIStates_ShowClose_Range(true);
                    }
                }
                else
                {
                    if (_target.color == Color.green)
                    {
                        UIStates_ShowClose_Chooser(false);
                        UIStates_ShowClose_Range(false);
                    }
                    _target.transform.position = new Vector2(p.x, p.y);
                    _target.color = Color.red;
                }
            }
        }
        private void UIStates_ShowClose_Chooser(bool show)
        {
            if (show)
            {
                if (!_chooserOpen)
                {
                    _chooserOpen = true;
                    _chooser.gameObject.SetActive(true);
                }
                _chooser.transform.position = _target.transform.position;
            }
            else
            {
                if (_chooserOpen)
                {
                    _chooserOpen = false;
                    _chooser.gameObject.SetActive(false);
                }
            }
        }
        private void UIStates_Update_Chooser()
        {
            Vector3 p = _camera.ScreenToWorldPoint(Input.mousePosition);
            _target.transform.position = new Vector2(p.x, p.y);
            if (!_inChooser)
            {
                Vector2 dp = p - _chooser.transform.position;
                if (dp.y > dp.x)
                {
                    if (dp.y > -dp.x && _orientation != 0)
                    {
                        _orientation = 0;
                        _up.enabled = true;
                        _down.enabled = false;
                        _left.enabled = false;
                        _right.enabled = false;
                        UIStates_ShowClose_Range(true);
                    }
                    else if (dp.y <= -dp.x && _orientation != 3)
                    {
                        _orientation = 3;
                        _up.enabled = false;
                        _down.enabled = false;
                        _left.enabled = true;
                        _right.enabled = false;
                        UIStates_ShowClose_Range(true);
                    }
                }
                else
                {
                    if (dp.y > -dp.x && _orientation != 1)
                    {
                        _orientation = 1;
                        _up.enabled = false;
                        _down.enabled = false;
                        _left.enabled = false;
                        _right.enabled = true;
                        UIStates_ShowClose_Range(true);
                    }
                    else if (dp.y <= -dp.x && _orientation != 2)
                    {
                        _orientation = 2;
                        _up.enabled = false;
                        _down.enabled = true;
                        _left.enabled = false;
                        _right.enabled = false;
                        UIStates_ShowClose_Range(true);
                    }
                }
                _chooser.color = new Color(0, 1, 0, 0.4f);
                _target.color = Color.green;
            }
            else
            {
                _orientation = -1;
                _up.enabled = false;
                _down.enabled = false;
                _left.enabled = false;
                _right.enabled = false;
                _chooser.color = new Color(1, 1, 0, 0.4f);
                _target.color = Color.yellow;
                UIStates_ShowClose_Range(false);
            }
        }
        private void UIStates_ShowClose_Range(bool show)
        {
            if (show)
            {
                if (!_rangeOpen)
                {
                    _rangeOpen = true;
                    _rangeImgCollection.SetActive(true);
                }
                UIStates_Update_Range();
            }
            else
            {
                if (_rangeOpen)
                {
                    _rangeOpen = false;
                    _rangeImgCollection.SetActive(false);
                }
            }
        }
        private void UIStates_Update_Range()
        {
            //=====================================================================================================================================
            Color color = new Color(255, 160, 0);
            (int x, int y)[] attackRange;
            if (_selectedPlaceData != null && _selectedEntity == null)
            {
                (int x, int y) pos = ((int)(_chooser.transform.position.x + 0.5), (int)(_chooser.transform.position.y + 0.5));
            }
            else if (_selectedPlaceData == null && _selectedEntity != null)
            {
                attackRange = _selectedEntity.VisionRange;
            }
            else
            {
                return;
            }
            int rangeImgCount = _rangeImg.Count;
            if (rangeImgCount >= attackRange.Length)
            {
                for (int i = 0; i < attackRange.Length; i++)
                {
                    _rangeImg[i].transform.position = new Vector2(attackRange[i].x, attackRange[i].y);
                    _rangeImg[i].color = color;
                    _rangeImg[i].enabled = true;
                }
                for (int i = attackRange.Length; i < rangeImgCount; i++)
                {
                    _rangeImg[i].enabled = false;
                }
            }
            else
            {
                for (int i = 0; i < rangeImgCount; i++)
                {
                    _rangeImg[i].transform.position = new Vector2(attackRange[i].x, attackRange[i].y);
                    _rangeImg[i].color = color;
                    _rangeImg[i].enabled = true;
                }
                for (int i = rangeImgCount; i < attackRange.Length; i++)
                {
                    _rangeImg.Add(Object.Instantiate(_rangeImg[0], new Vector2(attackRange[i].x, attackRange[i].y), Quaternion.identity, _rangeImgCollection.transform));
                }
            }
            //=====================================================================================================================================
        }
        private void UIStates_ShowClose_Canset(bool show)
        {
            if (show)
            {
                if (!_cansetOpen)
                {
                    _cansetOpen = true;
                }
                UIStates_Update_Canset();
            }
            else
            {
                if (_cansetOpen)
                {
                    _cansetOpen = false;
                    for (int i = 0; i < _isAffordableBlockList.Count; i++)
                    {
                        MapDataManager.Manager.BlockDataMatrix[_isAffordableBlockList[i].i, _isAffordableBlockList[i].j].Material.color = Color.white;
                    }
                }
            }
        }
        private void UIStates_Update_Canset()
        {
            //=====================================================================================================================================
            Color lightGreen = new Color(0, 0.4f, 0);
            FetchMapEntityData();
            for (int i = 0; i < _isAffordableBlockList.Count; i++)
            {
                MapDataManager.Manager.BlockDataMatrix[_isAffordableBlockList[i].i, _isAffordableBlockList[i].j].Material.color = Color.white;
            }
            _isAffordableBlockList.Clear();
            switch (_isAffordableType)
            {
                case 0:
                    for (int i = 0; i < _iSize; i++)
                    {
                        for (int j = 0; j < _jSize; j++)
                        {
                            if (_lowerCanSetBlock[i, j] && !_staticEntityExistBlock[i, j])
                            {
                                _isAffordableBlockList.Add((i, j));
                            }
                        }
                    }
                    break;
                case 1:
                    for (int i = 0; i < _iSize; i++)
                    {
                        for (int j = 0; j < _jSize; j++)
                        {
                            if (_higherCanSetBlock[i, j] && !_staticEntityExistBlock[i, j])
                            {
                                _isAffordableBlockList.Add((i, j));
                            }
                        }
                    }
                    break;
                case 2:
                    for (int i = 0; i < _iSize; i++)
                    {
                        for (int j = 0; j < _jSize; j++)
                        {
                            if ((_lowerCanSetBlock[i, j] || _higherCanSetBlock[i, j]) && !_staticEntityExistBlock[i, j])
                            {
                                _isAffordableBlockList.Add((i, j));
                            }
                        }
                    }
                    break;
                case 3:
                    for (int i = 0; i < _iSize; i++)
                    {
                        for (int j = 0; j < _jSize; j++)
                        {
                            if ((_lowerCanSetBlock[i, j] || _higherCanSetBlock[i, j]) && _staticEntityExistBlock[i, j])
                            {
                                _isAffordableBlockList.Add((i, j));
                            }
                        }
                    }
                    break;
                default: break;
            }
            //չʾ�ɷ��õķ�Χ
            for (int i = 0; i < _isAffordableBlockList.Count; i++)
            {
                MapDataManager.Manager.BlockDataMatrix[_isAffordableBlockList[i].i, _isAffordableBlockList[i].j].Material.color = lightGreen;
            }
            //=====================================================================================================================================
        }
        private void UIStates_ShowSomethingAndOtherClose(string[] shows)
        {
            //"leftmessage", "operator", "dragger","choosing", "range", "canset"
            bool showHave = shows != null;
            UIStates_ShowClose_Leftmessage(showHave && shows.Contains("leftmessage"));
            UIStates_ShowClose_Operator(showHave && shows.Contains("operator"));
            UIStates_ShowClose_Dragger(showHave && shows.Contains("dragger"));
            UIStates_ShowClose_Chooser(showHave && shows.Contains("choosing"));
            UIStates_ShowClose_Range(showHave && shows.Contains("range"));
            UIStates_ShowClose_Canset(showHave && shows.Contains("canset"));
        }
        
        private void UIStates_SwitchTo_Normal()
        {
            _isSlow = false;
            SetTimeScale();
            _currentUIState = UIState.normal;
            //=====================================================================================================================================
            _selectedStaticEntityID = null;
            _selectedEntity = null;
            //=====================================================================================================================================
            if (_selectedPlaceData != null)
            {
                _selectedPlaceData.SelectorMove(false);
                _selectedPlaceData = null;
            }
            UIStates_ShowSomethingAndOtherClose(null);

        }
        private void UIStates_SwitchTo_ViewBeforeSet(StaticEntityPlaceData staticEntityPlaceData)
        {
            _isSlow = true;
            SetTimeScale();
            _selectedPlaceData = staticEntityPlaceData;
            //=====================================================================================================================================
            _selectedStaticEntityID = staticEntityPlaceData.EntityId;
            _selectedEntity = null;
            //=====================================================================================================================================
            UIStates_ShowSomethingAndOtherClose(new string[2] { "leftmessage", "canset" });
            if (_currentUIState == UIState.normal)
            {
                _currentUIState = UIState.viewBeforeSet;
                FixedUpdate();
            }
            _currentUIState = UIState.viewBeforeSet;
        }
        private void UIStates_SwitchTo_Setting(StaticEntityPlaceData staticEntityPlaceData)
        {
            _isSlow = true;
            SetTimeScale();
            _selectedPlaceData = staticEntityPlaceData;
            //=====================================================================================================================================
            _selectedStaticEntityID = staticEntityPlaceData.EntityId;
            _selectedEntity = null;
            //=====================================================================================================================================
            UIStates_ShowSomethingAndOtherClose(new string[3] { "leftmessage", "dragger", "canset" });
            if (_currentUIState == UIState.normal)
            {
                _currentUIState = UIState.setting;
                FixedUpdate();
            }
            _currentUIState = UIState.setting;
        }
        private void UIStates_SwitchTo_Chooseing(StaticEntityPlaceData staticEntityPlaceData)
        {
            _isSlow = true;
            SetTimeScale();
            _selectedPlaceData = staticEntityPlaceData;
            //=====================================================================================================================================
            _selectedStaticEntityID = staticEntityPlaceData.EntityId;
            _selectedEntity = null;
            //=====================================================================================================================================
            UIStates_ShowSomethingAndOtherClose(new string[4] { "leftmessage", "dragger", "choosing", "canset" });
            if (_currentUIState == UIState.normal)
            {
                _currentUIState = UIState.choosing;
                FixedUpdate();
            }
            _currentUIState = UIState.choosing;
        }
        private void UIStates_SwitchTo_ViewAfterSet(Entity entitySelected)
        {
            _isSlow = true;
            SetTimeScale();
            //=====================================================================================================================================
            _selectedEntity = entitySelected;
            _selectedStaticEntityID = entitySelected.EntityData.ID;
            //=====================================================================================================================================
            _selectedPlaceData = null;
            UIStates_ShowSomethingAndOtherClose(new string[3] { "leftmessage", "operator", "range" });
            if (_currentUIState == UIState.normal)
            {
                _currentUIState = UIState.viewAfterSet;
                FixedUpdate();
            }
            _currentUIState = UIState.viewAfterSet;
        }
        private void UIStateMachine()
        {
            switch (_currentUIState)
            {
                case UIState.normal:
                    break;
                case UIState.viewBeforeSet:
                    UIStates_Update_Canset();
                    UIStates_Update_Leftmessage();
                    break;
                case UIState.setting:
                    UIStates_Update_Canset();
                    UIStates_Update_Leftmessage();
                    break;
                case UIState.choosing:
                    UIStates_Update_Canset();
                    UIStates_Update_Leftmessage();
                    break;
                case UIState.viewAfterSet:
                    UIStates_Update_Range();
                    UIStates_Update_Leftmessage();
                    UIStates_Update_Operator();
                    break;
            }
        }
    }
}

