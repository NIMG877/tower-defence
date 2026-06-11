using System.Collections.Generic;

namespace SkillSystem.Components
{
    /// <summary>
    /// Destroys buffs that a prior component (typically <see cref="ApplyBuff"/>)
    /// wrote to the per-Entity shared blackboard. Reads two parallel lists
    /// from <c>ctx.sharedBlackboard</c> at the configured keys:
    ///   <c>inputTargetKey</c> -> <c>List&lt;Entity&gt;</c>  (one entry per buff to destroy)
    ///   <c>inputBuffKey</c>   -> <c>List&lt;Buff&gt;</c>    (the actual buff to destroy)
    /// The two lists are walked in parallel — index i in each list is the pair.
    ///
    /// <para>Both keys MUST be set; if either is empty the component is a
    /// no-op (silently returns). Mirrors the <c>needWrite</c> gate in
    /// <c>ApplyBuff</c> so the two components are symmetric: ApplyBuff
    /// writes pairs only when both output keys are set; DestroyBuff reads
    /// pairs only when both input keys are set.</para>
    ///
    /// <para>After consuming, both blackboard keys are removed. This matches
    /// ApplyBuff's "this round" semantics: the lists are a per-round handoff,
    /// and leaving them in place would risk double-destroy on a re-trigger.</para>
    ///
    /// Null/empty list entries are skipped (entity without buffController,
    /// or a null Buff slot from a failed CreateBuff). The null-pad contract
    /// from ApplyBuff's <c>outputTarget</c> / <c>outputBuff</c> writers is
    /// preserved through the read.
    /// </summary>
    [RegisterComponent("DestroyBuff")]
    public class DestroyBuff : ISkillComponent
    {
        private string _inputTargetKey;
        private string _inputBuffKey;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _inputTargetKey = p.GetString("inputTarget", "");
            _inputBuffKey   = p.GetString("inputBuff",   "");
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (string.IsNullOrEmpty(_inputTargetKey) || string.IsNullOrEmpty(_inputBuffKey)) return;
            if (ctx.sharedBlackboard == null) return;

            var targets = ctx.sharedBlackboard.Get<List<Entity>>(_inputTargetKey, null);
            var buffs   = ctx.sharedBlackboard.Get<List<Buff>>(_inputBuffKey, null);
            if (targets == null || buffs == null) return;

            int n = targets.Count < buffs.Count ? targets.Count : buffs.Count;
            for (int i = 0; i < n; i++)
            {
                var e = targets[i];
                var b = buffs[i];
                if (e == null || e.buffController == null) continue;
                if (b == null) continue;
                e.buffController.DestroyBuff(b);
            }

            // Consume: clear both keys so a second OnTrigger call in the same
            // skill window doesn't re-destroy the same buffs.
            ctx.sharedBlackboard.Remove(_inputTargetKey);
            ctx.sharedBlackboard.Remove(_inputBuffKey);
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
