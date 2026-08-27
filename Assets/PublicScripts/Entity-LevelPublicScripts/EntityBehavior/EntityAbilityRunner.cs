using System.Collections.Generic;
using UnityEngine;
using AbilitySystem;
using AbilitySystem.Components;

/// <summary>
/// 技能子系统（POCO）。持有 AbilityRuntime 列表并 tick SP / 组件；
/// 订阅 Entity 事件（攻击 / 受击 / 死亡 / 动画）并桥成 AbilityEvent 分发。
///
/// 设计要点：
///   - POCO，无 MonoBehaviour 依赖。构造接受 Entity 引用作为事件桥。
///   - 由 Entity 在 PreWarm 中显式构造，OnInitialize / OnTeardown / Tick 由 Entity 生命周期驱动。
///   - 实体 prefab 上不挂载该组件（迁移自原 AbilitySystem.SkillRunner MonoBehaviour）。
/// </summary>
public class EntityAbilityRunner
{
    private readonly Entity _entity;
    // 单一来源:PreWarm/Add/Remove/Teardown 都改这一个列表,不分 Kind;
    // 对外按语义切分为 Skills / Talents / ExtraAbilities 三个只读视图(下方缓存实现)。
    private readonly List<AbilityRuntime> _abilities = new List<AbilityRuntime>();
    private readonly List<AbilityRuntime> _skillsCache = new List<AbilityRuntime>();
    private readonly List<AbilityRuntime> _talentsCache = new List<AbilityRuntime>();
    private readonly List<AbilityRuntime> _extrasCache = new List<AbilityRuntime>();
    private bool _abilitiesCacheDirty = true;

    public Blackboard sharedBlackboard = new Blackboard();

    // runtimeId 生成 (extras 用,见 spec §5.5)
    private int _extraCounter = 0;

    /// <summary>技能列表(AbilityKind.Skill)。PreWarm 时从 <c>EntityData.Skills</c> 注入。</summary>
    public IReadOnlyList<AbilityRuntime> Skills
    {
        get { EnsureAbilitiesCache(); return _skillsCache; }
    }
    /// <summary>天赋列表(AbilityKind.Talent)。PreWarm 时从 <c>EntityData.Talents</c> 注入。</summary>
    public IReadOnlyList<AbilityRuntime> Talents
    {
        get { EnsureAbilitiesCache(); return _talentsCache; }
    }
    /// <summary>额外能力列表(AbilityKind.ExtraAbility)。由 <see cref="AddExtraAbility"/> 运行时加入,可用 <see cref="RemoveExtraAbility"/> 移除。</summary>
    public IReadOnlyList<AbilityRuntime> ExtraAbilities
    {
        get { EnsureAbilitiesCache(); return _extrasCache; }
    }

    private void EnsureAbilitiesCache()
    {
        if (!_abilitiesCacheDirty) return;
        _skillsCache.Clear();
        _talentsCache.Clear();
        _extrasCache.Clear();
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            switch (a.Kind)
            {
                case AbilityKind.Skill: _skillsCache.Add(a); break;
                case AbilityKind.Talent: _talentsCache.Add(a); break;
                case AbilityKind.ExtraAbility: _extrasCache.Add(a); break;
            }
        }
        _abilitiesCacheDirty = false;
    }

    private void InvalidateAbilitiesCache()
    {
        _abilitiesCacheDirty = true;
    }

    public EntityAbilityRunner(Entity entity)
    {
        _entity = entity;
    }

    // Test-only constructor. 不订阅 Entity 事件,只挂一个空的 blackboard。
    // EditMode tests 用这个构造一个不依赖 prefab 的 runner。
    internal EntityAbilityRunner(Blackboard blackboard)
    {
        _entity = null;
        sharedBlackboard = blackboard ?? new Blackboard();
    }

    public void PreWarm()
    {
        ComponentAutoRegistry.EnsureRegistered();
        AbilityStepAutoRegistry.EnsureRegistered();
        var data = _entity != null ? _entity.EntityData : null;
        if (data == null) return;

        // Talents 在前,Skills 在后(语义优先 + 保持"天赋在技能前"的传统顺序)
        PreWarmList(data.Talents, AbilityKind.Talent);
        PreWarmList(data.Skills, AbilityKind.Skill);

        DispatchEvent(new PreWarmEvent());
    }

    private void PreWarmList(List<AbilityConfig> configs, AbilityKind kind)
    {
        if (configs == null) return;
        for (int i = 0; i < configs.Count; i++)
        {
            var cfg = configs[i];
            if (cfg == null) continue;
            BuildAbilityRuntime(cfg, kind);
        }
    }

    public void OnInitialize()
    {
        // 1) 重新订阅事件
        Subscribe();

        // 2) 复位每个 SPEngine
        for (int i = 0; i < _abilities.Count; i++)
        {
            _abilities[i].spEngine?.Reset();
        }

        // Cancel work from a previous lifetime before clearing the shared BB.
        for (int i = 0; i < _abilities.Count; i++)
        {
            _abilities[i].CancelStepExecutions();
        }

        // 3) 清空 per-Entity 共享黑板
        sharedBlackboard.Clear();

        // 4) 重新初始化组件，并派发InitializeEvent
        InitializeEvent evt= new InitializeEvent();
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            for (int c = 0; c < a.components.Count; c++)
            {
                var comp = a.components[c];
                if (c < a.componentParams.Count)
                {
                    var initCtx = a.MakeContext(comp, null, sharedBlackboard, _entity);
                    comp.OnInit(initCtx, a.componentParams[c]);
                }
            }
            DispatchToAbility(a,evt);
        }
    }

    public void OnTeardown()
    {
        Unsubscribe();

        // 1) 所有 active 的 ability 翻成 inactive (会触发 OnAbilityEnd,广播)
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            a.SetActive(false);
        }

        // 2) 调组件 OnTeardown 清残
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            // AbilityEnd may have started a new asynchronous sequence.
            a.CancelStepExecutions();
            for (int c = 0; c < a.components.Count; c++)
            {
                var ctx = a.MakeContext(a.components[c], null, sharedBlackboard, _entity);
                a.components[c].OnTeardown(ctx);
            }
        }

        // 3) Unwire + 仅清掉 ExtraAbility（运行时 AddExtraAbility 加入的动态能力）。
        //    Talents/Skills 保留在 _abilities,等下次 OnInitialize 走 spEngine.Reset + 组件 OnTeardown/OnInit 重置。
        for (int i = _abilities.Count - 1; i >= 0; i--)
        {
            var a = _abilities[i];
            if (a.Kind != AbilityKind.ExtraAbility) continue;
            UnwireRuntime(a);
            _abilities.RemoveAt(i);
        }
        // 缓存失效:Extras 列表被改了,下次访问重建。
        InvalidateAbilitiesCache();
    }

    /// <summary>
    /// 每物理帧 tick。Entity.FixedUpdate 调用。
    /// </summary>
    public void Tick(float dt)
    {
        // Snapshot every execution across every ability before advancing any of
        // them. Work triggered by an earlier execution starts with dt=0 and is
        // deliberately absent from this FixedUpdate's snapshot.
        AbilityRuntime[] abilitySnapshot = _abilities.ToArray();
        var executionSnapshot = new List<AbilityStepExecution>();
        for (int i = 0; i < abilitySnapshot.Length; i++)
        {
            abilitySnapshot[i].AppendStepExecutionSnapshot(executionSnapshot);
        }
        for (int i = 0; i < executionSnapshot.Count; i++)
        {
            executionSnapshot[i].Tick(dt);
        }
        for (int i = 0; i < abilitySnapshot.Length; i++)
        {
            abilitySnapshot[i].PruneCompletedStepExecutions();
        }

        // 1) per-ability SP tick
        for (int i = 0; i < abilitySnapshot.Length; i++)
        {
            AbilityRuntime ability = abilitySnapshot[i];
            if (!_abilities.Contains(ability)) continue;
            ability.spEngine?.OnTick(dt, 1f);
        }
        // 2) per-component tick
        for (int i = 0; i < abilitySnapshot.Length; i++)
        {
            var a = abilitySnapshot[i];
            if (!_abilities.Contains(a)) continue;
            if (!a.isActive) continue;
            DispatchToAbility(a, new TickEvent { deltaTime = dt });
            // An OnTick rule may synchronously remove this ability.
            if (!_abilities.Contains(a)) continue;
            for (int c = 0; c < a.components.Count; c++)
            {
                var comp = a.components[c];
                var ctx = a.MakeContext(comp, null, sharedBlackboard, _entity);
                comp.OnTick(ctx, dt);
                if (!_abilities.Contains(a)) break;
            }
        }
    }

    // ===== Public API: Add/Remove ExtraAbility =====

    public string AddExtraAbility(AbilityConfig cfg)
    {
        if (cfg == null)
        {
            Debug.LogError("[EntityAbilityRunner] AddExtraAbility: cfg is null");
            return null;
        }

        // 幂等:同 cfg 已存在则返回旧 id
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            if (a.Kind == AbilityKind.ExtraAbility && a.config == cfg)
            {
                return a.runtimeId;
            }
        }

        var runtime = BuildAbilityRuntime(cfg, AbilityKind.ExtraAbility);
        for (int i=0;i<runtime.components.Count;i++)
        {
            var ctx = runtime.MakeContext(runtime.components[i], null, sharedBlackboard, _entity);
            runtime.components[i].OnInit(ctx, runtime.componentParams[i]);
        }   
        DispatchToAbility(runtime, new InitializeEvent());
        DispatchToAbility(runtime, new AbilityAddedEvent { ability = runtime });  
        return runtime.runtimeId;
    }

    public bool RemoveExtraAbility(string runtimeId)
    {
        if (string.IsNullOrEmpty(runtimeId)) return false;
        int idx = -1;
        for (int i = 0; i < _abilities.Count; i++)
        {
            if (_abilities[i].runtimeId == runtimeId) { idx = i; break; }
        }
        if (idx < 0) return false;
        var a = _abilities[idx];
        if (a.Kind != AbilityKind.ExtraAbility)
        {
            Debug.LogError("[EntityAbilityRunner] RemoveExtraAbility: only ExtraAbility is removable");
            return false;
        }
        DispatchToAbility(a, new AbilityRemovedEvent { ability = a });
        a.SetActive(false);
        a.CancelStepExecutions();
        for (int c = 0; c < a.components.Count; c++)
        {
            var ctx = a.MakeContext(a.components[c], null, sharedBlackboard, _entity);
            a.components[c].OnTeardown(ctx);
        }
        UnwireRuntime(a);
        _abilities.RemoveAt(idx);
        InvalidateAbilitiesCache();
        return true;
    }

    public bool HasExtraAbility(string runtimeId)
    {
        if (string.IsNullOrEmpty(runtimeId)) return false;
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            if (a.runtimeId == runtimeId && a.Kind == AbilityKind.ExtraAbility) return true;
        }
        return false;
    }

    // ===== Build / Wire =====

    private AbilityRuntime BuildAbilityRuntime(AbilityConfig cfg, AbilityKind kind)
    {
        if (cfg == null) return null;

        // 强制覆盖 Kind —— 来源是 PreWarm 的列表(Skill/Talent)或 AddExtraAbility(ExtraAbility)。
        // 设计师不在 Inspector 上设 Kind(已 HideInInspector),这里无条件赋值确保 runtime.Kind 准确。
        cfg.Kind = kind;

        var runtime = new AbilityRuntime { config = cfg };
        runtime.runtimeId = GenerateRuntimeId(cfg, kind);

        runtime.BuildRules(cfg.GetRules());

        // SPEngine + 钩到 SetActive
        runtime.spEngine = new SPEngine(cfg.sp);
        runtime.spEngine.OnBegin += () => runtime.SetActive(true);
        runtime.spEngine.OnEnd   += () => runtime.SetActive(false);

        WireRuntime(runtime);

        runtime.isInitialized = true;
        // single source of truth:BuildAbilityRuntime 负责把 runtime 加入 _abilities。
        // 调用者(PreWarm 循环、AddExtraAbility)拿到的 runtime 已经在 list 里,不要再 Add。
        _abilities.Add(runtime);
        InvalidateAbilitiesCache();
        return runtime;
    }

    private string GenerateRuntimeId(AbilityConfig cfg, AbilityKind kind)
    {
        if (kind != AbilityKind.ExtraAbility) return cfg.abilityId;
        return $"{cfg.abilityId}_{_extraCounter++}";
    }

    private void WireRuntime(AbilityRuntime runtime)
    {
        System.Action beginHandler = () =>
            DispatchToAbility(runtime, new AbilityBeginEvent { ability = runtime });
        System.Action endHandler = () =>
            DispatchToAbility(runtime, new AbilityEndEvent   { ability = runtime });
        runtime.OnAbilityBegin += beginHandler;
        runtime.OnAbilityEnd   += endHandler;
        runtime._wireTeardown  = () =>
        {
            runtime.OnAbilityBegin -= beginHandler;
            runtime.OnAbilityEnd   -= endHandler;
        };
    }

    private void UnwireRuntime(AbilityRuntime runtime)
    {
        runtime._wireTeardown?.Invoke();
        runtime._wireTeardown = null;
    }

    private void Subscribe()
    {
        if (_entity == null) return;
        if (_entity.AttackBase != null)
        {
            _entity.AttackBase.OnBeforeAttack += OnBeforeAttack;
            _entity.AttackBase.OnAfterAttack += OnAfterAttack;
            _entity.AttackBase.OnBeforeTakeDamage += OnBeforeTakeDamage;
            _entity.AttackBase.OnAfterTakeDamage += OnAfterTakeDamage;
            _entity.AttackBase.OnAttackSuccessfully += OnAttackSuccessfully;
            _entity.AttackBase.OnAttackInterrupt += OnAttackInterrupt;
            _entity.AttackBase.OnBeforeTargetSelect += OnBeforeTargetSelect;
        }
        _entity.OnBeforeHurt += OnBeforeHurt;
        _entity.OnAfterHurt += OnAfterHurt;
        _entity.OnBeforeDieAnimation += OnBeforeDieAnimation;
        if (_entity.entityAM != null) _entity.entityAM.OnAttackAnimationBegin += OnAttackAnimBegin;
    }

    private void Unsubscribe()
    {
        if (_entity == null) return;
        if (_entity.AttackBase != null)
        {
            _entity.AttackBase.OnBeforeAttack -= OnBeforeAttack;
            _entity.AttackBase.OnAfterAttack -= OnAfterAttack;
            _entity.AttackBase.OnBeforeTakeDamage -= OnBeforeTakeDamage;
            _entity.AttackBase.OnAfterTakeDamage -= OnAfterTakeDamage;
            _entity.AttackBase.OnAttackSuccessfully -= OnAttackSuccessfully;
            _entity.AttackBase.OnAttackInterrupt -= OnAttackInterrupt;
            _entity.AttackBase.OnBeforeTargetSelect -= OnBeforeTargetSelect;
        }
        _entity.OnBeforeHurt -= OnBeforeHurt;
        _entity.OnAfterHurt -= OnAfterHurt;
        _entity.OnBeforeDieAnimation -= OnBeforeDieAnimation;
        if (_entity.entityAM != null) _entity.entityAM.OnAttackAnimationBegin -= OnAttackAnimBegin;
    }

    // ===== Event bridges =====

    private void OnBeforeAttack(Entity target, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int cumbo, ref int damageType, int applyType)
    {
        var evt = new BeforeAttackEvent { target = target, multiplyer = multiplyer, defPenetrate = defPenetrate, mgrPenetrate = mgrPenetrate, defPenetrate_value = defPenetrate_value, mgrPenetrate_value = mgrPenetrate_value, cumbo = cumbo, damageType = damageType, applyType = applyType };
        DispatchEvent(evt);
        multiplyer = evt.multiplyer; defPenetrate = evt.defPenetrate; mgrPenetrate = evt.mgrPenetrate;
        defPenetrate_value = evt.defPenetrate_value; mgrPenetrate_value = evt.mgrPenetrate_value;
        cumbo = evt.cumbo; damageType = evt.damageType;
    }

    private void OnAfterAttack(Entity target, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly)
    {
        DispatchEvent(new AfterAttackEvent { target = target, multiplyer = multiplyer, defPenetrate = defPenetrate, mgrPenetrate = mgrPenetrate, defPenetrate_value = defPenetrate_value, mgrPenetrate_value = mgrPenetrate_value, damageType = damageType, applyType = applyType, isDeadly = isDeadly });
    }

    private void OnBeforeTakeDamage(Entity target, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int damageType, int applyType)
    {
        var evt = new BeforeTakeDamageEvent { target = target, multiplyer = multiplyer, defPenetrate = defPenetrate, mgrPenetrate = mgrPenetrate, defPenetrate_value = defPenetrate_value, mgrPenetrate_value = mgrPenetrate_value, damageType = damageType, applyType = applyType };
        DispatchEvent(evt);
        multiplyer = evt.multiplyer; defPenetrate = evt.defPenetrate; mgrPenetrate = evt.mgrPenetrate;
        defPenetrate_value = evt.defPenetrate_value; mgrPenetrate_value = evt.mgrPenetrate_value; damageType = evt.damageType;
    }

    private void OnAfterTakeDamage(Entity target, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly)
    {
        DispatchEvent(new AfterTakeDamageEvent { target = target, multiplyer = multiplyer, defPenetrate = defPenetrate, mgrPenetrate = mgrPenetrate, defPenetrate_value = defPenetrate_value, mgrPenetrate_value = mgrPenetrate_value, damageType = damageType, applyType = applyType, isDeadly = isDeadly });
    }

    private void OnAttackSuccessfully() 
    { 
        DispatchEvent(new AttackSuccessfullyEvent()); 
        for (int i = 0; i < _abilities.Count; i++) _abilities[i].spEngine?.OnAttackSuccessfully(); 
    }
    private void OnAttackInterrupt() { DispatchEvent(new AttackInterruptEvent()); }

    private void OnBeforeTargetSelect(List<Entity> targets, ref int selectMaxNum, ref int selectMinNum, ref bool sameComp)
    {
        var evt = new BeforeTargetSelectEvent
        {
            targets = targets,
            selectMaxNum = selectMaxNum,
            selectMinNum = selectMinNum,
            sameComp = sameComp,
        };
        // 候选列表挂到保留键（同一 List 引用），派发窗口内组件按普通黑板键读写；
        // 窗口结束即摘除，窗口外读取走"键缺失"告警路径暴露配线错误。
        sharedBlackboard.Set(BlackboardKeys.AttackCandidates, targets);
        DispatchEvent(evt);
        selectMaxNum = evt.selectMaxNum;
        selectMinNum = evt.selectMinNum;
        sameComp = evt.sameComp;
        sharedBlackboard.Remove(BlackboardKeys.AttackCandidates);
    }

    private void OnBeforeHurt(Entity origin, ref float damage, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int damageType, int applyType)
    {
        var evt = new BeforeHurtEvent { origin = origin, damage = damage, multiplyer = multiplyer, defPenetrate = defPenetrate, mgrPenetrate = mgrPenetrate, defPenetrate_value = defPenetrate_value, mgrPenetrate_value = mgrPenetrate_value, damageType = damageType, applyType = applyType };
        DispatchEvent(evt);
        damage = evt.damage; multiplyer = evt.multiplyer; defPenetrate = evt.defPenetrate; mgrPenetrate = evt.mgrPenetrate;
        defPenetrate_value = evt.defPenetrate_value; mgrPenetrate_value = evt.mgrPenetrate_value; damageType = evt.damageType;
        for (int i = 0; i < _abilities.Count; i++) _abilities[i].spEngine?.OnBeforeHurt(applyType);
    }

    private void OnAfterHurt(Entity origin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly)
    {
        DispatchEvent(new AfterHurtEvent { origin = origin, damage = damage, multiplyer = multiplyer, defPenetrate = defPenetrate, mgrPenetrate = mgrPenetrate, defPenetrate_value = defPenetrate_value, mgrPenetrate_value = mgrPenetrate_value, damageType = damageType, applyType = applyType, isDeadly = isDeadly });
        for (int i = 0; i < _abilities.Count; i++) _abilities[i].spEngine?.OnAfterHurt(applyType, isDeadly);
    }

    private void OnAttackAnimBegin()
    {
        DispatchEvent(new AttackAnimBeginEvent());
        for (int i = 0; i < _abilities.Count; i++) _abilities[i].spEngine?.OnAttackAnimBegin();
    }

    private void OnBeforeDieAnimation() { DispatchEvent(new BeforeDieAnimationEvent()); }

    // ===== Dispatch core =====

    public void DispatchEvent(AbilityEvent evt)
    {
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            if (!a.isInitialized) continue;
            DispatchToAbility(a, evt);
        }
    }

    private void DispatchToAbility(AbilityRuntime a, AbilityEvent evt)
    {
        bool bypassActiveGate = evt is PreWarmEvent
                             || evt is InitializeEvent
                             || evt is AbilityBeginEvent
                             || evt is AbilityEndEvent
                             || evt is AbilityAddedEvent
                             || evt is AbilityRemovedEvent;
        if (!a.isActive && !bypassActiveGate) return;
        if (!a.rulesByTrigger.TryGetValue(evt.TriggerEvent, out var list)) return;

        var evalCtx = new ConditionEvalContext
        {
            sharedBlackboard = sharedBlackboard,
            entity = _entity,
            currentEvent = evt,
        };

        for (int i = 0; i < list.Count; i++)
        {
            var (rule, groups) = list[i];
            if (!ConditionEvaluator.Evaluate(groups, evalCtx)) continue;
            rule.Trigger(evt, sharedBlackboard, _entity);
        }
    }
}
