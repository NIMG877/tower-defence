using UnityEngine;

namespace AbilitySystem
{
    /// <summary>
    /// Shared add/mult/div/set math operator for ability-system fields. Single
    /// source of truth for the operator vocabulary — adding a new method (e.g.
    /// <c>"mod"</c>) is one switch instead of one switch per consumer.
    ///
    /// <para>Lives in <c>GameData</c> assembly so it can be used by both runtime
    /// components (BasicScripts) and any future designer-time tooling.</para>
    ///
    /// <para>Used by <c>AttackEventValueModifier</c> and <c>WriteBlackboard</c>.
    /// Unknown op returns the current value unchanged — callers that need a warning
    /// are responsible for logging once (see <see cref="OneShotWarn"/>).</para>
    /// </summary>
    public static class MathOps
    {
        public enum Op { Unknown, Add, Mult, Div, Set }

        public static bool TryParse(string method, out Op op)
        {
            switch (method)
            {
                case "add":  op = Op.Add;  return true;
                case "mult": op = Op.Mult; return true;
                case "div":  op = Op.Div;  return true;
                case "set":  op = Op.Set;  return true;
                default:     op = Op.Unknown; return false;
            }
        }

        public static int Apply(int current, int value, Op op)
        {
            switch (op)
            {
                case Op.Add:  return current + value;
                case Op.Mult: return current * value;
                case Op.Div:  return current / value;
                case Op.Set:  return value;
                default:      return current;
            }
        }

        public static float Apply(float current, float value, Op op)
        {
            switch (op)
            {
                case Op.Add:  return current + value;
                case Op.Mult: return current * value;
                case Op.Div:  return current / value;
                case Op.Set:  return value;
                default:      return current;
            }
        }

        public static double Apply(double current, double value, Op op)
        {
            switch (op)
            {
                case Op.Add:  return current + value;
                case Op.Mult: return current * value;
                case Op.Div:  return current / value;
                case Op.Set:  return value;
                default:      return current;
            }
        }

        // Mixed variant for int fields where a designer may type a float literal
        // for mult/div (e.g. cumbo * 1.5). add/set prefer the int value to keep
        // designer intent exact (2 stays 2, not 2.0).
        public static int ApplyIntMixed(int current, int intValue, float floatValue, Op op)
        {
            switch (op)
            {
                case Op.Mult: return Mathf.RoundToInt(current * floatValue);
                case Op.Div:  return Mathf.RoundToInt(current / floatValue);
                case Op.Add:  return current + intValue;
                case Op.Set:  return intValue;
                default:      return current;
            }
        }
    }
}
