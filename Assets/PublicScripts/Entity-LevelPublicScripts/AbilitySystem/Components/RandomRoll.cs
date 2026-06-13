using System;
using UnityEngine;

namespace AbilitySystem.Components
{
    /// <summary>
    /// Per-event random roll. Picks a result according to <c>mode</c> and
    /// writes it to the per-Entity shared Blackboard at <c>outputKey</c>.
    /// Three modes (1:1 with <c>input</c> format):
    ///
    /// <list type="bullet">
    /// <item><b>"probability"</b> (default): <c>input</c> is a single float in
    /// <c>[0,1]</c>. <c>RandomHelper.RandomP(p)</c> → writes
    /// <c>"True"</c> or <c>"False"</c> (matches <c>bool.TryParse</c>).</item>
    ///
    /// <item><b>"value"</b>: <c>input</c> is a 2-float CSV <c>"min,max"</c>.
    /// Uniform float in <c>[min,max]</c> → writes <c>float.ToString("R")</c>
    /// (round-trippable, parses back via <c>float.TryParse</c>).</item>
    ///
    /// <item><b>"list"</b>: <c>input</c> is a string CSV
    /// <c>"a,b,c,..."</c>. Uniform pick → writes the chosen trimmed string.
    /// <c>min &gt; max</c> in <c>"value"</c> is silently swapped.</item>
    /// </list>
    ///
    /// <para>All three parameters (<c>input</c>, <c>mode</c>, <c>outputKey</c>)
    /// support <c>fromBlackboard=true</c>; <c>input</c> is always read as a
    /// string (downstream readers should also read as a string — matches the
    /// <c>ConditionEvaluator</c> convention).</para>
    /// </summary>
    [RegisterComponent("RandomRoll")]
    public class RandomRoll : AbilityComponentBase
    {
        private Func<string> _input;
        private Func<string> _mode;
        private Func<string> _outputKey;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            var bb = ctx.sharedBlackboard;
            _input     = p.GetStringLazy("input",     "",              bb);
            _mode      = p.GetStringLazy("mode",      "probability",   bb);
            _outputKey = p.GetStringLazy("outputKey", "",              bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (ctx.sharedBlackboard == null) return;
            string key = _outputKey();
            if (string.IsNullOrEmpty(key)) return;
            string input = _input();
            if (string.IsNullOrEmpty(input)) return;

            string modeStr = _mode();
            if (string.IsNullOrEmpty(modeStr) || modeStr == "probability")
            {
                // 概率门:input 必须是 [0,1] 浮点。无法 parse → 静默跳过(designer 责任)。
                if (!float.TryParse(input, out var p)) return;
                // RandomP(p) 对 p>1 恒真、p<0 恒假,边界无需 clamp。
                bool passed = RandomHelper.Helper.RandomP(p);
                ctx.sharedBlackboard.Set(key, passed ? "true" : "false");
                return;
            }
            if (modeStr == "value")
            {
                // CSV "min,max";多/少取前两个,失败值当 0(CsvParser 的容错约定)。
                var range = CsvParser.Split<float>(
                    input, s => float.TryParse(s, out var v) ? v : 0f);
                if (range.Length < 2) return;
                // min > max 静默 swap 由 RandomF 内部处理,designer 写反也行。
                ctx.sharedBlackboard.Set(key, RandomHelper.Helper.RandomF(range[0], range[1]));
                return;
            }
            if (modeStr == "list")
            {
                var parts = CsvParser.SplitStrings(input);
                if (parts.Length == 0) return;
                ctx.sharedBlackboard.Set(key, RandomHelper.Helper.RandomL(parts));
                return;
            }
            Debug.LogWarning($"RandomRoll: unknown mode '{modeStr}'; skipping");
        }
    }
}
