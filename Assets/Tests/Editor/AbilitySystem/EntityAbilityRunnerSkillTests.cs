using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AbilitySystem.Tests
{
    /// <summary>
    /// AddSkill/RemoveSkill 生命周期测试：走真注册表（write_blackboard 为反射扫描
    /// 注册的 component op），验证"DTO → 运行时 AbilityConfig → AddSkill → Initialize
    /// 派发 → 黑板写入 → Remove"的完整闭环。用无实体的 runner 构造（公开 ctor，
    /// entity 为 null 时事件桥全部空转），不依赖场景。
    /// </summary>
    public class EntityAbilityRunnerSkillTests
    {
        private static AbilityConfigDto ProbeDto()
        {
            return new AbilityConfigDto
            {
                abilityId = "gen_runner_probe",
                abilityName = "运行器探针",
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
                                        new ParamEntry { key = "key", value = "runner_probe", type = ParamValueType.String },
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
        public void AddSkill_BuildsKindSkill_AndInitializes()
        {
            var runner = new EntityAbilityRunner((Entity)null);
            AbilityConfig cfg = AbilityConfigBuilder.FromDto(ProbeDto());
            try
            {
                string runtimeId = runner.AddSkill(cfg);
                Assert.AreEqual("gen_runner_probe", runtimeId, "Skill 的 runtimeId 即 cfg.abilityId");
                Assert.AreEqual(1, runner.Skills.Count);
                Assert.AreEqual(AbilityKind.Skill, runner.Skills[0].Kind);
                // InitializeEvent 派发（bypassActiveGate）→ write_blackboard 已写黑板。
                Assert.AreEqual(42, runner.sharedBlackboard.Get<object>("runner_probe"));
            }
            finally
            {
                Object.DestroyImmediate(cfg);
            }
        }

        [Test]
        public void AddSkill_SameConfig_IsIdempotent()
        {
            var runner = new EntityAbilityRunner((Entity)null);
            AbilityConfig cfg = AbilityConfigBuilder.FromDto(ProbeDto());
            try
            {
                string first = runner.AddSkill(cfg);
                string second = runner.AddSkill(cfg);
                Assert.AreEqual(first, second);
                Assert.AreEqual(1, runner.Skills.Count);
            }
            finally
            {
                Object.DestroyImmediate(cfg);
            }
        }

        [Test]
        public void RemoveSkill_RemovesAndIsIdempotent()
        {
            var runner = new EntityAbilityRunner((Entity)null);
            AbilityConfig cfg = AbilityConfigBuilder.FromDto(ProbeDto());
            try
            {
                string runtimeId = runner.AddSkill(cfg);
                Assert.IsTrue(runner.RemoveSkill(runtimeId));
                Assert.AreEqual(0, runner.Skills.Count);
                Assert.IsFalse(runner.RemoveSkill(runtimeId), "重复移除应幂等返回 false");
                // AddSkill(null) 走 LogError 分支（配置错误必须报错暴露）。string 重载
                // 的 Expect 是整条消息精确匹配，项目惯例用 Regex 子串。
                LogAssert.Expect(LogType.Error, new Regex("AddSkill: cfg is null"));
                Assert.IsNull(runner.AddSkill(null));
            }
            finally
            {
                Object.DestroyImmediate(cfg);
            }
        }

        [Test]
        public void RemoveSkill_CannotRemoveExtraAbility()
        {
            var runner = new EntityAbilityRunner((Entity)null);
            // Kind 存在 cfg 上且由构建入口强制赋值（既有设计），跨 Kind 复用同一 cfg 会
            // 互相改写——用两个独立 cfg 分别验证 ExtraAbility / Skill 路径。
            AbilityConfig extraCfg = AbilityConfigBuilder.FromDto(ProbeDto());
            AbilityConfig skillCfg = AbilityConfigBuilder.FromDto(ProbeDto());
            skillCfg.abilityId = "gen_runner_probe_2";
            try
            {
                string extraId = runner.AddExtraAbility(extraCfg);
                Assert.IsTrue(runner.HasExtraAbility(extraId));
                // RemoveSkill(非 Skill) 走 LogError 分支；Expect 必须用 Regex 重载（见上）。
                LogAssert.Expect(LogType.Error, new Regex("RemoveSkill: target is not a Skill"));
                Assert.IsFalse(runner.RemoveSkill(extraId), "RemoveSkill 不得移除非 Skill 能力");
                Assert.IsTrue(runner.HasExtraAbility(extraId), "被 RemoveSkill 拒绝后 ExtraAbility 应原样保留");

                string skillId = runner.AddSkill(skillCfg);
                Assert.IsTrue(runner.RemoveSkill(skillId));
            }
            finally
            {
                Object.DestroyImmediate(extraCfg);
                Object.DestroyImmediate(skillCfg);
            }
        }

        [Test]
        public void RemoveSkill_RemovesDataDrivenSkillKind_Too()
        {
            // RemoveSkill 按 Kind 工作，不区分来源（含 PreWarm 构建的数据驱动技能）——
            // 文档注释已声明该语义，这里锁定行为防回归。
            var runner = new EntityAbilityRunner((Entity)null);
            AbilityConfig cfg = AbilityConfigBuilder.FromDto(ProbeDto());
            try
            {
                string id = runner.AddSkill(cfg);
                runner.RemoveSkill(id);
                Assert.AreEqual(0, runner.Skills.Count);
                // 移除后再注入同 cfg 应重新构建（id 复用同值）。
                Assert.AreEqual(id, runner.AddSkill(cfg));
                Assert.AreEqual(1, runner.Skills.Count);
            }
            finally
            {
                Object.DestroyImmediate(cfg);
            }
        }
    }
}
