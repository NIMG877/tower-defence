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
        private Func<BuffType[]> _types;
        private Func<float[]> _values;
        private Func<bool> _isWhiteList;
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
            _types  = p.GetStringArrayLazy("buffTypes",  null, bb, s => (BuffType)Enum.Parse(typeof(BuffType), s));
            _values = p.GetFloatArrayLazy("buffValues", null, bb);
            _buffId          = p.GetStringLazy("buffId",        "skill_buff", bb);
            _buffTime        = p.GetFloatLazy ("buffTime",      -10f,         bb);
            _toSelf          = p.GetBoolLazy  ("toSelf",        true,         bb);
            _isWhiteList     = p.GetBoolLazy  ("isWhiteList",   false,        bb);
            _inputTargetKey  = p.GetStringLazy("blackboardKey", "",           bb);
            _outputTargetKey = p.GetStringLazy("outputTarget",  "",           bb);
            _outputBuffKey   = p.GetStringLazy("outputBuff",    "",           bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (ctx.entity == null) return;
            if (_types().Length == 0) return;

            List<Entity> targets = ResolveTargets(ctx);
            if (targets == null) return;

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
                Buff created = t.buffController.CreateBuff(_types(), null, _buffId(), _values(), _buffTime(), _isWhiteList());
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
    }
}
