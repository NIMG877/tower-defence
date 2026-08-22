using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace AbilitySystem
{
    [CreateAssetMenu(fileName = "AbilityConfig", menuName = "AbilitySystem/Ability Config", order = 0)]
    public class AbilityConfig : ScriptableObject, ISerializationCallbackReceiver
    {
        public string abilityId;
        public string abilityName;
        [TextArea(2, 5)] public string description;

        [Tooltip("技能图标")]
        public Sprite icon;

        // Kind 字段在 Inspector 上不显示;运行时由 EntityAbilityRunner.PreWarm
        // (或 AddExtraAbility) 按数据来源列表 / 入口强制赋值。设计意图:数据层
        // 不需要(也不应该)由设计师在每个 .asset 上单独设置 Kind——一个 cfg
        // 资产可以被多种来源(Skills/Talents/AddExtraAbility)引用,KIND 由调用入口决定。
        [HideInInspector] public AbilityKind Kind;
        public SPConfig sp;
        public AbilityRuleConfig[] rules = Array.Empty<AbilityRuleConfig>();

        // FormerlySerializedAs preserves the old top-level payload. The nested
        // ComponentConfig -> StepConfig shape cannot be migrated by Unity's field
        // rename alone, so GetRules projects it at runtime as a lossless fallback.
        [SerializeField, HideInInspector, FormerlySerializedAs("components")]
        private ComponentConfig[] legacyComponents = Array.Empty<ComponentConfig>();

        [NonSerialized] private AbilityRuleConfig[] _legacyRulesCache;

        public AbilityRuleConfig[] GetRules()
        {
            if (rules != null && rules.Length > 0)
            {
                if (legacyComponents != null && legacyComponents.Length > 0)
                {
                    OneShotWarn.WarnOnce(
                        "ability-rules-mixed:" + (abilityId ?? name),
                        $"[AbilityConfig] '{abilityId ?? name}' contains both rules and legacy components. " +
                        "Only rules are executed; finish or revert the partial migration.");
                }
                return rules;
            }
            if (legacyComponents == null || legacyComponents.Length == 0)
                return rules ?? Array.Empty<AbilityRuleConfig>();
            if (_legacyRulesCache != null) return _legacyRulesCache;

            _legacyRulesCache = new AbilityRuleConfig[legacyComponents.Length];
            for (int i = 0; i < legacyComponents.Length; i++)
            {
                ComponentConfig legacy = legacyComponents[i];
                _legacyRulesCache[i] = new AbilityRuleConfig
                {
                    triggers = legacy?.triggers ?? Array.Empty<ConditionConfig>(),
                    steps = legacy == null
                        ? Array.Empty<StepConfig>()
                        : new[]
                        {
                            new StepConfig
                            {
                                // The runtime accepts legacy component type names
                                // as aliases for their canonical snake_case ops.
                                op = legacy.componentType,
                                args = legacy.parameters ?? new ParamList(),
                            },
                        },
                };
            }
            return _legacyRulesCache;
        }

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            _legacyRulesCache = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _legacyRulesCache = null;
        }
#endif
    }
}
