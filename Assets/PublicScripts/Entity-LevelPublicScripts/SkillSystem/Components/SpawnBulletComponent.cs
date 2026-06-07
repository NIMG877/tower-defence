namespace SkillSystem.Components
{
    [RegisterComponent("SpawnBullet")]
    public class SpawnBulletComponent : ISkillComponent
    {
        // Populated by the migration tool (Phase 5/6) via direct field access.
        private BulletData _bullet;
        private float _speed = 10f;

        public void SetBullet(BulletData bullet) { _bullet = bullet; }

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _speed = p.GetFloat("speed", 10f);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (ctx.entity == null || _bullet.Equals(default(BulletData))) return;
            // Mirrors the original SkeletonTalent1: emit a bullet that calls a callback on hit.
            // The callback is wired by editor-side migration; here we just keep the data and speed.
            // Concrete bullet instantiation is performed by the editor / migration pipeline.
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
