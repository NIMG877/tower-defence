using System.Collections.Generic;
using UnityEngine;

namespace AbilitySystem
{
    /// <summary>
    /// Per-key one-shot warning. The first time <see cref="WarnOnce"/> sees a key,
    /// the message is logged; subsequent calls with the same key are no-ops. This
    /// bounds log spam when a misconfigured parameter triggers on every event
    /// dispatch, every frame, or every component instance.
    ///
    /// <para>Single shared <see cref="HashSet{T}"/> across the application — callers
    /// should namespace their keys with a category prefix to avoid collisions
    /// (e.g. <c>"cond-op:" + op</c>, <c>"bb-missing:" + paramKey</c>).</para>
    ///
    /// <para>Used by <c>ConditionEvaluator</c> and <c>ParamList</c>.</para>
    /// </summary>
    public static class OneShotWarn
    {
        private static readonly HashSet<string> _seen = new HashSet<string>();

        public static void WarnOnce(string key, string message)
        {
            if (_seen.Add(key)) Debug.LogWarning(message);
        }

        // Test / hot-reload hook. Not called in production code.
        public static void Reset() => _seen.Clear();
    }
}
