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
        public static BuffType[] ParseBuffTypes(string csv) => ParseCsv(csv, s => (BuffType)Enum.Parse(typeof(BuffType), s));
        public static float[] ParseFloats(string csv) => ParseCsv(csv, float.Parse);

        private static T[] ParseCsv<T>(string csv, Func<string, T> parse)
        {
            if (string.IsNullOrEmpty(csv)) return Array.Empty<T>();
            var parts = csv.Split(',');
            var arr = new T[parts.Length];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = parse(parts[i].Trim());
            return arr;
        }
    }
}
