using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AbilitySystem.Tests
{
    public class AbilityStepAssetTests
    {
        private const string RoundTripAssetPath =
            "Assets/Tests/Editor/AbilitySystem/__AbilityStepRoundTrip.asset";

        [TearDown]
        public void DeleteGeneratedAsset()
        {
            if (AssetDatabase.LoadMainAssetAtPath(RoundTripAssetPath) != null)
                AssetDatabase.DeleteAsset(RoundTripAssetPath);
        }

        [Test]
        public void MigratedAbilityAssets_HaveExpectedShapeAndRegisteredCanonicalOps()
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:AbilityConfig",
                new[] { "Assets/Resources" });
            Array.Sort(guids, StringComparer.Ordinal);

            Assert.That(guids, Has.Length.EqualTo(20),
                "The migrated Resources inventory must contain exactly 20 AbilityConfig assets.");

            int ruleCount = 0;
            int stepCount = 0;
            int triggerCount = 0;
            var errors = new List<string>();

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                AbilityConfig config = AssetDatabase.LoadAssetAtPath<AbilityConfig>(path);
                if (config == null)
                {
                    errors.Add($"{path}: failed to load AbilityConfig");
                    continue;
                }

                AbilityRuleConfig[] rules = config.rules ?? Array.Empty<AbilityRuleConfig>();
                ruleCount += rules.Length;
                for (int r = 0; r < rules.Length; r++)
                {
                    AbilityRuleConfig rule = rules[r];
                    if (rule == null)
                    {
                        errors.Add($"{path}: rules[{r}] is null");
                        continue;
                    }

                    triggerCount += rule.triggers?.Length ?? 0;
                    StepConfig[] steps = rule.steps ?? Array.Empty<StepConfig>();
                    stepCount += steps.Length;
                    ValidateOpsRecursive(path, $"rules[{r}].steps", steps, errors);
                }
            }

            Assert.That(errors, Is.Empty, string.Join("\n", errors));
            Assert.That(ruleCount, Is.EqualTo(46));
            Assert.That(stepCount, Is.EqualTo(101));
            Assert.That(triggerCount, Is.EqualTo(50));
        }

        [Test]
        public void NestedSteps_RoundTripThroughUnityManagedReferenceSerialization()
        {
            AssetDatabase.DeleteAsset(RoundTripAssetPath);
            var config = ScriptableObject.CreateInstance<AbilityConfig>();
            config.abilityId = "test_nested_step_round_trip";
            config.rules = new[]
            {
                new AbilityRuleConfig
                {
                    triggers = new[]
                    {
                        new ConditionConfig { triggerEvent = TriggerEvent.OnInitialize },
                    },
                    steps = new[]
                    {
                        new StepConfig
                        {
                            op = "branch",
                            args = Args(Arg("label", "root-branch")),
                            condition = EqualCondition("route", "then"),
                            steps = new[]
                            {
                                new StepConfig
                                {
                                    op = "loop",
                                    args = Args(Arg("count", "2", ParamValueType.Int)),
                                    steps = new[]
                                    {
                                        new StepConfig
                                        {
                                            op = "write_blackboard",
                                            args = Args(
                                                Arg("key", "round_trip_key"),
                                                Arg("value", "round_trip_value")),
                                        },
                                    },
                                },
                            },
                            elseSteps = new[]
                            {
                                new StepConfig
                                {
                                    op = "delay",
                                    args = Args(Arg("seconds", "0.25", ParamValueType.Float)),
                                },
                            },
                        },
                    },
                },
            };

            AssetDatabase.CreateAsset(config, RoundTripAssetPath);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(config);
            AssetDatabase.ImportAsset(
                RoundTripAssetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            AbilityConfig loaded =
                AssetDatabase.LoadAssetAtPath<AbilityConfig>(RoundTripAssetPath);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded, Is.Not.SameAs(config));
            Assert.That(loaded.rules, Has.Length.EqualTo(1));
            Assert.That(loaded.rules[0].steps, Has.Length.EqualTo(1));

            StepConfig branch = loaded.rules[0].steps[0];
            Assert.That(branch.op, Is.EqualTo("branch"));
            Assert.That(branch.args.GetString("label"), Is.EqualTo("root-branch"));
            Assert.That(branch.condition[0].units[0].leftKey, Is.EqualTo("route"));
            Assert.That(branch.steps, Has.Length.EqualTo(1));
            Assert.That(branch.elseSteps, Has.Length.EqualTo(1));

            StepConfig loop = branch.steps[0];
            Assert.That(loop.op, Is.EqualTo("loop"));
            Assert.That(loop.args.GetInt("count"), Is.EqualTo(2));
            Assert.That(loop.steps, Has.Length.EqualTo(1));
            Assert.That(loop.steps[0].op, Is.EqualTo("write_blackboard"));
            Assert.That(
                loop.steps[0].args.GetString("key"),
                Is.EqualTo("round_trip_key"));
            Assert.That(
                loop.steps[0].args.GetString("value"),
                Is.EqualTo("round_trip_value"));

            Assert.That(branch.elseSteps[0].op, Is.EqualTo("delay"));
            Assert.That(branch.elseSteps[0].args.GetFloat("seconds"), Is.EqualTo(0.25f));
        }

        private static void ValidateOpsRecursive(
            string path,
            string location,
            StepConfig[] steps,
            ICollection<string> errors)
        {
            if (steps == null) return;
            for (int i = 0; i < steps.Length; i++)
            {
                StepConfig step = steps[i];
                string stepLocation = $"{location}[{i}]";
                if (step == null)
                {
                    errors.Add($"{path}: {stepLocation} is null");
                    continue;
                }

                if (!AbilityStepOpRegistry.IsRegistered(step.op))
                {
                    errors.Add($"{path}: {stepLocation} has unknown op '{step.op}'");
                }
                else if (AbilityStepOpRegistry.ResolveCanonical(step.op) != step.op)
                {
                    errors.Add(
                        $"{path}: {stepLocation} uses legacy alias '{step.op}' instead of " +
                        $"'{AbilityStepOpRegistry.ResolveCanonical(step.op)}'");
                }

                ValidateOpsRecursive(path, stepLocation + ".steps", step.steps, errors);
                ValidateOpsRecursive(path, stepLocation + ".elseSteps", step.elseSteps, errors);
            }
        }

        private static ParamList Args(params ParamEntry[] entries) =>
            new ParamList { entries = entries ?? Array.Empty<ParamEntry>() };

        private static ParamEntry Arg(
            string key,
            string value,
            ParamValueType type = ParamValueType.String) =>
            new ParamEntry { key = key, value = value, type = type };

        private static List<ConditionGroup> EqualCondition(string key, string value) =>
            new List<ConditionGroup>
            {
                new ConditionGroup
                {
                    units = new List<ConditionUnit>
                    {
                        new ConditionUnit
                        {
                            op = ConditionOp.Equal,
                            leftKey = key,
                            rightValue = value,
                        },
                    },
                },
            };
    }
}
