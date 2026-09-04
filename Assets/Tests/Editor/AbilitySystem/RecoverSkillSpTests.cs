using System.Collections.Generic;
using System.Text.RegularExpressions;
using AbilitySystem.Components;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AbilitySystem.Tests
{
    /// <summary>RecoverSkillSp（"部署后立即获得技力"：EntityAbilityRunner 领域方法 + 同名组件）的
    /// EditMode 契约测试。EntityAbilityRunner 公共构造 + 轻量 Entity（AddComponent 即可，子系统不构建）
    /// 就能走真实 PreWarm 构建路径；生产链路上组件经 ctx.entity.AbilityRunner 取 runner，
    /// 该接线和 OnInitialize 的"先 Reset 后派发"时序由 PlayMode 验证。</summary>
    public class RecoverSkillSpTests
    {
        private GameObject _go;
        private Entity _entity;
        private EntityAbilityRunner _runner;
        private readonly List<ScriptableObject> _scratchAssets = new List<ScriptableObject>();

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("recover_sp_test_entity");
            _entity = _go.AddComponent<Entity>();
            _entity.EntityData = new EntityData();
            _runner = new EntityAbilityRunner(_entity);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _scratchAssets.Count; i++)
            {
                Object.DestroyImmediate(_scratchAssets[i]);
            }
            _scratchAssets.Clear();
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void RecoversIntoSelectedSkillSpEngine()
        {
            BuildWithSkills(NewSkill(initialSp: 0, totalSp: 50));

            _runner.RecoverSkillSp(30);

            Assert.That(_runner.Skills[0].spEngine.CurrentSp, Is.EqualTo(30));
        }

        [Test]
        public void CapsAtTotalSp()
        {
            BuildWithSkills(NewSkill(initialSp: 40, totalSp: 50));

            _runner.RecoverSkillSp(30);

            Assert.That(_runner.Skills[0].spEngine.CurrentSp, Is.EqualTo(50), "RecoverSp 语义：单蓄能封顶 totalSp");
        }

        [Test]
        public void MultiChargeSkill_ChargesUpPerTotalSp()
        {
            AbilityConfig skill = NewSkill(initialSp: 0, totalSp: 25);
            skill.sp.chargeNum = 2;
            BuildWithSkills(skill);

            _runner.RecoverSkillSp(30);

            Assert.That(_runner.Skills[0].spEngine.CurrentCharge, Is.EqualTo(1));
            Assert.That(_runner.Skills[0].spEngine.CurrentSp, Is.EqualTo(5), "30 = 一整管 25 进蓄能 + 余 5");
        }

        [Test]
        public void NoBuiltSkill_LogsErrorAndSkips()
        {
            // 天赋配在无技能实体上属配置错误：不抛异常打断派发，但必须报错暴露。
            LogAssert.Expect(LogType.Error, new Regex("RecoverSkillSp"));
            Assert.DoesNotThrow(() => _runner.RecoverSkillSp(30));
        }

        [Test]
        public void Component_DetachedNullEntity_LogsError()
        {
            LogAssert.Expect(LogType.Error, new Regex("RecoverSkillSp"));
            var comp = new RecoverSkillSp();
            comp.OnInit(new AbilityContext { sharedBlackboard = new Blackboard() },
                Params(("amount", "30")));
            comp.OnTrigger(new AbilityContext { sharedBlackboard = new Blackboard() });
        }

        [Test]
        public void OpName_IsAutoRegistered_AsSnakeCase()
        {
            // lava_t1 资产用 op: recover_skill_sp；组件 Pascal 名是别名，二者都解析到同一 canonical。
            Assert.That(AbilityStepOpRegistry.ResolveCanonical("RecoverSkillSp"), Is.EqualTo("recover_skill_sp"));
            Assert.That(AbilityStepOpRegistry.ResolveCanonical("recover_skill_sp"), Is.EqualTo("recover_skill_sp"));
        }

        private AbilityConfig NewSkill(int initialSp, int totalSp)
        {
            var cfg = ScriptableObject.CreateInstance<AbilityConfig>();
            cfg.abilityId = "recover_sp_test_skill";
            cfg.sp = new SPConfig { totalSp = totalSp, initialSp = initialSp };
            _scratchAssets.Add(cfg);
            return cfg;
        }

        private void BuildWithSkills(params AbilityConfig[] skills)
        {
            _entity.EntityData.Skills = new List<AbilityConfig>(skills);
            _runner.PreWarm();
        }

        private static ParamList Params(params (string key, string value)[] kv)
        {
            var entries = new ParamEntry[kv.Length];
            for (int i = 0; i < kv.Length; i++)
            {
                entries[i] = new ParamEntry
                {
                    key = kv[i].key,
                    value = kv[i].value,
                    fromBlackboard = false,
                    type = ParamValueType.String,
                };
            }
            return new ParamList { entries = entries };
        }
    }
}
