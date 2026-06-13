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
    public class AttackEventValueModifier : AbilityComponentBase
    {
        private Func<string[]> _fields;
        // Per-type parsed values; one slot per field index. Type determined by the field's
        // known type — float fields read _floatValues[i], int fields read _intValues[i].
        // Only one is ever populated per index.
        private Func<float[]> _floatValues;
        private Func<int[]> _intValues;
        private Func<string[]> _methods;

        public override void OnInit(AbilityContext ctx, ParamList parameters)
        {
            var bb = ctx.sharedBlackboard;
            _fields      = parameters.GetStringArrayLazy<string>("fields",  null, bb);
            _methods     = parameters.GetStringArrayLazy<string>("methods", null, bb);
            _floatValues = parameters.GetFloatArrayLazy ("values",  null, bb);
            _intValues   = parameters.GetIntArrayLazy   ("values",  null, bb);
        }

        public override void OnTrigger(AbilityContext ctx)
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

            // Snapshot the arrays once so we don't re-run the lazy getters inside the loop.
            string[] fields  = _fields();
            float[]  floats  = _floatValues();
            int[]    ints    = _intValues();
            string[] methods = _methods();

            for (int i = 0; i < min; i++)
            {
                string field  = fields[i];
                string method = methods[i];
                if (!MathOps.TryParse(method, out var op))
                {
                    WarnUnknownMethodOnce(method, field);
                    continue;
                }

                switch (field)
                {
                    case "multiplyer":
                        dab.multiplyer = MathOps.Apply(dab.multiplyer, floats[i], op);
                        break;
                    case "defPenetrate":
                        dab.defPenetrate = MathOps.Apply(dab.defPenetrate, floats[i], op);
                        break;
                    case "mgrPenetrate":
                        dab.mgrPenetrate = MathOps.Apply(dab.mgrPenetrate, floats[i], op);
                        break;
                    case "defPenetrate_value":
                        dab.defPenetrate_value = MathOps.Apply(dab.defPenetrate_value, floats[i], op);
                        break;
                    case "mgrPenetrate_value":
                        dab.mgrPenetrate_value = MathOps.Apply(dab.mgrPenetrate_value, floats[i], op);
                        break;
                    case "damageType":
                        dab.damageType = MathOps.ApplyIntMixed(dab.damageType, ints[i], floats[i], op);
                        break;
                    case "applyType":
                        dab.applyType = MathOps.ApplyIntMixed(dab.applyType, ints[i], floats[i], op);
                        break;
                    case "cumbo":
                        if (bae == null)
                        {
                            Debug.Log($"AttackEventValueModifier: 'cumbo' skipped — event is not BeforeAttackEvent");
                            break;
                        }
                        bae.cumbo = MathOps.ApplyIntMixed(bae.cumbo, ints[i], floats[i], op);
                        break;
                    default:
                        Debug.LogWarning($"AttackEventValueModifier: unknown field '{field}'; skipped");
                        break;
                }
            }
        }

        // ---- helpers ----

        // Bounded-log warning for unknown method strings (e.g. typo in a designer-authored
        // methods=... CSV). OneShotWarn keeps a single log per unique (method,field) pair
        // across the app lifetime.
        private static void WarnUnknownMethodOnce(string method, string field)
        {
            OneShotWarn.WarnOnce(
                "aevm-unknown-method:" + method + ":" + field,
                $"AttackEventValueModifier: unknown method '{method}' for field '{field}'; skipped");
        }
    }
}
