using UnityEngine;

public enum DamageResolutionKind
{
    Damage,
    Healing,
    Dodged,
}

public readonly struct DamageResolution
{
    public Entity Target { get; }
    public Entity Origin { get; }
    public float Amount { get; }
    public int DamageType { get; }
    public int ApplyType { get; }
    public DamageResolutionKind Kind { get; }
    public bool IsCritical { get; }
    public bool IsDeadly { get; }

    public DamageResolution(
        Entity target,
        Entity origin,
        float amount,
        int damageType,
        int applyType,
        DamageResolutionKind kind,
        bool isCritical = false,
        bool isDeadly = false)
    {
        Target = target;
        Origin = origin;
        Amount = amount;
        DamageType = damageType;
        ApplyType = applyType;
        Kind = kind;
        IsCritical = isCritical;
        IsDeadly = isDeadly;
    }
}

/// <summary>
/// 实体协调者。持有 8 个 POCO 子系统（AttributeStore/EntityStats/EntityVision/EntityMovement/
/// EntityCombat/EntityAttack/EntityAbilityRunner/EntityStateMachine，构造点见本文件）+ 阵营副作用 + 公开事件。
/// 数据访问统一走子系统入口：<see cref="Stats"/> / <see cref="Vision"/> / <see cref="Movement"/> / <see cref="Combat"/> / <see cref="Attack"/> / <see cref="AbilityRunner"/> / <see cref="StateMachine"/>。
/// </summary>
public class Entity : MonoBehaviour, IPoolOperation
{
    public EntityData EntityData;
    public AnimationMachine entityAM;
    [HideInInspector] public EntityVisuals visuals;
    [HideInInspector] public EntityFacing facing;
    [HideInInspector] public BuffController buffController;
    private EntityAttack _attack;
    public EntityAttack Attack => _attack;

    // Test-only construction hook: EditMode tests compile in a separate assembly without InternalsVisibleTo.
    public EntityAttack CreateAttackForTests()
    {
        _attack = new EntityAttack(this);
        return _attack;
    }
    [HideInInspector] public MoveBase MoveBase;
    public InteractableStatic InteractableStatic;

    // === 子系统持有 ===
    private EntityStats _stats;
    private AttributeStore _attributeStore;
    private EntityVision _vision;
    private EntityMovement _movement;
    private EntityCombat _combat;
    private EntityAbilityRunner _skillRunner;
    private readonly EntityStateMachine _stateMachine = new EntityStateMachine();

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
    /// <summary>逻辑状态子系统：EntityState/AttackPhase/转换规则/ban 表（唯一状态真相源）。</summary>
    public EntityStateMachine StateMachine { get { return _stateMachine; } }

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

    public EntityPool thisEntityPool;
    /// <summary>实体池生命周期回调数组：CreateNewEntity 时扫描一次并缓存，
    /// 出池/入池派发 PreWarm/Initialize/Dormancy 时直接使用，避免每次 GetComponents。</summary>
    public IPoolOperation[] PoolOps { get; set; }
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

    /// <summary>
    /// 生效技能在 <see cref="EntityData"/>.Skills 中的索引（部署期实例状态，同 Camp/Orientation）。
    /// 池化实体跨部署复用（回收再部署），每次 CallOut 由部署方重新断言；变更须在休眠期走 <see cref="SetSelectedSkill"/>。
    /// </summary>
    public int SelectedSkillIndex;
    /// <summary>
    /// 设置生效技能。索引变化时重建技能 runtime（拆旧建新）；仅可在实体休眠期
    /// （CallOut 取出后、Initialize 前）调用，避免拆到运行中的能力。
    /// </summary>
    public void SetSelectedSkill(int index)
    {
        if (index == SelectedSkillIndex) return;
        SelectedSkillIndex = index;
        _skillRunner.RebuildSelectedSkill();
    }

    #region//委托事件
    public delegate void OperationsBeforeHurt(Entity origin, ref float damage, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int damageType, int applyType);
    /// <summary>在受到伤害之前调用。</summary>
    public event OperationsBeforeHurt OnBeforeHurt;
    public delegate void OperationsAfterHurt(Entity origin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly);
    /// <summary>在受到伤害之后调用。</summary>
    public event OperationsAfterHurt OnAfterHurt;
    public delegate void OperationsDamageResolved(DamageResolution resolution);
    public event OperationsDamageResolved OnDamageResolved;
    public delegate void OperationsBeforeDieAnimation();
    /// <summary>在播放死亡动画之前调用。</summary>
    public event OperationsBeforeDieAnimation OnBeforeDieAnimation;
    #endregion

    // === 事件桥（供子系统触发，public 事件 API 不变） ===
    internal void RaiseOnBeforeHurt(Entity origin, ref float damage, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int damageType, int applyType)
    { OnBeforeHurt?.Invoke(origin, ref damage, ref multiplyer, ref defPenetrate, ref mgrPenetrate, ref defPenetrate_value, ref mgrPenetrate_value, ref damageType, applyType); }
    internal void RaiseOnAfterHurt(Entity origin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly)
    { OnAfterHurt?.Invoke(origin, damage, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, applyType, isDeadly); }
    internal void RaiseDamageResolved(DamageResolution resolution)
    { OnDamageResolved?.Invoke(resolution); }
    internal void RaiseOnBeforeDieAnimation()
    { OnBeforeDieAnimation?.Invoke(); }

    protected void FixedUpdate()
    {
        if (!Stats.IsActive)
            return;
        Stats.RecoverTick();
        Stats.CheckDeath();
        Vision.Refresh();
        _stateMachine.Tick(Time.fixedDeltaTime);
        _attack?.Tick(Time.fixedDeltaTime);
        if (_skillRunner != null) _skillRunner.Tick(Time.fixedDeltaTime);
    }

    public void Die()
    {
        Stats.BeginDie();
        _stateMachine.TrySetState(EntityState.Die, false);
        if (Camp == 1)
        {
            if (EntityData.ID.ID_C == "t")
            {
                AudioManager.Manager.PlayAudio("token_die");
            }
            else
            {
                AudioManager.Manager.PlayAudio("char_die");
            }
        }
        else
        {
            AudioManager.Manager.PlayAudio("enemy_die");
        }
    }
    /// <summary>到达终点退场：失活（关死移动/攻击/技能门禁，防淡出窗口内二次死亡/攻击造成双重回池）
    /// + 转 Default + 淡出 + 回池。</summary>
    public void ArriveEnd()
    {
        Stats.IsActive = false;
        _stateMachine.TrySetState(EntityState.Default, true);
        visuals.FadeOut(0.2f, () => thisEntityPool.Return(this));
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
        visuals = GetComponent<EntityVisuals>();
        facing = GetComponent<EntityFacing>();
        if (this.TryGetComponent(out BuffController bF))
        {
            buffController = bF;
        }
        else
        {
            Debug.LogWarning("未绑定Buff控制器");
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
        _attack = new EntityAttack(this);
        _skillRunner = new EntityAbilityRunner(this);
        _stateMachine.DieAnimationCompleted += () => visuals.FadeOut(0.2f, () => thisEntityPool.Return(this));

        _vision.InitializeFromData(EntityData);
        Stats.AttributesCaculateFirst(EntityData);
        Movement.Initialize();
        _attack.PreWarm();

        _skillRunner.PreWarm();
    }

    public virtual void Initialize()
    {
        _stateMachine.TrySetState(EntityState.Start, false);
        Stats.ResetState();
        _attack.Initialize();
        TempContainer = new GameObject("TempContainer").transform;
        TempContainer.position = this.transform.position;
        TempContainer.parent = this.transform;
        if (Camp == 2)
        {
            LevelActionManager.Manager.AddToWaveEntities(this);
        }
        bool canmove = MoveBase;
        bool camp2 = Camp == 2;
        SlidersManager.Manager.SetSlider(this, 4, camp2 ? 0 : 1, 0, camp2, canmove);
        if (EntityData != null && EntityData.Skills != null && EntityData.Skills.Count >= 1)
        {
            SlidersManager.Manager.SetSlider(this, 10, camp2 ? 3 : 4, 1, camp2, canmove);
        }

        _skillRunner.OnInitialize();
    }

    public virtual void Dormancy()
    {
        _attack.Dormancy();
        _stateMachine.ResetForPool();
        // 静态实体（阻挡者）回池前先逐个通知被挡敌人解除——Dormancy 链上 PoolOps 正序派发，
        // 且 CreateNewEntity 中 InteractableStatic 先于 Entity 添加，故 InteractableStatic.Dormancy
        // 先执行、本方法随后；而 InteractableStatic.Dormancy 并不通知被挡敌人解除，
        // 不主动通知的话被挡敌人会永久保持阻挡状态
        if (InteractableStatic != null)
        {
            for (int i = Movement.ResistList.Count - 1; i >= 0; i--)
            {
                Movement.ResistList[i].MoveBase.RelieveBlock(this);
            }
        }
        Movement.ResistList.Clear();
        Vision.ClearLists();

        //清空事件注册
        OnBeforeHurt = null;
        OnAfterHurt = null;
        OnDamageResolved = null;
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
