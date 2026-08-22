using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace AbilitySystem.Tests
{
    public class AbilityStepRuntimeTests
    {
        private const string RecordOp = "test_record_step";
        private const string IncrementOp = "test_increment_step";
        private const string RestartOnInitOp = "test_restart_on_init_step";
        private const string TriggerRuleOp = "test_trigger_rule_step";
        private static readonly List<string> Trace = new List<string>();
        private static int _restartsRemaining;

        private sealed class RecordingStepOp : AbilityStepOp
        {
            public override void OnInit(AbilityContext ctx)
            {
                Trace.Add(ctx.step.args.GetString("value"));
            }

            public override AbilityStepStatus OnTick(AbilityContext ctx, float deltaTime) =>
                AbilityStepStatus.Completed;
        }

        private sealed class IncrementStepOp : AbilityStepOp
        {
            public override void OnInit(AbilityContext ctx)
            {
                string key = ctx.step.args.GetString("key");
                string raw = ctx.sharedBlackboard.Get(key, "0");
                int value = int.TryParse(raw, out int parsed) ? parsed : 0;
                ctx.sharedBlackboard.Set(key, (value + 1).ToString());
            }

            public override AbilityStepStatus OnTick(AbilityContext ctx, float deltaTime) =>
                AbilityStepStatus.Completed;
        }

        private sealed class RestartOnInitStepOp : AbilityStepOp
        {
            public override void OnInit(AbilityContext ctx)
            {
                Trace.Add("enter");
                if (_restartsRemaining <= 0) return;
                _restartsRemaining--;
                ctx.stepExecution.Rule.Trigger(
                    ctx.currentEvent,
                    ctx.sharedBlackboard,
                    ctx.entity);
            }

            public override AbilityStepStatus OnTick(AbilityContext ctx, float deltaTime) =>
                AbilityStepStatus.Completed;
        }

        private sealed class TriggerRuleStepOp : AbilityStepOp
        {
            public override void OnInit(AbilityContext ctx)
            {
                int ruleIndex = ctx.step.args.GetInt("ruleIndex", -1);
                if (ruleIndex < 0 || ruleIndex >= ctx.ability.ruleRuntimes.Count) return;
                ctx.ability.ruleRuntimes[ruleIndex].Trigger(
                    ctx.currentEvent,
                    ctx.sharedBlackboard,
                    ctx.entity);
            }

            public override AbilityStepStatus OnTick(AbilityContext ctx, float deltaTime) =>
                AbilityStepStatus.Completed;
        }

        [OneTimeSetUp]
        public void RegisterTestOps()
        {
            if (!AbilityStepOpRegistry.IsRegistered(RecordOp))
                AbilityStepOpRegistry.Register(RecordOp, () => new RecordingStepOp());
            if (!AbilityStepOpRegistry.IsRegistered(IncrementOp))
                AbilityStepOpRegistry.Register(IncrementOp, () => new IncrementStepOp());
            if (!AbilityStepOpRegistry.IsRegistered(RestartOnInitOp))
                AbilityStepOpRegistry.Register(RestartOnInitOp, () => new RestartOnInitStepOp());
            if (!AbilityStepOpRegistry.IsRegistered(TriggerRuleOp))
                AbilityStepOpRegistry.Register(TriggerRuleOp, () => new TriggerRuleStepOp());
        }

        [SetUp]
        public void ClearTrace()
        {
            Trace.Clear();
        }

        [Test]
        public void Registry_AllowsPocoExtensionAndKeepsAliasOutOfCanonicalList()
        {
            string suffix = Guid.NewGuid().ToString("N");
            string canonical = "test_registry_extension_" + suffix;
            string alias = "TestRegistryExtension_" + suffix;

            AbilityStepOpRegistry.Register(canonical, () => new RecordingStepOp(), alias);

            Assert.That(AbilityStepOpRegistry.IsRegistered(canonical), Is.True);
            Assert.That(AbilityStepOpRegistry.IsRegistered(alias), Is.True);
            Assert.That(AbilityStepOpRegistry.ResolveCanonical(alias), Is.EqualTo(canonical));
            Assert.That(AbilityStepOpRegistry.Create(canonical), Is.TypeOf<RecordingStepOp>());
            Assert.That(AbilityStepOpRegistry.RegisteredOps, Does.Contain(canonical));
            Assert.That(AbilityStepOpRegistry.RegisteredOps, Does.Not.Contain(alias));
        }

        [Test]
        public void Sequence_RunsImmediateStepsSynchronouslyInOrder()
        {
            AbilityRuleRuntime rule = BuildRule(
                RuleReentry.IgnoreWhileRunning,
                Record("select"),
                Record("damage"),
                Record("buff"));

            rule.Trigger(null, new Blackboard(), null);

            CollectionAssert.AreEqual(new[] { "select", "damage", "buff" }, Trace);
            Assert.That(rule.IsRunning, Is.False);
        }

        [Test]
        public void Delay_PausesOnlyUntilAccumulatedTickTimeReachesSeconds()
        {
            AbilityRuleRuntime rule = BuildRule(
                RuleReentry.IgnoreWhileRunning,
                Record("before"),
                Step("delay", Arg("seconds", "0.3", ParamValueType.Float)),
                Record("after"));

            rule.Trigger(null, new Blackboard(), null);
            CollectionAssert.AreEqual(new[] { "before" }, Trace);
            Assert.That(rule.IsRunning, Is.True);

            rule.Tick(0.29f);
            CollectionAssert.AreEqual(new[] { "before" }, Trace);

            rule.Tick(0.02f);
            CollectionAssert.AreEqual(new[] { "before", "after" }, Trace);
            Assert.That(rule.IsRunning, Is.False);
        }

        [Test]
        public void WaitUntil_ContinuesInSameTickWhenConditionBecomesTrue()
        {
            var blackboard = new Blackboard();
            StepConfig wait = Step("wait_until");
            wait.condition = EqualCondition("ready", "yes");
            AbilityRuleRuntime rule = BuildRule(
                RuleReentry.IgnoreWhileRunning,
                Record("before"),
                wait,
                Record("after"));

            rule.Trigger(null, blackboard, null);
            CollectionAssert.AreEqual(new[] { "before" }, Trace);

            rule.Tick(1f);
            CollectionAssert.AreEqual(new[] { "before" }, Trace);

            blackboard.Set("ready", "yes");
            rule.Tick(0f);
            CollectionAssert.AreEqual(new[] { "before", "after" }, Trace);
            Assert.That(rule.IsRunning, Is.False);
        }

        [TestCase("yes", "then")]
        [TestCase("no", "else")]
        public void Branch_RunsOnlySelectedNestedSequence(string value, string expected)
        {
            var blackboard = new Blackboard();
            blackboard.Set("take_then", value);
            StepConfig branch = Step("branch");
            branch.condition = EqualCondition("take_then", "yes");
            branch.steps = new[] { Record("then") };
            branch.elseSteps = new[] { Record("else") };
            AbilityRuleRuntime rule = BuildRule(RuleReentry.IgnoreWhileRunning, branch);

            rule.Trigger(null, blackboard, null);

            CollectionAssert.AreEqual(new[] { expected }, Trace);
            Assert.That(rule.IsRunning, Is.False);
        }

        [Test]
        public void Loop_WithCount_RunsNestedSequenceExactlyCountTimes()
        {
            StepConfig loop = Step("loop", Arg("count", "3", ParamValueType.Int));
            loop.steps = new[] { Record("body") };
            AbilityRuleRuntime rule = BuildRule(RuleReentry.IgnoreWhileRunning, loop);

            rule.Trigger(null, new Blackboard(), null);

            CollectionAssert.AreEqual(new[] { "body", "body", "body" }, Trace);
            Assert.That(rule.IsRunning, Is.False);
        }

        [Test]
        public void Loop_WithCondition_ReevaluatesBeforeEveryIteration()
        {
            var blackboard = new Blackboard();
            blackboard.Set("iterations", "0");
            StepConfig loop = Step("loop");
            loop.condition = CompareCondition(
                ConditionOp.Less,
                "iterations",
                "3");
            loop.steps = new[]
            {
                Step(IncrementOp, Arg("key", "iterations")),
            };
            AbilityRuleRuntime rule = BuildRule(RuleReentry.IgnoreWhileRunning, loop);

            rule.Trigger(null, blackboard, null);

            Assert.That(blackboard.Get("iterations", "missing"), Is.EqualTo("3"));
            Assert.That(rule.IsRunning, Is.False);
        }

        [TestCase(RuleReentry.IgnoreWhileRunning, 1, 1)]
        [TestCase(RuleReentry.Restart, 2, 1)]
        [TestCase(RuleReentry.Parallel, 2, 2)]
        public void ReentryPolicy_ControlsSecondTrigger(
            RuleReentry reentry,
            int expectedStarts,
            int expectedFinishes)
        {
            AbilityRuleRuntime rule = BuildRule(
                reentry,
                Record("start"),
                Step("delay", Arg("seconds", "0.5", ParamValueType.Float)),
                Record("finish"));
            var blackboard = new Blackboard();

            rule.Trigger(null, blackboard, null);
            rule.Trigger(null, blackboard, null);

            Assert.That(Trace.Count(x => x == "start"), Is.EqualTo(expectedStarts));
            Assert.That(rule.ActiveExecutionCount,
                Is.EqualTo(reentry == RuleReentry.Parallel ? 2 : 1));

            rule.Tick(0.5f);

            Assert.That(Trace.Count(x => x == "finish"), Is.EqualTo(expectedFinishes));
            Assert.That(rule.IsRunning, Is.False);
        }

        [Test]
        public void Restart_ReenteredSynchronouslyFromOnInit_CancelsOldExecutionSafely()
        {
            _restartsRemaining = 1;
            AbilityRuleRuntime rule = BuildRule(
                RuleReentry.Restart,
                Step(RestartOnInitOp),
                Record("finish"));

            Assert.DoesNotThrow(() => rule.Trigger(null, new Blackboard(), null));

            CollectionAssert.AreEqual(new[] { "enter", "enter", "finish" }, Trace);
            Assert.That(rule.IsRunning, Is.False);
        }

        [TestCase(0, 1)]
        [TestCase(1, 0)]
        public void RuntimeTick_NewCrossRuleExecutionWaitsUntilNextTickRegardlessOfRuleOrder(
            int sourceRuleIndex,
            int targetRuleIndex)
        {
            var runtime = new AbilityRuntime();
            var rules = new AbilityRuleConfig[2];
            rules[sourceRuleIndex] = Rule(
                Step("delay", Arg("seconds", "0.1", ParamValueType.Float)),
                Step(
                    TriggerRuleOp,
                    Arg("ruleIndex", targetRuleIndex.ToString(), ParamValueType.Int)));
            rules[targetRuleIndex] = Rule(
                Step("delay", Arg("seconds", "0.1", ParamValueType.Float)),
                Record("target_finished"));
            runtime.BuildRules(rules);

            var blackboard = new Blackboard();
            runtime.ruleRuntimes[sourceRuleIndex].Trigger(null, blackboard, null);
            runtime.TickStepExecutions(0.1f);

            Assert.That(Trace, Is.Empty,
                "An execution created during this tick must not consume this tick's deltaTime.");
            Assert.That(runtime.ruleRuntimes[targetRuleIndex].IsRunning, Is.True);

            runtime.TickStepExecutions(0.1f);

            CollectionAssert.AreEqual(new[] { "target_finished" }, Trace);
            Assert.That(runtime.ruleRuntimes[targetRuleIndex].IsRunning, Is.False);
        }

        [Test]
        public void SetActive_BeginCancelsUnfinishedEndLifetimeExecution()
        {
            var runtime = new AbilityRuntime();
            runtime.BuildRules(new[]
            {
                Rule(
                    Step("delay", Arg("seconds", "0.5", ParamValueType.Float)),
                    Record("stale_end_tail")),
            });
            var blackboard = new Blackboard();
            AbilityRuleRuntime endRule = runtime.ruleRuntimes[0];
            runtime.OnAbilityEnd += () => endRule.Trigger(
                new AbilityEndEvent { ability = runtime },
                blackboard,
                null);

            runtime.SetActive(true);
            runtime.SetActive(false);
            Assert.That(endRule.IsRunning, Is.True);

            runtime.SetActive(true);
            Assert.That(endRule.IsRunning, Is.False);

            runtime.TickStepExecutions(1f);
            Assert.That(Trace, Is.Empty);
        }

        private static AbilityRuleRuntime BuildRule(
            RuleReentry reentry,
            params StepConfig[] steps)
        {
            var runtime = new AbilityRuntime();
            runtime.BuildRules(new[]
            {
                new AbilityRuleConfig
                {
                    reentry = reentry,
                    triggers = new[]
                    {
                        new ConditionConfig { triggerEvent = TriggerEvent.OnInitialize },
                    },
                    steps = steps,
                },
            });
            return runtime.ruleRuntimes[0];
        }

        private static AbilityRuleConfig Rule(params StepConfig[] steps) =>
            new AbilityRuleConfig
            {
                reentry = RuleReentry.IgnoreWhileRunning,
                triggers = new[]
                {
                    new ConditionConfig { triggerEvent = TriggerEvent.OnInitialize },
                },
                steps = steps ?? Array.Empty<StepConfig>(),
            };

        private static StepConfig Record(string value) =>
            Step(RecordOp, Arg("value", value));

        private static StepConfig Step(string op, params ParamEntry[] args) =>
            new StepConfig
            {
                op = op,
                args = new ParamList { entries = args ?? Array.Empty<ParamEntry>() },
            };

        private static ParamEntry Arg(
            string key,
            string value,
            ParamValueType type = ParamValueType.String) =>
            new ParamEntry { key = key, value = value, type = type };

        private static List<ConditionGroup> EqualCondition(string key, string value) =>
            CompareCondition(ConditionOp.Equal, key, value);

        private static List<ConditionGroup> CompareCondition(
            ConditionOp op,
            string key,
            string value) =>
            new List<ConditionGroup>
            {
                new ConditionGroup
                {
                    units = new List<ConditionUnit>
                    {
                        new ConditionUnit
                        {
                            op = op,
                            leftKey = key,
                            rightValue = value,
                        },
                    },
                },
            };
    }
}
