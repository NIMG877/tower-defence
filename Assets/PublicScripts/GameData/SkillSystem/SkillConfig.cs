using System;
using UnityEngine;

namespace SkillSystem
{
    [CreateAssetMenu(fileName = "SkillConfig", menuName = "SkillSystem/Skill Config", order = 0)]
    public class SkillConfig : ScriptableObject
    {
        public string skillId;
        public string skillName;
        [TextArea(2, 5)] public string description;

        [Tooltip("技能图标")]
        public Sprite icon;

        public SPConfig sp;
        public ComponentConfig[] components = Array.Empty<ComponentConfig>();
    }
}
