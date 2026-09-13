using System.Collections.Generic;
using System.Text.RegularExpressions;
using AbilitySystem.Components;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AbilitySystem.Tests
{
    /// <summary>
    /// GenerateSkill 完成处理逻辑测试：不走网络，直接调 HandleServerResponse /
    /// ApplyGeneratedAbility（public 测试面）。runner 用无实体构造
    /// （ReplaceSkill 只操作 runner 自己的能力列表，不触碰 Entity）。
    /// 提交/轮询的驱动属于薄网络层，由 PlayMode 手测覆盖。
    /// </summary>
    public class GenerateSkillComponentTests
    {
        private static AbilityConfigDto ValidDto(string abilityId)
        {
            return new AbilityConfigDto
            {
                abilityId = abilityId,
                abilityName = "生成技能测试",
                description = "d",
                sp = new SPConfig { totalSp = 0, consumeMode = SpConsumeMode.NoConsume, openMode = AbilityOpenMode.Auto },
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
                                        new ParamEntry { key = "key", value = "gen_probe", type = ParamValueType.String },
                                        new ParamEntry { key = "method", value = "set", type = ParamValueType.String },
                                        new ParamEntry { key = "source", value = "value", type = ParamValueType.String },
                                        new ParamEntry { key = "value", value = "42", type = ParamValueType.Int },
                                    },
                                },
                            },
                        },
                    },
                },
            };
        }

        [Test]
        public void ApplyGeneratedAbility_ReplacesCurrentSkill_AndReturnsRuntimeId()
        {
            var runner = new EntityAbilityRunner((Entity)null);
            var comp = new GenerateSkill();
            AbilityConfig placeholder = AbilityConfigBuilder.FromDto(ValidDto("gen_placeholder"));
            try
            {
                runner.AddSkill(placeholder);
                string runtimeId = comp.ApplyGeneratedAbility(runner, ValidDto("gen_t1"));

                Assert.AreEqual("gen_t1", runtimeId, "ReplaceSkill 返回新技能 runtimeId（=abilityId）");
                Assert.AreEqual(1, runner.Skills.Count, "替换后 Skills 只剩新技能");
                Assert.AreEqual("gen_t1", runner.Skills[0].config.abilityId);
                Assert.AreEqual(AbilityKind.Skill, runner.Skills[0].Kind);
            }
            finally
            {
                Object.DestroyImmediate(placeholder);
                foreach (AbilityRuntime runtime in runner.Skills)
                    Object.DestroyImmediate(runtime.config);
            }
        }

        [Test]
        public void ApplyGeneratedAbility_SecondApply_ReplacesAgain()
        {
            var runner = new EntityAbilityRunner((Entity)null);
            var comp = new GenerateSkill();
            try
            {
                comp.ApplyGeneratedAbility(runner, ValidDto("gen_t1"));
                string runtimeId = comp.ApplyGeneratedAbility(runner, ValidDto("gen_t2"));

                Assert.AreEqual("gen_t2", runtimeId);
                Assert.AreEqual(1, runner.Skills.Count, "连续替换不叠加技能");
                Assert.AreEqual("gen_t2", runner.Skills[0].config.abilityId);
            }
            finally
            {
                foreach (AbilityRuntime runtime in runner.Skills)
                    Object.DestroyImmediate(runtime.config);
            }
        }

        [Test]
        public void HandleServerResponse_Rejected_LogsErrorAndKeepsSkill()
        {
            var runner = new EntityAbilityRunner((Entity)null);
            var comp = new GenerateSkill();
            AbilityConfig placeholder = AbilityConfigBuilder.FromDto(ValidDto("gen_placeholder"));
            try
            {
                runner.AddSkill(placeholder);
                string raw = JsonConvert.SerializeObject(new AgentJobStatus
                {
                    done = true,
                    response = new AgentJobStatus.GenerateResponse
                    {
                        status = "rejected",
                        report = new AgentJobStatus.Report
                        {
                            attempts = 3,
                            issues = new List<AgentJobStatus.Issue>
                            {
                                new AgentJobStatus.Issue { severity = "error", path = "op", message = "unknown op" },
                            },
                        },
                    },
                });

                LogAssert.Expect(LogType.Error, "[GenerateSkill] 服务端拒绝生成（attempts=3）");
                LogAssert.Expect(LogType.Error, "[GenerateSkill][服务端 error] op: unknown op");
                comp.HandleServerResponse(raw);

                Assert.AreEqual("gen_placeholder", runner.Skills[0].config.abilityId, "拒绝时不注入");
            }
            finally
            {
                Object.DestroyImmediate(placeholder);
            }
        }

        [Test]
        public void HandleServerResponse_ServerError_LogsError()
        {
            var comp = new GenerateSkill();
            string raw = JsonConvert.SerializeObject(new AgentJobStatus { done = true, error = "boom" });

            LogAssert.Expect(LogType.Error, "[GenerateSkill] 服务端任务异常：boom");
            comp.HandleServerResponse(raw);
        }

        [Test]
        public void HandleServerResponse_MalformedJson_LogsError()
        {
            var comp = new GenerateSkill();

            LogAssert.Expect(LogType.Error, new Regex(@"\[GenerateSkill\] 轮询响应解析失败"));
            comp.HandleServerResponse("{ not json");
        }
    }
}
