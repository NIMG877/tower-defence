using System;
using UnityEngine;

namespace SkillSystem
{
    [CreateAssetMenu(fileName = "AbilityConfig", menuName = "SkillSystem/Ability Config", order = 0)]
    public class AbilityConfig : ScriptableObject
    {
        public string abilityId;
        public string abilityName;
        [TextArea(2, 5)] public string description;

        [Tooltip("技能图标")]
        public Sprite icon;

        public AbilityKind Kind = AbilityKind.Skill;
        public SPConfig sp;
        public ComponentConfig[] components = Array.Empty<ComponentConfig>();
    }
}
