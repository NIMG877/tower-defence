using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MyUI
{
    /// <summary>
    /// Owns selected-entity details, entity commands, and all attack-range presentation.
    /// These features share one selection context and intentionally remain one cohesive module.
    /// </summary>
    internal sealed class LevelMessageEntityModule
    {
        private enum RangeDisplayMode
        {
            None,
            Normal,
            Skill,
        }

        private enum DetailsPage
        {
            Ability,
            SubProfession,
            Talent,
            Buff,
        }

        private readonly LevelMessageSelectionContext _context;
        private readonly System.Action _returnToNormal;
        private readonly Camera _camera;

        // Details panel.
        private readonly GameObject _leftMessage;
        private readonly Image _class;
        private readonly RectTransform _rangeSelfTile;
        private readonly RectTransform _rangeArea;
        private readonly List<RectTransform> _rangeTiles;
        private readonly TextMeshProUGUI _name;
        private readonly TextMeshProUGUI _statsText;
        private readonly RectTransform _hpSlider;
        private readonly RectTransform _hpBackground;
        private readonly Vector2 _hpSliderSize;
        private readonly TextMeshProUGUI _hpText;
        private readonly Sprite[] _professionsLighten;
        private readonly RectTransform _skillTalentRect;
        private readonly RectTransform _skillTalentRectParent;
        private readonly ScrollRect _detailsScrollRect;
        private readonly AbilityCard _abilityCard;
        private readonly SubpCard _subpCard;
        private readonly List<TalentCard> _talentCards = new List<TalentCard>();
        private readonly List<BuffCard> _buffCards = new List<BuffCard>();
        private readonly Image[] _skillTalentSwitchButtons;

        // Entity operator.
        private readonly GameObject _operateArea;
        private readonly Image _callBack;
        private readonly Image _skillOpen;
        private readonly Image _skillRange;
        private readonly Image _spBackground;
        private readonly Image _spState;
        private readonly Image _spMask;
        private readonly Image _stop;
        private readonly Image _skillChargeNum;
        private readonly TextMeshProUGUI _spText;
        private readonly TextMeshProUGUI _skillChargeNumText;
        private readonly Sprite[] _spMessageAtlas;
        private readonly Sprite[] _skillRangeButton;
        private readonly EventTrigger.Entry _callBackClick;

        // World range rendering.
        private readonly GameObject _rangeImageCollection;
        private readonly List<SpriteRenderer> _rangeImages;

        private Camera _uiCamera;
        private Vector3 _cameraOriginalPosition;
        private Vector3 _uiCameraOriginalPosition;
        private float _operatorCameraOffsetX;
        private bool _leftMessageOpen;
        private bool _operatorOpen;
        private DetailsPage _currentDetailsPage;
        private AbilitySystem.AbilityConfig _selectedAbilityConfig;
        private AbilitySystem.AbilityRuntime _selectedAbilityRuntime;
        private AbilitySystem.ParamList _skillRangeComponentParams;
        private RangeDisplayMode _rangeDisplayMode;
        private Color _rangeDisplayColor;

        private readonly Color _selectedTabColor = new Color(0, 0, 0, 0.5882f);
        private readonly Color _unselectedTabColor = new Color(0.3529f, 0.3529f, 0.3529f, 0.7843f);
        private readonly Color _lightGreen = new Color(0.796f, 0.925f, 0.278f);
        private readonly Color _lightGreenHalf = new Color(0.796f, 0.925f, 0.278f, 0.5f);
        private readonly Color _orange = new Color(1, 0.412f, 0);
        private readonly Color _orangeHalf = new Color(1, 0.412f, 0, 0.5f);
        private readonly Color _gray = new Color(0.259f, 0.259f, 0.259f);

        public LevelMessageEntityModule(
            GameObject root,
            LevelMessageSelectionContext context,
            System.Action returnToNormal)
        {
            _context = context;
            _returnToNormal = returnToNormal;
            _camera = LevelResourceSharing.MainCamera;

            _professionsLighten = Resources.LoadAll<Sprite>(
                "Prefabs/UI/MyUIs/UISprites/CharacterHandSprites/atlas_profession_lighten");
            _spMessageAtlas = Resources.LoadAll<Sprite>(
                "Prefabs/UI/MyUIs/UISprites/LevelMessagePanel/atlas_spState");
            _skillRangeButton = new[]
            {
                Resources.Load<Sprite>("Prefabs/UI/MyUIs/UISprites/LevelMessagePanel/sprite_skill_range_off"),
                Resources.Load<Sprite>("Prefabs/UI/MyUIs/UISprites/LevelMessagePanel/sprite_skill_range_on"),
            };

            _leftMessage = LevelMessageViewLookup.Get<Transform>(root, "leftMessageArea").gameObject;
            _class = LevelMessageViewLookup.Get<Image>(root, "leftMessageArea/class");
            _rangeSelfTile = LevelMessageViewLookup.Get<RectTransform>(
                root, "leftMessageArea/attributes/atkRange/area/self");
            _rangeArea = LevelMessageViewLookup.Get<RectTransform>(
                root, "leftMessageArea/attributes/atkRange/area");
            _rangeTiles = new List<RectTransform>
            {
                LevelMessageViewLookup.Get<RectTransform>(
                    root, "leftMessageArea/attributes/atkRange/area/range"),
            };
            _name = LevelMessageViewLookup.Get<TextMeshProUGUI>(root, "leftMessageArea/name");
            _statsText = LevelMessageViewLookup.Get<TextMeshProUGUI>(
                root, "leftMessageArea/attributes/admb");
            _hpSlider = LevelMessageViewLookup.Get<RectTransform>(
                root, "leftMessageArea/attributes/hpSliderBk/hpSlider");
            _hpBackground = LevelMessageViewLookup.Get<RectTransform>(
                root, "leftMessageArea/attributes/hpBk");
            _hpText = LevelMessageViewLookup.Get<TextMeshProUGUI>(
                root, "leftMessageArea/attributes/hpBk/hpText");
            _hpSliderSize = _hpSlider.rect.size;

            _skillTalentRect = LevelMessageViewLookup.Get<RectTransform>(
                root, "leftMessageArea/skillTalent/content/content");
            _skillTalentRectParent = LevelMessageViewLookup.Get<RectTransform>(
                root, "leftMessageArea/skillTalent/content");
            _detailsScrollRect = _skillTalentRectParent.GetComponent<ScrollRect>();
            _abilityCard = new AbilityCard(
                Vector2.zero, _skillTalentRect, Color.white, _skillTalentRect.rect.width);
            _subpCard = new SubpCard(
                Vector2.zero, _skillTalentRect, Color.white, _skillTalentRect.rect.width);
            _subpCard.SubpRT.gameObject.SetActive(false);
            _skillTalentSwitchButtons = BindDetailsTabs(root);

            _operateArea = LevelMessageViewLookup.Get<Transform>(root, "operateArea").gameObject;
            _callBack = LevelMessageViewLookup.Get<Image>(root, "operateArea/callback");
            _skillOpen = LevelMessageViewLookup.Get<Image>(root, "operateArea/skillOpen");
            _skillRange = LevelMessageViewLookup.Get<Image>(root, "operateArea/skillRange");
            _spBackground = LevelMessageViewLookup.Get<Image>(root, "operateArea/skillOpen/sp/spBk");
            _spState = LevelMessageViewLookup.Get<Image>(root, "operateArea/skillOpen/sp/spBk/spState");
            _spText = LevelMessageViewLookup.Get<TextMeshProUGUI>(root, "operateArea/skillOpen/sp/spText");
            _spMask = LevelMessageViewLookup.Get<Image>(root, "operateArea/skillOpen/spmask");
            _stop = LevelMessageViewLookup.Get<Image>(root, "operateArea/skillOpen/stop");
            _skillChargeNum = LevelMessageViewLookup.Get<Image>(root, "operateArea/skillOpen/skillChargeNum");
            _skillChargeNumText = LevelMessageViewLookup.Get<TextMeshProUGUI>(
                root, "operateArea/skillOpen/skillChargeNum/skillChargeText");
            _callBackClick = BindOperatorEvents();

            _rangeImageCollection = new GameObject("rangeImgCollection");
            _rangeImageCollection.transform.SetParent(LevelResourceSharing.LM);
            SpriteRenderer rangeSample = Object.Instantiate(
                Resources.Load<GameObject>("Prefabs/EffectPrefabs/rangeImg"),
                _rangeImageCollection.transform).GetComponent<SpriteRenderer>();
            _rangeImages = new List<SpriteRenderer> { rangeSample };
        }

        public void OnEnter()
        {
            _leftMessageOpen = false;
            _operatorOpen = false;
            _rangeDisplayMode = RangeDisplayMode.None;
            _leftMessage.SetActive(false);
            _operateArea.SetActive(false);
            _rangeImageCollection.SetActive(false);
            ClearRangePool();

            _cameraOriginalPosition = _camera.transform.position;
            _uiCamera = LevelResourceSharing.UICamera;
            _uiCameraOriginalPosition = _uiCamera.transform.position;
            _operatorCameraOffsetX = _cameraOriginalPosition.x - _operateArea.transform.position.x;
        }

        public void ShowLeftMessage(bool show)
        {
            if (!show)
            {
                if (_leftMessageOpen)
                {
                    _leftMessageOpen = false;
                    _leftMessage.SetActive(false);
                }
                return;
            }

            if (!_leftMessageOpen)
            {
                _leftMessageOpen = true;
                _leftMessage.SetActive(true);
            }
            if (!_context.SelectedStaticEntityID.HasValue)
                return;

            EntityData entityData = GetCurrentEntityData();
            SwitchDetailsPage(_currentDetailsPage, entityData, _context.SelectedEntity);
            ShowAttackRangeAttributes(entityData.VisionRange);
            _name.text = entityData.ChineseName;
            _class.sprite = _professionsLighten[entityData.CharacterJob];
            UpdateLeftMessage();
        }

        public void UpdateLeftMessage()
        {
            if (!_context.SelectedStaticEntityID.HasValue)
                return;

            EntityData entityData = GetCurrentEntityData();
            EntityStats stats = GetCurrentEntityStats();
            float attack = stats != null ? stats.AttackS : entityData.Attack;
            float defense = stats != null ? stats.DefS : entityData.Defense;
            float magicResistance = stats != null ? stats.MagicResistanceS : entityData.MagicResistance;
            int block = stats != null ? stats.BlockOccupationS : entityData.BlockOccupation;
            _statsText.text =
                $"攻击  {(int)attack}\n防御  {(int)defense}\n法抗  {(int)magicResistance}\n阻挡  {block}";

            float maxHp = stats != null ? stats.MaxHpS : entityData.MaxHp;
            float currentHp = stats != null && stats.IsActive ? stats.CurrentHp : maxHp;
            float hpRatio = maxHp > 0 ? currentHp / maxHp : 0;
            _hpSlider.sizeDelta = new Vector2(
                _hpSliderSize.x * (hpRatio - 1),
                _hpSliderSize.y);
            _hpText.text = $"{(int)currentHp}/{(int)maxHp}";
            _hpBackground.anchoredPosition = new Vector2(
                _hpSliderSize.x * hpRatio > 81 ? _hpSliderSize.x * hpRatio : 81,
                0);
        }

        public void ShowOperator(bool show)
        {
            if (!show)
            {
                if (_operatorOpen)
                {
                    _operatorOpen = false;
                    _operateArea.SetActive(false);
                    MoveCamera(_cameraOriginalPosition, 0.1f);
                }
                _selectedAbilityConfig = null;
                _selectedAbilityRuntime = null;
                _skillRangeComponentParams = null;
                return;
            }

            Entity entity = _context.SelectedEntity;
            if (entity == null)
                return;
            if (!_operatorOpen)
            {
                _operatorOpen = true;
                _operateArea.SetActive(true);
            }
            MoveCamera(entity.Movement.Position + _operatorCameraOffsetX * Vector2.right, 0.1f);

            bool canCallBack = entity.EntityData.CanCallBack;
            _callBack.enabled = canCallBack;
            _callBackClick.callback.RemoveAllListeners();
            if (canCallBack)
            {
                _callBackClick.callback.AddListener(_ =>
                {
                    LevelResourceManager.Manager.ChangeCost(
                        (int)(entity.GetComponent<InteractableStatic>().CurrentSetCost * 0.5f));
                    entity.Stats.IsActive = false;
                    entity.thisEntityPool.Return(entity);
                    AudioManager.Manager.PlayAudio("escape", 1, false, false);
                    _returnToNormal();
                });
            }

            _selectedAbilityRuntime = GetPrimarySkill(entity);
            if (_selectedAbilityRuntime != null)
            {
                _selectedAbilityConfig = _selectedAbilityRuntime.config;
                _skillOpen.gameObject.SetActive(true);
                _skillOpen.sprite = _selectedAbilityConfig.icon;
                _skillRangeComponentParams = FindAttackRangeOverrideParams(_selectedAbilityRuntime);
                bool hasRange = _skillRangeComponentParams != null &&
                                _skillRangeComponentParams.HasKey("range");
                _skillRange.gameObject.SetActive(hasRange);
                _skillRange.sprite = _skillRangeButton[0];
            }
            else
            {
                _selectedAbilityRuntime = null;
                _selectedAbilityConfig = null;
                _skillRangeComponentParams = null;
                _skillOpen.gameObject.SetActive(false);
                _skillRange.gameObject.SetActive(false);
            }
            UpdateOperator();
        }

        public void UpdateOperator()
        {
            if (_selectedAbilityRuntime == null ||
                _selectedAbilityRuntime.spEngine == null ||
                _selectedAbilityConfig == null ||
                _selectedAbilityConfig.sp == null)
            {
                return;
            }

            var sp = _selectedAbilityRuntime.spEngine;
            var config = _selectedAbilityConfig.sp;
            bool canBegin = sp.CanBegin();
            _skillOpen.raycastTarget =
                canBegin && config.openMode == AbilitySystem.AbilityOpenMode.Manual;
            _skillOpen.color = canBegin ? Color.white : Color.gray;

            float currentSpRate = config.totalSp > 0
                ? Mathf.Clamp01(sp.CurrentSp / config.totalSp)
                : 0;
            int currentCharge = sp.CurrentCharge;
            _spMask.fillAmount = currentSpRate;
            _skillChargeNum.gameObject.SetActive(currentCharge >= 1);
            if (currentCharge >= 1)
                _skillChargeNumText.text = currentCharge.ToString();

            if (!sp.IsActive)
            {
                RenderInactiveSkillState(sp, config, currentSpRate, currentCharge);
                return;
            }

            RenderActiveSkillState(config, currentSpRate);
        }

        private void RenderInactiveSkillState(
            AbilitySystem.SPEngine sp,
            AbilitySystem.SPConfig config,
            float currentSpRate,
            int currentCharge)
        {
            _spState.sprite = _spMessageAtlas[0];
            _stop.enabled = false;
            bool ready = sp.CurrentSp >= config.totalSp ||
                         config.chargeNum > 1 && sp.CurrentCharge >= config.chargeNum;
            if (ready)
            {
                _spMask.enabled = false;
                _spText.text = "READY";
                _spBackground.color = _lightGreen;
                _spState.color = Color.white;
                _spText.color = Color.black;
                return;
            }

            _spMask.enabled = true;
            _spMask.color = _lightGreenHalf;
            _spText.text = $"{(int)(config.totalSp * currentSpRate)}/{config.totalSp}";
            if (currentCharge == 0 && currentSpRate < 1)
            {
                _spBackground.color = _gray;
                _spState.color = _lightGreen;
                _spText.color = Color.white;
            }
            else
            {
                _spBackground.color = _lightGreen;
                _spState.color = Color.white;
                _spText.color = Color.black;
            }
        }

        private void RenderActiveSkillState(AbilitySystem.SPConfig config, float currentSpRate)
        {
            int consumeType = (int)config.consumeMode;
            _spBackground.color = _orange;
            _spState.color = Color.white;
            if (consumeType < 3)
            {
                _spMask.enabled = true;
                _spMask.color = _orangeHalf;
                _spState.sprite = _spMessageAtlas[consumeType + 1];
                _spText.color = Color.white;
                if (consumeType == 0)
                    _spText.text = (config.abilityAmount * currentSpRate).ToString("0.0") + "s";
                else
                    _spText.text = config.abilityAmount * currentSpRate + "/" + config.abilityAmount;
            }
            else
            {
                _spMask.enabled = false;
                _spState.sprite = _spMessageAtlas[4];
                _spText.text = null;
            }
            _stop.enabled = config.canManualClose;
        }

        public void ShowNormalRange(bool show)
        {
            SetRangeMode(show ? RangeDisplayMode.Normal : RangeDisplayMode.None);
        }

        public void UpdateRange()
        {
            if (_rangeDisplayMode == RangeDisplayMode.None)
                return;
            (int x, int y)[] tiles = GetCurrentDisplayRange();
            if (tiles == null)
            {
                ClearRangePool();
                return;
            }
            RenderRangeToPool(tiles);
        }

        private Image[] BindDetailsTabs(GameObject root)
        {
            Transform selectBar = LevelMessageViewLookup.Get<Transform>(
                root, "leftMessageArea/skillTalent/selectBar");
            var buttons = new Image[4];
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i] = selectBar.GetChild(i).GetComponent<Image>();
                int index = i;
                var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                click.callback.AddListener(_ =>
                {
                    DetailsPage page = (DetailsPage)index;
                    if (_currentDetailsPage == page || !_context.SelectedStaticEntityID.HasValue)
                        return;
                    SwitchDetailsPage(page, GetCurrentEntityData(), _context.SelectedEntity);
                });
                buttons[i].GetComponent<EventTrigger>().triggers.Add(click);
            }
            return buttons;
        }

        private EventTrigger.Entry BindOperatorEvents()
        {
            var callBackClick = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            _callBack.GetComponent<EventTrigger>().triggers.Add(callBackClick);

            var skillOpenClick = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            skillOpenClick.callback.AddListener(_ =>
            {
                var sp = _selectedAbilityRuntime != null ? _selectedAbilityRuntime.spEngine : null;
                if (sp != null && sp.CanBegin())
                {
                    sp.StartAbility();
                    _returnToNormal();
                }
            });
            _skillOpen.GetComponent<EventTrigger>().triggers.Add(skillOpenClick);

            var skillRangeClick = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            skillRangeClick.callback.AddListener(_ =>
            {
                if (_context.SelectedEntity == null || _skillRangeComponentParams == null)
                    return;
                ShowSkillRange(_rangeDisplayMode != RangeDisplayMode.Skill);
            });
            _skillRange.GetComponent<EventTrigger>().triggers.Add(skillRangeClick);

            var stopClick = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            stopClick.callback.AddListener(_ =>
            {
                if (_selectedAbilityRuntime != null && _selectedAbilityRuntime.spEngine != null)
                    _selectedAbilityRuntime.spEngine.EndAbility();
                _returnToNormal();
                AudioManager.Manager.PlayAudio("skill_boostclose", 1, false, false);
            });
            _stop.GetComponent<EventTrigger>().triggers.Add(stopClick);
            return callBackClick;
        }

        private EntityStats GetCurrentEntityStats()
        {
            return _context.SelectedEntity != null
                ? _context.SelectedEntity.Stats
                : _context.SelectedPlaceData?.EntityStats;
        }

        private EntityData GetCurrentEntityData()
        {
            return _context.SelectedEntity != null
                ? _context.SelectedEntity.EntityData
                : _context.SelectedPlaceData?.EntityData;
        }

        private (int x, int y)[] GetCurrentWorldRange()
        {
            if (_context.SelectedEntity != null)
                return _context.SelectedEntity.Vision?.Range;

            if (_context.SelectedPlaceData != null &&
                _context.Orientation != -1 &&
                _context.HasPreviewPosition)
            {
                var baseRange = _context.SelectedPlaceData.EntityVision?.BaseRange;
                if (baseRange != null)
                {
                    Vector2 position = _context.PreviewPosition;
                    (int x, int y) tilePosition =
                        ((int)(position.x + 0.5), (int)(position.y + 0.5));
                    return MapDataManager.Manager.RangeCaculator(
                        baseRange,
                        tilePosition,
                        _context.Orientation);
                }
            }

            if (!_context.SelectedStaticEntityID.HasValue)
                return null;
            List<Vector2Int> baseVisionRange = GetCurrentEntityData()?.VisionRange;
            if (baseVisionRange == null)
                return null;
            var result = new (int x, int y)[baseVisionRange.Count];
            for (int i = 0; i < baseVisionRange.Count; i++)
                result[i] = (baseVisionRange[i].x, baseVisionRange[i].y);
            return result;
        }

        private void ShowSkillRange(bool show)
        {
            if (show)
            {
                if (_context.SelectedEntity == null || _skillRangeComponentParams == null)
                    return;
                SetRangeMode(RangeDisplayMode.Skill);
                _skillRange.sprite = _skillRangeButton[1];
            }
            else
            {
                SetRangeMode(RangeDisplayMode.Normal);
                _skillRange.sprite = _skillRangeButton[0];
            }
        }

        private (int x, int y)[] GetCurrentDisplayRange()
        {
            if (_rangeDisplayMode == RangeDisplayMode.Normal)
            {
                if (!_context.SelectedStaticEntityID.HasValue)
                    return null;
                return GetCurrentWorldRange();
            }

            if (_rangeDisplayMode != RangeDisplayMode.Skill ||
                _context.SelectedEntity == null ||
                _skillRangeComponentParams == null)
            {
                return null;
            }

            Entity entity = _context.SelectedEntity;
            Vector2Int[] skillRange = _skillRangeComponentParams.GetVector2IntArrayLazy(
                "range",
                null,
                entity.AbilityRunner.sharedBlackboard)();
            if (skillRange == null || skillRange.Length == 0)
                return null;
            Vector2 origin = entity.Movement.Position;
            (int x, int y) tilePosition =
                ((int)(origin.x + 0.5), (int)(origin.y + 0.5));
            return MapDataManager.Manager.RangeCaculator(
                ToTupleRange(skillRange),
                tilePosition,
                entity.Orientation);
        }

        private void SetRangeMode(RangeDisplayMode mode)
        {
            _rangeDisplayMode = mode;
            _rangeDisplayColor = mode == RangeDisplayMode.Skill
                ? new Color(1, 0.2f, 0.2f)
                : new Color(1, 160f / 255f, 0);
            if (mode == RangeDisplayMode.None)
            {
                _rangeImageCollection.SetActive(false);
                ClearRangePool();
            }
            else
            {
                _rangeImageCollection.SetActive(true);
                UpdateRange();
            }
        }

        private void RenderRangeToPool((int x, int y)[] tiles)
        {
            int existingCount = _rangeImages.Count;
            int reusedCount = Mathf.Min(existingCount, tiles.Length);
            for (int i = 0; i < reusedCount; i++)
            {
                _rangeImages[i].transform.position = new Vector2(tiles[i].x, tiles[i].y);
                _rangeImages[i].color = _rangeDisplayColor;
                _rangeImages[i].enabled = true;
            }
            for (int i = tiles.Length; i < existingCount; i++)
                _rangeImages[i].enabled = false;
            for (int i = existingCount; i < tiles.Length; i++)
            {
                SpriteRenderer image = Object.Instantiate(
                    _rangeImages[0],
                    new Vector2(tiles[i].x, tiles[i].y),
                    Quaternion.identity,
                    _rangeImageCollection.transform);
                image.color = _rangeDisplayColor;
                image.enabled = true;
                _rangeImages.Add(image);
            }
        }

        private void ClearRangePool()
        {
            for (int i = 0; i < _rangeImages.Count; i++)
                _rangeImages[i].enabled = false;
        }

        private static AbilitySystem.ParamList FindAttackRangeOverrideParams(
            AbilitySystem.AbilityRuntime runtime)
        {
            if (runtime == null || runtime.components == null)
                return null;
            for (int i = 0; i < runtime.components.Count; i++)
            {
                if (runtime.components[i] is AbilitySystem.Components.AttackRangeOverride)
                {
                    return i < runtime.componentParams.Count
                        ? runtime.componentParams[i]
                        : null;
                }
            }
            return null;
        }

        private static (int x, int y)[] ToTupleRange(Vector2Int[] range)
        {
            var result = new (int x, int y)[range.Length];
            for (int i = 0; i < range.Length; i++)
                result[i] = (range[i].x, range[i].y);
            return result;
        }

        private void MoveCamera(Vector2 targetPosition, float duration)
        {
            Vector2 currentPosition = _camera.transform.position;
            DOTween.To(value =>
            {
                Vector2 position = (1 - value) * currentPosition + value * targetPosition;
                _camera.transform.position = new Vector3(
                    position.x,
                    position.y,
                    _cameraOriginalPosition.z);
                _uiCamera.transform.position = new Vector3(
                    position.x,
                    position.y,
                    _uiCameraOriginalPosition.z);
                SlidersManager.Manager.TakeOverSliderMove();
            }, 0, 1, duration).SetUpdate(true).SetId("LevelMessagePanel");
        }

        private void SwitchDetailsPage(DetailsPage page, EntityData entityData, Entity entity = null)
        {
            if (_currentDetailsPage != page)
            {
                _currentDetailsPage = page;
                _abilityCard.AbilityRT.gameObject.SetActive(false);
                _subpCard.SubpRT.gameObject.SetActive(false);
                for (int i = 0; i < _talentCards.Count; i++)
                    _talentCards[i].TalentRT.gameObject.SetActive(false);
                for (int i = 0; i < _buffCards.Count; i++)
                    _buffCards[i].BuffRT.gameObject.SetActive(false);
                for (int i = 0; i < _skillTalentSwitchButtons.Length; i++)
                {
                    _skillTalentSwitchButtons[i].color = i == (int)page
                        ? _selectedTabColor
                        : _unselectedTabColor;
                }
            }

            switch (_currentDetailsPage)
            {
                case DetailsPage.Ability:
                    ShowAbilityDetails(entityData, entity);
                    break;
                case DetailsPage.SubProfession:
                    _subpCard.SubpRT.gameObject.SetActive(true);
                    _subpCard.UpdateSubpCardMessage(entityData);
                    break;
                case DetailsPage.Talent:
                    ShowTalentDetails(entityData, entity);
                    break;
                case DetailsPage.Buff:
                    ShowBuffDetails(entity);
                    break;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_skillTalentRect);
            _detailsScrollRect.vertical =
                _skillTalentRect.rect.height > _skillTalentRectParent.rect.height;
        }

        private void ShowAbilityDetails(EntityData entityData, Entity entity)
        {
            AbilitySystem.AbilityConfig config = null;
            AbilitySystem.AbilityRuntime runtime = GetPrimarySkill(entity);
            if (entity != null)
            {
                if (runtime != null)
                    config = runtime.config;
            }
            else if (entityData.Skills != null && entityData.Skills.Count > 0)
            {
                config = entityData.Skills[0];
            }

            _abilityCard.AbilityRT.gameObject.SetActive(config != null);
            if (config != null)
                _abilityCard.UpdateAbilityCardMessage(config, runtime);
        }

        private static AbilitySystem.AbilityRuntime GetPrimarySkill(Entity entity)
        {
            var runner = entity != null ? entity.AbilityRunner : null;
            return runner != null && runner.Skills != null && runner.Skills.Count > 0
                ? runner.Skills[0]
                : null;
        }

        private void ShowTalentDetails(EntityData entityData, Entity entity)
        {
            AbilitySystem.AbilityConfig[] talents;
            var runner = entity != null ? entity.AbilityRunner : null;
            if (runner != null)
            {
                talents = new AbilitySystem.AbilityConfig[runner.Talents.Count];
                for (int i = 0; i < runner.Talents.Count; i++)
                    talents[i] = runner.Talents[i].config;
            }
            else
            {
                talents = entityData.Talents != null
                    ? entityData.Talents.ToArray()
                    : System.Array.Empty<AbilitySystem.AbilityConfig>();
            }
            UpdateTalentCards(talents);
        }

        private void UpdateTalentCards(AbilitySystem.AbilityConfig[] talents)
        {
            for (int i = 0; i < talents.Length; i++)
            {
                if (i >= _talentCards.Count)
                {
                    _talentCards.Add(new TalentCard(
                        Vector2.zero,
                        _skillTalentRect,
                        Color.white,
                        _skillTalentRect.rect.width));
                }
                _talentCards[i].TalentRT.gameObject.SetActive(true);
                _talentCards[i].UpdateTalentCardMessage(talents[i]);
            }
            for (int i = talents.Length; i < _talentCards.Count; i++)
                _talentCards[i].TalentRT.gameObject.SetActive(false);
        }

        private void ShowBuffDetails(Entity entity)
        {
            List<Buff> buffs = entity != null && entity.buffController != null
                ? entity.buffController.Buffs
                : null;
            int buffCount = buffs?.Count ?? 0;
            for (int i = 0; i < buffCount; i++)
            {
                if (i >= _buffCards.Count)
                {
                    _buffCards.Add(new BuffCard(
                        Vector2.zero,
                        _skillTalentRect,
                        Color.white,
                        _skillTalentRect.rect.width));
                }
                _buffCards[i].BuffRT.gameObject.SetActive(true);
                _buffCards[i].UpdateBuffCardMessage(buffs[i]);
            }
            for (int i = buffCount; i < _buffCards.Count; i++)
                _buffCards[i].BuffRT.gameObject.SetActive(false);
        }

        private void ShowAttackRangeAttributes(List<Vector2Int> baseRange)
        {
            if (baseRange == null || baseRange.Count == 0)
                return;

            int maxX = int.MinValue;
            int maxY = int.MinValue;
            int minX = int.MaxValue;
            int minY = int.MaxValue;
            var range = new (int x, int y)[baseRange.Count];
            for (int i = 0; i < range.Length; i++)
            {
                range[i] = (baseRange[i].y, -baseRange[i].x);
                maxX = Mathf.Max(maxX, range[i].x);
                maxY = Mathf.Max(maxY, range[i].y);
                minX = Mathf.Min(minX, range[i].x);
                minY = Mathf.Min(minY, range[i].y);
            }

            float tileWidth = _rangeArea.rect.width / (maxX - minX + 1);
            float tileHeight = _rangeArea.rect.height / (maxY - minY + 1);
            float tileSize = Mathf.Min(15, tileWidth, tileHeight);
            float centerX = (minX + maxX) / 2f;
            float centerY = (minY + maxY) / 2f;
            while (_rangeTiles.Count < range.Length)
            {
                _rangeTiles.Add(Object.Instantiate(
                    _rangeTiles[0].gameObject,
                    _rangeArea).GetComponent<RectTransform>());
            }
            for (int i = range.Length; i < _rangeTiles.Count; i++)
                _rangeTiles[i].gameObject.SetActive(false);
            for (int i = 0; i < range.Length; i++)
            {
                _rangeTiles[i].sizeDelta = Vector2.one * tileSize * 0.95f;
                _rangeTiles[i].anchoredPosition = new Vector2(
                    tileSize * (range[i].x - centerX),
                    tileSize * (range[i].y - centerY));
                _rangeTiles[i].gameObject.SetActive(true);
            }
            _rangeSelfTile.sizeDelta = Vector2.one * tileSize * 0.95f;
            _rangeSelfTile.anchoredPosition = new Vector2(
                -tileSize * centerX,
                -tileSize * centerY);
        }
    }
}
