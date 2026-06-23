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
            public EntityStats EntityStats;
            // 缓存池里样本的 EntityVision，镜像 EntityStats 字段：ViewBeforeSet/setting/choosing
            // 等"池预览"态没有 _selectedEntity 时走这里。池样本的 Range 字段为 null（未调
            // SetOrientation），预览侧用 BaseRange 喂给 RangeCaculator 当场算。
            public EntityVision EntityVision;

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
                // 从实体池取一个示例 Entity，缓存其 EntityStats。
                // EntityPool.GetEntity() 不摘出，仅返回 inp_entities[0] 的引用；
                // EntityStats 持有者是同一实例，后续 XxxS 计算始终读到最新值。
                EntityPool pool = EntityPoolManager.Manager.FetchEntityPool(staticId);
                if (pool != null)
                {
                    Entity sample = pool.GetEntity();
                    if (sample != null)
                    {
                        EntityStats = sample.Stats;
                        EntityVision = sample.Vision;
                    }
                }

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
        private AbilityCard _abilityCard;
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
        private AbilitySystem.AbilityConfig _selectAbilityConfig;
        private AbilitySystem.AbilityRuntime _selectAbilityRuntime;
        // 范围图元池的"当前显示模式":None=池子关;Normal=基础攻击范围;Skill=技能攻击范围。
        // 单一真理源:UIStates_ShowClose_Range / UIStates_ShowClose_SkillRange / _skillRangeClick
        // 都通过 SetRangeMode 切换,池子的开/关/写图元逻辑只走 SetRangeMode + RenderRangeToPool
        // 一条路径,避免多处 Show/Update 互踩残留。
        private enum RangeDisplayMode { None, Normal, Skill }
        private RangeDisplayMode _rangeDisplayMode = RangeDisplayMode.None;
        private Color _rangeDisplayColor;
        // 技能范围预览的数据源缓存:当前 _selectAbilityRuntime 上 AttackRangeOverride 组件的
        // ParamList(原始 range 字面量 / BB 引用,ViewAfterSet 期间不会改;玩家重复点击 _skillRange
        // 时无需重新扫 components)。null = 该技能没有 AttackRangeOverride 组件 / 不可预览。
        private AbilitySystem.ParamList _skillRangeComponentParams;
        private EventTrigger.Entry _callBackClick, _skillRangeClick;

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
        //   viewBeforeSet/setting/choosing → _selectedStaticEntityID + _selectedPlaceData（池里 Stats）
        //   viewAfterSet                   → _selectedStaticEntityID + _selectedEntity（已部署，实时 Stats）
        //   normal/无选中                  → 三个全 null
        private StaticEntityPlaceData _selectedPlaceData;
        private Entity _selectedEntity;
        private EntityID? _selectedStaticEntityID;

        // ===== Map/Block Data =====
        private bool[,] _higherCanSetBlock;
        private bool[,] _lowerCanSetBlock;
        private bool[,] _staticEntityExistBlock;
        private int _canSetType;
        private List<(int i, int j)> _canSetBlockList;
        private BlockState[,] _blockDatas;
        private int _iSize, _jSize;

        // ===== Camera =====
        private Camera _camera;
        private Vector3 _cameraOriginalPos;
        private Camera _uiCamera;
        private Vector3 _uiCameraOriginalPos;
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
            _canSetBlockList = new List<(int i, int j)>();
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
            _abilityCard = new AbilityCard(new Vector2(0, 0), _skillTalentRect, Color.white, _skillTalentRect.rect.width);
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
                    if (_currentShow != index && _selectedStaticEntityID.HasValue)
                    {
                        EntityData entityData = GameDataService.EntityRepository.Get(_selectedStaticEntityID.Value);
                        SwitchShowAbilityTalent(index, entityData, _selectedEntity);
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
                var sp = _selectAbilityRuntime != null ? _selectAbilityRuntime.spEngine : null;
                if (sp != null && sp.CanBegin())
                {
                    sp.StartAbility();
                    UIStates_SwitchTo_Normal();
                }
            });
            _skillOpen.GetComponent<EventTrigger>().triggers.Add(skillOpenClick);
            _skillRangeClick = new EventTrigger.Entry();
            _skillRangeClick.eventID = EventTriggerType.PointerClick;
            _skillRangeClick.callback.AddListener((data) =>
            {
                // toggle 技能期间攻击范围预览。前提：viewAfterSet 下 _selectedEntity != null 且
                // _skillRangeComponentParams 已被 UIStates_ShowClose_Operator 缓存（_skillRange 按钮
                // 可见时保证非空）。模式切换交给 SetRangeMode 统一管；sprite 跟当前模式走。
                if (_selectedEntity == null || _skillRangeComponentParams == null) return;
                bool isOn = _rangeDisplayMode == RangeDisplayMode.Skill;
                UIStates_ShowClose_SkillRange(!isOn);
            });
            _skillRange.GetComponent<EventTrigger>().triggers.Add(_skillRangeClick);
            EventTrigger.Entry skillstop = new EventTrigger.Entry();
            skillstop.eventID = EventTriggerType.PointerClick;
            skillstop.callback.AddListener((data) =>
            {
                if (_selectAbilityRuntime != null && _selectAbilityRuntime.spEngine != null)
                    _selectAbilityRuntime.spEngine.EndAbility();
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
                    Entity entity = EntityManager.Manager.GetStaticEntityInBlock(i, j);
                    if (entity != null && entity.Stats.IsActive)
                    {
                        // 已部署的 entity：直接传 Entity 进去拿实时 Stats，不再走 EntityID → Repository 的模板回退
                        UIStates_SwitchTo_ViewAfterSet(entity.EntityData.ID, entity);
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
            // 新签名:Dictionary<EntityID,int>;并行的两个数组 → 字典
            var poolDict = new System.Collections.Generic.Dictionary<EntityID, int>(idList.Length);
            for (int i = 0; i < idList.Length; i++) poolDict[idList[i]] = nums[i];
            EntityPoolManager.Manager.CreateOrExpandEntityPool(poolDict);
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
        public void AddStaticEntityPrefabToSelector(EntityID[] idList, int[] nums)
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
        public void EntityBackToSelector(Entity entityToBack)
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
                AddStaticEntityPrefabToSelector(new EntityID[1] { entityToBack.EntityData.ID }, new int[1] { 1 });
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
            _currentCost = LevelRescurceManager.Manager.CostMessage.currentCost;
            _cost.text = _currentCost.ToString();
            RefreshAllPlaceDataAffordability();
        }
        public void CanSetNumUpDate()
        {
            _isAffordableNum = LevelRescurceManager.Manager.CanSetNumLeft;
            _isAffordableNumText.text = _isAffordableNum.ToString();
            RefreshAllPlaceDataAffordability();
        }
        private void RefreshAllPlaceDataAffordability()
        {
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
            EntityData[] prefab = new EntityData[characters.Length];
            _characterChineseName = new string[characters.Length];
            _damageStatisticDatas = new DamageStatisticData[characters.Length];
            int[] num = new int[characters.Length];
            for (int i = 0; i < characters.Length; i++)
            {
                EntityData attr = CharacterCardManager.cardManager.GetCharacterAttribute(characters[i]);
                prefab[i] = attr;
                num[i] = 1;
                _damageStatisticDatas[i] = new DamageStatisticData(characters[i], new float[3] { 0, 0, 0 }, new float[1] { 0 }, new float[3] { 0, 0, 0 });
                _characterChineseName[i] = attr.ChineseName;
            }
            InitializeStaticEntityPrefabToSelector(characters, num);
            _currentUIState = UIState.normal;
            _leftmessageOpen = false;
            _operaterOpen = false;
            _draggerOpen = false;
            _rangeOpen = false;
            _cansetOpen = false;
            _pause.image.sprite = _c;
            _pauseMask.SetActive(false);
            _timeMultiple.image.sprite = _x1;
            _blockDatas = MapDataManager.Manager.BlockStateMatrix;
            (_iSize, _jSize) = MapDataManager.Manager.MapSize;
            _rangeImgCollection.SetActive(false);
            CostSliderAndCanSetNumUpdate();
            _cameraOriginalPos = _camera.transform.position;
            _uiCamera = LevelResourceSharing.UICamera;
            _uiCameraOriginalPos = _uiCamera.transform.position;
            _deltaX = _cameraOriginalPos.x - _operateArea.transform.position.x;
        }
        public override void OnExit()
        {
            base.OnExit();
            UIStates_SwitchTo_Normal();
            _canSetBlockList.Clear();
            _selectedStaticEntityID = null;
            _selectedPlaceData = null;
            _selectedEntity = null;
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
        #endregion

        #region UI States
        // ===== 5 SwitchTo methods =====
        private void UIStates_SwitchTo_Normal()
        {
            _isSlow = false;
            SetTimeScale();
            _currentUIState = UIState.normal;
            _selectedStaticEntityID = null;
            _selectedEntity = null;
            if (_selectedPlaceData != null)
            {
                _selectedPlaceData.SelectorMove(false);
                _selectedPlaceData = null;
            }
            UIStates_ShowSomethingAndOtherClose(null);

        }

        private void UIStates_SwitchTo_ViewBeforeSet(StaticEntityPlaceData staticEntityPlaceData)
        {
            // 离开 viewAfterSet 一定清:viewAfterSet 留下的 _selectedEntity 会让
            // GetCurrentDisplayRange 走"已部署 entity.Range"分支,在 setting/choosing/
            // viewBeforeSet 这些"池预览"态下显示错位(显示的是上一个已部署 entity 的范围,
            // 不是新拖出的)。
            EnterPoolPreviewState(UIState.viewBeforeSet, staticEntityPlaceData,
                new string[2] { "leftmessage", "canset" });
        }

        private void UIStates_SwitchTo_Setting(StaticEntityPlaceData staticEntityPlaceData)
        {
            EnterPoolPreviewState(UIState.setting, staticEntityPlaceData,
                new string[3] { "leftmessage", "dragger", "canset" });
        }

        private void UIStates_SwitchTo_Chooseing(StaticEntityPlaceData staticEntityPlaceData)
        {
            EnterPoolPreviewState(UIState.choosing, staticEntityPlaceData,
                new string[4] { "leftmessage", "dragger", "choosing", "canset" });
        }

        private void UIStates_SwitchTo_ViewAfterSet(EntityID entityID, Entity entity = null)
        {
            _isSlow = true;
            SetTimeScale();
            _selectedStaticEntityID = entityID;
            _selectedEntity = entity;
            _selectedPlaceData = null;
            UIStates_ShowSomethingAndOtherClose(new string[3] { "leftmessage", "operator", "range" });
            EnterUIStateAndPump(UIState.viewAfterSet);
        }

        // 4 个"池预览"态共享:清 _selectedEntity、写 placeData + ID、切面板、走状态机循环。
        private void EnterPoolPreviewState(UIState next, StaticEntityPlaceData placeData, string[] shows)
        {
            _isSlow = true;
            SetTimeScale();
            _selectedEntity = null;
            _selectedPlaceData = placeData;
            _selectedStaticEntityID = placeData.EntityId;
            UIStates_ShowSomethingAndOtherClose(shows);
            EnterUIStateAndPump(next);
        }

        // 从 normal 切到非常驻态时启动 FixedUpdate 轮询;切到同一态也再调一次无副作用
        // (FixedUpdate 是 async void,第二次直接返回)。
        private void EnterUIStateAndPump(UIState next)
        {
            if (_currentUIState == UIState.normal)
            {
                _currentUIState = next;
                FixedUpdate();
            }
            _currentUIState = next;
        }

        // ===== State Machine + Dispatch =====
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
                    // 死亡检测（轮询）：_selectedEntity 失活（被致命伤害打挂、淡出中）时立刻关掉操作面板，
                    // 行为对齐撤退路径。该守卫只在 viewAfterSet 触发：其它态（pool 预览）的 EntityStats
                    // 来自 _selectedPlaceData 缓存，池中实体 IsActive 恒为 false，会误判。
                    if (_selectedEntity == null || !_selectedEntity.Stats.IsActive)
                    {
                        UIStates_SwitchTo_Normal();
                        break;
                    }
                    UIStates_Update_Range();
                    UIStates_Update_Leftmessage();
                    UIStates_Update_Operator();
                    break;
            }
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

        // 通用 show/hide 守卫:_xxxOpen 已经是 X 状态时不再重复 SetActive;false 路径留给调用方
        // 写额外的 onClose 副作用。
        private void ShowClosePanel(ref bool open, GameObject go, bool show, System.Action onClose = null)
        {
            if (show)
            {
                if (!open)
                {
                    open = true;
                    go.SetActive(true);
                }
            }
            else
            {
                if (open)
                {
                    open = false;
                    go.SetActive(false);
                    onClose?.Invoke();
                }
            }
        }

        // ===== 6 Panels: ShowClose/Update =====
        // LeftMessage
        private void UIStates_ShowClose_Leftmessage(bool show)
        {
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

                EntityData entityData = GetCurrentEntityData();

                SwitchShowAbilityTalent(_currentShow, entityData, _selectedEntity);
                // LeftMessage 小预览固定显示 EntityData.VisionRange(基础范围,相对偏移,
                // self=(0,0))。不取 GetCurrentVisionRange():后者在 viewAfterSet 下是已
                // SetOrientation/AttackRangeOverride 的世界坐标,经 RangeCaculator 偏移过,
                // self 不再位于 (0,0),且范围会随朝向变化,不符合"小预览"语义。
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
        }
        private void UIStates_Update_Leftmessage()
        {
            if (!_selectedStaticEntityID.HasValue)
                return;
            EntityData entityData = GetCurrentEntityData();
            EntityStats stats = GetCurrentEntityStats();

            // === 战斗属性 ===
            // Def / MagicResistance / BlockOccupation 切到 XxxS（含战斗过程 buff）。
            // Attack 已迁入 EntityStats（AttackS），与其它属性同一路径。
            float attack = stats != null ? stats.AttackS : entityData.Attack;
            float def = stats != null ? stats.DefS : entityData.Defense;
            float mgr = stats != null ? stats.MagicResistanceS : entityData.MagicResistance;
            int blo = stats != null ? stats.BlockOccupationS : entityData.BlockOccupation;
            _statsText.text = $"攻击  {(int)attack}\n防御  {(int)def}\n法抗  {(int)mgr}\n阻挡  {blo}";

            // === HP：取自 EntityStats 实时值 ===
            // _selectedEntity（已部署）→ 真实 CurrentHp；否则（池里的 placeData）→ 按满血显示。
            float maxHp = stats != null ? stats.MaxHpS : entityData.MaxHp;
            float currentHp = (stats != null && stats.IsActive) ? stats.CurrentHp : maxHp;
            float leftLength = _hpSliderSize.width;
            float hpRatio = maxHp > 0 ? currentHp / maxHp : 0;

            _hpSlider.sizeDelta = new Vector2(leftLength * (hpRatio-1), _hpSliderSize.height);
            _hpText.text = $"{(int)currentHp}/{(int)maxHp}";
            _hpBk.anchoredPosition = new Vector2(leftLength * hpRatio > 81 ? leftLength *  hpRatio : 81, 0);
        }

        /// <summary>
        /// 当前选中态的 EntityStats：已部署 entity 优先（实时），否则取 placeData 缓存（池）。
        /// </summary>
        private EntityStats GetCurrentEntityStats()
        {
            if (_selectedEntity != null)
            {
                return _selectedEntity.Stats;
            }
            return _selectedPlaceData?.EntityStats;
        }
        /// <summary>
        /// 当前选中态的 EntityData：已部署 entity 优先，否则取 placeData 缓存。避免每个调用点
        /// 重复走 <c>GameDataService.EntityRepository.Get(_selectedStaticEntityID.Value)</c>。
        /// </summary>
        private EntityData GetCurrentEntityData()
        {
            if (_selectedEntity != null)
            {
                return _selectedEntity.EntityData;
            }
            return _selectedPlaceData?.EntityData;
        }
        /// <summary>
        /// 当前选中态的攻击范围（<c>List&lt;Vector2Int&gt;</c>），供操作员面板的世界图元
        /// （<c>UIStates_Update_Range</c>）使用。三条数据通路分别处理：
        ///
        /// <list type="bullet">
        ///   <item><b>已部署 entity（viewAfterSet）</b>：直接返回 <c>entity.Vision.Range</c>，由
        ///   <c>SetOrientation</c> / <c>AttackRangeOverride</c> 在 <c>Range</c> setter 里现跑
        ///   <c>RangeCaculator</c> 维护。</item>
        ///   <item><b>放置预览（setting/choosing）</b>：chooser 已被激活（<c>_orientation != -1</c>），
        ///   用 <c>EntityVision.BaseRange</c>（<c>EntityData.VisionRange</c> 拷贝）+ chooser 当前位置 +
        ///   朝向，调用 <c>MapDataManager.RangeCaculator</c> 即时算出，与 <c>SetStaticEntity</c> 落盘时
        ///   的输入完全一致。</item>
        ///   <item><b>viewBeforeSet 等尚无预览数据的场景</b>（chooser 未激活，<c>_orientation == -1</c>）：
        ///   兜底到 <c>EntityData.VisionRange</c> 模板。</item>
        /// </list>
        /// LeftMessage 小预览不调本方法,直接用 <see cref="EntityData.VisionRange"/>(基础范围,
        /// 相对偏移,self=(0,0)),不随朝向变化。
        /// </summary>
        private List<Vector2Int> GetCurrentVisionRange()
        {
            // === 已部署：直接读 Vision.Range（已 SetOrientation / AttackRangeOverride） ===
            if (_selectedEntity != null)
            {
                return TuplesToVector2IntList(_selectedEntity.Vision?.Range);
            }
            // === 预览：BaseRange + chooserTile + _orientation 当场跑 RangeCaculator ===
            if (_selectedPlaceData != null && _orientation != -1 && _chooser != null)
            {
                var baseRange = _selectedPlaceData.EntityVision?.BaseRange;
                if (baseRange != null)
                {
                    var chooserPos = _chooser.transform.position;
                    (int x, int y) tilePos = ((int)(chooserPos.x + 0.5), (int)(chooserPos.y + 0.5));
                    return TuplesToVector2IntList(
                        MapDataManager.Manager.RangeCaculator(baseRange, tilePos, _orientation));
                }
            }
            // === 兜底：viewBeforeSet 等无 chooser 的态走模板 ===
            if (_selectedStaticEntityID.HasValue)
            {
                return GetCurrentEntityData()?.VisionRange;
            }
            return null;
        }
        private static List<Vector2Int> TuplesToVector2IntList((int x, int y)[] tuples)
        {
            if (tuples == null) return null;
            var list = new List<Vector2Int>(tuples.Length);
            for (int i = 0; i < tuples.Length; i++)
            {
                list.Add(new Vector2Int(tuples[i].x, tuples[i].y));
            }
            return list;
        }
        // Operator
        private void UIStates_ShowClose_Operator(bool show)
        {
            if (show)
            {
                var entity = _selectedEntity;
                if (entity == null) return;
                if (!_operaterOpen)
                {
                    _operaterOpen = true;
                    _operateArea.SetActive(true);
                }
                // 镜头横移：把 entity 横向对齐到 _operateArea 锚点，y 跟随实体世界位置
                MoveCamera(entity.EntityPosition + _deltaX * Vector2.right, 0.1f);

                // 撤退按钮：仅当该 entity 配置为可撤退时启用；点击 → 池回收 + 50% 退款
                bool canCallBack = entity.EntityData.CanCallBack;
                _callBack.enabled = canCallBack;
                _callBackClick.callback.RemoveAllListeners();
                if (canCallBack)
                {
                    _callBackClick.callback.AddListener((data) =>
                    {
                        // 先退款再回收：CurrentSetCost 在 Return() 后仍可读（SetActive(false) 不销毁组件），
                        // 但语义上"先退一半费用再还池"更符合玩家认知。
                        LevelRescurceManager.Manager.ChangeCost(
                            (int)(entity.GetComponent<InteractableStatic>().CurrentSetCost * 0.5f));
                        // 关键：设 IsActive = false 是给 Slider 自清理的信号。
                        // 死亡路径里 EntityStats.BeginDie 会把 _participateIn 置 false（IsActive 走 false 分支 → slider ReturnSlider）；
                        // 撤退路径没有 BeginDie 这一步，Entity.Dormancy 也不动 _participateIn，slider 看不到任何"已离场"信号，
                        // 就会引用一个 SetActive(false) 的 Entity 永远卡着。这一行对齐死亡路径的信号。
                        entity.Stats.IsActive = false;
                        entity.thisEntityPool.Return(entity);
                        AudioManager.Manager.PlayAudio("escape", 1, false, false);
                        UIStates_SwitchTo_Normal();
                    });
                }

                // 技能按钮与技能范围预览（新 AbilitySystem 数据源：EntityAbilityRunner / AbilityRuntime / SPConfig）
                var runner = entity.SkillRunner;
                if (runner != null && runner.Skills != null && runner.Skills.Count > 0)
                {
                    _selectAbilityRuntime = runner.Skills[0];
                    _selectAbilityConfig = _selectAbilityRuntime.config;
                    _skillOpen.gameObject.SetActive(true);
                    _skillOpen.sprite = _selectAbilityConfig.icon;
                    // 范围预览:从 AttackRangeOverride 组件的 ParamList 读取原始 range(可能为
                    // Vector2Int[] 字面量或 BB 引用)。只有当组件存在且 range 非空时才显示
                    // _skillRange toggle 按钮;首次进入 viewAfterSet 默认预览关闭,off sprite。
                    _skillRangeComponentParams = FindAttackRangeOverrideParams(_selectAbilityRuntime);
                    bool hasRange = _skillRangeComponentParams != null
                                    && _skillRangeComponentParams.HasKey("range");
                    _skillRange.gameObject.SetActive(hasRange);
                    _skillRange.sprite = _skillRangeButton[0]; // off
                }
                else
                {
                    _selectAbilityRuntime = null;
                    _selectAbilityConfig = null;
                    _skillRangeComponentParams = null;
                    _skillOpen.gameObject.SetActive(false);
                    _skillRange.gameObject.SetActive(false);
                }
                UIStates_Update_Operator();
            }
            else
            {
                // 离开 viewAfterSet：仅在面板确实处于打开态时才推回镜头，避免状态机反复切回时抖动
                if (_operaterOpen)
                {
                    _operaterOpen = false;
                    _operateArea.SetActive(false);
                    MoveCamera(_cameraOriginalPos, 0.1f);
                    _selectAbilityConfig = null;
                    _selectAbilityRuntime = null;
                }
            }
        }
        private void UIStates_Update_Operator()
        {
            if (_selectAbilityRuntime == null
                || _selectAbilityRuntime.spEngine == null
                || _selectAbilityConfig == null
                || _selectAbilityConfig.sp == null)
                return;
            var sp = _selectAbilityRuntime.spEngine;
            var cfg = _selectAbilityConfig.sp;

            bool canBegin = sp.CanBegin();
            if (!canBegin)
            {
                _skillOpen.raycastTarget = false;
                _skillOpen.color = Color.gray;
            }
            else
            {
                if (cfg.openMode == AbilitySystem.AbilityOpenMode.Manual)
                {
                    _skillOpen.raycastTarget = true;
                }
                else
                {
                    _skillOpen.raycastTarget = false;
                }
                _skillOpen.color = Color.white;
            }

            float currentSpRate = cfg.totalSp > 0 ? Mathf.Clamp01(sp.CurrentSp / cfg.totalSp) : 0f;
            int currentChargeNum = sp.CurrentCharge;
            bool isSkill = sp.IsActive;
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
                float tsp = cfg.totalSp;
                _spState.sprite = _spMessageAtlas[0];
                _stop.enabled = false;
                if (!(sp.CurrentSp >= cfg.totalSp || (cfg.chargeNum > 1 && sp.CurrentCharge >= cfg.chargeNum)))
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
                int consumeType = (int)cfg.consumeMode;
                _spBk.color = _orange;
                _spState.color = Color.white;
                if (consumeType < 3)
                {
                    float tsa = cfg.abilityAmount;
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
                if (cfg.canManualClose)
                {
                    _stop.enabled = true;
                }
                else
                {
                    _stop.enabled = false;
                }
            }
        }
        // Dragger
        private void UIStates_ShowClose_Dragger(bool show)
        {
            ShowClosePanel(ref _draggerOpen, _target.gameObject, show, () =>
            {
                _up.enabled = false;
                _right.enabled = false;
                _left.enabled = false;
                _down.enabled = false;
            });
        }
        private void UIStates_Update_Dragger()
        {
            if (_currentUIState == UIState.setting)
            {
                Vector3 p = _camera.ScreenToWorldPoint(Input.mousePosition);
                (int i, int j) = ((int)(p.y + 0.5), (int)(p.x + 0.5));
                if (_canSetBlockList.Contains((i, j)))
                {
                    _target.transform.position = new Vector2(j, i);
                    if (_selectedPlaceData.EntityData.NeedsDirectionSelection)
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
        // Chooser
        private void UIStates_ShowClose_Chooser(bool show)
        {
            ShowClosePanel(ref _chooserOpen, _chooser.gameObject, show);
            if (show && _chooserOpen)
            {
                _chooser.transform.position = _target.transform.position;
            }
        }
        private void UIStates_Update_Chooser()
        {
            Vector3 p = _camera.ScreenToWorldPoint(Input.mousePosition);
            _target.transform.position = new Vector2(p.x, p.y);
            if (!_inChooser)
            {
                Vector2 dp = p - _chooser.transform.position;
                int newOrient = ResolveChooserOrientation(dp);
                if (newOrient != -1 && _orientation != newOrient)
                {
                    _orientation = newOrient;
                    SetOrientationArrow(newOrient);
                    UIStates_ShowClose_Range(true);
                }
                _chooser.color = new Color(0, 1, 0, 0.4f);
                _target.color = Color.green;
            }
            else
            {
                _orientation = -1;
                SetOrientationArrow(-1);
                _chooser.color = new Color(1, 1, 0, 0.4f);
                _target.color = Color.yellow;
                UIStates_ShowClose_Range(false);
            }
        }

        // 选择器朝向判定:dp.y 主导上半 vs 下半;若 dp.y 主导则按 dp.y vs -dp.x 分 up/left,
        // 否则按 dp.y vs -dp.x 分 right/down。原 dp.y > -dp.x 边界同时被 up/right 用,
        // 这里用 > / <= 与原条件逐位一致。返回 -1 = 在两条分界线的盲区,保持原朝向不变。
        private static int ResolveChooserOrientation(Vector2 dp)
        {
            if (dp.y > dp.x)
            {
                if (dp.y > -dp.x) return 0; // up
                if (dp.y <= -dp.x) return 3; // left
            }
            else
            {
                if (dp.y > -dp.x) return 1; // right
                if (dp.y <= -dp.x) return 2; // down
            }
            return -1;
        }

        // 朝向 → 四个方向箭头 enabled。-1 全关。indices 与 _orientation 对齐(0=up,1=right,2=down,3=left)。
        private void SetOrientationArrow(int orient)
        {
            _up.enabled = orient == 0;
            _right.enabled = orient == 1;
            _down.enabled = orient == 2;
            _left.enabled = orient == 3;
        }
        // Range — 普通攻击范围（保持原签名供 UIStates_ShowSomethingAndOtherClose 调用）。
        // 内部委托给 SetRangeMode，池子开/关/写图元统一走同一条路径。
        private void UIStates_ShowClose_Range(bool show)
        {
            SetRangeMode(show ? RangeDisplayMode.Normal : RangeDisplayMode.None);
        }
        // SkillRange — 技能攻击范围预览。show=true 切到 Skill 模式;show=false 回退到 Normal。
        // viewAfterSet 默认 Normal,关预览时回到基础范围,与 SetRangeMode 一致。
        private void UIStates_ShowClose_SkillRange(bool show)
        {
            if (show)
            {
                // 必要的健康检查:没选 entity / 该技能没配 AttackRangeOverride 就不开。
                if (_selectedEntity == null || _skillRangeComponentParams == null) return;
                SetRangeMode(RangeDisplayMode.Skill);
                _skillRange.sprite = _skillRangeButton[1]; // on
            }
            else
            {
                SetRangeMode(RangeDisplayMode.Normal);
                _skillRange.sprite = _skillRangeButton[0]; // off
            }
        }
        // Range update（viewAfterSet 每帧调一次）。按当前 _rangeDisplayMode 选数据源 + 颜色,
        // 渲染只走 RenderRangeToPool 一条路径,Normal 和 Skill 不再各写一份。
        private void UIStates_Update_Range()
        {
            if (_rangeDisplayMode == RangeDisplayMode.None) return;
            var tiles = GetCurrentDisplayRange();
            if (tiles == null) { ClearRangePool(); return; }
            RenderRangeToPool(tiles);
        }
        // 数据源：按当前 _rangeDisplayMode 返回该模式当前帧的格子集（已转 (int x, int y)[]）。
        // null = 该模式当前没有可显示的数据（典型:Normal 模式下未选 entity）。
        private (int x, int y)[] GetCurrentDisplayRange()
        {
            switch (_rangeDisplayMode)
            {
                case RangeDisplayMode.Normal:
                {
                    if (!_selectedStaticEntityID.HasValue) return null;
                    var visionRange = GetCurrentVisionRange();
                    if (visionRange == null) return null;
                    var arr = new (int x, int y)[visionRange.Count];
                    for (int i = 0; i < visionRange.Count; i++) arr[i] = (visionRange[i].x, visionRange[i].y);
                    return arr;
                }
                case RangeDisplayMode.Skill:
                {
                    if (_selectedEntity == null || _skillRangeComponentParams == null) return null;
                    var entity = _selectedEntity;
                    // 原始 range 是设计师写的"模板范围",需结合 entity 的位置 / 朝向
                    // 喂给 MapDataManager.RangeCaculator,语义与 SetStaticEntity 落盘时对齐。
                    // 与 AttackRangeOverride.OnTrigger 行为一致:ctx=SkillRunner.sharedBlackboard
                    // 让 fromBlackboard=true 的设计能读到运行时 BB。
                    Vector2Int[] range = _skillRangeComponentParams.GetVector2IntArrayLazy(
                        "range", null, entity.SkillRunner.sharedBlackboard)();
                    if (range == null || range.Length == 0) return null;
                    Vector2 origin = entity.EntityPosition;
                    (int x, int y) tilePos = ((int)(origin.x + 0.5), (int)(origin.y + 0.5));
                    return MapDataManager.Manager.RangeCaculator(
                        ToTupleRange(range), tilePos, entity.Orientation);
                }
                default:
                    return null;
            }
        }
        // 池子写入：按 _rangeDisplayColor 把 tiles 写到 _rangeImg 池子;tiles 长度变化时
        // 自动扩缩 (_rangeImg 池复用,Add 用 Instantiate)。Normal / Skill 共用此函数,
        // 颜色与数据源由 SetRangeMode 切换时分别设到 _rangeDisplayColor 与 GetCurrentDisplayRange。
        private void RenderRangeToPool((int x, int y)[] tiles)
        {
            int rangeImgCount = _rangeImg.Count;
            if (rangeImgCount >= tiles.Length)
            {
                for (int i = 0; i < tiles.Length; i++)
                {
                    _rangeImg[i].transform.position = new Vector2(tiles[i].x, tiles[i].y);
                    _rangeImg[i].color = _rangeDisplayColor;
                    _rangeImg[i].enabled = true;
                }
                for (int i = tiles.Length; i < rangeImgCount; i++)
                {
                    _rangeImg[i].enabled = false;
                }
            }
            else
            {
                for (int i = 0; i < rangeImgCount; i++)
                {
                    _rangeImg[i].transform.position = new Vector2(tiles[i].x, tiles[i].y);
                    _rangeImg[i].color = _rangeDisplayColor;
                    _rangeImg[i].enabled = true;
                }
                for (int i = rangeImgCount; i < tiles.Length; i++)
                {
                    var newImg = Object.Instantiate(_rangeImg[0], new Vector2(tiles[i].x, tiles[i].y), Quaternion.identity, _rangeImgCollection.transform);
                    newImg.color = _rangeDisplayColor;
                    _rangeImg.Add(newImg);
                }
            }
        }
        // 池子清除：所有图元 enabled=false（保留对象,下次 RenderRangeToPool 复用）。
        private void ClearRangePool()
        {
            int rangeImgCount = _rangeImg.Count;
            for (int i = 0; i < rangeImgCount; i++) _rangeImg[i].enabled = false;
        }
        // 模式切换的唯一入口：负责 (1) 设 _rangeDisplayMode + _rangeDisplayColor
        // (2) 按 mode 开/关 _rangeImgCollection + _rangeOpen
        // (3) 立即渲一帧。Normal / Skill / None 三态互斥逻辑全在这里,别处不再有开关池子的代码。
        private static readonly Color RangeColorNormal = new Color(255, 160, 0);
        private static readonly Color RangeColorSkill  = new Color(1f, 0.2f, 0.2f);
        private void SetRangeMode(RangeDisplayMode mode)
        {
            _rangeDisplayMode = mode;
            _rangeDisplayColor = (mode == RangeDisplayMode.Skill) ? RangeColorSkill : RangeColorNormal;
            if (mode == RangeDisplayMode.None)
            {
                if (_rangeOpen) { _rangeOpen = false; _rangeImgCollection.SetActive(false); }
                ClearRangePool();
            }
            else
            {
                if (!_rangeOpen) { _rangeOpen = true; _rangeImgCollection.SetActive(true); }
                UIStates_Update_Range();
            }
        }
        // 在 AbilityRuntime 的 components 列表里找第一个 AttackRangeOverride 类型的组件,
        // 返回它对应的 ParamList(原始字段,UI 侧缓存以便 toggle 时不再扫 components)。
        // 找不到返 null(此时 _skillRange 按钮会隐藏,提示"该技能无攻击范围覆盖")。
        private static AbilitySystem.ParamList FindAttackRangeOverrideParams(AbilitySystem.AbilityRuntime runtime)
        {
            if (runtime == null || runtime.components == null) return null;
            for (int i = 0; i < runtime.components.Count; i++)
            {
                if (runtime.components[i] is AbilitySystem.Components.AttackRangeOverride)
                {
                    return (i < runtime.componentParams.Count) ? runtime.componentParams[i] : null;
                }
            }
            return null;
        }
        private static (int x, int y)[] ToTupleRange(Vector2Int[] range)
        {
            var arr = new (int x, int y)[range.Length];
            for (int i = 0; i < range.Length; i++) arr[i] = (range[i].x, range[i].y);
            return arr;
        }
        // CanSet
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
                    ResetCanSetBlockColors();
                }
            }
        }
        private void UIStates_Update_Canset()
        {
            // 0=地面 1=高台 2=均可放置（来自 EntityData.CanSetType）
            _canSetType = _selectedPlaceData != null ? _selectedPlaceData.EntityData.CanSetType : 0;

            Color lightGreen = new Color(0, 0.4f, 0);
            FetchMapEntityData();
            ResetCanSetBlockColors();
            _canSetBlockList.Clear();
            System.Func<int, int, bool> predicate = GetCanSetPredicate(_canSetType);
            if (predicate != null)
            {
                for (int i = 0; i < _iSize; i++)
                {
                    for (int j = 0; j < _jSize; j++)
                    {
                        if (predicate(i, j))
                        {
                            _canSetBlockList.Add((i, j));
                        }
                    }
                }
            }
            ColorBlockList(lightGreen);
        }

        // 把上一帧高亮的格子全部还原为白色,避免状态切换后残留绿块。
        private void ResetCanSetBlockColors()
        {
            for (int i = 0; i < _canSetBlockList.Count; i++)
            {
                ref BlockState bS = ref MapDataManager.Manager.GetPosBlockRef(_canSetBlockList[i].i, _canSetBlockList[i].j);
                if (bS.material != null) bS.material.color = Color.white;
            }
        }

        private void ColorBlockList(Color c)
        {
            for (int i = 0; i < _canSetBlockList.Count; i++)
            {
                ref BlockState bS = ref MapDataManager.Manager.GetPosBlockRef(_canSetBlockList[i].i, _canSetBlockList[i].j);
                if (bS.material != null) bS.material.color = c;
            }
        }

        // 把原 switch 三臂的"格子可放置判定"抽成谓词,主循环只跑一份。
        // 0=仅地面 / 1=仅高台 / 2=都可;FetchMapEntityData 已保证对应 bool[,] 已加载。
        private System.Func<int, int, bool> GetCanSetPredicate(int canSetType)
        {
            switch (canSetType)
            {
                case 0: return (i, j) => _lowerCanSetBlock[i, j] && !_staticEntityExistBlock[i, j];
                case 1: return (i, j) => _higherCanSetBlock[i, j] && !_staticEntityExistBlock[i, j];
                case 2: return (i, j) => (_lowerCanSetBlock[i, j] || _higherCanSetBlock[i, j]) && !_staticEntityExistBlock[i, j];
                default: return null;
            }
        }

        #endregion

        #region Helpers
        private void FetchMapEntityData()
        {
            _staticEntityExistBlock = EntityManager.Manager.StaticEntityExistBlock;
            switch (_canSetType)
            {
                case 0:
                    _lowerCanSetBlock = MapDataManager.Manager.LowerCanSetBlock;
                    break;
                case 1:
                    _higherCanSetBlock = MapDataManager.Manager.HigherCanSetBlock;
                    break;
                case 2:
                    _lowerCanSetBlock = MapDataManager.Manager.LowerCanSetBlock;
                    _higherCanSetBlock = MapDataManager.Manager.HigherCanSetBlock;
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
                // MCam 与 UICam 同步平移 x/y；两机 z 不同（MCam=-1, UICam=0），各自锁回原 z 不插值
                _camera.transform.position = new Vector3(tmp.x, tmp.y, _cameraOriginalPos.z);
                _uiCamera.transform.position = new Vector3(tmp.x, tmp.y, _uiCameraOriginalPos.z);
                SlidersManager.Manager.TakeOverSliderMove();
            }, 0, 1, duration).SetUpdate(true);
        }
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
        // entity 选填：池预览（viewBeforeSet 等）传 null，viewAfterSet 传 _selectedEntity 以拿到运行时数据。
        private void SwitchShowAbilityTalent(int show, EntityData entityData, Entity entity = null)
        {
            if (_currentShow != show)
            {
                _currentShow = show;
                _abilityCard.AbilityRT.gameObject.SetActive(false);
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

            switch (_currentShow)
            {
                case 0:
                    // Skill: 优先用 live entity 的 AbilityRuntime（未来可显示 SP 实时状态），回退到模板
                    AbilitySystem.AbilityConfig abilityConfig = null;
                    AbilitySystem.AbilityRuntime abilityRuntime = null;
                    if (entity != null)
                    {
                        var skillRunner = entity.SkillRunner;
                        if (skillRunner != null && skillRunner.Skills != null && skillRunner.Skills.Count > 0)
                        {
                            abilityRuntime = skillRunner.Skills[0];
                            abilityConfig = abilityRuntime.config;
                        }
                    }
                    else if (entityData.Skills != null && entityData.Skills.Count > 0)
                    {
                        abilityConfig = entityData.Skills[0];
                    }
                    if (abilityConfig != null)
                    {
                        _abilityCard.AbilityRT.gameObject.SetActive(true);
                        _abilityCard.UpdateAbilityCardMessage(abilityConfig, abilityRuntime);
                    }
                    else
                    {
                        _abilityCard.AbilityRT.gameObject.SetActive(false);
                    }
                    break;
                case 1:
                    _subpCard.SubpRT.gameObject.SetActive(true);
                    _subpCard.UpdateSubpCardMessage(entityData);
                    break;
                case 2:
                    // Talent: live entity 优先（运行时实际挂载的 AbilityRuntime 缓存视图），回退到 entityData.Talents（数据层模板）
                    AbilitySystem.AbilityConfig[] ts;
                    var talentRunner = entity != null ? entity.SkillRunner : null;
                    if (talentRunner != null)
                    {
                        var runtimes = talentRunner.Talents;
                        ts = new AbilitySystem.AbilityConfig[runtimes.Count];
                        for (int i = 0; i < runtimes.Count; i++) ts[i] = runtimes[i].config;
                    }
                    else if (entityData.Talents != null)
                    {
                        ts = entityData.Talents.ToArray();
                    }
                    else
                    {
                        ts = new AbilitySystem.AbilityConfig[0];
                    }
                    if (ts.Length <= _talentCards.Count)
                    {
                        for (int i = 0; i < ts.Length; i++)
                        {
                            _talentCards[i].TalentRT.gameObject.SetActive(true);
                            _talentCards[i].UpdateTalentCardMessage(ts[i]);
                        }
                        for (int i = ts.Length; i < _talentCards.Count; i++)
                        {
                            _talentCards[i].TalentRT.gameObject.SetActive(false);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < _talentCards.Count; i++)
                        {
                            _talentCards[i].TalentRT.gameObject.SetActive(true);
                            _talentCards[i].UpdateTalentCardMessage(ts[i]);
                        }
                        for (int i = _talentCards.Count; i < ts.Length; i++)
                        {
                            TalentCard ti = new TalentCard(new Vector2(0, 0), _skillTalentRect, Color.white, _skillTalentRect.rect.width);
                            ti.UpdateTalentCardMessage(ts[i]);
                            _talentCards.Add(ti);
                        }
                    }
                    break;
                case 3:
                    // Buff: 来自 live entity 的 BuffController（运行时数据，池预览无；该路径下显示空）
                    List<Buff> bs = (entity != null && entity.buffController != null)
                        ? entity.buffController.Buffs
                        : new List<Buff>();
                    if (bs.Count <= _buffCards.Count)
                    {
                        for (int i = 0; i < bs.Count; i++)
                        {
                            _buffCards[i].BuffRT.gameObject.SetActive(true);
                            _buffCards[i].UpdateBuffCardMessage(bs[i]);
                        }
                        for (int i = bs.Count; i < _buffCards.Count; i++)
                        {
                            _buffCards[i].BuffRT.gameObject.SetActive(false);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < _buffCards.Count; i++)
                        {
                            _buffCards[i].BuffRT.gameObject.SetActive(true);
                            _buffCards[i].UpdateBuffCardMessage(bs[i]);
                        }
                        for (int i = _buffCards.Count; i < bs.Count; i++)
                        {
                            BuffCard bi = new BuffCard(new Vector2(0, 0), _skillTalentRect, Color.white, _skillTalentRect.rect.width);
                            bi.UpdateBuffCardMessage(bs[i]);
                            _buffCards.Add(bi);
                        }
                    }
                    break;
            }
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
        // 把 EntityData.VisionRange（基础范围，相对偏移，self=(0,0)）摆到 LeftMessage 的
        // 小预览 UI 中：以 base range 包围盒中心为锚点居中（视觉上整张图在 _rangeArea 中
        // 摆正），self 跟随 (0, 0) entry 的位置（(0-centerX, 0-centerY) * l，对称范围时与
        // 包围盒中心重合,非对称范围时偏向一侧）。tile 边长 l 由 base range 的最大延伸算出,
        // 保证整张图塞得进 _rangeArea。保留 (y,-x) 旋转约定（与原版一致）。
        // base range 是模板,与 viewAfterSet/_orientation/RangeCaculator 无关,
        // 任意状态下显示都一致。
        private void ShowAttackRangeAttributes(List<Vector2Int> baseRange)
        {
            if (baseRange == null || baseRange.Count == 0) return;

            int maxX, minX, maxY, minY;
            maxX = -1000;
            maxY = -1000;
            minX = 1000;
            minY = 1000;
            (int x, int y)[] range = new (int x, int y)[baseRange.Count];
            for (int i = 0; i < range.Length; i++)
            {
                range[i] = (baseRange[i].y, -baseRange[i].x);
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
                // 整图居中:所有 tile 减掉包围盒中心,(0,0) entry 偏移到 (-centerX,-centerY)*l。
                _rangeTiles[i].anchoredPosition = new Vector2(l * (range[i].x - centerX), l * (range[i].y - centerY));
                _rangeTiles[i].gameObject.SetActive(true);
            }
            // self tile 落在 (0,0) entry 居中后的位置 — 对称范围 = 视觉中心;
            // 非对称范围(如"向右一条线")=(0,0) entry 偏移后落在图的一侧。
            _rangeSelfTile.sizeDelta = new Vector2(l * (1 - gap), l * (1 - gap));
            _rangeSelfTile.anchoredPosition = new Vector2(-l * centerX, -l * centerY);
        }
        private async void CostSliderAndCanSetNumUpdate()
        {
            while (true)
            {
                var cm = LevelRescurceManager.Manager.CostMessage;
                _isAffordableNum = LevelRescurceManager.Manager.CanSetNumLeft;
                _currentCost = cm.currentCost;
                _cost.text = _currentCost.ToString();
                _costSlider.fillAmount = cm.costTimer;
                _isAffordableNumText.text = _isAffordableNum.ToString();
                RefreshAllPlaceDataAffordability();
                await UniTask.WaitForFixedUpdate(LevelResourceSharing.LevelCtk);
            }
        }
        #endregion
    }
}

