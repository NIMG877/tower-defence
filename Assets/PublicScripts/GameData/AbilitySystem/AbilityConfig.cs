using System;
using UnityEngine;

namespace AbilitySystem
{
    [CreateAssetMenu(fileName = "AbilityConfig", menuName = "AbilitySystem/Ability Config", order = 0)]
    public class AbilityConfig : ScriptableObject
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
        public ComponentConfig[] components = Array.Empty<ComponentConfig>();
    }
}
