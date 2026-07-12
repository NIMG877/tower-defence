using UnityEngine;


/// <summary>
/// 实体协调者。持有 4 个 POCO 子系统（Stats/Vision/Movement/Combat）+ 阵营副作用 + 公开事件。
/// 数据访问统一走子系统入口：<see cref="Stats"/> / <see cref="Vision"/> / <see cref="Movement"/> / <see cref="Combat"/>。
/// </summary>
public class Entity : MonoBehaviour, IPoolOperation
{
    public EntityData EntityData;
    public AnimationMachine entityAM;
    [HideInInspector] public BuffController buffController;
    [HideInInspector] public AttackBase AttackBase;
    [HideInInspector] public MoveBase MoveBase;
    public InteractableStatic InteractableStatic;

    // === 子系统持有 ===
    private EntityStats _stats;
    private AttributeStore _attributeStore;
    private EntityVision _vision;
    private EntityMovement _movement;
    private EntityCombat _combat;
    private EntityAbilityRunner _skillRunner;

    private int _camp;

    /// <summary>属性子系统：HP/防御/法抗/闪避/格位/嘲讽/状态标志。</summary>
    public EntityStats Stats { get { return _stats; } }
    /// <summary>视野子系统：BaseRange/Range/Radius/朝向镜像/视野内实体列表。</summary>
    public EntityVision Vision { get { return _vision; } }
    /// <summary>移动子系统：Position/InBlocks/ResistList/优先级/阵营镜像。</summary>
    public EntityMovement Movement { get { return _movement; } }
    /// <summary>战斗子系统：EntityUpdate/PriorityOrder。</summary>
    public EntityCombat Combat { get { return _combat; } }
    /// <summary>Ability 子系统：AbilityRuntime 列表 + 事件桥 + SP/组件 tick。</summary>
    public EntityAbilityRunner AbilityRunner { get { return _skillRunner; } }

    public string NAME { get { return EntityData.ChineseName; } }

    /// <summary>
    /// 阵营。setter 留 Entity 根（带 EntityManager 块/列表 churn 副作用）；getter 直接读 _camp。
    /// </summary>
    public int Camp
    {
        get { return _camp; }
        set
        {
            if (_camp != value)
            {
                EntityManager.Manager.RemoveEntityFromBlock(Movement.InBlocks, this, _camp);
                if (EntityManager.Manager.RemoveEntityFromList(this, _camp))
                {
                    EntityManager.Manager.AddEntityToList(this, value);
                }
                _camp = value;
                EntityManager.Manager.AddEntityToBlock(Movement.InBlocks, this, _camp);
            }
        }
    }

    /// <summary>
    public EntityPool thisEntityPool;
    [SerializeField] public Transform TempContainer;
    /// <summary>
    /// 朝向 0-up 1-right 2-down 3-left。setter 走 <see cref="SetOrientation"/>（带 Vision 同步副作用）。
    /// </summary>
    protected int _orientation;
    /// <summary>
    /// 当前朝向（0-up 1-right 2-down 3-left）。仅供 UI 侧读取（如技能攻击范围预览需要
    /// entity 的当前朝向喂给 <c>MapDataManager.RangeCaculator</c>）。写仍走
    /// <see cref="SetOrientation"/>。
    /// </summary>
    public int Orientation => _orientation;

    #region//委托事件
    public delegate void OperationsBeforeHurt(Entity origin, ref float damage, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int damageType, int applyType);
    /// <summary>在受到伤害之前调用。</summary>
    public event OperationsBeforeHurt OnBeforeHurt;
    public delegate void OperationsAfterHurt(Entity origin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly);
    /// <summary>在受到伤害之后调用。</summary>
    public event OperationsAfterHurt OnAfterHurt;
    public delegate void OperationsBeforeDieAnimation();
    /// <summary>在播放死亡动画之前调用。</summary>
    public event OperationsBeforeDieAnimation OnBeforeDieAnimation;
    #endregion

    // === 事件桥（供子系统触发，public 事件 API 不变） ===
    internal void RaiseOnBeforeHurt(Entity origin, ref float damage, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int damageType, int applyType)
    { OnBeforeHurt?.Invoke(origin, ref damage, ref multiplyer, ref defPenetrate, ref mgrPenetrate, ref defPenetrate_value, ref mgrPenetrate_value, ref damageType, applyType); }
    internal void RaiseOnAfterHurt(Entity origin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly)
    { OnAfterHurt?.Invoke(origin, damage, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, applyType, isDeadly); }
    internal void RaiseOnBeforeDieAnimation()
    { OnBeforeDieAnimation?.Invoke(); }

    protected void FixedUpdate()
    {
        if (!Stats.IsActive)
            return;
        Stats.RecoverTick();
        Stats.CheckDeath();
        Vision.Refresh();
        if (_skillRunner != null) _skillRunner.Tick(Time.fixedDeltaTime);
    }

    public void Die()
    {
        Stats.BeginDie();
        entityAM.TrySetState(EntityState.Die, false);
        if (Camp == 1)
        {
            if (EntityData.ID.ID_C == "t")
            {
                AudioManager.Manager.PlayAudio("token_die", 1, false, false);
            }
            else
            {
                AudioManager.Manager.PlayAudio("char_die", 1, false, false);
            }
        }
        else
        {
            AudioManager.Manager.PlayAudio("enemy_die", 1, false, false);
        }
    }
    /// <summary>
    /// 设置朝向
    /// </summary>
    /// <param name="orientation">0-up 1-right 2-down 3-left</param>
    public void SetOrientation(int orientation)
    {
        _orientation = orientation;
        Vision.SetOrientation(orientation);
    }
    public virtual void PreWarm()
    {
        if (this.TryGetComponent(out AnimationMachine aM))
        {
            entityAM = aM;
        }
        else
        {
            Debug.LogWarning("未绑定动画状态机");
        }
        if (this.TryGetComponent(out BuffController bF))
        {
            buffController = bF;
        }
        else
        {
            Debug.LogWarning("未绑定Buff控制器");
        }
        if (this.TryGetComponent(out AttackBase ab))
        {
            AttackBase = ab;
        }
        else
        {
            Debug.LogWarning("未绑定攻击模块");
        }
        if (this.TryGetComponent(out MoveBase mb))
        {
            MoveBase = mb;
        }
        else if (this.TryGetComponent(out InteractableStatic iS))
        {
            InteractableStatic = iS;
        }
        else
        {
            Debug.LogWarning("未绑定可移动或交互静止模块");
        }

        // === 构造子系统，注入依赖 ===
        _attributeStore = new AttributeStore();
        _stats = new EntityStats(this);
        _stats.Bind(_attributeStore);
        if (buffController != null) buffController.Bind(_attributeStore);
        _vision = new EntityVision(this);
        _movement = new EntityMovement(this);
        _combat = new EntityCombat(this);
        _skillRunner = new EntityAbilityRunner(this);

        _vision.InitializeFromData(EntityData);
        Stats.AttributesCaculateFirst(EntityData);
        Movement.Initialize();

        _skillRunner.PreWarm();
    }

    public virtual void Initialize()
    {
        entityAM.TrySetState(EntityState.Start, false);
        Stats.ResetState();
        TempContainer = new GameObject("TempContainer").transform;
        TempContainer.position = this.transform.position;
        TempContainer.parent = this.transform;
        if (Camp == 2)
        {
            LevelActionManager.Manager.AddToWaveEntities(this);
        }
        bool canmove = MoveBase;
        bool camp2 = Camp == 2;
        SlidersManager.Manager.SetSlider<HpSliderController>(this, 4, camp2 ? 0 : 1, 0, camp2, canmove);
        if (EntityData != null && EntityData.Skills != null && EntityData.Skills.Count >= 1)
        {
            SlidersManager.Manager.SetSlider<SpSliderController>(this, 10, camp2 ? 3 : 4, 1, camp2, canmove);
        }

        _skillRunner.OnInitialize();
    }

    public virtual void Dormancy()
    {
        Movement.ResistList.Clear();
        Vision.ClearLists();

        //清空事件注册
        OnBeforeHurt = null;
        OnAfterHurt = null;
        OnBeforeDieAnimation = null;
        EntityManager.Manager.RemoveEntityFromList(this, Camp);
        EntityManager.Manager.RemoveEntityFromBlock(Movement.InBlocks, this, Camp);
        Movement.ClearInBlocks();
        Destroy(TempContainer.gameObject);
        TempContainer = null;
        if (Camp == 2)
        {
            LevelActionManager.Manager.RemoveFromWaveEntities(this);
        }

        _skillRunner.OnTeardown();
    }
}
