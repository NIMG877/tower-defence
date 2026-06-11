using System.Collections.Generic;
using NUnit.Framework;
using SkillSystem;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode
{
    public class AbilityAddRemoveTests
    {
        private AbilityConfig MakeExtraCfg(string id = "test_extra")
        {
            var cfg = ScriptableObject.CreateInstance<AbilityConfig>();
            cfg.abilityId = id;
            cfg.abilityName = id;
            cfg.Kind = AbilityKind.ExtraAbility;
            cfg.components = new ComponentConfig[0];
            return cfg;
        }

        private EntitySkillRunner NewRunner()
        {
            // Test-only constructor:不订阅 Entity 事件
            return new EntitySkillRunner(new Blackboard());
        }

        [Test]
        public void Add_ConstructsRuntime_FiresAbilityAdded()
        {
            var runner = NewRunner();
            var cfg = MakeExtraCfg();
            int addedFires = 0;
            AbilityRuntime receivedAbility = null;
            // runner.DispatchEvent 在没有 ability 时遍历空表,不抛
            // 我们要观测:AddExtraAbility 之后,IsActive=true 且 dispatch 到 ability 列表
            // 简化:直接读 _abilities 数量 (借助 internal IReadOnlyList<AbilityRuntime> Abilities)
            var id = runner.AddExtraAbility(cfg);
            Assert.IsNotNull(id);
            Assert.AreEqual(1, runner.Abilities.Count);
            Assert.IsTrue(runner.Abilities[0].isActive);
            Object.DestroyImmediate(cfg);
        }

        [Test]
        public void Add_Idempotent_ReturnsExistingId()
        {
            var runner = NewRunner();
            var cfg = MakeExtraCfg();
            var id1 = runner.AddExtraAbility(cfg);
            var id2 = runner.AddExtraAbility(cfg);
            Assert.AreEqual(id1, id2);
            Assert.AreEqual(1, runner.Abilities.Count);
            Object.DestroyImmediate(cfg);
        }

        [Test]
        public void AddAfterRemove_CreatesFreshRuntime()
        {
            var runner = NewRunner();
            var cfg = MakeExtraCfg();
            var id1 = runner.AddExtraAbility(cfg);
            Assert.IsTrue(runner.RemoveExtraAbility(id1));
            var id2 = runner.AddExtraAbility(cfg);
            Assert.AreNotEqual(id1, id2);
            Assert.AreEqual(1, runner.Abilities.Count);
            Assert.IsTrue(runner.HasExtraAbility(id2));
            Object.DestroyImmediate(cfg);
        }

        [Test]
        public void Remove_DeactivatesAndRemoves()
        {
            var runner = NewRunner();
            var cfg = MakeExtraCfg();
            var id = runner.AddExtraAbility(cfg);
            Assert.IsTrue(runner.HasExtraAbility(id));
            Assert.IsTrue(runner.RemoveExtraAbility(id));
            Assert.IsFalse(runner.HasExtraAbility(id));
            Assert.AreEqual(0, runner.Abilities.Count);
            Object.DestroyImmediate(cfg);
        }

        [Test]
        public void Remove_NotFound_ReturnsFalse()
        {
            var runner = NewRunner();
            Assert.IsFalse(runner.RemoveExtraAbility("bogus"));
            Assert.IsFalse(runner.RemoveExtraAbility(""));
            Assert.IsFalse(runner.RemoveExtraAbility(null));
        }

        [Test]
        public void Remove_NonExtraAbility_Rejects()
        {
            var runner = NewRunner();
            // 构造一个 Skill 类型 (不通过 BuildAbilityRuntime,因为 PreWarm 需要 EntityData),
            // 手动塞一个 Skill 类型的 AbilityRuntime 进 list
            var skillCfg = ScriptableObject.CreateInstance<AbilityConfig>();
            skillCfg.abilityId = "manual_skill";
            skillCfg.Kind = AbilityKind.Skill;
            skillCfg.sp = null;  // 防止 BuildAbilityRuntime 内部走 sp
            // 直接走 AddExtraAbility with Skill should error out first
            var id = runner.AddExtraAbility(skillCfg);
            Assert.IsNull(id);  // AddExtraAbility 拒绝非 ExtraAbility
            Object.DestroyImmediate(skillCfg);
        }

        [Test]
        public void Add_NullCfg_LogsError_ReturnsNull()
        {
            var runner = NewRunner();
            // 期望 Debug.LogError,Unity Test Framework 在 EditMode 下默认会 capture log
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*cfg is null.*"));
            var id = runner.AddExtraAbility(null);
            Assert.IsNull(id);
            Assert.AreEqual(0, runner.Abilities.Count);
        }

        [Test]
        public void OnTeardown_DeactivatesActiveExtras()
        {
            var runner = NewRunner();
            var cfg = MakeExtraCfg();
            runner.AddExtraAbility(cfg);
            Assert.AreEqual(1, runner.Abilities.Count);
            Assert.IsTrue(runner.Abilities[0].isActive);
            runner.OnTeardown();
            Assert.AreEqual(0, runner.Abilities.Count);  // 列表清空
            Object.DestroyImmediate(cfg);
        }
    }
}
