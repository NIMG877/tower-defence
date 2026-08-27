using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MyUI
{
    /// <summary>
    /// Data and view behavior for one deployable entity selector.
    /// </summary>
    internal sealed class LevelMessagePlaceData
    {
        private readonly LevelMessageDeploymentModule _owner;
        private readonly GameObject _selectorRoot;
        private readonly RectTransform _selectorRect;
        private readonly Image _photoImage;
        private readonly Image _classImage;
        private readonly Image _respawnRate;
        private readonly GameObject _respawn;
        private readonly TextMeshProUGUI _costText;
        private readonly TextMeshProUGUI _countText;
        private readonly TextMeshProUGUI _respawnRateText;
        private readonly EventTrigger _trigger;

        private float _respawnTimer;
        private const float SelectorYAnchor = -60;
        private int _deployCount;
        private int _remainingCount;

        public EntityID EntityId { get; private set; }
        public EntityData EntityData { get; private set; }
        public EntityStats EntityStats { get; private set; }
        public EntityVision EntityVision { get; private set; }
        public bool IsAffordable { get; private set; }

        public LevelMessagePlaceData(LevelMessageDeploymentModule owner, GameObject selector)
        {
            _owner = owner;
            _selectorRoot = selector;
            _selectorRect = selector.GetComponent<RectTransform>();
            _photoImage = selector.transform.Find("photo").GetComponent<Image>();
            _classImage = selector.transform.Find("head/class").GetComponent<Image>();
            _costText = selector.transform.Find("head/cost").GetComponent<TextMeshProUGUI>();
            _countText = selector.transform.Find("count").GetComponent<TextMeshProUGUI>();
            _respawn = selector.transform.Find("respawn").gameObject;
            _respawnRate = _respawn.transform.Find("rate").GetComponent<Image>();
            _respawnRateText = _respawn.transform.Find("rateText").GetComponent<TextMeshProUGUI>();
            _trigger = _photoImage.GetComponent<EventTrigger>();
        }

        public void Initialize(EntityID staticId, int number)
        {
            EntityId = staticId;
            EntityData = GameDataService.EntityRepository.Get(staticId);

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
            _remainingCount = number;
            IsAffordable = false;
            _respawn.SetActive(false);
            UpdateCountText();
            _selectorRoot.SetActive(true);
            _photoImage.sprite = EntityData.HeadImage;
            _classImage.sprite = _owner.ProfessionSprites[EntityData.CharacterJob];
            BindEvents();
        }

        public int CalculateCost()
        {
            if (EntityData.RespawnCostUp <= 0)
                return EntityData.Cost;

            float multiplier = 1 + EntityData.RespawnCostUp / 100;
            if (_deployCount == 0)
                return EntityData.Cost;
            if (_deployCount == 1)
                return (int)(EntityData.Cost * multiplier);
            return (int)(EntityData.Cost * multiplier * multiplier);
        }

        public void DeltaNum(int delta)
        {
            _remainingCount += delta;
            if (_remainingCount < 0)
                _remainingCount = 0;
            UpdateCountText();
            _selectorRoot.SetActive(_remainingCount > 0);
        }

        public void SelectorMove(bool up)
        {
            Vector2 position = _selectorRect.anchoredPosition;
            float targetY = up ? SelectorYAnchor + 10 : SelectorYAnchor;
            DOTween.To(value =>
            {
                position.y = value;
                _selectorRect.anchoredPosition = position;
            }, position.y, targetY, 0.1f).SetUpdate(true).SetId("LevelMessagePanel");
        }

        public void SetNum(int number)
        {
            DeltaNum(-number);
            _deployCount++;
            if (EntityData.RespawnStrategy == 0 ||
                EntityData.RespawnStrategy == 2 && _remainingCount == 0)
            {
                StartRespawnCooldown();
            }
        }

        public void CallBackNum(int number)
        {
            DeltaNum(number);
            if (EntityData.RespawnStrategy == 1)
                StartRespawnCooldown();
        }

        public void StartRespawnCooldown()
        {
            _respawnTimer = EntityData.RespawnTime;
            _respawn.SetActive(true);
            UpdateRespawnVisual();
        }

        public void Tick(float deltaTime)
        {
            if (_respawnTimer > 0)
            {
                _respawnTimer = Mathf.Max(0, _respawnTimer - deltaTime);
                UpdateRespawnVisual();
            }
            UpdateAffordability();
        }

        public void UpdateAffordability()
        {
            int cost = CalculateCost();
            _costText.text = cost.ToString();
            IsAffordable =
                _respawnTimer <= 0 &&
                cost <= LevelResourceManager.Manager.CostMessage.currentCost &&
                LevelResourceManager.Manager.CanSetNumLeft - EntityData.MaxOccupyCount >= 0;
            _photoImage.color = IsAffordable ? Color.white : Color.gray;
        }

        private void UpdateCountText()
        {
            _countText.text = _remainingCount > 1 ? $"��{_remainingCount}" : string.Empty;
        }

        private void UpdateRespawnVisual()
        {
            float totalTime = EntityData.RespawnTime;
            _respawn.SetActive(_respawnTimer > 0);
            _respawnRateText.text = _respawnTimer.ToString("0.0");
            _respawnRate.fillAmount = totalTime > 0
                ? 1 - _respawnTimer / totalTime
                : 1;
        }

        private void BindEvents()
        {
            _trigger.triggers.Clear();
            AddTrigger(EventTriggerType.PointerClick, _ => _owner.HandleSelectorClick(this));
            AddTrigger(EventTriggerType.BeginDrag, _ => _owner.HandleSelectorBeginDrag(this));
            AddTrigger(EventTriggerType.Drag, _ => _owner.UpdateDragger());
            AddTrigger(EventTriggerType.EndDrag, _ => _owner.HandleSelectorEndDrag(this));
        }

        private void AddTrigger(EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> callback)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(callback);
            _trigger.triggers.Add(entry);
        }
    }

    /// <summary>
    /// Owns selector inventory, placement validation, drag placement, and orientation selection.
    /// </summary>
    internal sealed class LevelMessageDeploymentModule
    {
        private readonly LevelMessageSelectionContext _context;
        private readonly Camera _camera;
        private readonly System.Func<LevelMessageUIState> _getState;
        private readonly System.Action<LevelMessagePlaceData> _showPreview;
        private readonly System.Action<LevelMessagePlaceData> _startSetting;
        private readonly System.Action<LevelMessagePlaceData> _startChoosing;
        private readonly System.Action _returnToNormal;
        private readonly System.Action<bool> _showNormalRange;

        private readonly List<LevelMessagePlaceData> _placeDataList = new List<LevelMessagePlaceData>();
        private readonly List<GameObject> _selectorObjects = new List<GameObject>();
        private readonly HashSet<(int i, int j)> _canSetBlocks = new HashSet<(int i, int j)>();
        private readonly Transform _content;
        private readonly GameObject _selectorSample;
        private readonly Image _chooser;
        private readonly Image _target;
        private readonly Image _up;
        private readonly Image _right;
        private readonly Image _down;
        private readonly Image _left;

        private bool[,] _higherCanSetBlock;
        private bool[,] _lowerCanSetBlock;
        private bool[,] _staticEntityExistBlock;
        private int _mapRows;
        private int _mapColumns;
        private bool _inChooser;
        private bool _draggerOpen;
        private bool _chooserOpen;
        private bool _canSetOpen;
        private bool _canSetDirty = true;
        private int _cachedCanSetType = int.MinValue;

        public Sprite[] ProfessionSprites { get; }

        public LevelMessageDeploymentModule(
            GameObject root,
            LevelMessageSelectionContext context,
            Camera camera,
            System.Func<LevelMessageUIState> getState,
            System.Action<LevelMessagePlaceData> showPreview,
            System.Action<LevelMessagePlaceData> startSetting,
            System.Action<LevelMessagePlaceData> startChoosing,
            System.Action returnToNormal,
            System.Action<bool> showNormalRange)
        {
            _context = context;
            _camera = camera;
            _getState = getState;
            _showPreview = showPreview;
            _startSetting = startSetting;
            _startChoosing = startChoosing;
            _returnToNormal = returnToNormal;
            _showNormalRange = showNormalRange;

            ProfessionSprites = Resources.LoadAll<Sprite>(
                "Prefabs/UI/MyUIs/UISprites/CharacterHandSprites/atlas_profession_v2");
            _selectorSample = LevelMessageViewLookup.Get<Transform>(
                root, "staticEntityArea/content/ses0").gameObject;
            _content = LevelMessageViewLookup.Get<Transform>(root, "staticEntityArea/content");
            _chooser = LevelMessageViewLookup.Get<Image>(root, "leftMessageArea/chooser");
            _target = LevelMessageViewLookup.Get<Image>(root, "leftMessageArea/target");
            _up = LevelMessageViewLookup.Get<Image>(root, "leftMessageArea/target/up");
            _right = LevelMessageViewLookup.Get<Image>(root, "leftMessageArea/target/right");
            _down = LevelMessageViewLookup.Get<Image>(root, "leftMessageArea/target/down");
            _left = LevelMessageViewLookup.Get<Image>(root, "leftMessageArea/target/left");
            BindChooserEvents();
        }

        public void OnEnter(EntityID[] ids, int[] numbers)
        {
            (_mapRows, _mapColumns) = MapDataManager.Manager.MapSize;
            _inChooser = false;
            _draggerOpen = false;
            _chooserOpen = false;
            _canSetOpen = false;
            _target.gameObject.SetActive(false);
            _chooser.gameObject.SetActive(false);
            SetOrientationArrow(-1);
            _canSetDirty = true;
            _cachedCanSetType = int.MinValue;
            InitializeSelectors(ids, numbers);
        }

        public void OnExit()
        {
            ResetCanSetBlockColors();
            _canSetBlocks.Clear();
            _placeDataList.Clear();
            _context.SetOrientation(-1);
            _context.ClearPreview();
        }

        public void OnPause()
        {
            ResetCanSetBlockColors();
            _canSetBlocks.Clear();
            _canSetOpen = false;
            _canSetDirty = true;
        }

        public void OnResume()
        {
            _context.SelectedPlaceData?.SelectorMove(true);
        }

        public void InitializeSelectors(EntityID[] idList, int[] numbers)
        {
            ValidateSelectorInput(idList, numbers);
            _placeDataList.Clear();
            var poolDictionary = new Dictionary<EntityID, int>(idList.Length);
            for (int i = 0; i < idList.Length; i++)
                poolDictionary[idList[i]] = numbers[i];
            EntityPoolManager.Manager.CreateOrExpandEntityPool(poolDictionary);

            int existingSelectors = _selectorObjects.Count;
            for (int i = 0; i < idList.Length; i++)
            {
                if (i >= existingSelectors)
                    _selectorObjects.Add(Object.Instantiate(_selectorSample, _content));

                var placeData = new LevelMessagePlaceData(this, _selectorObjects[i]);
                placeData.Initialize(idList[i], numbers[i]);
                _placeDataList.Add(placeData);
            }
            for (int i = idList.Length; i < existingSelectors; i++)
                _selectorObjects[i].SetActive(false);
            MarkCanSetDirty();
        }

        public void AddSelectors(EntityID[] idList, int[] numbers)
        {
            ValidateSelectorInput(idList, numbers);
            for (int i = 0; i < idList.Length; i++)
            {
                LevelMessagePlaceData existing = null;
                for (int j = 0; j < _placeDataList.Count; j++)
                {
                    if (_placeDataList[j].EntityId == idList[i])
                    {
                        existing = _placeDataList[j];
                        break;
                    }
                }

                if (existing != null)
                {
                    existing.DeltaNum(numbers[i]);
                    continue;
                }

                int selectorIndex = _placeDataList.Count;
                if (selectorIndex >= _selectorObjects.Count)
                    _selectorObjects.Add(Object.Instantiate(_selectorSample, _content));

                var placeData = new LevelMessagePlaceData(this, _selectorObjects[selectorIndex]);
                placeData.Initialize(idList[i], numbers[i]);
                _placeDataList.Add(placeData);
            }
            MarkCanSetDirty();
        }

        public void EntityBackToSelector(Entity entity)
        {
            if (entity == null || !entity.EntityData.CanRespawn)
                return;

            for (int i = 0; i < _placeDataList.Count; i++)
            {
                if (_placeDataList[i].EntityId != entity.EntityData.ID)
                    continue;

                _placeDataList[i].CallBackNum(1);
                MarkCanSetDirty();
                return;
            }
            AddSelectors(new[] { entity.EntityData.ID }, new[] { 1 });
        }

        public void Tick(float deltaTime)
        {
            for (int i = 0; i < _placeDataList.Count; i++)
                _placeDataList[i].Tick(deltaTime);
            UpdateCanSet();
        }

        public void RefreshAffordability()
        {
            for (int i = 0; i < _placeDataList.Count; i++)
                _placeDataList[i].UpdateAffordability();
        }

        public void DeselectCurrentPlaceData()
        {
            _context.SelectedPlaceData?.SelectorMove(false);
        }

        public void ShowDragger(bool show)
        {
            if (show)
            {
                if (!_draggerOpen)
                {
                    _draggerOpen = true;
                    _target.gameObject.SetActive(true);
                }
                return;
            }

            if (!_draggerOpen)
                return;
            _draggerOpen = false;
            _target.gameObject.SetActive(false);
            SetOrientationArrow(-1);
        }

        public void ShowChooser(bool show)
        {
            if (show)
            {
                if (!_chooserOpen)
                {
                    _chooserOpen = true;
                    _chooser.gameObject.SetActive(true);
                }
                _chooser.transform.position = _target.transform.position;
                _context.SetPreview(_chooser.transform.position);
                return;
            }

            if (_chooserOpen)
            {
                _chooserOpen = false;
                _chooser.gameObject.SetActive(false);
            }
            _context.ClearPreview();
        }

        public void ShowCanSet(bool show)
        {
            if (show)
            {
                bool reopening = !_canSetOpen;
                _canSetOpen = true;
                int canSetType = GetSelectedCanSetType();
                if (canSetType != _cachedCanSetType)
                    _canSetDirty = true;
                if (_canSetDirty)
                    UpdateCanSet();
                else if (reopening)
                    ColorBlockList(new Color(0, 0.4f, 0));
                return;
            }

            if (!_canSetOpen)
                return;
            _canSetOpen = false;
            ResetCanSetBlockColors();
        }

        public void UpdateCanSet()
        {
            if (!_canSetOpen)
                return;
            int canSetType = GetSelectedCanSetType();
            if (canSetType != _cachedCanSetType)
                _canSetDirty = true;
            if (!_canSetDirty)
                return;
            FetchMapEntityData(canSetType);
            ResetCanSetBlockColors();
            _canSetBlocks.Clear();

            System.Func<int, int, bool> predicate = GetCanSetPredicate(canSetType);
            if (predicate != null)
            {
                for (int i = 0; i < _mapRows; i++)
                {
                    for (int j = 0; j < _mapColumns; j++)
                    {
                        if (predicate(i, j))
                            _canSetBlocks.Add((i, j));
                    }
                }
            }
            ColorBlockList(new Color(0, 0.4f, 0));
            _canSetDirty = false;
            _cachedCanSetType = canSetType;
        }

        public void UpdateDragger()
        {
            if (_getState() != LevelMessageUIState.Setting)
                return;

            Vector3 pointer = _camera.ScreenToWorldPoint(Input.mousePosition);
            (int i, int j) block = ((int)(pointer.y + 0.5), (int)(pointer.x + 0.5));
            if (_canSetBlocks.Contains(block))
            {
                _target.transform.position = new Vector2(block.j, block.i);
                if (_context.SelectedPlaceData.EntityData.NeedsDirectionSelection)
                {
                    _target.color = Color.yellow;
                }
                else
                {
                    _target.color = Color.green;
                    _context.SetOrientation(0);
                    ShowChooser(true);
                    _showNormalRange(true);
                }
            }
            else
            {
                if (_target.color == Color.green)
                {
                    ShowChooser(false);
                    _showNormalRange(false);
                }
                _target.transform.position = new Vector2(pointer.x, pointer.y);
                _target.color = Color.red;
            }
        }

        public void ConfirmPlacementOrResetTarget()
        {
            if (_context.Orientation != -1)
            {
                Entity entity = EntityManager.Manager.SetStaticEntity(
                    _context.SelectedPlaceData.EntityId,
                    _chooser.transform.position,
                    1,
                    _context.Orientation);
                int cost = _context.SelectedPlaceData.CalculateCost();
                entity.GetComponent<InteractableStatic>().CurrentSetCost = cost;
                LevelResourceManager.Manager.ChangeCost(-cost);
                _context.SelectedPlaceData.SelectorMove(false);
                _context.SelectedPlaceData.SetNum(1);
                _chooser.color = new Color(1, 1, 0, 0.4f);
                _canSetDirty = true;
                _returnToNormal();
            }
            else
            {
                _target.transform.DOMove(_chooser.transform.position, 0.2f)
                    .SetUpdate(true)
                    .SetId("LevelMessagePanel");
            }
        }

        public void HandleSelectorClick(LevelMessagePlaceData placeData)
        {
            if (!_context.SelectedStaticEntityID.HasValue)
            {
                _showPreview(placeData);
                placeData.SelectorMove(true);
            }
            else if (_context.SelectedPlaceData == placeData)
            {
                _returnToNormal();
                placeData.SelectorMove(false);
            }
            else
            {
                _context.SelectedPlaceData?.SelectorMove(false);
                _showPreview(placeData);
                placeData.SelectorMove(true);
            }
        }

        public void HandleSelectorBeginDrag(LevelMessagePlaceData placeData)
        {
            if (!_context.SelectedStaticEntityID.HasValue)
            {
                placeData.SelectorMove(true);
            }
            else if (_context.SelectedPlaceData != placeData)
            {
                _context.SelectedPlaceData?.SelectorMove(false);
                placeData.SelectorMove(true);
            }

            if (placeData.IsAffordable)
                _startSetting(placeData);
            else
                _showPreview(placeData);
        }

        public void HandleSelectorEndDrag(LevelMessagePlaceData placeData)
        {
            if (_getState() != LevelMessageUIState.Setting)
                return;

            if (_target.color == Color.yellow)
            {
                _startChoosing(placeData);
            }
            else if (_target.color == Color.green)
            {
                ConfirmPlacementOrResetTarget();
            }
            else
            {
                _showPreview(placeData);
            }
        }

        private void BindChooserEvents()
        {
            EventTrigger trigger = _chooser.GetComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.PointerEnter, _ => _inChooser = true);
            AddTrigger(trigger, EventTriggerType.PointerExit, _ => _inChooser = false);
            AddTrigger(trigger, EventTriggerType.Drag, _ => UpdateChooser());
            AddTrigger(trigger, EventTriggerType.EndDrag, _ => ConfirmPlacementOrResetTarget());
        }

        private static void AddTrigger(
            EventTrigger trigger,
            EventTriggerType type,
            UnityEngine.Events.UnityAction<BaseEventData> callback)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(callback);
            trigger.triggers.Add(entry);
        }

        private void UpdateChooser()
        {
            Vector3 pointer = _camera.ScreenToWorldPoint(Input.mousePosition);
            _target.transform.position = new Vector2(pointer.x, pointer.y);
            if (!_inChooser)
            {
                Vector2 delta = pointer - _chooser.transform.position;
                int orientation = ResolveChooserOrientation(delta);
                if (orientation != -1 && _context.Orientation != orientation)
                {
                    _context.SetOrientation(orientation);
                    SetOrientationArrow(orientation);
                    _showNormalRange(true);
                }
                _chooser.color = new Color(0, 1, 0, 0.4f);
                _target.color = Color.green;
            }
            else
            {
                _context.SetOrientation(-1);
                SetOrientationArrow(-1);
                _chooser.color = new Color(1, 1, 0, 0.4f);
                _target.color = Color.yellow;
                _showNormalRange(false);
            }
        }

        private static int ResolveChooserOrientation(Vector2 delta)
        {
            if (delta.y > delta.x)
                return delta.y > -delta.x ? 0 : 3;
            return delta.y > -delta.x ? 1 : 2;
        }

        private void SetOrientationArrow(int orientation)
        {
            _up.enabled = orientation == 0;
            _right.enabled = orientation == 1;
            _down.enabled = orientation == 2;
            _left.enabled = orientation == 3;
        }

        private void FetchMapEntityData(int canSetType)
        {
            _staticEntityExistBlock = EntityManager.Manager.StaticEntityExistBlock;
            if (canSetType == 0 || canSetType == 2)
                _lowerCanSetBlock = MapDataManager.Manager.LowerCanSetBlock;
            if (canSetType == 1 || canSetType == 2)
                _higherCanSetBlock = MapDataManager.Manager.HigherCanSetBlock;
        }

        private System.Func<int, int, bool> GetCanSetPredicate(int canSetType)
        {
            switch (canSetType)
            {
                case 0:
                    return (i, j) => _lowerCanSetBlock[i, j] && !_staticEntityExistBlock[i, j];
                case 1:
                    return (i, j) => _higherCanSetBlock[i, j] && !_staticEntityExistBlock[i, j];
                case 2:
                    return (i, j) =>
                        (_lowerCanSetBlock[i, j] || _higherCanSetBlock[i, j]) &&
                        !_staticEntityExistBlock[i, j];
                default:
                    return null;
            }
        }

        private void MarkCanSetDirty()
        {
            _canSetDirty = true;
        }

        private static void ValidateSelectorInput(EntityID[] idList, int[] numbers)
        {
            if (idList == null)
                throw new System.ArgumentNullException(nameof(idList));
            if (numbers == null)
                throw new System.ArgumentNullException(nameof(numbers));
            if (idList.Length != numbers.Length)
            {
                throw new System.ArgumentException(
                    "Entity ID and selector count arrays must have the same length.");
            }
        }

        private int GetSelectedCanSetType()
        {
            return _context.SelectedPlaceData != null
                ? _context.SelectedPlaceData.EntityData.CanSetType
                : 0;
        }

        private void ResetCanSetBlockColors()
        {
            foreach ((int i, int j) block in _canSetBlocks)
            {
                ref Material material = ref MapDataManager.Manager.GetMaterialRef(
                    block.i,
                    block.j);
                if (material != null)
                    material.color = Color.white;
            }
        }

        private void ColorBlockList(Color color)
        {
            foreach ((int i, int j) block in _canSetBlocks)
            {
                ref Material material = ref MapDataManager.Manager.GetMaterialRef(
                    block.i,
                    block.j);
                if (material != null)
                    material.color = color;
            }
        }
    }
}
