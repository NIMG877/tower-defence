using System.Collections.Generic;
using UnityEngine;
using SkillSystem;

/// <summary>
/// 技能子系统（POCO）。持有 SkillRuntime 列表并 tick SP / 组件；
/// 订阅 Entity 事件（攻击 / 受击 / 死亡 / 动画）并桥成 SkillEvent 分发。
///
/// 设计要点：
///   - POCO，无 MonoBehaviour 依赖。构造接受 Entity 引用作为事件桥。
///   - 由 Entity 在 PreWarm 中显式构造，OnInitialize / OnTeardown / Tick 由 Entity 生命周期驱动。
///   - 实体 prefab 上不挂载该组件（迁移自原 SkillSystem.SkillRunner MonoBehaviour）。
///   - TempContainer 字段已移除：原本只是缓存 Entity.TempContainer，组件内无任何使用点。
/// </summary>
public class EntitySkillRunner
{
    private readonly Entity _entity;
    private readonly List<SkillRuntime> _skills = new List<SkillRuntime>();
    public Blackboard sharedBlackboard = new Blackboard();

    public IReadOnlyList<SkillRuntime> Skills => _skills;

    public EntitySkillRunner(Entity entity)
    {
        _entity = entity;
    }

    public void PreWarm()
    {
        ComponentAutoRegistry.EnsureRegistered();
        var data = _entity != null ? _entity.EntityData : null;
        if (data == null || data.Skills == null) return;

        for (int i = 0; i < data.Skills.Count; i++)
        {
            var cfg = data.Skills[i];
            if (cfg == null) continue;
            BuildSkillRuntime(cfg);
        }

        // 事件订阅不在 PreWarm 一次完成；改为每次部署 OnInitialize / OnTeardown 配对，
        // 避免池化复用路径下订阅丢失（Bug B）。
        DispatchEvent(new PreWarmEvent());
    }

    public void OnInitialize()
    {
        // 1) 重新订阅事件（每次部署配对，池化复用路径不会丢订阅）
        Subscribe();

        // 2) 复位每个 SPEngine（currentSp / charge / duration / isActive / recoverForbid）
        for (int i = 0; i < _skills.Count; i++)
        {
            _skills[i].spEngine?.Reset();
        }

        // 3) 重新初始化组件（先 OnTeardown 清残，再 OnInit 用保存的参数重置）
        for (int i = 0; i < _skills.Count; i++)
        {
            var s = _skills[i];
            for (int c = 0; c < s.components.Count; c++)
            {
                var comp = s.components[c];
                if (c < s.componentParams.Count)
                {
                    var teardownCtx = s.MakeContext(comp, null);
                    comp.OnTeardown(teardownCtx);

                    var initCtx = s.MakeContext(comp, null);
                    comp.OnInit(initCtx, s.componentParams[c]);
                }
            }
        }

        // 4) 清空 per-Entity 共享黑板
        sharedBlackboard.Clear();

        // 5) 派发 InitializeEvent 给组件
        DispatchEvent(new InitializeEvent());
    }

    public void OnTeardown()
    {
        // 退订事件（与 OnInitialize.Subscribe 配对；C# event += / -= 必须成对）
        Unsubscribe();

        // 调组件 OnTeardown 清残，但 _skills 列表与 spEngine 保留以便下次 OnInitialize 复用
        // （重建会丢事件订阅、丢组件参数、丢 component 列表对齐）
        for (int i = 0; i < _skills.Count; i++)
        {
            var s = _skills[i];
            for (int c = 0; c < s.components.Count; c++)
            {
                var ctx = s.MakeContext(s.components[c], null);
                s.components[c].OnTeardown(ctx);
            }
        }
    }

    /// <summary>
    /// 每物理帧 tick。Entity.FixedUpdate 调用。
    /// </summary>
    public void Tick(float dt)
    {
        // 1) per-skill SP tick
        for (int i = 0; i < _skills.Count; i++)
        {
            _skills[i].spEngine?.OnTick(dt, 1f);
        }
        // 2) per-component tick
        for (int i = 0; i < _skills.Count; i++)
        {
            var s = _skills[i];
            if (!s.isActive) continue;
            for (int c = 0; c < s.tickingComponents.Count; c++)
            {
                // IntervalTickEvent removed: OnTick has dt as an explicit parameter,
                // and ctx.currentEvent is null inside OnTick by design.
                var comp = s.tickingComponents[c];
                var ctx = PrepareContext(s.MakeContext(comp, null));
                comp.OnTick(ctx, dt);
            }
        }
    }

    private void BuildSkillRuntime(SkillConfig cfg)
    {
        var runtime = new SkillRuntime { config = cfg };
        if (cfg.sp != null && cfg.sp.totalSp > 0)
        {
            runtime.spEngine = new SPEngine(cfg.sp);
            runtime.spEngine.OnBegin += () => OnSkillBeginWindow(runtime);
            runtime.spEngine.OnEnd   += () => OnSkillEndWindow(runtime);
        }
        if (cfg.components != null)
        {
            for (int i = 0; i < cfg.components.Length; i++)
            {
                ComponentConfig ccfg = cfg.components[i];
                if (ccfg == null || string.IsNullOrEmpty(ccfg.componentType)) continue;
                ISkillComponent  inst = ComponentFactory.Create(ccfg.componentType);
                if (inst == null)
                {
                    Debug.LogError($"[EntitySkillRunner] Unknown component type: {ccfg.componentType} in skill {cfg.skillId}");
                    continue;
                }
                SkillContext ctx = runtime.MakeContext(inst, null);
                inst.OnInit(ctx, ccfg.parameters);
                runtime.components.Add(inst);
                runtime.componentParams.Add(ccfg.parameters);
                if (inst is ITickingComponent t) runtime.tickingComponents.Add(t);

                // Bucket by trigger. Each ConditionConfig contributes one entry; the
                // same component instance can land in multiple buckets when its
                // config declares multiple triggers — that's expected.
                //
                // We project ConditionConfig -> (inst, cond.groups) here so the
                // runtime bucket only carries what Evaluate needs. The triggerEvent
                // is already encoded in the bucket key.
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

                // Warn when a non-ticking component declared no triggers — it
                // will never receive OnTrigger. ITickingComponent gets an
                // implicit pass because OnTick is its primary channel.
                if (triggerCount == 0 && !(inst is ITickingComponent))
                {
                    Debug.LogWarning(
                        $"[SkillRuntime] Component {ccfg.componentType} in skill {cfg.skillId} "
                        + "declares no triggers — it will never receive OnTrigger. "
                        + "Add ConditionConfig entries to triggers[] if this is unintended.");
                }
            }
        }
        runtime.isInitialized = true;
        _skills.Add(runtime);
    }

    private void OnSkillBeginWindow(SkillRuntime runtime)
    {
        runtime.OpenActiveWindow();
        DispatchEvent(new SkillBeginEvent { skill = runtime });
    }

    private void OnSkillEndWindow(SkillRuntime runtime)
    {
        DispatchEvent(new SkillEndEvent { skill = runtime });
        runtime.CloseActiveWindow();
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

    private void OnAttackSuccessfully() { DispatchEvent(new AttackSuccessfullyEvent()); for (int i = 0; i < _skills.Count; i++) _skills[i].spEngine?.OnAttackSuccessfully(); }
    private void OnAttackInterrupt() { DispatchEvent(new AttackInterruptEvent()); }

    private void OnBeforeHurt(Entity origin, ref float damage, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int damageType, int applyType)
    {
        var evt = new BeforeHurtEvent { origin = origin, damage = damage, multiplyer = multiplyer, defPenetrate = defPenetrate, mgrPenetrate = mgrPenetrate, defPenetrate_value = defPenetrate_value, mgrPenetrate_value = mgrPenetrate_value, damageType = damageType, applyType = applyType };
        DispatchEvent(evt);
        damage = evt.damage; multiplyer = evt.multiplyer; defPenetrate = evt.defPenetrate; mgrPenetrate = evt.mgrPenetrate;
        defPenetrate_value = evt.defPenetrate_value; mgrPenetrate_value = evt.mgrPenetrate_value; damageType = evt.damageType;
        for (int i = 0; i < _skills.Count; i++) _skills[i].spEngine?.OnBeforeHurt(applyType);
    }

    private void OnAfterHurt(Entity origin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly)
    {
        DispatchEvent(new AfterHurtEvent { origin = origin, damage = damage, multiplyer = multiplyer, defPenetrate = defPenetrate, mgrPenetrate = mgrPenetrate, defPenetrate_value = defPenetrate_value, mgrPenetrate_value = mgrPenetrate_value, damageType = damageType, applyType = applyType, isDeadly = isDeadly });
        NotifySpEnginesAfterHurt(applyType);
    }

    private void OnAttackAnimBegin()
    {
        DispatchEvent(new AttackAnimBeginEvent());
        for (int i = 0; i < _skills.Count; i++) _skills[i].spEngine?.OnAttackAnimBegin();
    }

    private void OnBeforeDieAnimation() { DispatchEvent(new BeforeDieAnimationEvent()); }

    // Shared SPEngine fan-out for the AfterHurt hook (used by both OnAfterTakeDamage and OnAfterHurt).
    private void NotifySpEnginesAfterHurt(int applyType)
    {
        for (int i = 0; i < _skills.Count; i++) _skills[i].spEngine?.OnAfterHurt(applyType);
    }

    // ===== Dispatch core =====

    public void DispatchEvent(SkillEvent evt)
    {
        for (int i = 0; i < _skills.Count; i++)
        {
            var s = _skills[i];
            if (!s.isInitialized) continue;
            DispatchToSkill(s, evt);
        }
    }

    private void DispatchToSkill(SkillRuntime s, SkillEvent evt)
    {
        // Active-window gate: a skill's components only see events while the
        // skill is firing, EXCEPT for these four lifecycle events which always
        // bypass the gate. They still need to be declared in config.triggers[]
        // to be received — bypass is gate-only, not bucket-only.
        bool bypassActiveGate = evt is PreWarmEvent
                             || evt is InitializeEvent
                             || evt is SkillBeginEvent
                             || evt is SkillEndEvent;
        if (!s.isActive && !bypassActiveGate) return;
        if (!s.componentsByTrigger.TryGetValue(evt.TriggerEvent, out var list)) return;

        // All (comp, groups) for this event share the same eval context.
        var evalCtx = new ConditionEvalContext
        {
            sharedBlackboard = sharedBlackboard,
            entity = _entity,
            currentEvent = evt,
        };

        // Single-pass over the bucket. No dedup: a component with N
        // ConditionConfig entries for the same triggerEvent produces up
        // to N OnTrigger calls (one per passing entry). Designers who
        // want at-most-one should put the OR inside a single
        // ConditionConfig's groups (see spec §2.4).
        for (int i = 0; i < list.Count; i++)
        {
            var (comp, groups) = list[i];
            if (!ConditionEvaluator.Evaluate(groups, evalCtx)) continue;
            var ctx = PrepareContext(s.MakeContext(comp, evt));
            comp.OnTrigger(ctx);
        }
    }

    // SkillRuntime.MakeContext lives in shared code and can't inject per-Entity state,
    // so layer entity + sharedBlackboard onto every context handed to a component.
    private SkillContext PrepareContext(SkillContext ctx)
    {
        ctx.sharedBlackboard = sharedBlackboard;
        ctx.entity = _entity;
        return ctx;
    }
}
