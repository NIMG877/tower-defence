using System;
using UnityEngine;

namespace SkillSystem
{
    public enum SkillKind { Passive, ActiveSkill, Aura, OnDeath }

    [Serializable]
    public class SkillConfig
    {
        public string skillId;
        public string skillName;
        [TextArea(2, 5)] public string description;
        public SkillKind kind = SkillKind.Passive;

        public SPConfig sp;
        public ConditionConfig[] globalConditions = Array.Empty<ConditionConfig>();
        public ComponentConfig[] components = Array.Empty<ComponentConfig>();
    }
}
