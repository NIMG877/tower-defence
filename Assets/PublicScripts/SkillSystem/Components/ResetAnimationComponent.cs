using System;

namespace SkillSystem.Components
{
    [RegisterComponent("ResetAnimation")]
    public class ResetAnimationComponent : ISkillComponent
    {
        private int[] _resets;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            var csv = p.GetString("resetIndices", "");
            if (string.IsNullOrEmpty(csv)) { _resets = Array.Empty<int>(); return; }
            var parts = csv.Split(',');
            _resets = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) _resets[i] = int.Parse(parts[i].Trim());
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (ctx.entity == null || ctx.entity.entityAM == null) return;
            ctx.entity.entityAM.ResetAnimation(_resets);
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
