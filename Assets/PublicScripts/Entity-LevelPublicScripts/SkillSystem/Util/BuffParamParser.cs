using System;

namespace SkillSystem
{
    /// <summary>
    /// Shared CSV parsers for buff-related skill parameters.
    /// Hoisted out of ApplyBuffComponent / PeriodicAuraBuffComponent to keep parsing rules
    /// in one place — any future change (whitespace handling, culture, separators) is one edit.
    /// </summary>
    public static class BuffParamParser
    {
        public static BuffType[] ParseBuffTypes(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return Array.Empty<BuffType>();
            var parts = csv.Split(',');
            var arr = new BuffType[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                arr[i] = (BuffType)Enum.Parse(typeof(BuffType), parts[i].Trim());
            return arr;
        }

        public static float[] ParseFloats(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return Array.Empty<float>();
            var parts = csv.Split(',');
            var arr = new float[parts.Length];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = float.Parse(parts[i].Trim());
            return arr;
        }
    }
}
