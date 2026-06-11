using NUnit.Framework;
using SkillSystem;
using UnityEngine;

namespace Tests.EditMode
{
    public class AbilityRuntimeTests
    {
        private AbilityConfig MakeCfg(AbilityKind kind = AbilityKind.Talent)
        {
            return ScriptableObject.CreateInstance<AbilityConfig>();
        }

        [Test]
        public void NewRuntime_IsInactive()
        {
            var cfg = MakeCfg();
            cfg.abilityId = "test";
            var rt = new AbilityRuntime { config = cfg };
            Assert.IsFalse(rt.isActive);
        }

        [Test]
        public void SetActiveTrue_FiresOnAbilityBeginOnce()
        {
            var rt = new AbilityRuntime { config = MakeCfg() };
            int calls = 0;
            rt.OnAbilityBegin += () => calls++;
            rt.SetActive(true);
            rt.SetActive(true);  // second call is no-op
            Assert.AreEqual(1, calls);
            Assert.IsTrue(rt.isActive);
        }

        [Test]
        public void SetActiveFalse_AfterTrue_FiresOnAbilityEnd()
        {
            var rt = new AbilityRuntime { config = MakeCfg() };
            rt.SetActive(true);
            int endCalls = 0;
            rt.OnAbilityEnd += () => endCalls++;
            rt.SetActive(false);
            Assert.AreEqual(1, endCalls);
            Assert.IsFalse(rt.isActive);
        }

        [Test]
        public void SetActiveFalse_FromInactive_IsNoOp()
        {
            var rt = new AbilityRuntime { config = MakeCfg() };
            int endCalls = 0;
            rt.OnAbilityEnd += () => endCalls++;
            rt.SetActive(false);
            Assert.AreEqual(0, endCalls);
        }

        [Test]
        public void SetActiveTrueThenFalse_BothFireOnce()
        {
            var rt = new AbilityRuntime { config = MakeCfg() };
            int begin = 0, end = 0;
            rt.OnAbilityBegin += () => begin++;
            rt.OnAbilityEnd += () => end++;
            rt.SetActive(true);
            rt.SetActive(false);
            rt.SetActive(true);
            rt.SetActive(false);
            Assert.AreEqual(2, begin);
            Assert.AreEqual(2, end);
        }

        [Test]
        public void Kind_PassesThroughFromConfig()
        {
            var cfg = MakeCfg(AbilityKind.Skill);
            var rt = new AbilityRuntime { config = cfg };
            Assert.AreEqual(AbilityKind.Skill, rt.Kind);
        }

        [Test]
        public void RuntimeId_DefaultsToEmpty()
        {
            var rt = new AbilityRuntime { config = MakeCfg() };
            Assert.IsNull(rt.runtimeId);
        }
    }
}
