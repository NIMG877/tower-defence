using System.Collections.Generic;
using UnityEngine;

namespace SkillSystem
{
    public class SkillRunner : MonoBehaviour
    {
        private Entity _entity;
        private readonly List<SkillRuntime> _skills = new List<SkillRuntime>();
        public Blackboard sharedBlackboard = new Blackboard();
        public GameObject TempContainer;

        public IReadOnlyList<SkillRuntime> Skills => _skills;

        public void PreWarm()
        {
            ComponentAutoRegistry.EnsureRegistered();
            _entity = GetComponent<Entity>();
            if (TempContainer == null) TempContainer = _entity != null ? _entity.TempContainer : null;

            var data = _entity != null ? _entity.EntityData : null;
            if (data == null || data.Skills == null) return;

            for (int i = 0; i < data.Skills.Count; i++)
            {
                var cfg = data.Skills[i];
                if (cfg == null) continue;
                BuildSkillRuntime(cfg);
            }

            Subscribe();
            DispatchEvent(new PreWarmEvent());
        }

        public void OnInitialize()
        {
            DispatchEvent(new InitializeEvent());
        }

        public void OnTeardown()
        {
            for (int i = 0; i < _skills.Count; i++)
            {
                var s = _skills[i];
                for (int c = 0; c < s.components.Count; c++)
                {
                    var ctx = s.MakeContext(s.components[c], null);
                    s.components[c].OnTeardown(ctx);
                }
            }
            Unsubscribe();
            _skills.Clear();
        }

        public void OnDeath()
        {
            DispatchEvent(new DeathEvent());
        }

        private void BuildSkillRuntime(SkillConfig cfg)
        {
            var runtime = new SkillRuntime { config = cfg };
            if (cfg.sp != null && cfg.sp.totalSp > 0)
            {
                runtime.spEngine = new SPEngine(cfg.sp, () => OnSkillFire(runtime));
            }
            if (cfg.components != null)
            {
                for (int i = 0; i < cfg.components.Length; i++)
                {
                    var ccfg = cfg.components[i];
                    if (ccfg == null || string.IsNullOrEmpty(ccfg.componentType)) continue;
                    var inst = ComponentFactory.Create(ccfg.componentType);
                    if (inst == null)
                    {
                        Debug.LogError($"[SkillRunner] Unknown component type: {ccfg.componentType} in skill {cfg.skillId}");
                        continue;
                    }
                    var ctx = runtime.MakeContext(inst, null);
                    inst.OnInit(ctx, ccfg.parameters);
                    runtime.components.Add(inst);
                    if (inst is ITickingComponent t) runtime.tickingComponents.Add(t);
                }
            }
            _skills.Add(runtime);
        }

        private void OnSkillFire(SkillRuntime runtime)
        {
            runtime.isActive = true;
            DispatchToSkill(runtime, new SkillBeginEvent { skill = runtime });
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

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            // 1) per-skill SP tick
            for (int i = 0; i < _skills.Count; i++)
            {
                _skills[i].spEngine?.OnTick(dt, 1f);
            }
            // 2) per-component tick
            for (int i = 0; i < _skills.Count; i++)
            {
                var s = _skills[i];
                for (int c = 0; c < s.tickingComponents.Count; c++)
                {
                    var ctx = s.MakeContext(s.tickingComponents[c], new IntervalTickEvent { dt = dt });
                    ctx.sharedBlackboard = sharedBlackboard;
                    s.tickingComponents[c].OnTick(ctx, dt);
                }
            }
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
            for (int i = 0; i < _skills.Count; i++) _skills[i].spEngine?.OnAfterHurt(applyType);
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
            for (int i = 0; i < _skills.Count; i++) _skills[i].spEngine?.OnAfterHurt(applyType);
        }

        private void OnAttackAnimBegin()
        {
            DispatchEvent(new AttackAnimBeginEvent());
            for (int i = 0; i < _skills.Count; i++) _skills[i].spEngine?.OnAttackAnimBegin();
        }

        private void OnBeforeDieAnimation() { DispatchEvent(new BeforeDieAnimationEvent()); }

        // ===== Dispatch core =====

        public void DispatchEvent(SkillEvent evt)
        {
            var te = TriggerEventFor(evt);
            for (int i = 0; i < _skills.Count; i++)
            {
                var s = _skills[i];
                if (!s.isInitialized) continue;
                DispatchToSkill(s, evt, te);
            }
        }

        private void DispatchToSkill(SkillRuntime s, SkillEvent evt, TriggerEvent te = TriggerEvent.OnInitialize)
        {
            for (int i = 0; i < s.components.Count; i++)
            {
                var comp = s.components[i];
                var ctx = s.MakeContext(comp, evt);
                ctx.sharedBlackboard = sharedBlackboard;
                ctx.entity = _entity;
                comp.OnTrigger(ctx);
            }
        }

        private static TriggerEvent TriggerEventFor(SkillEvent evt)
        {
            switch (evt)
            {
                case PreWarmEvent _: return TriggerEvent.OnPreWarm;
                case InitializeEvent _: return TriggerEvent.OnInitialize;
                case BeforeAttackEvent _: return TriggerEvent.OnBeforeAttack;
                case AfterAttackEvent _: return TriggerEvent.OnAfterAttack;
                case BeforeTakeDamageEvent _: return TriggerEvent.OnBeforeTakeDamage;
                case AfterTakeDamageEvent _: return TriggerEvent.OnAfterTakeDamage;
                case AttackSuccessfullyEvent _: return TriggerEvent.OnAttackSuccessfully;
                case AttackInterruptEvent _: return TriggerEvent.OnAttackInterrupt;
                case BeforeHurtEvent _: return TriggerEvent.OnBeforeHurt;
                case AfterHurtEvent _: return TriggerEvent.OnAfterHurt;
                case AttackAnimBeginEvent _: return TriggerEvent.OnAttackAnimBegin;
                case BeforeDieAnimationEvent _: return TriggerEvent.OnBeforeDieAnimation;
                case DeathEvent _: return TriggerEvent.OnDeath;
                case IntervalTickEvent _: return TriggerEvent.OnIntervalTick;
                case SkillBeginEvent _: return TriggerEvent.OnSkillBegin;
                case SkillEndEvent _: return TriggerEvent.OnSkillEnd;
                default: return TriggerEvent.OnInitialize;
            }
        }
    }
}
