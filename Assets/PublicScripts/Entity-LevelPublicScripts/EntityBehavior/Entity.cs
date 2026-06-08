using UnityEngine;

public enum OrderLogic
{
    ResistFirst_Priority_Des,
    Priority_Des,
    Hprate_NoFull_Asc,
    ResistFirst_OtherCampFirst_Priority_Des,
}


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
    [HideInInspector] public Skill[] skill;
    [HideInInspector] public Talent[] Talents;

    // === 子系统持有 ===
    private EntityStats _stats;
    private EntityVision _vision;
    private EntityMovement _movement;
    private EntityCombat _combat;

    private int _camp;

    /// <summary>属性子系统：HP/防御/法抗/闪避/格位/嘲讽/状态标志。</summary>
    public EntityStats Stats { get { return _stats; } }
    /// <summary>视野子系统：BaseRange/Range/Radius/朝向镜像/视野内实体列表。</summary>
    public EntityVision Vision { get { return _vision; } }
    /// <summary>移动子系统：Position/InBlocks/ResistList/优先级/阵营镜像。</summary>
    public EntityMovement Movement { get { return _movement; } }
    /// <summary>战斗子系统：EntityUpdate/PriorityOrder。</summary>
    public EntityCombat Combat { get { return _combat; } }

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
    /// 实体世界坐标。getter 直接读 transform.position；setter 委托到 Movement.SetPosition（隐含 FindSelfInBlocks）。
    /// </summary>
    public Vector2 EntityPosition
    {
        get { return Movement.Position; }
        set { Movement.SetPosition(value, true); }
    }

    public EntityPool thisEntityPool;
    [SerializeField] public Transform TempContainer;
    protected OrderLogic EntityOrderLogic;
    /// <summary>
    /// 朝向 0-up 1-right 2-down 3-left
    /// </summary>
    protected int _orientation;

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
    public void HPUpdate()
    {
        Stats.CheckDeath();
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
    /// <summary>
    /// 造成伤害
    /// </summary>
    /// <returns>本次造成伤害是否为致命伤</returns>
    public bool TakeDamage(Entity damageOrigin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType)
    {
        return Stats.ApplyDamage(damageOrigin, damage, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, applyType);
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
        _stats = new EntityStats(this);
        if (buffController != null) _stats.BindBuffController(buffController);
        _vision = new EntityVision(this);
        _movement = new EntityMovement(this);
        _combat = new EntityCombat(this);

        _vision.InitializeFromData(EntityData);
        Stats.AttributesCaculateFirst(EntityData);
        Movement.Initialize();

        // === SkillRunner 生命周期接入（Phase 2 迁移期，与旧 Skill[]/Talent[] 共存） ===
        if (TryGetComponent<SkillSystem.SkillRunner>(out var skillRunner))
            skillRunner.PreWarm();
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
        if (EntityData != null && EntityData.Skills != null && EntityData.Skills.Count == 1)
        {
            SlidersManager.Manager.SetSlider<SpSliderController>(this, 10, camp2 ? 3 : 4, 1, camp2, canmove);
        }

        // === SkillRunner 生命周期接入（Phase 2 迁移期，与旧 Skill[]/Talent[] 共存） ===
        if (TryGetComponent<SkillSystem.SkillRunner>(out var skillRunner))
            skillRunner.OnInitialize();
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
        Destroy(TempContainer.gameObject);
        TempContainer = null;
        if (Camp == 2)
        {
            LevelActionManager.Manager.RemoveFromWaveEntities(this);
        }

        // === SkillRunner 生命周期接入（Phase 2 迁移期，与旧 Skill[]/Talent[] 共存） ===
        if (TryGetComponent<SkillSystem.SkillRunner>(out var skillRunner))
            skillRunner.OnTeardown();
    }
}
