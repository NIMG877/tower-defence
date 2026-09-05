using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using AbilitySystem;

namespace AbilitySystem.Tests
{
    /// <summary>
    /// 回归：OnPreWarm 派发早于组件参数绑定（绑定参数的 OnInit(ctx, param) 原本
    /// 只在 OnInitialize 跑），OnPreWarm 规则里的组件 step 会以 null 懒加载委托
    /// 被触发。catap/fang T1 部署费天赋是首个使用 OnPreWarm→组件 step 的资产，
    /// NRE 沿派发栈抛进 CutToLevelPanel 的 DOTween 回调，safe mode 吞掉后关卡
    /// 入场链中断（面板空壳 + LevelStart 不执行）。
    /// 修复：PreWarm 在派发 PreWarmEvent 前为所有 runtime 绑定组件参数。
    /// </summary>
    public class RunnerPreWarmBindingTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void PreWarm_DispatchesOnPreWarmRulesWithBoundComponentParams()
        {
            // 复刻 catap_t1 的规则形状：OnPreWarm → apply_buff（level 作用域）
            var config = ScriptableObject.CreateInstance<AbilityConfig>();
            config.abilityId = "prewarm_binding_regression";
            config.sp = new SPConfig();
            config.rules = new[]
            {
                new AbilityRuleConfig
                {
                    triggers = new[] { new ConditionConfig { triggerEvent = TriggerEvent.OnPreWarm } },
                    steps = new[]
                    {
                        new StepConfig
                        {
                            op = "apply_buff",
                            args = new ParamList
                            {
                                entries = new[]
                                {
                                    new ParamEntry { key = "attributes", value = "Cost", type = ParamValueType.String },
                                    new ParamEntry { key = "ops", value = "AddFlat", type = ParamValueType.String },
                                    new ParamEntry { key = "magnitudes", value = "-1", type = ParamValueType.Float },
                                    new ParamEntry { key = "buffId", value = "prewarm_regression_cost", type = ParamValueType.String },
                                    new ParamEntry { key = "buffTime", value = "-5", type = ParamValueType.Float },
                                    new ParamEntry { key = "buffScope", value = "level", type = ParamValueType.String },
                                },
                            },
                        },
                    },
                },
            };

            _go = new GameObject("prewarm-binding-subject");
            Entity entity = _go.AddComponent<Entity>();
            entity.EntityData = new EntityData
            {
                Talents = new List<AbilityConfig> { config },
            };

            var runner = new EntityAbilityRunner(entity);

            // 修复前：TryResolveScope 调用尚未绑定的 _buffScope 委托 → NRE 沿
            // PreWarm 派发栈抛出，本断言失败。
            Assert.DoesNotThrow(() => runner.PreWarm());
        }
    }
}
