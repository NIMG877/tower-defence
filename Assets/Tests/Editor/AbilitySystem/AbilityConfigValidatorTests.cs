using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace AbilitySystem.Tests
{
    public class AbilityConfigValidatorTests
    {
        private static AbilityConfigDto ValidDto()
        {
            // 与 EntityAbilityRunnerSkillTests 的真管线规则同构：OnInitialize 写黑板。
            return new AbilityConfigDto
            {
                abilityId = "gen_validate_1",
                abilityName = "校验样例",
                description = "d",
                rules = new[]
                {
                    new AbilityRuleConfig
                    {
                        triggers = new[]
                        {
                            new ConditionConfig { triggerEvent = TriggerEvent.OnInitialize, groups = new List<ConditionGroup>() },
                        },
                        steps = new[]
                        {
                            new StepConfig
                            {
                                op = "write_blackboard",
                                args = new ParamList
                                {
                                    entries = new[]
                                    {
                                        new ParamEntry { key = "key", value = "probe", type = ParamValueType.String },
                                        new ParamEntry { key = "method", value = "set", type = ParamValueType.String },
                                        new ParamEntry { key = "source", value = "value", type = ParamValueType.String },
                                        new ParamEntry { key = "value", value = "1", type = ParamValueType.Int },
                                    },
                                },
                            },
                        },
                    },
                },
            };
        }

        [Test]
        public void ValidDto_Passes_WithoutIssues()
        {
            AbilityConfigValidator.Result result = AbilityConfigValidator.Validate(ValidDto());
            Assert.IsTrue(result.Ok, string.Join("\n", result.Issues.Select(i => i.ToString())));
            Assert.IsEmpty(result.Issues);
            Assert.AreEqual(1, result.Sanitized.rules.Length);
        }

        [Test]
        public void HostAssetBounds_OutOfRangeReferences_AreErrors()
        {
            // 与服务端 P0-2 同款边界（M2 终检）：越界引用在注入前拒绝。
            AbilityConfigDto dto = ValidDto();
            dto.rules[0].steps = new[]
            {
                new StepConfig
                {
                    op = "spawn_entity",
                    args = new ParamList
                    {
                        entries = new[] { new ParamEntry { key = "spawnIndex", value = "3", type = ParamValueType.Int } },
                    },
                },
            };
            var host = new HostAssets
            {
                canSpawnEntities = new List<HostAssets.SpawnableEntry>
                {
                    new HostAssets.SpawnableEntry { id = "s-0" },
                },
            };
            AbilityConfigValidator.Result result = AbilityConfigValidator.Validate(dto, host);
            Assert.IsFalse(result.Ok);
            Assert.IsTrue(result.Issues.Any(i => i.Message.Contains("spawnIndex=3 out of range")));

            // 在界下标 → 通过
            dto.rules[0].steps[0].args.entries[0].value = "0";
            Assert.IsTrue(AbilityConfigValidator.Validate(dto, host).Ok);

            // fire_bullets 越界 bulletDataIndex → error
            dto.rules[0].steps[0].op = "fire_bullets";
            dto.rules[0].steps[0].args.entries[0] =
                new ParamEntry { key = "bulletDataIndex", value = "2", type = ParamValueType.Int };
            host.bullets = new List<HostAssets.BulletEntry>
            {
                new HostAssets.BulletEntry { index = 0 },
            };
            result = AbilityConfigValidator.Validate(dto, host);
            Assert.IsFalse(result.Ok);
            Assert.IsTrue(result.Issues.Any(i => i.Message.Contains("bulletDataIndex=2 out of range")));

            // apply_animation_override 引用清单外动画名 → error
            dto.rules[0].steps[0].op = "apply_animation_override";
            dto.rules[0].steps[0].args.entries = new[]
            {
                new ParamEntry { key = "slots", value = "AttackRemote", type = ParamValueType.String },
                new ParamEntry { key = "resources", value = "attack_far,attack_cut", type = ParamValueType.String },
            };
            host.animations = new HostAssets.AnimationNames
            {
                named = new List<string> { "attack_far" },
                groups = new List<string> { "attack_cut" },
            };
            host.bullets.Clear();
            dto.rules[0].steps[0].op = "apply_animation_override";
            Assert.IsTrue(AbilityConfigValidator.Validate(dto, host).Ok,
                "两个动画名都在清单内，不应报错");

            host.animations.named.Clear();
            result = AbilityConfigValidator.Validate(dto, host);
            Assert.IsFalse(result.Ok);
            Assert.IsTrue(result.Issues.Any(i => i.Message.Contains("attack_far") && i.Message.Contains("hostAssets.animations")));

            // fromBlackboard 的下标是黑板键名，运行时才解析 → 不做静态边界检查
            dto.rules[0].steps[0].op = "spawn_entity";
            dto.rules[0].steps[0].args.entries = new[]
            {
                new ParamEntry { key = "spawnIndex", value = "enemy_idx", type = ParamValueType.String, fromBlackboard = true },
            };
            Assert.IsTrue(AbilityConfigValidator.Validate(dto, host).Ok);
        }

        [Test]
        public void NullDto_IsError()
        {
            AbilityConfigValidator.Result result = AbilityConfigValidator.Validate(null);
            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void UnknownOp_IsError_AndStepKeptForDiagnostics()
        {
            AbilityConfigDto dto = ValidDto();
            dto.rules[0].steps[0].op = "make_big_explosion";
            AbilityConfigValidator.Result result = AbilityConfigValidator.Validate(dto);
            Assert.IsFalse(result.Ok);
            AbilityConfigValidator.Issue issue = result.Issues.Single(i => i.IsError);
            StringAssert.Contains("make_big_explosion", issue.Message);
            // 未解析步骤按原样保留（整技能已拒绝，保留仅为诊断）。
            Assert.AreEqual("make_big_explosion", result.Sanitized.rules[0].steps[0].op);
        }

        [Test]
        public void EmptyTriggers_IsError()
        {
            AbilityConfigDto dto = ValidDto();
            dto.rules[0].triggers = System.Array.Empty<ConditionConfig>();
            AbilityConfigValidator.Result result = AbilityConfigValidator.Validate(dto);
            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void EmptyRules_IsError()
        {
            AbilityConfigDto dto = ValidDto();
            dto.rules = System.Array.Empty<AbilityRuleConfig>();
            AbilityConfigValidator.Result result = AbilityConfigValidator.Validate(dto);
            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void UnknownParamKey_IsDropped_WithWarning()
        {
            AbilityConfigDto dto = ValidDto();
            dto.rules[0].steps[0].args.entries = dto.rules[0].steps[0].args.entries
                .Concat(new[] { new ParamEntry { key = "frobnicate", value = "1", type = ParamValueType.Int } })
                .ToArray();
            AbilityConfigValidator.Result result = AbilityConfigValidator.Validate(dto);
            Assert.IsTrue(result.Ok);
            Assert.IsTrue(result.Issues.Any(i => i.Message.Contains("frobnicate")));
            Assert.IsFalse(result.Sanitized.rules[0].steps[0].args.entries.Any(e => e.key == "frobnicate"));
            // 已知键全部保留。
            Assert.AreEqual(4, result.Sanitized.rules[0].steps[0].args.entries.Length);
        }

        [Test]
        public void OutOfRangeFloat_IsClamped_WithWarning()
        {
            AbilityConfigDto dto = ValidDto();
            dto.rules[0].steps = new[]
            {
                new StepConfig
                {
                    op = "apply_damage",
                    args = new ParamList
                    {
                        entries = new[]
                        {
                            new ParamEntry { key = "multiplier", value = "5000", type = ParamValueType.Float },
                            new ParamEntry { key = "baseValueMode", value = "fixed", type = ParamValueType.String },
                            new ParamEntry { key = "baseValue", value = "120", type = ParamValueType.Float },
                        },
                    },
                },
            };
            AbilityConfigValidator.Result result = AbilityConfigValidator.Validate(dto);
            Assert.IsTrue(result.Ok, string.Join("\n", result.Issues.Select(i => i.ToString())));
            Assert.AreEqual("1000", result.Sanitized.rules[0].steps[0].args.GetRaw("multiplier"));
            Assert.IsTrue(result.Issues.Any(i => i.Message.Contains("clamped")));
            Assert.AreEqual("120", result.Sanitized.rules[0].steps[0].args.GetRaw("baseValue"));
        }

        [Test]
        public void UnparsableValue_IsWarning_ValueKept()
        {
            AbilityConfigDto dto = ValidDto();
            dto.rules[0].steps = new[]
            {
                new StepConfig
                {
                    op = "delay",
                    args = new ParamList
                    {
                        entries = new[] { new ParamEntry { key = "seconds", value = "soon", type = ParamValueType.Float } },
                    },
                },
            };
            AbilityConfigValidator.Result result = AbilityConfigValidator.Validate(dto);
            Assert.IsTrue(result.Ok);
            Assert.IsTrue(result.Issues.Any(i => i.Message.Contains("does not parse")));
            Assert.AreEqual("soon", result.Sanitized.rules[0].steps[0].args.GetRaw("seconds"));
        }

        [Test]
        public void TokenOutsideVocabulary_IsWarning()
        {
            AbilityConfigDto dto = ValidDto();
            dto.rules[0].steps = new[]
            {
                new StepConfig
                {
                    op = "apply_damage",
                    args = new ParamList
                    {
                        entries = new[] { new ParamEntry { key = "targetMode", value = "everyone", type = ParamValueType.String } },
                    },
                },
            };
            AbilityConfigValidator.Result result = AbilityConfigValidator.Validate(dto);
            Assert.IsTrue(result.Ok);
            Assert.IsTrue(result.Issues.Any(i => i.Message.Contains("outside the accepted tokens")));
        }

        [Test]
        public void ConditionOnComponentOp_IsWarning()
        {
            AbilityConfigDto dto = ValidDto();
            dto.rules[0].steps[0].condition = new List<ConditionGroup>
            {
                new ConditionGroup
                {
                    units = new List<ConditionUnit>
                    {
                        new ConditionUnit { op = ConditionOp.Equal, leftKey = "k", rightValue = "v" },
                    },
                },
            };
            AbilityConfigValidator.Result result = AbilityConfigValidator.Validate(dto);
            Assert.IsTrue(result.Ok);
            Assert.IsTrue(result.Issues.Any(i => i.Message.Contains("condition ignored")));
        }

        [Test]
        public void BlackboardReadWithoutProducer_IsWarning()
        {
            AbilityConfigDto dto = ValidDto();
            dto.rules[0].steps = new[]
            {
                new StepConfig
                {
                    op = "select_targets",
                    args = new ParamList
                    {
                        entries = new[]
                        {
                            // snapshot: 前缀的快照键已随 knownBlackboardKeys 清空移除，
                            // 现在同样是"读键无生产者"警告。
                            new ParamEntry { key = "subjectBlackboardKey", value = "snapshot:lowest_enemy_id", type = ParamValueType.String },
                        },
                    },
                },
                new StepConfig
                {
                    op = "apply_damage",
                    args = new ParamList
                    {
                        entries = new[]
                        {
                            new ParamEntry { key = "targetMode", value = "blackboard", type = ParamValueType.String },
                            // 无生产者的自造键 → 告警。
                            new ParamEntry { key = "blackboardKey", value = "ghost_key", type = ParamValueType.String },
                        },
                    },
                },
            };
            AbilityConfigValidator.Result result = AbilityConfigValidator.Validate(dto);
            Assert.IsTrue(result.Ok, string.Join("\n", result.Issues.Select(i => i.ToString())));
            Assert.IsTrue(result.Issues.Any(i => i.Message.Contains("snapshot:lowest_enemy_id")));
            Assert.IsTrue(result.Issues.Any(i => i.Message.Contains("ghost_key")));
        }

        [Test]
        public void ProducerConsumerPair_Passes()
        {
            AbilityConfigDto dto = ValidDto();
            dto.rules[0].steps = new[]
            {
                new StepConfig
                {
                    op = "select_targets",
                    args = new ParamList
                    {
                        entries = new[] { new ParamEntry { key = "outputEntitiesKey", value = "targets", type = ParamValueType.String } },
                    },
                },
                new StepConfig
                {
                    op = "apply_damage",
                    args = new ParamList
                    {
                        entries = new[]
                        {
                            new ParamEntry { key = "targetMode", value = "blackboard", type = ParamValueType.String },
                            new ParamEntry { key = "blackboardKey", value = "targets", type = ParamValueType.String },
                        },
                    },
                },
            };
            AbilityConfigValidator.Result result = AbilityConfigValidator.Validate(dto);
            Assert.IsTrue(result.Ok, string.Join("\n", result.Issues.Select(i => i.ToString())));
            Assert.IsEmpty(result.Issues);
        }

        [Test]
        public void Schema_LoadsFromResources_WithFullCoverage()
        {
            AbilityOpsSchema schema = AbilityOpsSchema.Load();
            Assert.GreaterOrEqual(schema.protocolVersion, 1);
            Assert.AreEqual(34, schema.componentOps.Count, "组件 op 数量应与 Components 目录具体组件数一致");
            Assert.AreEqual(4, schema.primitives.Count);
            // 原语 + canonical + PascalCase 别名都可解析。
            Assert.IsTrue(schema.TryResolveOp("delay", out _));
            Assert.IsTrue(schema.TryResolveOp("apply_damage", out _));
            Assert.IsTrue(schema.TryResolveOp("ApplyDamage", out _));
            Assert.IsTrue(schema.TryResolveOp("select_targets", out _));
            Assert.IsFalse(schema.TryResolveOp("nope", out _));
        }
    }
}
