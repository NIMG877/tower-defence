using System;
using UnityEngine;

namespace AbilitySystem.Components
{
    /// <summary>
    /// Generic field-rewriter for <see cref="DamageEventBase"/> events (covers
    /// <see cref="BeforeAttackEvent"/>, <see cref="AfterAttackEvent"/>,
    /// <see cref="BeforeTakeDamageEvent"/>, <see cref="AfterTakeDamageEvent"/>).
    /// Reads three parallel CSVs from <c>OnInit</c>:
    /// <list type="bullet">
    ///   <item><c>fields</c> — comma-separated field names (whitelisted; see below)</item>
    ///   <item><c>values</c> — comma-separated numeric values (float or int, per field type)</item>
    ///   <item><c>methods</c> — comma-separated operators: <c>mult</c> / <c>add</c> / <c>set</c> / <c>div</c></item>
    /// </list>
    /// Each triple at the same index is applied in order. <c>cumbo</c> is only meaningful on
    /// <see cref="BeforeAttackEvent"/> and is silently skipped on other DamageEventBase events.
    ///
    /// <para>Supersedes the removed <c>AttackMultiplierBoost</c> (<c>multiplyer *= N</c>) and
    /// <c>SetAttackCombo</c> (<c>cumbo = N</c>), both of which were folded into this component
    /// as CSV triples (<c>fields=multiplyer, methods=mult</c> /
    /// <c>fields=cumbo, methods=set</c>).</para>
    /// </summary>
    [RegisterComponent("AttackEventValueModifier")]
    public class AttackEventValueModifier : IAbilityComponent
    {
        private Func<string[]> _fields;
        // Per-type parsed values; one slot per field index. Type determined by the field's
        // known type — float fields read _floatValues[i], int fields read _intValues[i].
        // Only one is ever populated per index.
        private Func<float[]> _floatValues;
        private Func<int[]> _intValues;
        private Func<string[]> _methods;

        public void OnInit(AbilityContext ctx, ParamList parameters)
        {
            var bb = ctx.sharedBlackboard;
            var rawFields  = parameters.GetStringLazy("fields",  "", bb);
            var rawValues  = parameters.GetStringLazy("values",  "", bb);
            var rawMethods = parameters.GetStringLazy("methods", "", bb);
            _fields  = () => SplitCsv(rawFields());
            _methods = () => SplitCsv(rawMethods());
            // Pre-parse values by attempting both float and int. Each index's type is locked
            // by the field's known type (see OnTrigger switch), so we keep both arrays and
            // ignore the other at apply time. A non-numeric value silently becomes 0 in
            // both arrays; the wrong-type slot is never read at apply time (e.g. a value
            // parsed into _intValues is only read for an int field), so the other array's
            // 0 is harmless.
            _floatValues = () =>
            {
                var arr = SplitCsv(rawValues());
                var f = new float[arr.Length];
                for (int i = 0; i < arr.Length; i++) float.TryParse(arr[i], out f[i]);
                return f;
            };
            _intValues = () =>
            {
                var arr = SplitCsv(rawValues());
                var n = new int[arr.Length];
                for (int i = 0; i < arr.Length; i++) int.TryParse(arr[i], out n[i]);
                return n;
            };
        }

        public void OnTrigger(AbilityContext ctx)
        {
            if (ctx.currentEvent is not DamageEventBase dab)
            {
                Debug.LogError("AttackEventValueModifier: current event is not a DamageEventBase; skipping");
                return;
            }
            BeforeAttackEvent bae = dab as BeforeAttackEvent;

            // Length-mismatch guard: log once, then trim to min length so OnTrigger
            // can index safely. Lenient by design (per spec §"CSV 错误处理").
            int fLen = _fields().Length;
            int vLen = _floatValues().Length;
            int mLen = _methods().Length;
            int min = Math.Min(Math.Min(fLen, vLen), mLen);
            if (fLen != vLen || vLen != mLen)
            {
                Debug.LogWarning($"AttackEventValueModifier: length mismatch fields={fLen} values={vLen} methods={mLen}; applying first {min} entries");
            }

            for (int i = 0; i < min; i++)
            {
                string field = _fields()[i];
                string method = _methods()[i];

                switch (field)
                {
                    case "multiplyer":
                        dab.multiplyer = ApplyFloat(dab.multiplyer, _floatValues()[i], method, field);
                        break;
                    case "defPenetrate":
                        dab.defPenetrate = ApplyFloat(dab.defPenetrate, _floatValues()[i], method, field);
                        break;
                    case "mgrPenetrate":
                        dab.mgrPenetrate = ApplyFloat(dab.mgrPenetrate, _floatValues()[i], method, field);
                        break;
                    case "defPenetrate_value":
                        dab.defPenetrate_value = ApplyFloat(dab.defPenetrate_value, _floatValues()[i], method, field);
                        break;
                    case "mgrPenetrate_value":
                        dab.mgrPenetrate_value = ApplyFloat(dab.mgrPenetrate_value, _floatValues()[i], method, field);
                        break;
                    case "damageType":
                        dab.damageType = ApplyInt(dab.damageType, _intValues()[i], _floatValues()[i], method, field);
                        break;
                    case "applyType":
                        dab.applyType = ApplyInt(dab.applyType, _intValues()[i], _floatValues()[i], method, field);
                        break;
                    case "cumbo":
                        if (bae == null)
                        {
                            Debug.Log($"AttackEventValueModifier: 'cumbo' skipped — event is not BeforeAttackEvent");
                            break;
                        }
                        bae.cumbo = ApplyInt(bae.cumbo, _intValues()[i], _floatValues()[i], method, field);
                        break;
                    default:
                        Debug.LogWarning($"AttackEventValueModifier: unknown field '{field}'; skipped");
                        break;
                }
            }
        }

        public void OnTick(AbilityContext ctx, float dt) { }
        public void OnTeardown(AbilityContext ctx) { }

        // ---- helpers ----

        private static float ApplyFloat(float current, float value, string method, string field)
        {
            switch (method)
            {
                case "mult": return current * value;
                case "add":  return current + value;
                case "set":  return value;
                case "div":  return current / value;
                default:
                    Debug.LogWarning($"AttackEventValueModifier: unknown method '{method}' for field '{field}'; skipped");
                    return current;
            }
        }

        // Int overload also takes the float-parsed value for the 'mult' case so a designer
        // who typed "1.5" for cumbo still gets a sensible (rounded) result instead of a parse
        // failure. 'add' and 'set' prefer the int-parsed value to keep designer intent exact.
        private static int ApplyInt(int current, int intValue, float floatValue, string method, string field)
        {
            switch (method)
            {
                case "mult": return Mathf.RoundToInt(current * floatValue);
                case "add":  return current + intValue;
                case "set":  return intValue;
                case "div":  return Mathf.RoundToInt(current / floatValue);
                default:
                    Debug.LogWarning($"AttackEventValueModifier: unknown method '{method}' for field '{field}'; skipped");
                    return current;
            }
        }

        private static string[] SplitCsv(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return Array.Empty<string>();
            var parts = csv.Split(',');
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
            return parts;
        }
    }
}
