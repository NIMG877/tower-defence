using System;
using System.Collections.Generic;

namespace AbilitySystem.Components
{
    /// <summary>
    /// 刷新既有 buff 的 modifier 数值（<c>BuffController.SetBuffValues</c>），
    /// 补上 ApplyBuff/DestroyBuff 之外缺的第三块 buff 管理。读 ApplyBuff 写入
    /// 黑板的 (target, buff) 平行对，把按三 CSV 构造的新 <c>Modifier[]</c> 设进去。
    ///
    /// <para>与 DestroyBuff 的两点差异：①消费后不清空黑板 key——对是持久句柄，
    /// 数值型 buff 需要反复刷新；②modifiers 每次 OnTrigger 重新构造，不缓存
    /// （magnitudes 允许 fromBlackboard，正是为"数值随黑板计数变化"的场景；
    /// ApplyBuff 缓存是因为 aura 每 tick 重跑的性能取舍，本组件按事件频率触发）。</para>
    /// </summary>
    [RegisterComponent("UpdateBuff")]
    public class UpdateBuff : AbilityComponentBase
    {
        private Func<string> _inputTargetKey;
        private Func<string> _inputBuffKey;
        private Func<string[]> _attributes;
        private Func<string[]> _ops;
        private Func<float[]> _magnitudes;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            var bb = ctx.sharedBlackboard;
            _inputTargetKey = p.GetStringLazy("inputTarget", "", bb);
            _inputBuffKey   = p.GetStringLazy("inputBuff",   "", bb);
            _attributes     = p.GetStringArrayLazy<string>("attributes", null, bb);
            _ops            = p.GetStringArrayLazy<string>("ops",        null, bb);
            _magnitudes     = p.GetFloatArrayLazy ("magnitudes", null, bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            // 与 DestroyBuff 对称的门控：两个 key 都配置才消费。
            if (string.IsNullOrEmpty(_inputTargetKey()) || string.IsNullOrEmpty(_inputBuffKey())) return;
            if (ctx.sharedBlackboard == null) return;

            var targets = ctx.sharedBlackboard.Get<List<Entity>>(_inputTargetKey(), null);
            var buffs   = ctx.sharedBlackboard.Get<List<Buff>>(_inputBuffKey(), null);
            if (targets == null || buffs == null) return;

            Modifier[] modifiers = BuildModifiers();
            if (modifiers.Length == 0) return;

            int n = targets.Count < buffs.Count ? targets.Count : buffs.Count;
            for (int i = 0; i < n; i++)
            {
                var e = targets[i];
                var b = buffs[i];
                if (e == null || e.buffController == null) continue;
                if (b == null) continue;
                e.buffController.SetBuffValues(modifiers, b);
            }
        }

        /// <summary>三 CSV 按下标对齐构造 Modifier[]。每次调用重建（不缓存），
        /// magnitudes 走 fromBlackboard 时才能读到最新计数派生值。
        /// 长度不一致取最短 + 一次性 warn，沿用 ApplyBuff 的容错口径。</summary>
        private Modifier[] BuildModifiers()
        {
            string[] attrs = _attributes() ?? Array.Empty<string>();
            string[] ops   = _ops()        ?? Array.Empty<string>();
            float[]  mags  = _magnitudes() ?? Array.Empty<float>();
            int len = Math.Min(Math.Min(attrs.Length, ops.Length), mags.Length);
            if (attrs.Length != ops.Length || ops.Length != mags.Length)
            {
                OneShotWarn.WarnOnce("update-buff-csv-length",
                    $"UpdateBuff: attributes/ops/magnitudes 长度不一致 ({attrs.Length}/{ops.Length}/{mags.Length}); 取最短 {len}。");
            }
            var modifiers = new Modifier[len];
            for (int i = 0; i < len; i++)
            {
                ModifierOp op = (ModifierOp)Enum.Parse(typeof(ModifierOp), ops[i]);
                modifiers[i] = new Modifier(attrs[i], op, mags[i]);
            }
            return modifiers;
        }
    }
}
