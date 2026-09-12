using System;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace AbilitySystem.Tests
{
    public class AbilityConfigBuilderTests
    {
        private const string SampleJson = @"{
  ""abilityId"": ""gen_test_1"",
  ""abilityName"": ""测试生成技能"",
  ""description"": ""测试用"",
  ""iconKey"": ""no_such_icon_key"",
  ""sp"": {
    ""totalSp"": 30, ""initialSp"": 10, ""chargeNum"": 1, ""abilityAmount"": 0,
    ""recoverMode"": ""Natural"", ""consumeMode"": ""NoConsume"", ""openMode"": ""Manual"",
    ""recoverForbidDuringAbility"": false, ""canManualClose"": true
  },
  ""rules"": [
    {
      ""triggers"": [ { ""triggerEvent"": ""OnInitialize"", ""groups"": [] } ],
      ""reentry"": ""IgnoreWhileRunning"",
      ""steps"": [
        { ""op"": ""write_blackboard"", ""args"": { ""entries"": [
          { ""key"": ""key"", ""value"": ""builder_probe"", ""type"": ""String"", ""fromBlackboard"": false },
          { ""key"": ""method"", ""value"": ""set"", ""type"": ""String"", ""fromBlackboard"": false },
          { ""key"": ""source"", ""value"": ""value"", ""type"": ""String"", ""fromBlackboard"": false },
          { ""key"": ""value"", ""value"": ""42"", ""type"": ""Int"", ""fromBlackboard"": false }
        ] } }
      ]
    }
  ]
}";

        [Test]
        public void Parse_MapsAllFields()
        {
            AbilityConfigDto dto = AbilityConfigBuilder.Parse(SampleJson);

            Assert.AreEqual("gen_test_1", dto.abilityId);
            Assert.AreEqual("测试生成技能", dto.abilityName);
            Assert.AreEqual(30, dto.sp.totalSp);
            Assert.AreEqual(10, dto.sp.initialSp);
            Assert.AreEqual(SpConsumeMode.NoConsume, dto.sp.consumeMode);
            Assert.AreEqual(AbilityOpenMode.Manual, dto.sp.openMode);
            Assert.IsTrue(dto.sp.canManualClose);
            Assert.AreEqual(1, dto.rules.Length);
            Assert.AreEqual(TriggerEvent.OnInitialize, dto.rules[0].triggers[0].triggerEvent);

            StepConfig step = dto.rules[0].steps[0];
            Assert.AreEqual("write_blackboard", step.op);
            Assert.AreEqual("builder_probe", step.args.GetRaw("key"));
            ParamEntry valueEntry = step.args.entries.Single(e => e.key == "value");
            Assert.AreEqual("42", valueEntry.value);
            Assert.AreEqual(ParamValueType.Int, valueEntry.type);
            Assert.IsFalse(valueEntry.fromBlackboard);
        }

        [Test]
        public void Parse_RejectsUnknownMembers()
        {
            const string json = @"{ ""abilityId"": ""x"", ""typoField"": 1 }";
            JsonSerializationException ex = Assert.Throws<JsonSerializationException>(
                () => AbilityConfigBuilder.Parse(json));
            StringAssert.Contains("typoField", ex.Message);
        }

        [Test]
        public void Parse_RejectsUnknownEnumNames()
        {
            const string json = @"{ ""abilityId"": ""x"", ""sp"": { ""recoverMode"": ""Bogus"" } }";
            Assert.Throws<JsonSerializationException>(() => AbilityConfigBuilder.Parse(json));
        }

        [Test]
        public void FromDto_MapsConfigFields()
        {
            AbilityConfigDto dto = AbilityConfigBuilder.Parse(SampleJson);
            AbilityConfig cfg = AbilityConfigBuilder.FromDto(dto);
            try
            {
                Assert.AreEqual("gen_test_1", cfg.abilityId);
                Assert.AreEqual("测试生成技能", cfg.abilityName);
                Assert.AreEqual(30, cfg.sp.totalSp);
                // 未注册的 iconKey 回落 null（AbilityCard 有 null 兜底）。
                Assert.IsNull(cfg.icon);
                // GetRules 走普通字段读取，运行时构造的 SO 与资产同路径。
                AbilityRuleConfig[] rules = cfg.GetRules();
                Assert.AreEqual(1, rules.Length);
                Assert.AreEqual("write_blackboard", rules[0].steps[0].op);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cfg);
            }
        }

        [Test]
        public void FromDto_NullSpAndRules_GetSafeDefaults()
        {
            AbilityConfig cfg = AbilityConfigBuilder.FromDto(new AbilityConfigDto { abilityId = "gen_min" });
            try
            {
                Assert.IsNotNull(cfg.sp);
                Assert.AreEqual(0, cfg.GetRules().Length);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cfg);
            }
        }
    }
}
