using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkillSystem
{
    public enum ParamValueType { Int, Float, Bool, String, Vector2Int, AnimationRef, Prefab, EntityId, Color }

    [Serializable]
    public class ParamEntry
    {
        public string key;
        public ParamValueType type;
        [TextArea(1, 3)] public string value;
    }

    [Serializable]
    public class ParamList
    {
        public ParamEntry[] entries = Array.Empty<ParamEntry>();

        public bool HasKey(string key)
        {
            if (entries == null) return false;
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].key == key) return true;
            return false;
        }

        public string GetRaw(string key, string defaultValue = "")
        {
            if (entries == null) return defaultValue;
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].key == key) return entries[i].value;
            return defaultValue;
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            var raw = GetRaw(key);
            return int.TryParse(raw, out var v) ? v : defaultValue;
        }

        public float GetFloat(string key, float defaultValue = 0f)
        {
            var raw = GetRaw(key);
            return float.TryParse(raw, out var v) ? v : defaultValue;
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            var raw = GetRaw(key);
            if (string.IsNullOrEmpty(raw)) return defaultValue;
            return bool.TryParse(raw, out var v) ? v : defaultValue;
        }

        public string GetString(string key, string defaultValue = "")
        {
            return GetRaw(key, defaultValue);
        }

        public Vector2Int GetVector2Int(string key, Vector2Int defaultValue = default)
        {
            var raw = GetRaw(key);
            if (string.IsNullOrEmpty(raw)) return defaultValue;
            var parts = raw.Split(',');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var x) &&
                int.TryParse(parts[1], out var y))
                return new Vector2Int(x, y);
            return defaultValue;
        }
    }

    public enum TriggerEvent
    {
        OnPreWarm, OnInitialize,
        OnBeforeAttack, OnAfterAttack,
        OnBeforeTakeDamage, OnAfterTakeDamage,
        OnAttackSuccessfully, OnAttackInterrupt,
        OnBeforeHurt, OnAfterHurt,
        OnAttackAnimBegin,
        OnBeforeDieAnimation,
        // Renamed from OnSkillBegin/OnSkillEnd: now fire on any isActive transition.
        OnAbilityBegin, OnAbilityEnd,
        // New: broadcast when an ExtraAbility is added/removed.
        OnAbilityAdded, OnAbilityRemoved,
    }

    public enum AbilityKind
    {
        Skill,
        Talent,
        ExtraAbility,
    }

    public enum ConditionOp
    {
        None, Equal, NotEqual,
        Greater, GreaterOrEqual, Less, LessOrEqual,
        HasBuff, NotHasBuff,
        IsInAbnormalState, NotInAbnormalState,
        HasBlackboardKey, NotHasBlackboardKey,
    }

    // A single comparison: op(leftKey, rightValue). The runtime semantics
    // are documented in ConditionEvaluator.EvaluateUnit.
    [Serializable]
    public class ConditionUnit
    {
        public ConditionOp op = ConditionOp.None;
        public string leftKey;
        public string rightValue;
    }

    // A list of units combined with AND. An empty list is treated as
    // "passes" (matches the legacy op: 0 / None short-circuit).
    [Serializable]
    public class ConditionGroup
    {
        public List<ConditionUnit> units = new List<ConditionUnit>();
    }

    // A trigger expression: outer list is OR across groups, inner list
    // is AND across units. Empty groups list is treated as "always
    // passes". Designer-facing field; the runtime bucket projects
    // `cond.groups` (the List<ConditionGroup>) for fast iteration.
    [Serializable]
    public class ConditionConfig
    {
        public TriggerEvent triggerEvent;
        public List<ConditionGroup> groups = new List<ConditionGroup>();
    }

    [Serializable]
    public class ComponentConfig
    {
        [ComponentTypeRef]
        public string componentType;
        public ParamList parameters = new ParamList();
        public ConditionConfig[] triggers = Array.Empty<ConditionConfig>();
    }
}
