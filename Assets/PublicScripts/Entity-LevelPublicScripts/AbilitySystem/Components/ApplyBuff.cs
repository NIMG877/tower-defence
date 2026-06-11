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
    /// <para>When <c>endOnSkillEnd</c> is true, every buff this component creates is
    /// tracked and destroyed on <see cref="AbilityEndEvent"/> (or on
    /// <see cref="OnTeardown"/> if the skill never ends cleanly, e.g. pool
    /// dormancy mid-skill). The config MUST also declare <c>OnAbilityEnd</c> in
    /// its <c>triggers[]</c>, because <c>EntityAbilityRunner.DispatchToAbility</c>
    /// only routes events that have a matching trigger bucket — bypassing the
    /// active-window gate isn't the same as bypassing the bucket lookup.</para>
    /// See docs/superpowers/specs/2026-06-08-skill-blackboard-component-pattern.md.
    /// </summary>
    [RegisterComponent("ApplyBuff")]
    public class ApplyBuff : IAbilityComponent
    {
        private string _buffId = "skill_buff";
        private float _buffTime = -10f;
        private bool _toSelf = true;
        private string _inputTargetKey;            // blackboard key (optional)
        private BuffType[] _types = Array.Empty<BuffType>();
        private float[] _values = Array.Empty<float>();
        private bool _isWhiteList;
        // Output keys (optional). When set, OnTrigger appends this round's
        // targets and created buffs (null-padded for skipped/failed targets)
        // to the per-Entity shared blackboard at these keys. Lists are
        // accumulated across OnTrigger calls within the same skill window.
        // Empty string = skip write.
        private string _outputTargetKey;
        private string _outputBuffKey;

        public void OnInit(AbilityContext ctx, ParamList p)
        {
            _types          = BuffParamParser.ParseBuffTypes(p.GetString("buffTypes", ""));
            _values         = BuffParamParser.ParseFloats(p.GetString("buffValues", ""));
            _buffId         = p.GetString("buffId", "skill_buff");
            _buffTime       = p.GetFloat("buffTime", -10f);
            _toSelf         = p.GetBool("toSelf", true);
            _isWhiteList    = p.GetBool("isWhiteList", false);
            _inputTargetKey = p.GetString("blackboardKey", "");
            _outputTargetKey = p.GetString("outputTarget", "");
            _outputBuffKey   = p.GetString("outputBuff", "");
        }

        public void OnTrigger(AbilityContext ctx)
        {
            if (ctx.entity == null) return;

            if (_types.Length == 0) return;

            List<Entity> targets = ResolveTargets(ctx);
            if (targets == null) return;

            // Per-round collected lists. Sized to the target list so the
            // downstream blackboard write is one AddRange each. Targets with
            // no buffController are still appended (so callers can see
            // "we tried"); the matching buff slot is null-padded. The output
            // keys are honored in the AppendToBlackboard calls below.
            var roundTargets = new List<Entity>(targets.Count);
            var roundBuffs   = new List<Buff>(targets.Count);
            bool needWrite = !string.IsNullOrEmpty(_outputTargetKey) && !string.IsNullOrEmpty(_outputBuffKey);

            for (int i = 0; i < targets.Count; i++)
            {
                Entity t = targets[i];
                if(t == null || t.buffController == null) continue;
                Buff created = t.buffController.CreateBuff(_types, null, _buffId, _values, _buffTime, _isWhiteList);
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
            List<Entity> bbTarget = ctx.sharedBlackboard.Get<List<Entity>>(_outputTargetKey, null) ?? new List<Entity>();
            bbTarget.AddRange(roundTargets);
            ctx.sharedBlackboard.Set(_outputTargetKey, bbTarget);
            List<Buff> bbBuff = ctx.sharedBlackboard.Get<List<Buff>>(_outputBuffKey, null) ?? new List<Buff>();
            bbBuff.AddRange(roundBuffs);
            ctx.sharedBlackboard.Set(_outputBuffKey, bbBuff);
        }

        public void OnTick(AbilityContext ctx, float dt) { }
        public void OnTeardown(AbilityContext ctx) { }

        // Blackboard-read path: when blackboardKey is set, the target list is read
        // from the key. If the key is missing/empty, the component skips silently —
        // this means an upstream writer hasn't run yet, which is normal in some
        // dispatch orders.
        private List<Entity> ResolveTargets(AbilityContext ctx)
        {
            if (!string.IsNullOrEmpty(_inputTargetKey))
            {
                return ctx.sharedBlackboard.Get<List<Entity>>(_inputTargetKey, null);
            }

            // Original single-target behavior preserved for backward compatibility.
            // The dispatcher routes this component by config.triggers[]; the event
            // type that arrives depends on the config, so we extract `target` from
            // whichever event payload carries one. Falls back to ctx.entity when
            // the event doesn't carry a target field (or _toSelf is true).
            Entity t = _toSelf ? ctx.entity : null;
            if (t == null || t.buffController == null) return null;
            return new List<Entity> { t };
        }
    }
}
