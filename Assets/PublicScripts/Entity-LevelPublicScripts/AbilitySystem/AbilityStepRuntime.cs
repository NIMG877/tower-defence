using System;
using System.Collections.Generic;
using System.Reflection;
using AbilitySystem.Components;
using UnityEngine;

namespace AbilitySystem
{
    /// <summary>Marks a parameterless POCO step operation for automatic registration.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class RegisterAbilityStepOpAttribute : Attribute
    {
        public string Op { get; }
        public string[] Aliases { get; }

        public RegisterAbilityStepOpAttribute(string op, params string[] aliases)
        {
            Op = op;
            Aliases = aliases ?? Array.Empty<string>();
        }
    }

    public enum AbilityStepStatus
    {
        Running,
        Completed,
        Failed,
    }

    /// <summary>
    /// One activation of a step operation. Instances are never shared between
    /// executions, so delay/loop state remains isolated under Parallel reentry.
    /// </summary>
    public abstract class AbilityStepOp
    {
        internal virtual AbilityComponentBase BoundComponent => null;

        public virtual void OnInit(AbilityContext ctx) { }
        public abstract AbilityStepStatus OnTick(AbilityContext ctx, float deltaTime);
        public virtual void OnCancel(AbilityContext ctx) { }
        public virtual void OnTeardown(AbilityContext ctx) { }
    }

    internal sealed class AbilityStepOpRegistration
    {
        public string CanonicalOp;
        public Func<AbilityStepOp> Create;
        public Func<AbilityRuntime, StepConfig, Func<AbilityStepOp>> Bind;
    }

    /// <summary>
    /// Open registry for step POCOs. Canonical names are designer-facing;
    /// aliases are accepted only while resolving old payloads.
    /// </summary>
    public static class AbilityStepOpRegistry
    {
        private static readonly Dictionary<string, AbilityStepOpRegistration> _byName =
            new Dictionary<string, AbilityStepOpRegistration>(StringComparer.Ordinal);
        private static readonly HashSet<string> _canonical =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly object _lock = new object();

        public static IReadOnlyList<string> RegisteredOps
        {
            get
            {
                AbilityStepAutoRegistry.EnsureRegistered();
                lock (_lock)
                {
                    var result = new List<string>(_canonical);
                    result.Sort(StringComparer.Ordinal);
                    return result;
                }
            }
        }

        public static void Register(
            string canonicalOp,
            Func<AbilityStepOp> ctor,
            params string[] aliases)
        {
            if (string.IsNullOrWhiteSpace(canonicalOp))
                throw new ArgumentException("canonicalOp must not be empty", nameof(canonicalOp));
            if (ctor == null) throw new ArgumentNullException(nameof(ctor));

            var registration = new AbilityStepOpRegistration
            {
                CanonicalOp = canonicalOp,
                Create = ctor,
                Bind = (runtime, step) => ctor,
            };
            RegisterCore(registration, aliases);
        }

        public static AbilityStepOp Create(string op)
        {
            AbilityStepAutoRegistry.EnsureRegistered();
            lock (_lock)
            {
                return TryGetRegistration(op, out var registration)
                    ? registration.Create()
                    : null;
            }
        }

        public static bool IsRegistered(string op)
        {
            AbilityStepAutoRegistry.EnsureRegistered();
            lock (_lock) return !string.IsNullOrEmpty(op) && _byName.ContainsKey(op);
        }

        public static string ResolveCanonical(string op)
        {
            AbilityStepAutoRegistry.EnsureRegistered();
            lock (_lock)
            {
                return TryGetRegistration(op, out var registration)
                    ? registration.CanonicalOp
                    : null;
            }
        }

        internal static Func<AbilityStepOp> Bind(
            AbilityRuntime runtime,
            StepConfig step,
            out string canonicalOp)
        {
            AbilityStepAutoRegistry.EnsureRegistered();
            lock (_lock)
            {
                if (!TryGetRegistration(step?.op, out var registration))
                {
                    canonicalOp = step?.op;
                    return null;
                }
                canonicalOp = registration.CanonicalOp;
                return registration.Bind(runtime, step);
            }
        }

        internal static void RegisterComponentAdapter(string canonicalOp, string componentType)
        {
            var registration = new AbilityStepOpRegistration
            {
                CanonicalOp = canonicalOp,
                Create = () => new ComponentAbilityStepOp(ComponentFactory.Create(componentType)),
                Bind = (runtime, step) =>
                {
                    AbilityComponentBase component = ComponentFactory.Create(componentType);
                    if (component == null) return () => new MissingAbilityStepOp();

                    ParamList parameters = step?.args ?? new ParamList();
                    runtime.components.Add(component);
                    runtime.componentParams.Add(parameters);
                    return () => new ComponentAbilityStepOp(component);
                },
            };
            RegisterCore(registration, new[] { componentType });
        }

        private static void RegisterCore(
            AbilityStepOpRegistration registration,
            IEnumerable<string> aliases)
        {
            lock (_lock)
            {
                var names = new List<string> { registration.CanonicalOp };
                if (aliases != null)
                {
                    foreach (string alias in aliases)
                    {
                        if (!string.IsNullOrWhiteSpace(alias) && !names.Contains(alias))
                            names.Add(alias);
                    }
                }

                // Reflection order is deliberately irrelevant: two ops may not
                // claim the same canonical name or alias. Silent last-writer-wins
                // would make an existing asset execute different code by build.
                for (int i = 0; i < names.Count; i++)
                {
                    if (_byName.TryGetValue(names[i], out var existing) &&
                        !ReferenceEquals(existing, registration))
                    {
                        throw new InvalidOperationException(
                            $"Ability step op name '{names[i]}' is already registered " +
                            $"for canonical op '{existing.CanonicalOp}' and cannot also " +
                            $"be registered for '{registration.CanonicalOp}'.");
                    }
                }

                _canonical.Add(registration.CanonicalOp);
                for (int i = 0; i < names.Count; i++) _byName[names[i]] = registration;
            }
        }

        private static bool TryGetRegistration(
            string op,
            out AbilityStepOpRegistration registration)
        {
            if (string.IsNullOrEmpty(op))
            {
                registration = null;
                return false;
            }
            return _byName.TryGetValue(op, out registration);
        }

        internal static void ClearForAutoRegistryReset()
        {
            lock (_lock)
            {
                _byName.Clear();
                _canonical.Clear();
            }
        }
    }

    /// <summary>Registers attributed POCO ops and adapters for every component.</summary>
    public static class AbilityStepAutoRegistry
    {
        private static readonly object _lock = new object();
        private static bool _done;

        public static void EnsureRegistered()
        {
            if (_done) return;
            RegisterAll();
        }

        public static void RegisterAll()
        {
            lock (_lock)
            {
                if (_done) return;

                ComponentAutoRegistry.EnsureRegistered();
                foreach (string componentType in ComponentFactory.RegisteredTypes)
                {
                    string canonical = componentType == "EntitySelector"
                        ? "select_targets"
                        : componentType == "EntityFilter"
                            ? "filter_targets"
                            : ToSnakeCase(componentType);
                    AbilityStepOpRegistry.RegisterComponentAdapter(canonical, componentType);
                }

                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (Type type in GetLoadableTypes(assembly))
                    {
                        var attr = type.GetCustomAttribute<RegisterAbilityStepOpAttribute>();
                        if (attr == null || type.IsAbstract || type.IsInterface) continue;
                        if (!typeof(AbilityStepOp).IsAssignableFrom(type)) continue;
                        AbilityStepOpRegistry.Register(
                            attr.Op,
                            () => (AbilityStepOp)Activator.CreateInstance(type),
                            attr.Aliases);
                    }
                }
                _done = true;
            }
        }

        public static void Reset()
        {
            lock (_lock)
            {
                _done = false;
                AbilityStepOpRegistry.ClearForAutoRegistryReset();
            }
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                var result = new List<Type>();
                Type[] types = ex.Types;
                if (types == null) return result;
                for (int i = 0; i < types.Length; i++)
                    if (types[i] != null) result.Add(types[i]);
                return result;
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        private static string ToSnakeCase(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var chars = new List<char>(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsUpper(c) && i > 0)
                {
                    char previous = value[i - 1];
                    bool nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);
                    if (char.IsLower(previous) || char.IsDigit(previous) || nextIsLower)
                        chars.Add('_');
                }
                chars.Add(char.ToLowerInvariant(c));
            }
            return new string(chars.ToArray());
        }
    }

    internal sealed class ComponentAbilityStepOp : AbilityStepOp
    {
        private readonly AbilityComponentBase _component;

        public ComponentAbilityStepOp(AbilityComponentBase component)
        {
            _component = component;
        }

        internal override AbilityComponentBase BoundComponent => _component;

        public override void OnInit(AbilityContext ctx)
        {
            _component?.OnTrigger(ctx);
        }

        public override AbilityStepStatus OnTick(AbilityContext ctx, float deltaTime) =>
            AbilityStepStatus.Completed;
    }

    internal sealed class MissingAbilityStepOp : AbilityStepOp
    {
        public override AbilityStepStatus OnTick(AbilityContext ctx, float deltaTime) =>
            AbilityStepStatus.Completed;
    }

    [RegisterAbilityStepOp("delay")]
    public sealed class DelayAbilityStepOp : AbilityStepOp
    {
        private float _remaining;

        public override void OnInit(AbilityContext ctx)
        {
            ParamList args = ctx.step?.args ?? new ParamList();
            _remaining = Mathf.Max(0f,
                args.GetFloatLazy("seconds", 0f, ctx.sharedBlackboard)());
        }

        public override AbilityStepStatus OnTick(AbilityContext ctx, float deltaTime)
        {
            _remaining -= Mathf.Max(0f, deltaTime);
            return _remaining <= 0f ? AbilityStepStatus.Completed : AbilityStepStatus.Running;
        }
    }

    [RegisterAbilityStepOp("wait_until")]
    public sealed class WaitUntilAbilityStepOp : AbilityStepOp
    {
        public override AbilityStepStatus OnTick(AbilityContext ctx, float deltaTime)
        {
            return EvaluateStepCondition(ctx)
                ? AbilityStepStatus.Completed
                : AbilityStepStatus.Running;
        }

        internal static bool EvaluateStepCondition(AbilityContext ctx)
        {
            return ConditionEvaluator.Evaluate(
                ctx.step?.condition,
                new ConditionEvalContext
                {
                    sharedBlackboard = ctx.sharedBlackboard,
                    entity = ctx.entity,
                    currentEvent = ctx.currentEvent,
                });
        }
    }

    [RegisterAbilityStepOp("branch")]
    public sealed class BranchAbilityStepOp : AbilityStepOp
    {
        internal bool TakeThenBranch { get; private set; }

        public override void OnInit(AbilityContext ctx)
        {
            TakeThenBranch = WaitUntilAbilityStepOp.EvaluateStepCondition(ctx);
        }

        public override AbilityStepStatus OnTick(AbilityContext ctx, float deltaTime) =>
            AbilityStepStatus.Completed;
    }

    [RegisterAbilityStepOp("loop")]
    public sealed class LoopAbilityStepOp : AbilityStepOp
    {
        internal int Count { get; private set; }

        public override void OnInit(AbilityContext ctx)
        {
            ParamList args = ctx.step?.args ?? new ParamList();
            bool hasCondition = ctx.step?.condition != null && ctx.step.condition.Count > 0;
            int defaultCount = hasCondition ? -1 : 1;
            Count = args.GetIntLazy("count", defaultCount, ctx.sharedBlackboard)();
        }

        internal bool ConditionPasses(AbilityContext ctx) =>
            WaitUntilAbilityStepOp.EvaluateStepCondition(ctx);

        public override AbilityStepStatus OnTick(AbilityContext ctx, float deltaTime) =>
            AbilityStepStatus.Completed;
    }

    [RegisterAbilityStepOp("spawn_entity")]
    public sealed class SpawnEntityAbilityStepOp : AbilityStepOp
    {
        private AbilityStepStatus _status;

        public override void OnInit(AbilityContext ctx)
        {
            _status = Spawn(ctx) ? AbilityStepStatus.Completed : AbilityStepStatus.Failed;
        }

        public override AbilityStepStatus OnTick(AbilityContext ctx, float deltaTime) => _status;

        private static bool Spawn(AbilityContext ctx)
        {
            ParamList args = ctx.step?.args ?? new ParamList();
            EntityID id = ResolveEntityId(args, ctx.sharedBlackboard);
            if (id.IsNull)
            {
                Debug.LogError("[spawn_entity] Missing/invalid entityId or entityCategory/entityNumber.");
                return false;
            }

            Vector2 position = ResolvePosition(ctx, args);
            int camp = args.GetIntLazy("camp", -1, ctx.sharedBlackboard)();
            if (camp < 0) camp = ctx.entity != null ? ctx.entity.Camp : 1;

            EntityPool pool = EntityPoolManager.Manager.FetchEntityPool(id);
            if (pool == null)
            {
                Debug.LogError($"[spawn_entity] Entity pool '{id}' is not available.");
                return false;
            }

            string placement = NormalizeToken(
                args.GetStringLazy("placement", "auto", ctx.sharedBlackboard)());
            bool isStatic = placement == "static" ||
                            (placement == "auto" && pool.EntityData != null && pool.EntityData.IsStatic);
            Entity spawned = isStatic
                ? EntityManager.Manager.SetStaticEntity(
                    id,
                    position,
                    camp,
                    args.GetIntLazy("orientation", 0, ctx.sharedBlackboard)())
                : EntityManager.Manager.SetMovableEntity(
                    id,
                    position,
                    camp,
                    args.GetIntLazy("pathSerial", 0, ctx.sharedBlackboard)());

            string outputKey = args.GetStringLazy("outputKey", "", ctx.sharedBlackboard)();
            if (spawned != null && !string.IsNullOrEmpty(outputKey))
                ctx.sharedBlackboard?.Set(outputKey, spawned);
            return spawned != null;
        }

        private static EntityID ResolveEntityId(ParamList args, Blackboard blackboard)
        {
            string raw = args.GetStringLazy("entityId", "", blackboard)();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                int separator = raw.LastIndexOf('-');
                if (separator <= 0) separator = raw.LastIndexOf(':');
                if (separator > 0 && separator < raw.Length - 1 &&
                    int.TryParse(raw.Substring(separator + 1), out int number))
                    return new EntityID(raw.Substring(0, separator), number);
            }

            string category = args.GetStringLazy("entityCategory", "", blackboard)();
            int entityNumber = args.GetIntLazy("entityNumber", 0, blackboard)();
            return string.IsNullOrWhiteSpace(category)
                ? EntityID.Null
                : new EntityID(category, entityNumber);
        }

        private static Vector2 ResolvePosition(AbilityContext ctx, ParamList args)
        {
            string mode = NormalizeToken(
                args.GetStringLazy("positionMode", "self", ctx.sharedBlackboard)());
            Vector2 position;
            switch (mode)
            {
                case "eventtarget":
                    Entity eventTarget = ResolveEventEntity(ctx.currentEvent);
                    position = eventTarget != null
                        ? eventTarget.Movement.Position
                        : ctx.entity != null ? ctx.entity.Movement.Position : Vector2.zero;
                    break;
                case "blackboard":
                    string key = args.GetStringLazy("positionKey", "", ctx.sharedBlackboard)();
                    position = ReadBlackboardPosition(ctx.sharedBlackboard, key);
                    break;
                case "fixed":
                    position = args.GetVector2IntLazy("position", default, ctx.sharedBlackboard)();
                    break;
                default:
                    position = ctx.entity != null ? ctx.entity.Movement.Position : Vector2.zero;
                    break;
            }
            Vector2Int offset = args.GetVector2IntLazy("offset", default, ctx.sharedBlackboard)();
            return position + (Vector2)offset;
        }

        private static Entity ResolveEventEntity(AbilityEvent evt)
        {
            if (evt is DamageEventBase damage) return damage.target;
            if (evt is HurtEventBase hurt) return hurt.origin;
            return null;
        }

        private static Vector2 ReadBlackboardPosition(Blackboard blackboard, string key)
        {
            if (blackboard == null || string.IsNullOrEmpty(key)) return Vector2.zero;
            object value = blackboard.Get<object>(key, null);
            if (value is Vector2 vector) return vector;
            if (value is Vector2Int vectorInt) return vectorInt;
            if (value is Entity entity && entity != null) return entity.Movement.Position;
            return Vector2.zero;
        }

        private static string NormalizeToken(string value) =>
            (value ?? string.Empty).Trim().ToLowerInvariant();
    }

    internal sealed class AbilityCompiledStep
    {
        public StepConfig Config;
        public string CanonicalOp;
        public Func<AbilityStepOp> Create;
        public AbilityCompiledStep[] Steps;
        public AbilityCompiledStep[] ElseSteps;
    }

    internal static class AbilityStepCompiler
    {
        public static AbilityCompiledStep[] Compile(
            AbilityRuntime runtime,
            StepConfig[] steps)
        {
            if (steps == null || steps.Length == 0) return Array.Empty<AbilityCompiledStep>();
            var result = new AbilityCompiledStep[steps.Length];
            for (int i = 0; i < steps.Length; i++)
            {
                StepConfig config = steps[i] ?? new StepConfig();
                Func<AbilityStepOp> factory = AbilityStepOpRegistry.Bind(
                    runtime,
                    config,
                    out string canonical);
                if (factory == null)
                {
                    OneShotWarn.WarnOnce(
                        "ability-step-op:" + (config.op ?? "<null>"),
                        $"[AbilityRuntime] Unknown step op '{config.op}' in ability " +
                        $"'{runtime.config?.abilityId}'. The step is skipped.");
                    factory = () => new MissingAbilityStepOp();
                }

                result[i] = new AbilityCompiledStep
                {
                    Config = config,
                    CanonicalOp = canonical,
                    Create = factory,
                    Steps = Compile(runtime, config.steps),
                    ElseSteps = Compile(runtime, config.elseSteps),
                };
            }
            return result;
        }
    }

    public sealed class AbilityRuleRuntime
    {
        private readonly AbilityRuntime _ability;
        private readonly AbilityCompiledStep[] _steps;
        private readonly List<AbilityStepExecution> _executions =
            new List<AbilityStepExecution>();
        private bool _isCancelling;

        public AbilityRuleConfig Config { get; }
        public int ActiveExecutionCount => _executions.Count;
        public bool IsRunning => _executions.Count > 0;

        internal AbilityRuleRuntime(AbilityRuntime ability, AbilityRuleConfig config)
        {
            _ability = ability;
            Config = config ?? new AbilityRuleConfig();
            _steps = AbilityStepCompiler.Compile(_ability, Config.steps);
        }

        public void Trigger(AbilityEvent evt, Blackboard sharedBlackboard, Entity entity)
        {
            if (_isCancelling || _ability.isCancellingStepExecutions) return;
            RemoveCompleted();
            switch (Config.reentry)
            {
                case RuleReentry.IgnoreWhileRunning:
                    if (_executions.Count > 0) return;
                    break;
                case RuleReentry.Restart:
                    CancelAll();
                    break;
                case RuleReentry.Parallel:
                    break;
            }

            var execution = new AbilityStepExecution(
                _ability,
                this,
                _steps,
                evt,
                sharedBlackboard,
                entity);
            _executions.Add(execution);
            execution.Tick(0f);
            RemoveCompleted();
        }

        public void Tick(float deltaTime)
        {
            int executionCountAtTickStart = _executions.Count;
            for (int i = 0; i < executionCountAtTickStart && i < _executions.Count; i++)
                _executions[i].Tick(deltaTime);
            RemoveCompleted();
        }

        internal void AppendExecutionSnapshot(List<AbilityStepExecution> destination)
        {
            destination.AddRange(_executions);
        }

        internal void PruneCompletedExecutions()
        {
            RemoveCompleted();
        }

        public void CancelAll()
        {
            if (_isCancelling) return;
            _isCancelling = true;
            AbilityStepExecution[] snapshot = _executions.ToArray();
            _executions.Clear();
            try
            {
                for (int i = 0; i < snapshot.Length; i++) snapshot[i].Cancel();
            }
            finally
            {
                _isCancelling = false;
            }
        }

        private void RemoveCompleted()
        {
            for (int i = _executions.Count - 1; i >= 0; i--)
                if (_executions[i].IsComplete) _executions.RemoveAt(i);
        }
    }

    public sealed class AbilityStepExecution
    {
        private const int MaxSynchronousStepsPerPump = 1024;

        private sealed class LoopCursor
        {
            public AbilityCompiledStep Step;
            public int Count;
            public int StartedIterations;
        }

        private sealed class SequenceFrame
        {
            public AbilityCompiledStep[] Steps;
            public int Index;
            public LoopCursor Loop;
        }

        private readonly AbilityRuntime _ability;
        private readonly AbilityRuleRuntime _rule;
        private readonly AbilityEvent _event;
        private readonly Blackboard _blackboard;
        private readonly Entity _entity;
        private readonly List<SequenceFrame> _frames = new List<SequenceFrame>();

        private AbilityCompiledStep _activeStep;
        private AbilityStepOp _activeOp;
        private AbilityContext _activeContext;

        public AbilityRuleRuntime Rule => _rule;
        public AbilityEvent TriggerEvent => _event;
        public bool IsComplete { get; private set; }
        public bool WasCancelled { get; private set; }
        public bool Failed { get; private set; }

        internal AbilityStepExecution(
            AbilityRuntime ability,
            AbilityRuleRuntime rule,
            AbilityCompiledStep[] rootSteps,
            AbilityEvent evt,
            Blackboard blackboard,
            Entity entity)
        {
            _ability = ability;
            _rule = rule;
            _event = evt;
            _blackboard = blackboard ?? new Blackboard();
            _entity = entity;
            PushSequence(rootSteps, null);
        }

        public void Tick(float deltaTime)
        {
            if (IsComplete) return;
            int budget = MaxSynchronousStepsPerPump;

            if (_activeOp != null)
            {
                AbilityStepOp tickingOp = _activeOp;
                AbilityContext tickingContext = _activeContext;
                AbilityStepStatus activeStatus = tickingOp.OnTick(
                    tickingContext,
                    Mathf.Max(0f, deltaTime));
                if (IsComplete || !ReferenceEquals(_activeOp, tickingOp)) return;
                if (activeStatus == AbilityStepStatus.Running) return;
                FinishActive(activeStatus);
                if (IsComplete) return;
            }

            while (!IsComplete && budget-- > 0)
            {
                if (!TryTakeNextStep(out AbilityCompiledStep step))
                {
                    IsComplete = true;
                    return;
                }

                _activeStep = step;
                _activeOp = step.Create();
                _activeContext = MakeContext(step, _activeOp);
                AbilityStepOp startingOp = _activeOp;
                AbilityContext startingContext = _activeContext;
                startingOp.OnInit(startingContext);
                if (IsComplete || !ReferenceEquals(_activeOp, startingOp)) return;
                AbilityStepStatus status = startingOp.OnTick(startingContext, 0f);
                if (IsComplete || !ReferenceEquals(_activeOp, startingOp)) return;
                if (status == AbilityStepStatus.Running) return;

                AbilityStepOp completedOp = _activeOp;
                AbilityCompiledStep completedStep = _activeStep;
                AbilityContext completedContext = _activeContext;
                FinishActive(status);
                if (IsComplete) return;

                if (completedOp is BranchAbilityStepOp branch)
                {
                    PushSequence(
                        branch.TakeThenBranch ? completedStep.Steps : completedStep.ElseSteps,
                        null);
                }
                else if (completedOp is LoopAbilityStepOp loop)
                {
                    var cursor = new LoopCursor
                    {
                        Step = completedStep,
                        Count = loop.Count,
                    };
                    if (completedStep.Steps.Length == 0)
                    {
                        OneShotWarn.WarnOnce(
                            "ability-loop-empty:" + (_ability.config?.abilityId ?? "<unknown>"),
                            $"[AbilityRuntime] Empty loop body in ability " +
                            $"'{_ability.config?.abilityId}' is skipped.");
                    }
                    else if (ShouldStartIteration(cursor, completedContext))
                        PushSequence(completedStep.Steps, cursor);
                }
            }
        }

        public void Cancel()
        {
            if (IsComplete) return;
            WasCancelled = true;
            _frames.Clear();
            IsComplete = true;
            AbilityStepOp op = _activeOp;
            AbilityContext context = _activeContext;
            ClearActive();
            if (op != null)
            {
                op.OnCancel(context);
                op.OnTeardown(context);
            }
        }

        private AbilityContext MakeContext(AbilityCompiledStep step, AbilityStepOp op)
        {
            AbilityContext ctx = _ability.MakeContext(
                op.BoundComponent,
                _event,
                _blackboard,
                _entity);
            ctx.step = step.Config;
            ctx.stepExecution = this;
            return ctx;
        }

        private void FinishActive(AbilityStepStatus status)
        {
            AbilityStepOp op = _activeOp;
            AbilityContext context = _activeContext;
            ClearActive();
            op.OnTeardown(context);
            if (IsComplete) return;
            if (status == AbilityStepStatus.Failed)
            {
                Failed = true;
                _frames.Clear();
                IsComplete = true;
            }
        }

        private void ClearActive()
        {
            _activeStep = null;
            _activeOp = null;
            _activeContext = null;
        }

        private bool TryTakeNextStep(out AbilityCompiledStep step)
        {
            while (_frames.Count > 0)
            {
                SequenceFrame frame = _frames[_frames.Count - 1];
                if (frame.Index < frame.Steps.Length)
                {
                    step = frame.Steps[frame.Index++];
                    return true;
                }

                if (frame.Loop != null)
                {
                    AbilityContext loopContext = _ability.MakeContext(
                        null,
                        _event,
                        _blackboard,
                        _entity);
                    loopContext.step = frame.Loop.Step.Config;
                    loopContext.stepExecution = this;
                    if (ShouldStartIteration(frame.Loop, loopContext))
                    {
                        frame.Index = 0;
                        continue;
                    }
                }
                _frames.RemoveAt(_frames.Count - 1);
            }
            step = null;
            return false;
        }

        private bool ShouldStartIteration(LoopCursor cursor, AbilityContext ctx)
        {
            if (cursor.Count >= 0 && cursor.StartedIterations >= cursor.Count)
                return false;
            ctx.step = cursor.Step.Config;
            if (!WaitUntilAbilityStepOp.EvaluateStepCondition(ctx)) return false;
            cursor.StartedIterations++;
            return true;
        }

        private void PushSequence(AbilityCompiledStep[] steps, LoopCursor loop)
        {
            _frames.Add(new SequenceFrame
            {
                Steps = steps ?? Array.Empty<AbilityCompiledStep>(),
                Loop = loop,
            });
        }
    }
}
