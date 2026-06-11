using UnityEngine;

namespace SkillSystem.Components
{
    [RegisterComponent("PlayParticle")]
    public class PlayParticleComponent : ISkillComponent
    {
        private bool _play = true;
        private bool _stop = false;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _play = p.GetBool("play", true);
            _stop = p.GetBool("stop", false);
        }

        public void OnTrigger(SkillContext ctx)
        {
            // Editor/migration phase binds the actual ParticleSystem reference; here we expose a Blackboard indirection.
            var ps = ctx.sharedBlackboard != null ? ctx.sharedBlackboard.Get<ParticleSystem>("__particleSystem", null) : null;
            if (ps == null) return;
            if (_play) ps.Play(true);
            if (_stop) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
