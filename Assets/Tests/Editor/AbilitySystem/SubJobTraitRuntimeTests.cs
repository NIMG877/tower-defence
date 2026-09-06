using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using AbilitySystem;

namespace AbilitySystem.Tests
{
    /// <summary>
    /// 子职业特性(AbilityKind.SubJobTrait)运行时装配契约：
    /// PreWarm 从 EntityData.SubJobTrait 构建单实例；资产 sp 块与天赋同型
    /// (totalSp=0 + Auto + NoConsume) ⇒ 首帧 Tick 自动开启并常驻，规则随事件派发；
    /// OnTeardown 保留语义同 Talents(跨部署存活，不拆除)。
    /// </summary>
    public class SubJobTraitRuntimeTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private static AbilityConfig TraitConfig(string abilityId, TriggerEvent trigger, string bbKey)
        {
            var config = ScriptableObject.CreateInstance<AbilityConfig>();
            config.abilityId = abilityId;
            // 与 SubJobs 资产同型:totalSp=0 + Auto 开启 + NoConsume ⇒ 首帧 Tick 自动开启、常驻不关
            config.sp = new SPConfig
            {
                totalSp = 0,
                initialSp = 0,
                chargeNum = 1,
                abilityAmount = 0,
                recoverMode = SpRecoverMode.Natural,
                consumeMode = SpConsumeMode.NoConsume,
                openMode = AbilityOpenMode.Auto,
            };
            if (trigger != TriggerEvent.OnPreWarm)
            {
                config.rules = new[]
                {
                    new AbilityRuleConfig
                    {
                        triggers = new[] { new ConditionConfig { triggerEvent = trigger } },
                        steps = new[]
                        {
                            new StepConfig
                            {
                                op = "write_blackboard",
                                args = new ParamList
                                {
                                    entries = new[]
                                    {
                                        new ParamEntry { key = "key", value = bbKey, type = ParamValueType.String },
                                        new ParamEntry { key = "value", value = "fired", type = ParamValueType.String },
                                    },
                                },
                            },
                        },
                    },
                };
            }
            return config;
        }

        private EntityAbilityRunner CreateRunner(EntityData data)
        {
            _go = new GameObject("subjob-trait-subject");
            Entity entity = _go.AddComponent<Entity>();
            entity.EntityData = data;
            var runner = new EntityAbilityRunner(entity);
            runner.PreWarm();
            return runner;
        }

        [Test]
        public void PreWarm_WithSubJobTrait_BuildsSingleRuntimeWithSubJobTraitKind()
        {
            AbilityConfig trait = TraitConfig("subjob_under_test", TriggerEvent.OnPreWarm, null);
            var runner = CreateRunner(new EntityData
            {
                SubJobTrait = trait,
                Talents = new List<AbilityConfig>
                {
                    TraitConfig("co_talent", TriggerEvent.OnPreWarm, null),
                },
            });

            Assert.That(runner.SubJobTraits, Has.Count.EqualTo(1));
            Assert.That(runner.SubJobTraits[0].Kind, Is.EqualTo(AbilityKind.SubJobTrait));
            Assert.That(runner.SubJobTraits[0].config, Is.SameAs(trait));
            Assert.That(runner.SubJobTraits[0].runtimeId, Is.EqualTo("subjob_under_test"));
            // 不串味:不进 Talents/Skills 视图,天赋仍在自己的视图
            Assert.That(runner.Talents, Has.Count.EqualTo(1));
            Assert.That(runner.Skills, Is.Empty);
        }

        [Test]
        public void PreWarm_WithoutSubJobTrait_BuildsNothing()
        {
            // 怪物零配置:CharacterSubJob=0 ⇒ SubJobTrait=null ⇒ 不构建
            var runner = CreateRunner(new EntityData());

            Assert.That(runner.SubJobTraits, Is.Empty);
        }

        [Test]
        public void SubJobTrait_AutoBeginsOnFirstTick_AndStaysActive()
        {
            var runner = CreateRunner(new EntityData
            {
                SubJobTrait = TraitConfig("subjob_always_on", TriggerEvent.OnPreWarm, null),
            });
            AbilityRuntime runtime = runner.SubJobTraits[0];

            Assert.That(runtime.spEngine.IsActive, Is.False, "PreWarm 时尚未开启(与天赋一致,开启发生在首帧 Tick)");

            runner.Tick(0.016f);
            Assert.That(runtime.spEngine.IsActive, Is.True);

            runner.Tick(0.016f);
            Assert.That(runtime.spEngine.IsActive, Is.True, "NoConsume:常驻不关闭");
        }

        [Test]
        public void SubJobTrait_RulesDispatch_AfterAlwaysOnActivation()
        {
            // 常驻开启后,isActive 门放行:事件派发命中特性规则(空规则资产无效果,
            // 这里用 write_blackboard 步骤证明"规则会跑",即特性生效通路打通)
            var runner = CreateRunner(new EntityData
            {
                SubJobTrait = TraitConfig("subjob_dispatch", TriggerEvent.OnAfterAttack, "subjob_dispatch_key"),
            });

            runner.DispatchEvent(new AfterAttackEvent());
            Assert.That(runner.sharedBlackboard.Get<string>("subjob_dispatch_key", null),
                Is.Null, "开启前 isActive 门拦截,不派发");

            runner.Tick(0.016f);
            runner.DispatchEvent(new AfterAttackEvent());
            Assert.That(runner.sharedBlackboard.Get<string>("subjob_dispatch_key", null),
                Is.EqualTo("fired"));
        }

        [Test]
        public void OnTeardown_SubJobTraitKeptLikeTalents()
        {
            var runner = CreateRunner(new EntityData
            {
                SubJobTrait = TraitConfig("subjob_kept", TriggerEvent.OnPreWarm, null),
            });

            runner.OnInitialize();
            runner.OnTeardown();

            // 同 Talents:跨部署保留在 _abilities,仅翻 inactive,不 Unwire 不移除
            Assert.That(runner.SubJobTraits, Has.Count.EqualTo(1));
            Assert.That(runner.SubJobTraits[0].isActive, Is.False);

            // 二次部署:OnInitialize 复位后照常工作
            runner.OnInitialize();
            runner.Tick(0.016f);
            Assert.That(runner.SubJobTraits[0].spEngine.IsActive, Is.True);
        }
    }
}
