using System;
using UnityEngine;

namespace AbilitySystem.Components
{
    /// <summary>
    /// 改写本次攻击的目标数量上下限（selectMaxNum / selectMinNum）。改动经事件桥
    /// 的 ref 回写生效于正在进行的这次 AttackTargetSelect（候选已按优先序排好、
    /// 尚未裁剪），即"本次攻击多打/多治一个"。
    ///
    /// <para>与 <see cref="AttackEventValueModifier"/> 同构的三元 CSV：
    /// <c>fields</c>（白名单 selectmaxnum / selectminnum）、<c>methods</c>
    /// （mult / add / set / div，整数语义经 <c>ApplyIntMixed</c>）、<c>values</c>。
    /// 典型用法：同规则先 random_roll，再用 branch 条件包住本组件实现几率触发。
    /// </summary>
    [RegisterComponent("AttackTargetCountModifier")]
    public class AttackTargetCountModifier : AbilityComponentBase
    {
        private Func<string[]> _fields;
        private Func<float[]> _floatValues;
        private Func<string[]> _methods;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            var bb = ctx.sharedBlackboard;
            _fields      = p.GetStringArrayLazy<string>("fields",  null, bb);
            _methods     = p.GetStringArrayLazy<string>("methods", null, bb);
            _floatValues = p.GetFloatArrayLazy ("values",  null, bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (ctx.currentEvent is not BeforeTargetSelectEvent evt)
            {
                OneShotWarn.WarnOnce("attack-target-count-modifier-event",
                    "AttackTargetCountModifier: current event is not BeforeTargetSelectEvent " +
                    "(rule must trigger on OnBeforeTargetSelect); skipping.");
                return;
            }

            string[] fields  = _fields();
            float[]  floats  = _floatValues();
            string[] methods = _methods();
            int min = Math.Min(Math.Min(fields.Length, floats.Length), methods.Length);

            for (int i = 0; i < min; i++)
            {
                if (!MathOps.TryParse(methods[i], out var op))
                {
                    OneShotWarn.WarnOnce("atcm-unknown-method:" + methods[i],
                        $"AttackTargetCountModifier: unknown method '{methods[i]}'; skipped");
                    continue;
                }

                switch (Normalize(fields[i]))
                {
                    case "selectmaxnum":
                        evt.selectMaxNum = MathOps.ApplyIntMixed(evt.selectMaxNum, (int)floats[i], floats[i], op);
                        break;
                    case "selectminnum":
                        evt.selectMinNum = MathOps.ApplyIntMixed(evt.selectMinNum, (int)floats[i], floats[i], op);
                        break;
                    default:
                        OneShotWarn.WarnOnce("atcm-unknown-field:" + fields[i],
                            $"AttackTargetCountModifier: unknown field '{fields[i]}'; skipped");
                        break;
                }
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Trim().ToLowerInvariant();
        }
    }
}
