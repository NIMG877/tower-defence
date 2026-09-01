using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MyUI
{
    internal enum LevelMessageUIState
    {
        Normal,
        ViewBeforeSet,
        Setting,
        Choosing,
        ViewAfterSet,
    }

    /// <summary>
    /// The small piece of state shared by deployment and selected-entity presentation.
    /// State transitions are owned by <see cref="LevelMessagePanel"/>.
    /// </summary>
    internal sealed class LevelMessageSelectionContext
    {
        public LevelMessagePlaceData SelectedPlaceData { get; private set; }
        public Entity SelectedEntity { get; private set; }
        public EntityID? SelectedStaticEntityID { get; private set; }
        public int Orientation { get; private set; } = -1;
        public Vector2 PreviewPosition { get; private set; }
        public bool HasPreviewPosition { get; private set; }

        public void SelectPlace(LevelMessagePlaceData placeData)
        {
            SelectedEntity = null;
            SelectedPlaceData = placeData;
            SelectedStaticEntityID = placeData.EntityId;
            Orientation = -1;
            PreviewPosition = default;
            HasPreviewPosition = false;
        }

        public void SelectEntity(EntityID entityId, Entity entity)
        {
            SelectedPlaceData = null;
            SelectedEntity = entity;
            SelectedStaticEntityID = entityId;
            Orientation = -1;
            PreviewPosition = default;
            HasPreviewPosition = false;
        }

        public void Clear()
        {
            SelectedPlaceData = null;
            SelectedEntity = null;
            SelectedStaticEntityID = null;
            Orientation = -1;
            PreviewPosition = default;
            HasPreviewPosition = false;
        }

        public void SetOrientation(int orientation)
        {
            Orientation = orientation;
        }

        public void SetPreview(Vector2 position)
        {
            PreviewPosition = position;
            HasPreviewPosition = true;
        }

        public void ClearPreview()
        {
            PreviewPosition = default;
            HasPreviewPosition = false;
        }
    }

    internal static class LevelMessageViewLookup
    {
        public static T Get<T>(GameObject root, string path) where T : Component
        {
            Transform child = root.transform.Find(path);
            if (child == null)
                throw new MissingReferenceException($"LevelMessagePanel child path not found: {path}");
            T component = child.GetComponent<T>();
            if (component == null)
            {
                throw new MissingComponentException(
                    $"LevelMessagePanel child '{path}' has no component {typeof(T).Name}");
            }
            return component;
        }
    }

    /// <summary>
    /// Coordinates the level HUD modules and preserves the panel's existing public API.
    /// Business behavior lives in four cohesive modules: deployment, entity, HUD, and combat.
    /// </summary>
    public class LevelMessagePanel : BasePanel
    {
        private static LevelMessagePanel _instance;

        private readonly Camera _camera;
        private readonly LevelMessageSelectionContext _selection;
        private readonly LevelMessageDeploymentModule _deployment;
        private readonly LevelMessageEntityModule _entity;
        private readonly LevelMessageHudModule _hud;
        private readonly LevelMessageCombatModule _combat;

        private LevelMessageUIState _currentState;
        private bool _updateLoopRunning;
        private int _updateLoopGeneration;

        public static LevelMessagePanel Panel
        {
            get
            {
                if (_instance == null)
                    _instance = new LevelMessagePanel();
                return _instance;
            }
        }

        public DamageStatisticData[] DamageStatisticDatas => _combat.DamageStatistics;

        private LevelMessagePanel() : base(new UIType("Prefabs/UI/MyUIs/LevelMessagePanel"))
        {
            _camera = LevelResourceSharing.MainCamera;
            _selection = new LevelMessageSelectionContext();
            _hud = new LevelMessageHudModule(UIObject);
            _combat = new LevelMessageCombatModule(UIObject);
            _entity = new LevelMessageEntityModule(UIObject, _selection, SwitchToNormal);
            _deployment = new LevelMessageDeploymentModule(
                UIObject,
                _selection,
                _camera,
                () => _currentState,
                SwitchToViewBeforeSet,
                SwitchToSetting,
                SwitchToChoosing,
                SwitchToNormal,
                _entity.ShowNormalRange);
            BindPanelSelectionEvent();
        }

        #region Existing public API

        public void InitializeStaticEntityPrefabToSelector(EntityID[] idList, int[] nums)
        {
            _deployment.InitializeSelectors(idList, nums);
        }

        public void AddStaticEntityPrefabToSelector(EntityID[] idList, int[] nums)
        {
            _deployment.AddSelectors(idList, nums);
        }

        public void ShowText(Vector2 entityPos, int textType, int value)
        {
            _combat.ShowText(entityPos, textType, value);
        }

        public void EntityBackToSelector(Entity entityToBack)
        {
            _deployment.EntityBackToSelector(entityToBack);
        }

        public void AcceptDamageMessage(
            Entity target,
            Entity origin,
            float finalDamage,
            int damageType)
        {
            _combat.AcceptDamageMessage(target, origin, finalDamage, damageType);
        }

        public void CostTextUpDate()
        {
            _hud.UpdateCost();
            _deployment.RefreshAffordability();
        }

        public void CanSetNumUpDate()
        {
            _hud.UpdateCanSetNum();
            _deployment.RefreshAffordability();
        }

        public void LevelHpLeftTextUpdate()
        {
            _hud.UpdateLevelHp();
        }

        public void CurrentNumAndTotalNumUpdate()
        {
            _hud.UpdateOperateProgress();
        }

        public void ReSelectOrSetStaticEntity()
        {
            _deployment.ConfirmPlacementOrResetTarget();
        }

        #endregion

        #region Panel lifecycle

        public override void OnEnter()
        {
            base.OnEnter();

            var teamMembers = SaveSystem.GetTeamMembers(SaveSystem.CurrentTeamName);
            var teamSkillSelects = SaveSystem.GetTeamSkillSelects(SaveSystem.CurrentTeamName);
            var characters = new EntityID[teamMembers.Count];
            var numbers = new int[teamMembers.Count];
            var skillIndices = new int[teamMembers.Count];
            for (int i = 0; i < characters.Length; i++)
            {
                characters[i] = teamMembers[i];
                numbers[i] = 1;
                skillIndices[i] = teamSkillSelects[i];
            }

            _selection.Clear();
            _currentState = LevelMessageUIState.Normal;
            _combat.OnEnter(characters);
            _entity.OnEnter();
            _deployment.OnEnter(characters, numbers, skillIndices);
            _hud.OnEnter();
            ApplyStateView(LevelMessageUIState.Normal);
            StartUpdateLoop();
        }

        public override void OnExit()
        {
            base.OnExit();
            StopUpdateLoop();
            _combat.OnExit();
            _hud.OnExit();
            DOTween.Kill("LevelMessagePanel");
            SwitchToNormal();
            _deployment.OnExit();
            _selection.Clear();
        }

        public override void OnPause()
        {
            base.OnPause();
            StopUpdateLoop();
            _hud.OnPause();
            _combat.OnPause();
            DOTween.Kill("LevelMessagePanel");
            _deployment.OnPause();
        }

        public override void OnResume()
        {
            base.OnResume();
            _hud.OnResume();
            _deployment.OnResume();
            if (_currentState == LevelMessageUIState.ViewAfterSet &&
                (_selection.SelectedEntity == null || !_selection.SelectedEntity.Stats.IsActive))
            {
                SwitchToNormal();
            }
            else
            {
                ApplyStateView(_currentState);
            }
            StartUpdateLoop();
        }

        #endregion

        #region State coordination

        private void SwitchToNormal()
        {
            _hud.SetSlow(false);
            _currentState = LevelMessageUIState.Normal;
            _deployment.DeselectCurrentPlaceData();
            _selection.Clear();
            ApplyStateView(LevelMessageUIState.Normal);
        }

        private void SwitchToViewBeforeSet(LevelMessagePlaceData placeData)
        {
            EnterPoolPreviewState(LevelMessageUIState.ViewBeforeSet, placeData);
        }

        private void SwitchToSetting(LevelMessagePlaceData placeData)
        {
            EnterPoolPreviewState(LevelMessageUIState.Setting, placeData);
        }

        private void SwitchToChoosing(LevelMessagePlaceData placeData)
        {
            EnterPoolPreviewState(LevelMessageUIState.Choosing, placeData);
        }

        private void SwitchToViewAfterSet(EntityID entityId, Entity entity)
        {
            _hud.SetSlow(true);
            _selection.SelectEntity(entityId, entity);
            ApplyStateView(LevelMessageUIState.ViewAfterSet);
            EnterStateAndStartUpdates(LevelMessageUIState.ViewAfterSet);
        }

        private void EnterPoolPreviewState(
            LevelMessageUIState state,
            LevelMessagePlaceData placeData)
        {
            _hud.SetSlow(true);
            _selection.SelectPlace(placeData);
            ApplyStateView(state);
            EnterStateAndStartUpdates(state);
        }

        private void EnterStateAndStartUpdates(LevelMessageUIState state)
        {
            _currentState = state;
        }

        private void StartUpdateLoop()
        {
            _updateLoopRunning = true;
            int generation = ++_updateLoopGeneration;
            RunUpdateLoop(generation).Forget();
        }

        private void StopUpdateLoop()
        {
            _updateLoopRunning = false;
            _updateLoopGeneration++;
        }

        private async UniTask RunUpdateLoop(int generation)
        {
            while (_updateLoopRunning && generation == _updateLoopGeneration)
            {
                _hud.Tick();
                _deployment.Tick(Time.fixedDeltaTime);
                UpdateCurrentState();
                try
                {
                    await UniTask.WaitForFixedUpdate(LevelResourceSharing.LevelCtk);
                }
                catch (System.OperationCanceledException)
                {
                    return;
                }
            }
        }

        private void UpdateCurrentState()
        {
            switch (_currentState)
            {
                case LevelMessageUIState.ViewBeforeSet:
                case LevelMessageUIState.Setting:
                case LevelMessageUIState.Choosing:
                    _entity.UpdateLeftMessage();
                    break;
                case LevelMessageUIState.ViewAfterSet:
                    if (_selection.SelectedEntity == null ||
                        !_selection.SelectedEntity.Stats.IsActive)
                    {
                        SwitchToNormal();
                        return;
                    }
                    _entity.UpdateRange();
                    _entity.UpdateLeftMessage();
                    _entity.UpdateOperator();
                    break;
            }
        }

        private void ApplyStateView(LevelMessageUIState state)
        {
            bool leftMessage = state != LevelMessageUIState.Normal;
            bool operatorPanel = state == LevelMessageUIState.ViewAfterSet;
            bool dragger = state == LevelMessageUIState.Setting ||
                           state == LevelMessageUIState.Choosing;
            bool chooser = state == LevelMessageUIState.Choosing;
            bool range = state == LevelMessageUIState.ViewAfterSet;
            bool canSet = state == LevelMessageUIState.ViewBeforeSet ||
                          state == LevelMessageUIState.Setting ||
                          state == LevelMessageUIState.Choosing;

            _entity.ShowLeftMessage(leftMessage);
            _entity.ShowOperator(operatorPanel);
            _deployment.ShowDragger(dragger);
            _deployment.ShowChooser(chooser);
            _entity.ShowNormalRange(range);
            _deployment.ShowCanSet(canSet);
        }

        #endregion

        private void BindPanelSelectionEvent()
        {
            EventTrigger trigger = UIObject.GetComponent<EventTrigger>();
            var blankClick = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            blankClick.callback.AddListener(_ =>
            {
                if (_currentState != LevelMessageUIState.Normal)
                {
                    SwitchToNormal();
                    return;
                }

                Vector2 worldPosition = _camera.ScreenToWorldPoint(Input.mousePosition);
                (int i, int j) block =
                    ((int)(worldPosition.y + 0.5), (int)(worldPosition.x + 0.5));
                Entity entity = EntityManager.Manager.GetStaticEntityInBlock(block.i, block.j);
                if (entity != null && entity.Stats.IsActive)
                    SwitchToViewAfterSet(entity.EntityData.ID, entity);
            });
            trigger.triggers.Add(blankClick);
        }
    }
}
