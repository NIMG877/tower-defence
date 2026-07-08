using System;
using System.Collections.Generic;

namespace AbilitySystem.Components
{
    /// <summary>
    /// Applies buffs to one or more entities. When <c>blackboardKey</c> is empty,
    /// the existing single-target behavior is preserved (self or event target).
    /// When <c>blackboardKey</c> is set, the component reads a <c>List&lt;Entity&gt;</c>
    /// from that key and applies the buff to each entry.
    ///
    /// <para>When both <c>outputTarget</c> and <c>outputBuff</c> are set, this
    /// component hands the (target, created-buff) pair off to the per-Entity
    /// shared blackboard at those keys so a downstream <c>DestroyBuff</c> (or
    /// similar consumer) can act on them. Buffs created here are not auto-destroyed
    /// — lifecycle is the consumer's responsibility.</para>
    /// </summary>
    [RegisterComponent("ApplyBuff")]
    public class ApplyBuff : AbilityComponentBase
    {
        private Func<string> _buffId;
        private Func<float> _buffTime;
        private Func<bool> _toSelf;
        private Func<string> _inputTargetKey;            // blackboard key (optional)
        private Func<string[]> _attributes;
        private Func<string[]> _ops;
        private Func<float[]> _magnitudes;
        private Func<bool> _isWhiteList;
        private Func<string> _mode;
        // Output keys (optional). When both are set, OnTrigger appends this round's
        // (target, created-buff) pairs to the per-Entity shared blackboard at these
        // keys. Targets without a buffController are skipped entirely (not appended),
        // so the two output lists stay parallel and only contain entries that ran
        // through CreateBuff — a null buff slot means CreateBuff returned null.
        // Lists are accumulated across OnTrigger calls within the same skill window.
        // Empty string = skip write.
        private Func<string> _outputTargetKey;
        private Func<string> _outputBuffKey;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            var bb = ctx.sharedBlackboard;
            _attributes = p.GetStringArrayLazy<string>("attributes", null, bb);
            _ops         = p.GetStringArrayLazy<string>("ops",        null, bb);
            _magnitudes  = p.GetFloatArrayLazy ("magnitudes", null, bb);
            _buffId          = p.GetStringLazy("buffId",        "skill_buff", bb);
            _buffTime        = p.GetFloatLazy ("buffTime",      -10f,         bb);
            _toSelf          = p.GetBoolLazy  ("toSelf",        true,         bb);
            _isWhiteList     = p.GetBoolLazy  ("isWhiteList",   false,        bb);
            _mode            = p.GetStringLazy("mode",          "normal",     bb);
            _inputTargetKey  = p.GetStringLazy("blackboardKey", "",           bb);
            _outputTargetKey = p.GetStringLazy("outputTarget",  "",           bb);
            _outputBuffKey   = p.GetStringLazy("outputBuff",    "",           bb);
        }

        /// <summary>
        /// 把三 CSV（attributes/ops/magnitudes）按下标对齐构造成 Modifier[]。
        /// 长度不一致取最短 + 一次性 warn（沿用 AttackEventValueModifier 容错模式）。
        /// </summary>
        private Modifier[] BuildModifiers()
        {
            string[] attrs = _attributes() ?? System.Array.Empty<string>();
            string[] ops   = _ops()        ?? System.Array.Empty<string>();
            float[]  mags  = _magnitudes() ?? System.Array.Empty<float>();
            int len = System.Math.Min(System.Math.Min(attrs.Length, ops.Length), mags.Length);
            if (attrs.Length != ops.Length || ops.Length != mags.Length)
            {
                OneShotWarn.WarnOnce("apply-buff-csv-length",
                    $"ApplyBuff: attributes/ops/magnitudes 长度不一致 ({attrs.Length}/{ops.Length}/{mags.Length}); 取最短 {len}。");
            }
            var result = new Modifier[len];
            for (int i = 0; i < len; i++)
            {
                ModifierOp op = (ModifierOp)System.Enum.Parse(typeof(ModifierOp), ops[i].Trim());
                result[i] = new Modifier(attrs[i].Trim(), op, mags[i]);
            }
            return result;
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (ctx.entity == null) return;
            Modifier[] modifiers = BuildModifiers();
            if (modifiers.Length == 0) return;

            List<Entity> targets = ResolveTargets(ctx);
            if (targets == null) return;

            string mode = Normalize(_mode());
            if (mode == "aura")
            {
                SyncAura(ctx, targets);
                return;
            }
            if (mode != "normal")
            {
                OneShotWarn.WarnOnce(
                    "apply-buff-mode:" + mode,
                    $"ApplyBuff: unknown mode '{_mode()}'; expected 'normal' or 'aura'.");
                return;
            }

            bool needWrite = !string.IsNullOrEmpty(_outputTargetKey()) && !string.IsNullOrEmpty(_outputBuffKey());

            // Per-round collected lists. Sized to the target list so the
            // downstream blackboard write is one AddRange each. Targets without
            // a buffController are skipped (continue above), so the two lists
            // stay aligned: index i in roundTargets matches index i in roundBuffs.
            // A null in roundBuffs means CreateBuff returned null for that target.
            var roundTargets = new List<Entity>(targets.Count);
            var roundBuffs   = new List<Buff>(targets.Count);

            for (int i = 0; i < targets.Count; i++)
            {
                Entity t = targets[i];
                if(t == null || t.buffController == null) continue;
                Buff created = t.buffController.CreateBuff(modifiers, null, _buffId(), _buffTime(), _isWhiteList());
                if (needWrite)
                {
                    roundTargets.Add(t);
                    roundBuffs.Add(created);   // may be null if CreateBuff failed
                }
            }
            if (needWrite)
            {
                AppendToBlackboard(ctx, roundTargets, roundBuffs);
            }
        }

        private void SyncAura(AbilityContext ctx, List<Entity> targets)
        {
            string targetKey = _outputTargetKey();
            string buffKey = _outputBuffKey();
            if (ctx.sharedBlackboard == null || string.IsNullOrEmpty(_inputTargetKey())
                || string.IsNullOrEmpty(targetKey) || string.IsNullOrEmpty(buffKey))
            {
                OneShotWarn.WarnOnce(
                    "apply-buff-aura-keys",
                    "ApplyBuff: mode='aura' requires blackboardKey, outputTarget, and outputBuff.");
                return;
            }

            Modifier[] modifiers = BuildModifiers();
            if (modifiers.Length == 0)
            {
                OneShotWarn.WarnOnce(
                    "apply-buff-aura-empty",
                    "ApplyBuff: aura 模式下 modifiers 为空，跳过同步。");
                return;
            }

            var oldTargets = ctx.sharedBlackboard.Get<List<Entity>>(targetKey, null) ?? new List<Entity>();
            var oldBuffs = ctx.sharedBlackboard.Get<List<Buff>>(buffKey, null) ?? new List<Buff>();
            var nextTargets = new List<Entity>();
            var nextBuffs = new List<Buff>();
            var desired = new HashSet<Entity>();
            var retained = new HashSet<Buff>();

            for (int i = 0; i < targets.Count; i++)
            {
                Entity target = targets[i];
                if (target == null || target.buffController == null || !desired.Add(target)) continue;

                Buff tracked = FindTrackedBuff(target, oldTargets, oldBuffs);
                if (tracked != null && target.buffController.Buffs.Contains(tracked))
                {
                    target.buffController.SetBuffValues(modifiers, tracked);
                    tracked.buff_time = _buffTime();
                }
                else
                {
                    tracked = target.buffController.CreateBuff(
                        modifiers, null, _buffId(), _buffTime(), _isWhiteList());
                }

                nextTargets.Add(target);
                nextBuffs.Add(tracked);
                if (tracked != null) retained.Add(tracked);
            }

            int oldCount = Math.Min(oldTargets.Count, oldBuffs.Count);
            for (int i = 0; i < oldCount; i++)
            {
                Entity target = oldTargets[i];
                Buff buff = oldBuffs[i];
                if (target == null || target.buffController == null || buff == null || retained.Contains(buff)) continue;
                if (target.buffController.Buffs.Contains(buff)) target.buffController.DestroyBuff(buff);
            }

            ctx.sharedBlackboard.Remove(targetKey);
            ctx.sharedBlackboard.Remove(buffKey);
            ctx.sharedBlackboard.Set(targetKey, nextTargets);
            ctx.sharedBlackboard.Set(buffKey, nextBuffs);
        }

        private static Buff FindTrackedBuff(Entity target, List<Entity> targets, List<Buff> buffs)
        {
            int count = Math.Min(targets.Count, buffs.Count);
            for (int i = 0; i < count; i++)
                if (targets[i] == target) return buffs[i];
            return null;
        }

        public override void OnTeardown(AbilityContext ctx)
        {
            if (Normalize(_mode()) != "aura" || ctx.sharedBlackboard == null) return;
            string targetKey = _outputTargetKey();
            string buffKey = _outputBuffKey();
            if (string.IsNullOrEmpty(targetKey) || string.IsNullOrEmpty(buffKey)) return;

            var targets = ctx.sharedBlackboard.Get<List<Entity>>(targetKey, null);
            var buffs = ctx.sharedBlackboard.Get<List<Buff>>(buffKey, null);
            if (targets != null && buffs != null)
            {
                int count = Math.Min(targets.Count, buffs.Count);
                for (int i = 0; i < count; i++)
                {
                    Entity target = targets[i];
                    Buff buff = buffs[i];
                    if (target != null && target.buffController != null && buff != null
                        && target.buffController.Buffs.Contains(buff))
                        target.buffController.DestroyBuff(buff);
                }
            }
            ctx.sharedBlackboard.Remove(targetKey);
            ctx.sharedBlackboard.Remove(buffKey);
        }

        // Accumulate this round's targets/buffs into the per-Entity shared
        // blackboard at the configured output keys. Existing lists at those
        // keys are read, extended, and written back — so repeated OnTrigger
        // calls (multi-round skills, multi-trigger entries) accumulate.
        // Missing keys / wrong-typed values fall back to a fresh list.
        // Skipped entirely when both output keys are empty.
        private void AppendToBlackboard(AbilityContext ctx, List<Entity> roundTargets, List<Buff> roundBuffs)
        {
            List<Entity> bbTarget = ctx.sharedBlackboard.Get<List<Entity>>(_outputTargetKey(), null) ?? new List<Entity>();
            bbTarget.AddRange(roundTargets);
            ctx.sharedBlackboard.Set(_outputTargetKey(), bbTarget);
            List<Buff> bbBuff = ctx.sharedBlackboard.Get<List<Buff>>(_outputBuffKey(), null) ?? new List<Buff>();
            bbBuff.AddRange(roundBuffs);
            ctx.sharedBlackboard.Set(_outputBuffKey(), bbBuff);
        }

        // Blackboard-read path: when blackboardKey is set, the target list is read
        // from the key. If the key is missing/empty, the component skips silently —
        // this means an upstream writer hasn't run yet, which is normal in some
        // dispatch orders.
        private List<Entity> ResolveTargets(AbilityContext ctx)
        {
            if (!string.IsNullOrEmpty(_inputTargetKey()))
            {
                return ctx.sharedBlackboard.Get<List<Entity>>(_inputTargetKey(), null);
            }

            // Original single-target behavior preserved for backward compatibility.
            // The dispatcher routes this component by config.triggers[]; the event
            // type that arrives depends on the config, so we extract `target` from
            // whichever event payload carries one. Falls back to ctx.entity when
            // the event doesn't carry a target field (or _toSelf is true).
            Entity t = _toSelf() ? ctx.entity : null;
            if (t == null || t.buffController == null) return null;
            return new List<Entity> { t };
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Trim().ToLowerInvariant();
        }
    }
}
