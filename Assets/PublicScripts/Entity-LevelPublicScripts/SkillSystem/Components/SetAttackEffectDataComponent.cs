namespace SkillSystem.Components
{
    [RegisterComponent("SetAttackEffectData")]
    public class SetAttackEffectDataComponent : ISkillComponent
    {
        // Populated by the migration tool (Phase 5/6) via direct field access.
        private AttackBase.AttackEffectData _data;

        public void SetData(AttackBase.AttackEffectData data) { _data = data; }

        public void OnInit(SkillContext ctx, ParamList p) { /* set via editor/migration */ }

        public void OnTrigger(SkillContext ctx)
        {
            if (ctx.entity == null || ctx.entity.AttackBase == null) return;
            ctx.entity.AttackBase.SetAttackEffectData(_data);
        }
        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
