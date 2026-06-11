using System.Collections.Generic;

namespace AbilitySystem.Components
{
    /// <summary>
    /// Selects entities in a radius around the skill's owner and writes the result
    /// to a blackboard key. Downstream components (e.g. ApplyBuff) read the same key.
    /// See docs/superpowers/specs/2026-06-08-skill-blackboard-component-pattern.md.
    /// </summary>
    [RegisterComponent("EntitySelector")]
    public class EntitySelector : IAbilityComponent
    {
        private string _outputKey;
        private float _radius = 1f;
        private int _camp;            // 0 = use ctx.entity.Camp; else explicit camp id
        private bool _sameCamp = true;
        private bool _force;
        private bool _selectSelf;

        public void OnInit(AbilityContext ctx, ParamList p)
        {
            _outputKey  = p.GetString("blackboardKey", "");
            _radius     = p.GetFloat("radius", 1f);
            _camp       = p.GetInt("camp", 0);
            _sameCamp   = p.GetBool("sameCamp", true);
            _force      = p.GetBool("force", false);
            _selectSelf = p.GetBool("selectSelf", false);
        }

        public void OnTrigger(AbilityContext ctx)
        {
            // Empty key = opt out, no-op. The component still resolves harmlessly.
            if (string.IsNullOrEmpty(_outputKey) || ctx.entity == null) return;

            int camp = _camp == 0 ? ctx.entity.Camp : _camp;
            var pos = ctx.entity.Movement.Position;
            var ents = EntityManager.Manager.EntitySelector_Radius(
                (pos.x, pos.y), camp, _sameCamp, _radius, _force);

            // Copy before mutating: the manager may return a pooled/shared list, and downstream
            // consumers read the value we write. Operating on a local copy keeps both sides safe.
            var result = new List<Entity>(ents);
            if (_selectSelf && !result.Contains(ctx.entity)) result.Add(ctx.entity);

            // Convention: clear before write so re-trigger does not accumulate stale state.
            ctx.sharedBlackboard.Remove(_outputKey);
            ctx.sharedBlackboard.Set(_outputKey, result);
        }

        public void OnTick(AbilityContext ctx, float dt) { }
        public void OnTeardown(AbilityContext ctx) { }
    }
}
