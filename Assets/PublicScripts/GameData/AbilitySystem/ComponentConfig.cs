using System;
using System.Collections.Generic;
using Newtonsoft.Json;
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
        // 每次调用返回的 Func<T> 都会重新去 Blackboard 取值。
        // 仅对 GetXxxLazy 系列生效;老的 eager GetXxx 永远按字面量解析,忽略此标记。
        // 注:有类型版(GetIntLazy/GetFloatLazy/...)按泛型 T 解析;只有 GetValueLazy
        // 才按 `type` 解析(适用"值的类型由 ParamEntry 决定"的场景,如 WriteBlackboard)。
        public bool fromBlackboard;
        public ParamValueType type;
        
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

        // 数组版 Lazy:字面量同时吃 CSV("1.4,2.5")和单值("1.4")——CsvParser.Split 天然支持。
        // fromBlackboard 路径容错:BB 里存 float[]/float/int/string 都能读(int 无损升位);
        // 其它类型走 warn 桶。
        public Func<float[]> GetFloatArrayLazy(string key, float[] defaultValue = null, Blackboard bb = null)
        {
            var entry = FindEntry(key);
            if (entry == null) return () => defaultValue ?? Array.Empty<float>();
            if (entry.fromBlackboard)
            {
                if (bb == null)
                {
                    WarnMissingBlackboardOnce(entry.key);
                    return () => defaultValue ?? Array.Empty<float>();
                }
                return () => ReadFloatArrayFromBB(bb, entry.value, defaultValue);
            }
            float[] parsed = CsvParser.Split(entry.value, float.Parse);
            return () => parsed;
        }

        // 字符串数组版 Lazy,CSV/单值都吃,BB 容错 string[]/string。
        // 带 parser 时把每个 token 解析成 T(典型:Enum.Parse 给 ModifierOp[]);
        // 不带 parser 时 T 必须是 string,走 SplitStrings。
        public Func<T[]> GetStringArrayLazy<T>(string key, T[] defaultValue = null, Blackboard bb = null, Func<string, T> parser = null)
        {
            var entry = FindEntry(key);
            if (entry == null) return () => defaultValue ?? Array.Empty<T>();
            if (entry.fromBlackboard)
            {
                if (bb == null)
                {
                    WarnMissingBlackboardOnce(entry.key);
                    return () => defaultValue ?? Array.Empty<T>();
                }
                return () => ReadStringArrayFromBB(bb, entry.value, defaultValue, parser);
            }
            if (parser == null)
            {
                if (typeof(T) != typeof(string))
                {
                    WarnStringArrayParserRequiredOnce(entry.key, typeof(T));
                    return () => defaultValue ?? Array.Empty<T>();
                }
                string[] arr = CsvParser.SplitStrings(entry.value);
                return () => (T[])(object)arr;
            }
            T[] parsed = CsvParser.Split(entry.value, parser);
            return () => parsed;
        }

        // 整型数组版 Lazy,CSV/单值都吃,BB 容错 int[]/int/string。
        public Func<int[]> GetIntArrayLazy(string key, int[] defaultValue = null, Blackboard bb = null)
        {
            var entry = FindEntry(key);
            if (entry == null) return () => defaultValue ?? Array.Empty<int>();
            if (entry.fromBlackboard)
            {
                if (bb == null)
                {
                    WarnMissingBlackboardOnce(entry.key);
                    return () => defaultValue ?? Array.Empty<int>();
                }
                return () => ReadIntArrayFromBB(bb, entry.value, defaultValue);
            }
            int[] parsed = CsvParser.Split(entry.value, int.Parse);
            return () => parsed;
        }

        // Vector2Int 数组版 Lazy。字面量路径用 ParseVector2IntArray(格式
        // "[[x,y],[x,y],..." JSON 数组)。BB 路径走 ReadVector2IntArrayFromBB,
        // 支持 Vector2Int[]/Vector2Int/string 形态。命名上刻意不引入第二个 type
        // 参数(designer 写"Vector2Int 数组"只有一个类型),与 GetIntArrayLazy /
        // GetFloatArrayLazy 保持对称。
        public Func<Vector2Int[]> GetVector2IntArrayLazy(string key, Vector2Int[] defaultValue = null, Blackboard bb = null)
        {
            var entry = FindEntry(key);
            if (entry == null) return () => defaultValue ?? Array.Empty<Vector2Int>();
            if (entry.fromBlackboard)
            {
                if (bb == null)
                {
                    WarnMissingBlackboardOnce(entry.key);
                    return () => defaultValue ?? Array.Empty<Vector2Int>();
                }
                return () => ReadVector2IntArrayFromBB(bb, entry.value, defaultValue);
            }
            Vector2Int[] parsed = ParseVector2IntArray(entry.value);
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
            // Tolerant parse via CsvParser: 0 on failure (a malformed coord becomes
            // the zero vector, not an exception, so a typo doesn't break a skill's
            // whole loadout). We need exactly 2 ints; any other shape returns default.
            var parts = CsvParser.Split<int>(raw, ParseIntOrZero);
            return parts.Length == 2
                ? new Vector2Int(parts[0], parts[1])
                : defaultValue;
        }

        private static int ParseIntOrZero(string s) => int.TryParse(s, out var v) ? v : 0;

        // Parse the designer-facing Vector2Int array literal: "[[x,y],[x,y],...".
        // Newtonsoft.Json does the heavy lifting. Lenient: a malformed group
        // (wrong coord count / unparsable int) is dropped, and an unparseable
        // string returns an empty array (same posture as ParseVector2Int).
        private static Vector2Int[] ParseVector2IntArray(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return Array.Empty<Vector2Int>();
            try
            {
                var arr = JsonConvert.DeserializeObject<int[][]>(raw);
                if (arr == null) return Array.Empty<Vector2Int>();
                var result = new List<Vector2Int>(arr.Length);
                for (int i = 0; i < arr.Length; i++)
                {
                    var pair = arr[i];
                    if (pair != null && pair.Length >= 2) result.Add(new Vector2Int(pair[0], pair[1]));
                }
                return result.ToArray();
            }
            catch
            {
                return Array.Empty<Vector2Int>();
            }
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

        // 一次性 warn: 走 OneShotWarn,共享应用级 HashSet(按 category 前缀分桶避免
        // "missing-bb:foo" 和 "type-mismatch:foo" 互相吞掉)。
        private static void WarnMissingBlackboardOnce(string paramKey)
        {
            OneShotWarn.WarnOnce(
                "bb-missing:" + paramKey,
                $"[ParamList] Param '{paramKey}' has fromBlackboard=true but caller did not pass a Blackboard to GetXxxLazy. " +
                "Returning defaultValue. Pass ctx.sharedBlackboard to enable blackboard-sourced reads.");
        }

        private static void WarnBlackboardTypeMismatchOnce(string bbKey)
        {
            OneShotWarn.WarnOnce(
                "bb-type:" + bbKey,
                $"[ParamList] Blackboard key '{bbKey}' value runtime type does not match the requested type. " +
                "Returning defaultValue.");
        }

        // GetFloatArrayLazy 的 fromBlackboard 容错读。Get<object> 绕开硬转,运行时 type
        // switch 拿值;4 种 known case 走对应路径,其它类型走独立 warn 桶('bb-array-type:')
        // 避免和 'bb-type:' 互相吞掉。
        private static float[] ReadFloatArrayFromBB(Blackboard bb, string bbKey, float[] defaultValue)
        {
            
            object v = bb.Get<object>(bbKey, null);
            if (v == null) return defaultValue ?? Array.Empty<float>();
            switch (v)
            {
                case float[] arr: return arr;
                case float f:     return new[] { f };
                // int 无损升位,属数值标量契约(write_blackboard 字面量 type:Int、int
                // 属性装箱都会在 BB 留下 int)。有损方向不静默转:double 精度降位、float
                // 值读 int[] 都保留类型告警暴露。
                case int i:       return new[] { (float)i };
                case string s:    return CsvParser.Split(s, float.Parse);
                default:
                    WarnBlackboardArrayTypeMismatchOnce(bbKey);
                    return defaultValue ?? Array.Empty<float>();
            }
        }

        private static void WarnBlackboardArrayTypeMismatchOnce(string bbKey)
        {
            OneShotWarn.WarnOnce(
                "bb-array-type:" + bbKey,
                $"[ParamList] Blackboard key '{bbKey}' runtime type cannot be read as float[]; " +
                "expected float[]/float/int/string. Returning defaultValue.");
        }

        // GetStringArrayLazy<T> 的 fromBlackboard 容错读,与字面量路径对称:BB 里存
        // T[] 直返;string 走 CsvParser 拆分(有 parser 跑 parser,T=string 走
        // SplitStrings);其它类型走独立 warn 桶。
        private static T[] ReadStringArrayFromBB<T>(Blackboard bb, string bbKey, T[] defaultValue, Func<string, T> parser)
        {
            object v = bb.Get<object>(bbKey, null);
            if (v == null) return defaultValue ?? Array.Empty<T>();
            switch (v)
            {
                case T[] arr:
                    return arr;
                case string s:
                    if (parser != null) return CsvParser.Split(s, parser);
                    if (typeof(T) == typeof(string)) return (T[])(object)CsvParser.SplitStrings(s);
                    WarnStringArrayParserRequiredOnce(bbKey, typeof(T));
                    return defaultValue ?? Array.Empty<T>();
                default:
                    WarnBlackboardStringArrayTypeMismatchOnce(bbKey);
                    return defaultValue ?? Array.Empty<T>();
            }
        }

        private static void WarnBlackboardStringArrayTypeMismatchOnce(string bbKey)
        {
            OneShotWarn.WarnOnce(
                "bb-string-array-type:" + bbKey,
                $"[ParamList] Blackboard key '{bbKey}' runtime type cannot be read as string[]; " +
                "expected string[]/string. Returning defaultValue.");
        }

        private static void WarnStringArrayParserRequiredOnce(string key, Type t)
        {
            OneShotWarn.WarnOnce(
                "bb-string-array-parser:" + key,
                $"[ParamList] GetStringArrayLazy<{t.Name}> needs a Func<string, T> parser to convert each CSV element; " +
                "supply one, or use T=string (no parser needed). Returning defaultValue.");
        }

        // GetIntArrayLazy 的 fromBlackboard 容错读。int[] 直返,int 包 [v],string 走 CSV 拆分;其它走独立 warn 桶。
        private static int[] ReadIntArrayFromBB(Blackboard bb, string bbKey, int[] defaultValue)
        {
            object v = bb.Get<object>(bbKey, null);
            if (v == null) return defaultValue ?? Array.Empty<int>();
            switch (v)
            {
                case int[] arr:  return arr;
                case int i:      return new[] { i };
                case string s:   return CsvParser.Split(s, int.Parse);
                default:
                    WarnBlackboardIntArrayTypeMismatchOnce(bbKey);
                    return defaultValue ?? Array.Empty<int>();
            }
        }

        private static void WarnBlackboardIntArrayTypeMismatchOnce(string bbKey)
        {
            OneShotWarn.WarnOnce(
                "bb-int-array-type:" + bbKey,
                $"[ParamList] Blackboard key '{bbKey}' runtime type cannot be read as int[]; " +
                "expected int[]/int/string. Returning defaultValue.");
        }

        // GetVector2IntArrayLazy 的 fromBlackboard 容错读,与字面量路径对称:Vector2Int[]
        // 直返;Vector2Int 单值包 [v];string 走 ParseVector2IntArray(同字面量格式);
        // 其它类型走独立 warn 桶('bb-vec2i-array-type:')避免和 'bb-type:' /
        // 'bb-int-array-type:' 互相吞掉。
        private static Vector2Int[] ReadVector2IntArrayFromBB(Blackboard bb, string bbKey, Vector2Int[] defaultValue)
        {
            object v = bb.Get<object>(bbKey, null);
            if (v == null) return defaultValue ?? Array.Empty<Vector2Int>();
            switch (v)
            {
                case Vector2Int[] arr: return arr;
                case Vector2Int vi:    return new[] { vi };
                case string s:         return ParseVector2IntArray(s);
                default:
                    WarnBlackboardVector2IntArrayTypeMismatchOnce(bbKey);
                    return defaultValue ?? Array.Empty<Vector2Int>();
            }
        }

        private static void WarnBlackboardVector2IntArrayTypeMismatchOnce(string bbKey)
        {
            OneShotWarn.WarnOnce(
                "bb-vec2i-array-type:" + bbKey,
                $"[ParamList] Blackboard key '{bbKey}' runtime type cannot be read as Vector2Int[]; " +
                "expected Vector2Int[]/Vector2Int/string. Returning defaultValue.");
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
        // 通用物理帧事件。追加在末尾以保持既有 asset 的枚举序号稳定。
        OnTick,
        // 召唤物相关事件（WatchSummonDeath 桥接到宿主 runner 上派发）。
        // 同样追加在末尾以保持既有 asset 的枚举序号稳定。
        OnSummonDeath,
        // 索敌候选确定后、数量裁剪前派发（EntityAbilityRunner 桥接
        // EntityAttack.OnBeforeTargetSelect）。
        OnBeforeTargetSelect,
        // 子弹（视觉载弹）抵达目标点销毁时派发（FireBullets 桥接；子弹非实体，
        // 事件走宿主 runner）。追加在末尾以保持既有 asset 的枚举序号稳定。
        OnBulletLanded,
        OnAttackIdle,
    }

    public enum AbilityKind
    {
        Skill,
        Talent,
        ExtraAbility,
        // 追加在尾部：既有资产按序号序列化，禁插中间。
        SubJobTrait,
    }

    public enum ConditionOp
    {
        None, Equal, NotEqual,
        Greater, GreaterOrEqual, Less, LessOrEqual,
        // 追加在尾部：既有资产按序号序列化，禁插中间。
        KeyEqual, KeyNotEqual,
    }

    // A single comparison: op(leftKey, rightValue). The runtime semantics
    // are documented in ConditionEvaluator.EvaluateUnit.
    // KeyEqual/KeyNotEqual compare two blackboard keys (op(leftKey, rightKey))
    // for identity — rightKey is only read by those two ops.
    [Serializable]
    public class ConditionUnit
    {
        public ConditionOp op = ConditionOp.None;
        public string leftKey;
        public string rightValue;
        public string rightKey;
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

    /// <summary>
    /// Controls what happens when the same rule is triggered while one of its
    /// asynchronous step sequences is still running.
    /// </summary>
    public enum RuleReentry
    {
        IgnoreWhileRunning,
        Restart,
        Parallel,
    }

    /// <summary>
    /// One operation in an ability rule. <c>op</c> is resolved by the runtime
    /// registry; <c>args</c> keeps the existing Unity-safe parameter encoding.
    /// Composite operations use <c>condition</c>, <c>steps</c>, and
    /// <c>elseSteps</c> as needed.
    /// </summary>
    [Serializable]
    public class StepConfig
    {
        [StepOpRef]
        public string op;
        public ParamList args = new ParamList();
        public List<ConditionGroup> condition = new List<ConditionGroup>();
        // Managed-reference boundaries are required for this self-recursive
        // shape. Without them Unity expands StepConfig's type tree recursively
        // and hits its serialization depth limit even when child arrays are empty.
        public StepConfig[] steps = Array.Empty<StepConfig>();
        public StepConfig[] elseSteps = Array.Empty<StepConfig>();
    }

    /// <summary>
    /// An event/condition rule whose steps execute in array order.
    /// </summary>
    [Serializable]
    public class AbilityRuleConfig
    {
        public ConditionConfig[] triggers = Array.Empty<ConditionConfig>();
        public RuleReentry reentry = RuleReentry.IgnoreWhileRunning;
        public StepConfig[] steps = Array.Empty<StepConfig>();
        // Detached rules fork their executions to the level-scoped scheduler at
        // trigger time: the sequence keeps advancing after the host dies or
        // returns to the pool. The blackboard is cloned and entity data is
        // snapshotted at fork, so later steps never observe the recycled host
        // (spawn "self" resolves to the fork-time position/camp). Ops that need
        // a live host entity have no detached semantics and must not be used.
        // Reentry is effectively Parallel: detached executions are not counted
        // in the host rule's running set.
        public bool detached;
    }

    /// <summary>
    /// Legacy one-component rule shape. Kept solely so pre-upgrade assets can
    /// be read and projected to <see cref="AbilityRuleConfig"/> without loss.
    /// New authoring uses <see cref="StepConfig"/>.
    /// </summary>
    [Serializable]
    public class ComponentConfig
    {
        [ComponentTypeRef]
        public string componentType;
        public ParamList parameters = new ParamList();
        public ConditionConfig[] triggers = Array.Empty<ConditionConfig>();
    }
}
