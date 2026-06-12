using System;
using System.Collections.Generic;
using UnityEngine;

namespace AbilitySystem
{
    public enum ParamValueType { Int, Float, Bool, String, Vector2Int, AnimationRef, Prefab, EntityId, Color }

    [Serializable]
    public class ParamEntry
    {
        public string key;
        public string value;
        // 当为 true 时，运行时 GetXxxLazy 把 `value` 当作 Blackboard 的 key 名而非字面量;
        // 每次调用返回的 Func<T> 都会重新去 Blackboard 取值(按 `type` 期望的类型)。
        // 仅对 GetXxxLazy 系列生效;老的 eager GetXxx 永远按字面量解析,忽略此标记。
        public ParamValueType type;
        public bool fromBlackboard;
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
            return ParseVector2Int(GetRaw(key), defaultValue);
        }

        // ========= Lazy 取值器系列 =========
        // 返回 Func<T>,组件在需要时调用 () 获取当前值。
        //   - entry 不存在 → 闭包恒返 defaultValue
        //   - entry.fromBlackboard == true 且 bb != null → 闭包每次 bb.Get<T>(value, defaultValue),
        //     可在不同 trigger 间动态变化;类型错抛 InvalidCastException 时降级为 defaultValue 并一次性 warn
        //   - 其它(含 fromBlackboard=true 但 bb==null 的 footgun)→ 字面量 parse 一次缓存进闭包;
        //     bb==null 的 footgun 会一次性 Debug.LogWarning,避免设计师勾了 flag 但调用方没传 bb 时静默失效。

        public Func<int> GetIntLazy(string key, int defaultValue = 0, Blackboard bb = null)
        {
            var entry = FindEntry(key);
            if (entry == null) return () => defaultValue;
            if (entry.fromBlackboard) return MakeBlackboardGetter(entry, defaultValue, bb);
            int parsed = int.TryParse(entry.value, out var v) ? v : defaultValue;
            return () => parsed;
        }

        public Func<float> GetFloatLazy(string key, float defaultValue = 0f, Blackboard bb = null)
        {
            var entry = FindEntry(key);
            if (entry == null) return () => defaultValue;
            if (entry.fromBlackboard) return MakeBlackboardGetter(entry, defaultValue, bb);
            float parsed = float.TryParse(entry.value, out var v) ? v : defaultValue;
            return () => parsed;
        }

        public Func<bool> GetBoolLazy(string key, bool defaultValue = false, Blackboard bb = null)
        {
            var entry = FindEntry(key);
            if (entry == null) return () => defaultValue;
            if (entry.fromBlackboard) return MakeBlackboardGetter(entry, defaultValue, bb);
            bool parsed = !string.IsNullOrEmpty(entry.value) && bool.TryParse(entry.value, out var v)
                ? v : defaultValue;
            return () => parsed;
        }

        public Func<string> GetStringLazy(string key, string defaultValue = "", Blackboard bb = null)
        {
            var entry = FindEntry(key);
            if (entry == null) return () => defaultValue;
            if (entry.fromBlackboard) return MakeBlackboardGetter(entry, defaultValue, bb);
            string parsed = entry.value ?? defaultValue;
            return () => parsed;
        }

        public Func<Vector2Int> GetVector2IntLazy(string key, Vector2Int defaultValue = default, Blackboard bb = null)
        {
            var entry = FindEntry(key);
            if (entry == null) return () => defaultValue;
            if (entry.fromBlackboard) return MakeBlackboardGetter(entry, defaultValue, bb);
            Vector2Int parsed = ParseVector2Int(entry.value, defaultValue);
            return () => parsed;
        }

        // 通用版:把 entry.value 按 ParamEntry.type 解析为 object 后闭包返回,用于"值的类型由
        // ParamEntry 决定"的场景(典型:WriteBlackboard 组件,designer 在 ParamList 里通过
        // ParamEntry.type 指定写入 BB 的类型)。三分支语义同其它 GetXxxLazy:不存在/字面量
        // 缓存/fromBlackboard 每次重读;fromBlackboard 路径走 MakeBlackboardGetter<object>,
        // (object)v 是 upcast 永不会 InvalidCastException,无需 try/catch。
        public Func<object> GetValueLazy(string key, object defaultValue = null, Blackboard bb = null)
        {
            var entry = FindEntry(key);
            if (entry == null) return () => defaultValue;
            if (entry.fromBlackboard) return MakeBlackboardGetter(entry, defaultValue, bb);
            object parsed = ParseAsType(entry, defaultValue);
            return () => parsed;
        }

        // ========= 内部 helpers =========

        private ParamEntry FindEntry(string key)
        {
            if (entries == null) return null;
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].key == key) return entries[i];
            return null;
        }

        private static Vector2Int ParseVector2Int(string raw, Vector2Int defaultValue)
        {
            if (string.IsNullOrEmpty(raw)) return defaultValue;
            var parts = raw.Split(',');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var x) &&
                int.TryParse(parts[1], out var y))
                return new Vector2Int(x, y);
            return defaultValue;
        }

        // 按 ParamEntry.type 把字符串解析成对应的 boxed 值。defaultValue 若与目标类型匹配
        // 则用作 parse 失败的 fallback,否则用类型的自然零值。Unity 资产类型
        // (AnimationRef/Prefab/EntityId/Color) 无对应 parser,降级为存字符串 — designer
        // 责任保证 BB 写入有意义。
        private static object ParseAsType(ParamEntry entry, object defaultValue)
        {
            switch (entry.type)
            {
                case ParamValueType.Int:
                    int di = defaultValue is int x ? x : 0;
                    return int.TryParse(entry.value, out var i) ? i : di;
                case ParamValueType.Float:
                    float df = defaultValue is float y ? y : 0f;
                    return float.TryParse(entry.value, out var f) ? f : df;
                case ParamValueType.Bool:
                    return !string.IsNullOrEmpty(entry.value) && bool.TryParse(entry.value, out var b)
                        ? b
                        : defaultValue is bool z && z;
                case ParamValueType.String:
                    return entry.value ?? defaultValue as string ?? "";
                case ParamValueType.Vector2Int:
                    Vector2Int dv = defaultValue is Vector2Int v ? v : default;
                    return ParseVector2Int(entry.value, dv);
                default:
                    return entry.value ?? defaultValue;
            }
        }

        // fromBlackboard=true 路径的取值器工厂。bb==null 时降级到常量 defaultValue + 一次性 warn,
        // 避免组件作者忘记把 ctx.sharedBlackboard 接到 GetXxxLazy 上时静默失效。
        // Blackboard.Get<T> 当前是硬转 (T)v,类型错会 throw InvalidCastException;
        // 这里 try/catch 兜底降级为 defaultValue 并一次性 warn,免得设计师勾错类型整局崩。
        private static Func<T> MakeBlackboardGetter<T>(ParamEntry entry, T defaultValue, Blackboard bb)
        {
            if (bb == null)
            {
                WarnMissingBlackboardOnce(entry.key);
                return () => defaultValue;
            }
            string bbKey = entry.value;
            return () =>
            {
                try { return bb.Get(bbKey, defaultValue); }
                catch (InvalidCastException)
                {
                    WarnBlackboardTypeMismatchOnce(bbKey);
                    return defaultValue;
                }
            };
        }

        // 一次性 warn 集合,仿 ConditionEvaluator._warnedOps 模式,避免运行时日志刷屏。
        private static readonly HashSet<string> _warnedMissingBlackboard = new();
        private static readonly HashSet<string> _warnedTypeMismatch = new();

        private static void WarnMissingBlackboardOnce(string paramKey)
        {
            if (_warnedMissingBlackboard.Add(paramKey))
            {
                Debug.LogWarning(
                    $"[ParamList] Param '{paramKey}' has fromBlackboard=true but caller did not pass a Blackboard to GetXxxLazy. " +
                    "Returning defaultValue. Pass ctx.sharedBlackboard to enable blackboard-sourced reads.");
            }
        }

        private static void WarnBlackboardTypeMismatchOnce(string bbKey)
        {
            if (_warnedTypeMismatch.Add(bbKey))
            {
                Debug.LogWarning(
                    $"[ParamList] Blackboard key '{bbKey}' value runtime type does not match the requested type. " +
                    "Returning defaultValue.");
            }
        }
    }

    public enum TriggerEvent
    {
        // 生命周期事件
        OnPreWarm, OnInitialize,
        OnAbilityBegin, OnAbilityEnd,
        OnAbilityAdded, OnAbilityRemoved,
        // 攻击相关事件
        OnBeforeAttack, OnAfterAttack,
        OnBeforeTakeDamage, OnAfterTakeDamage,
        OnAttackSuccessfully, OnAttackInterrupt,
        // 受击相关事件
        OnBeforeHurt, OnAfterHurt,
        // 动画相关事件
        OnAttackAnimBegin,OnBeforeDieAnimation,
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
