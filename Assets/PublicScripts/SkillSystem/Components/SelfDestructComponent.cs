namespace SkillSystem.Components
{
    [RegisterComponent("SelfDestruct")]
    public class SelfDestructComponent : ITickingComponent
    {
        private float _duration;
        private float _timer;
        private bool _hasFired;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _duration = p.GetFloat("duration", 5f);
            _timer = 0f;
            _hasFired = false;
        }

        public void OnTrigger(SkillContext ctx) { }

        public void OnTick(SkillContext ctx, float dt)
        {
            if (_hasFired) return;
            if (ctx.entity == null) return;
            _timer += dt;
            if (_timer >= _duration)
            {
                _hasFired = true;
                ctx.entity.Die();
            }
        }

        public void OnTeardown(SkillContext ctx) { }
    }
}
