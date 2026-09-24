using System;

namespace AbilitySystem
{
    /// <summary>
    /// Shared CSV splitter for ability-system params. Comma-separated, trims each token.
    /// Empty / null input → empty array. Single source of truth for CSV parsing rules
    /// (whitespace handling, separator) — any future change is one edit.
    ///
    /// <para>Lives in <c>GameData</c> assembly so both <c>ParamList</c> (data layer) and
    /// runtime components (BasicScripts) can see it; <c>BasicScripts.asmdef</c> already
    /// references GameData.</para>
    ///
    /// Used by <c>ParamList</c> (GameData data layer: vector/coordinate/array
    /// parsing) and the <c>RandomRoll</c> component (BasicScripts).
    /// </summary>
    public static class CsvParser
    {
        public static string[] SplitStrings(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return Array.Empty<string>();
            var parts = csv.Split(',');
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
            return parts;
        }

        public static T[] Split<T>(string csv, Func<string, T> parser)
        {
            if (string.IsNullOrEmpty(csv)) return Array.Empty<T>();
            var parts = csv.Split(',');
            var arr = new T[parts.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = parser(parts[i].Trim());
            return arr;
        }
    }
}
