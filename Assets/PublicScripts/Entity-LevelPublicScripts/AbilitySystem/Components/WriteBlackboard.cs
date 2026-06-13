using System;
using UnityEngine;

namespace AbilitySystem.Components
{
    /// <summary>
    /// Writes a value to the per-Entity shared Blackboard. Two modes:
    ///
    /// <para><b>"set"</b> (default): writes the value as-is to the configured key. The
    /// value's runtime type is determined by ParamEntry.type (Int/Float/Bool/String/
    /// Vector2Int, plus the 4 Unity asset types which fall through to string). Both
    /// key and value support fromBlackboard=true.</para>
    ///
    /// <para><b>"add" / "mult" / "div"</b>: reads the existing value at the key, applies
    /// the operation with the configured value, and writes the result back. Only
    /// supported for numeric existing values (int/float/double); bool/string/Vector2Int
    /// log a warning and skip. Math semantics mirror <see cref="MathOps.Apply{T}"/>.</para>
    /// </summary>
    [RegisterComponent("WriteBlackboard")]
    public class WriteBlackboard : AbilityComponentBase
    {
        private Func<string>  _key;
        private Func<object> _value;
        private Func<string>  _method;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            var bb = ctx.sharedBlackboard;
            _key    = p.GetStringLazy("key",    "",    bb);
            _value  = p.GetValueLazy ("value",  null,  bb);
            _method = p.GetStringLazy("method", "set", bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (ctx.sharedBlackboard == null) return;
            string key = _key();
            if (string.IsNullOrEmpty(key)) return;

            object value = _value();
            string method = _method();

            switch (method)
            {
                case "set":
                    if (value != null) ctx.sharedBlackboard.Set(key, value);
                    return;
                case "add":
                case "mult":
                case "div":
                    ApplyOp(ctx.sharedBlackboard, key, value, method);
                    return;
                default:
                    Debug.LogWarning($"WriteBlackboard: unknown method '{method}'; skipping");
                    return;
            }
        }

        // "add" / "mult" / "div" path. Reads existing at key, parses value to existing's type,
        // applies the op, writes back. No existing value = no-op (designer should set first
        // or use method="set" to seed). Null value also no-ops (otherwise a missing param
        // would zero out BB via mult, or no-op via add — explicit is better).
        private static void ApplyOp(Blackboard bb, string key, object value, string method)
        {
            if (value == null) return;
            object existing = bb.Get<object>(key, null);
            if (existing == null) return;

            Type t = existing.GetType();
            if (!MathOps.TryParse(method, out var op))
            {
                // Defensive: OnTrigger's switch already rejects unknown methods.
                Debug.LogWarning($"WriteBlackboard: unknown method '{method}'; skipping");
                return;
            }
            try
            {
                if (t == typeof(int))
                    bb.Set(key, MathOps.Apply((int)existing, Convert.ToInt32(value), op));
                else if (t == typeof(float))
                    bb.Set(key, MathOps.Apply((float)existing, Convert.ToSingle(value), op));
                else if (t == typeof(double))
                    bb.Set(key, MathOps.Apply((double)existing, Convert.ToDouble(value), op));
                else
                    Debug.LogWarning($"WriteBlackboard: method='{method}' unsupported for type {t.Name}; skipping");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"WriteBlackboard: failed to convert value to {t.Name}: {ex.Message}; skipping");
            }
        }
    }
}
