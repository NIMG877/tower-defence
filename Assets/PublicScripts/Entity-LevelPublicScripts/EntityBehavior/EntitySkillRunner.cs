using System.Collections.Generic;
using UnityEngine;
using SkillSystem;

/// <summary>
/// 技能子系统（POCO）。持有 AbilityRuntime 列表并 tick SP / 组件；
/// 订阅 Entity 事件（攻击 / 受击 / 死亡 / 动画）并桥成 SkillEvent 分发。
///
/// 设计要点：
///   - POCO，无 MonoBehaviour 依赖。构造接受 Entity 引用作为事件桥。
///   - 由 Entity 在 PreWarm 中显式构造，OnInitialize / OnTeardown / Tick 由 Entity 生命周期驱动。
///   - 实体 prefab 上不挂载该组件（迁移自原 SkillSystem.SkillRunner MonoBehaviour）。
/// </summary>
public class EntitySkillRunner
{
    private readonly Entity _entity;
    private readonly List<AbilityRuntime> _abilities = new List<AbilityRuntime>();
    public Blackboard sharedBlackboard = new Blackboard();

    // runtimeId 生成 (extras 用,见 spec §5.5)
    private int _extraCounter = 0;

    public IReadOnlyList<AbilityRuntime> Abilities => _abilities;

    public EntitySkillRunner(Entity entity)
    {
        _entity = entity;
    }

    // Test-only constructor. 不订阅 Entity 事件,只挂一个空的 blackboard。
    // EditMode tests 用这个构造一个不依赖 prefab 的 runner。
    internal EntitySkillRunner(Blackboard blackboard)
    {
        _entity = null;
        sharedBlackboard = blackboard ?? new Blackboard();
    }

    public void PreWarm()
    {
        ComponentAutoRegistry.EnsureRegistered();
        var data = _entity != null ? _entity.EntityData : null;
        if (data == null || data.Abilities == null) return;

        for (int i = 0; i < data.Abilities.Count; i++)
        {
            var cfg = data.Abilities[i];
            if (cfg == null) continue;
            BuildAbilityRuntime(cfg);
        }

        DispatchEvent(new PreWarmEvent());
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

        // 3) 重新初始化组件
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            for (int c = 0; c < a.components.Count; c++)
            {
                var comp = a.components[c];
                if (c < a.componentParams.Count)
                {
                    var teardownCtx = a.MakeContext(comp, null);
                    comp.OnTeardown(teardownCtx);
                    var initCtx = a.MakeContext(comp, null);
                    comp.OnInit(initCtx, a.componentParams[c]);
                }
            }
        }

        // 4) 清空 per-Entity 共享黑板
        sharedBlackboard.Clear();

        // 5) 派发 InitializeEvent
        DispatchEvent(new InitializeEvent());

        // 6) 激活 Talents。Talent 的 SetActive(true) 触发 OnAbilityBegin,广播给所有 ability。
        // Skills 等待 SPEngine;extras 还没 runtime。
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            if (a.Kind == AbilityKind.Talent) a.SetActive(true);
        }
    }

    public void OnTeardown()
    {
        Unsubscribe();

        // 1) 所有 active 的 ability 翻成 inactive (会触发 OnAbilityEnd,广播)
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            if (a.isActive) a.SetActive(false);
        }

        // 2) 调组件 OnTeardown 清残
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            for (int c = 0; c < a.components.Count; c++)
            {
                var ctx = a.MakeContext(a.components[c], null);
                a.components[c].OnTeardown(ctx);
            }
        }

        // 3) Unwire + 清空列表 (下次 OnInitialize 走 PreWarm 重建)
        for (int i = _abilities.Count - 1; i >= 0; i--)
        {
            UnwireRuntime(_abilities[i]);
            _abilities.RemoveAt(i);
        }
    }

    /// <summary>
    /// 每物理帧 tick。Entity.FixedUpdate 调用。
    /// </summary>
    public void Tick(float dt)
    {
        // 1) per-ability SP tick
        for (int i = 0; i < _abilities.Count; i++)
        {
            _abilities[i].spEngine?.OnTick(dt, 1f);
        }
        // 2) per-component tick
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            if (!a.isActive) continue;
            for (int c = 0; c < a.tickingComponents.Count; c++)
            {
                var comp = a.tickingComponents[c];
                var ctx = PrepareContext(a.MakeContext(comp, null));
                comp.OnTick(ctx, dt);
            }
        }
    }

    // ===== Public API: Add/Remove ExtraAbility =====

    public string AddExtraAbility(AbilityConfig cfg)
    {
        if (cfg == null)
        {
            Debug.LogError("[EntitySkillRunner] AddExtraAbility: cfg is null");
            return null;
        }
        if (cfg.Kind != AbilityKind.ExtraAbility)
        {
            Debug.LogError($"[EntitySkillRunner] AddExtraAbility: cfg.Kind must be ExtraAbility (got {cfg.Kind})");
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

        var runtime = BuildAbilityRuntime(cfg);
        _abilities.Add(runtime);
        runtime.SetActive(true);
        DispatchEvent(new AbilityAddedEvent { ability = runtime });
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
            Debug.LogError("[EntitySkillRunner] RemoveExtraAbility: only ExtraAbility is removable");
            return false;
        }
        DispatchEvent(new AbilityRemovedEvent { ability = a });
        a.SetActive(false);
        for (int c = 0; c < a.components.Count; c++)
        {
            var ctx = a.MakeContext(a.components[c], null);
            a.components[c].OnTeardown(ctx);
        }
        UnwireRuntime(a);
        _abilities.RemoveAt(idx);
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

    private AbilityRuntime BuildAbilityRuntime(AbilityConfig cfg)
    {
        if (cfg == null) return null;

        // 配置校验
        if ((cfg.Kind == AbilityKind.Talent || cfg.Kind == AbilityKind.ExtraAbility) && cfg.sp != null)
        {
            Debug.LogError($"[EntitySkillRunner] BuildAbilityRuntime: {cfg.Kind} '{cfg.abilityId}' has sp != null, aborting");
            return null;
        }
        if (cfg.Kind == AbilityKind.Skill && cfg.sp == null)
        {
            Debug.LogWarning($"[EntitySkillRunner] BuildAbilityRuntime: Skill '{cfg.abilityId}' has sp == null, treating as passive");
        }

        var runtime = new AbilityRuntime { config = cfg };
        runtime.runtimeId = GenerateRuntimeId(cfg);

        if (cfg.components != null)
        {
            for (int i = 0; i < cfg.components.Length; i++)
            {
                ComponentConfig ccfg = cfg.components[i];
                if (ccfg == null || string.IsNullOrEmpty(ccfg.componentType)) continue;
                ISkillComponent inst = ComponentFactory.Create(ccfg.componentType);
                if (inst == null)
                {
                    Debug.LogError($"[EntitySkillRunner] Unknown component type: {ccfg.componentType} in ability {cfg.abilityId}");
                    continue;
                }
                SkillContext ctx = runtime.MakeContext(inst, null);
                inst.OnInit(ctx, ccfg.parameters);
                runtime.components.Add(inst);
                runtime.componentParams.Add(ccfg.parameters);
                if (inst is ITickingComponent t) runtime.tickingComponents.Add(t);

                int triggerCount = 0;
                if (ccfg.triggers != null)
                {
                    for (int tIdx = 0; tIdx < ccfg.triggers.Length; tIdx++)
                    {
                        var trig = ccfg.triggers[tIdx];
                        if (trig == null) continue;
                        var te = trig.triggerEvent;
                        if (!runtime.componentsByTrigger.TryGetValue(te, out var list))
                        {
                            list = new List<(ISkillComponent, List<ConditionGroup>)>();
                            runtime.componentsByTrigger[te] = list;
                        }
                        list.Add((inst, trig.groups));
                        triggerCount++;
                    }
                }
                if (triggerCount == 0 && !(inst is ITickingComponent))
                {
                    Debug.LogWarning(
                        $"[AbilityRuntime] Component {ccfg.componentType} in ability {cfg.abilityId} "
                        + "declares no triggers — it will never receive OnTrigger.");
                }
            }
        }

        // SPEngine + 钩到 SetActive
        if (cfg.sp != null)
        {
            runtime.spEngine = new SPEngine(cfg.sp);
            runtime.spEngine.OnBegin += () => runtime.SetActive(true);
            runtime.spEngine.OnEnd   += () => runtime.SetActive(false);
        }

        WireRuntime(runtime);

        runtime.isInitialized = true;
        _abilities.Add(runtime);
        return runtime;
    }

    private string GenerateRuntimeId(AbilityConfig cfg)
    {
        if (cfg.Kind != AbilityKind.ExtraAbility) return cfg.abilityId;
        return $"{cfg.abilityId}_{_extraCounter++}";
    }

    private void WireRuntime(AbilityRuntime runtime)
    {
        System.Action beginHandler = () =>
            DispatchEvent(new AbilityBeginEvent { ability = runtime });
        System.Action endHandler = () =>
            DispatchEvent(new AbilityEndEvent   { ability = runtime });
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
        NotifySpEnginesAfterHurt(applyType);
    }

    private void OnAttackSuccessfully() { DispatchEvent(new AttackSuccessfullyEvent()); for (int i = 0; i < _abilities.Count; i++) _abilities[i].spEngine?.OnAttackSuccessfully(); }
    private void OnAttackInterrupt() { DispatchEvent(new AttackInterruptEvent()); }

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
        NotifySpEnginesAfterHurt(applyType);
    }

    private void OnAttackAnimBegin()
    {
        DispatchEvent(new AttackAnimBeginEvent());
        for (int i = 0; i < _abilities.Count; i++) _abilities[i].spEngine?.OnAttackAnimBegin();
    }

    private void OnBeforeDieAnimation() { DispatchEvent(new BeforeDieAnimationEvent()); }

    private void NotifySpEnginesAfterHurt(int applyType)
    {
        for (int i = 0; i < _abilities.Count; i++) _abilities[i].spEngine?.OnAfterHurt(applyType);
    }

    // ===== Dispatch core =====

    public void DispatchEvent(SkillEvent evt)
    {
        for (int i = 0; i < _abilities.Count; i++)
        {
            var a = _abilities[i];
            if (!a.isInitialized) continue;
            DispatchToAbility(a, evt);
        }
    }

    private void DispatchToAbility(AbilityRuntime a, SkillEvent evt)
    {
        bool bypassActiveGate = evt is PreWarmEvent
                             || evt is InitializeEvent
                             || evt is AbilityBeginEvent
                             || evt is AbilityEndEvent
                             || evt is AbilityAddedEvent
                             || evt is AbilityRemovedEvent;
        if (!a.isActive && !bypassActiveGate) return;
        if (!a.componentsByTrigger.TryGetValue(evt.TriggerEvent, out var list)) return;

        var evalCtx = new ConditionEvalContext
        {
            sharedBlackboard = sharedBlackboard,
            entity = _entity,
            currentEvent = evt,
        };

        for (int i = 0; i < list.Count; i++)
        {
            var (comp, groups) = list[i];
            if (!ConditionEvaluator.Evaluate(groups, evalCtx)) continue;
            var ctx = PrepareContext(a.MakeContext(comp, evt));
            comp.OnTrigger(ctx);
        }
    }

    private SkillContext PrepareContext(SkillContext ctx)
    {
        ctx.sharedBlackboard = sharedBlackboard;
        ctx.entity = _entity;
        return ctx;
    }
}
