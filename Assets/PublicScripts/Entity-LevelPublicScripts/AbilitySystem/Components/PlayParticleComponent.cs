using UnityEngine;

namespace AbilitySystem.Components
{
    [RegisterComponent("PlayParticle")]
    public class PlayParticleComponent : IAbilityComponent
    {
        private bool _play = true;
        private bool _stop = false;

        public void OnInit(AbilityContext ctx, ParamList p)
        {
            _play = p.GetBool("play", true);
            _stop = p.GetBool("stop", false);
        }

        public void OnTrigger(AbilityContext ctx)
        {
            // Editor/migration phase binds the actual ParticleSystem reference; here we expose a Blackboard indirection.
            var ps = ctx.sharedBlackboard != null ? ctx.sharedBlackboard.Get<ParticleSystem>("__particleSystem", null) : null;
            if (ps == null) return;
            if (_play) ps.Play(true);
            if (_stop) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void OnTick(AbilityContext ctx, float dt) { }
        public void OnTeardown(AbilityContext ctx) { }
    }
}
