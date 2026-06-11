namespace AbilitySystem.Components
{
    [RegisterComponent("EntitySelectorRadiusEffect")]
    public class EntitySelectorRadiusEffectComponent : IAbilityComponent
    {
        private float _radius = 1f;
        private bool _sameCamp = true;
        private int _camp;
        private string _subComponentType;
        private ParamList _subParameters = new ParamList();

        public void OnInit(AbilityContext ctx, ParamList p)
        {
            _radius = p.GetFloat("radius", 1f);
            _sameCamp = p.GetBool("sameCamp", true);
            _camp = p.GetInt("camp", 0);
            _subComponentType = p.GetString("subComponentType", "");
            // Pass-through: the sub-component reads from the same ParamList as the parent.
            _subParameters = p;
        }

        /// <summary>
        /// IMPORTANT: This component constructs sub-components ad-hoc and forwards
        /// <c>ctx.currentEvent</c> into them. That bypasses the dispatcher's
        /// trigger-bucket guarantee — the sub-component sees whatever event type
        /// the parent received.
        ///
        /// When configuring this component, the sub-component declared in
        /// <c>subComponentType</c> MUST be able to handle every event type
        /// declared in this parent's <c>triggers[]</c>. Otherwise the sub-
        /// component's direct cast on <c>ctx.currentEvent</c> will NRE.
        ///
        /// Tracked for a later refactor; see spec
        /// docs/superpowers/specs/2026-06-10-skill-trigger-bucket-dispatch-design.md §6.4.
        /// </summary>
        public void OnTrigger(AbilityContext ctx)
        {
            if (ctx.entity == null || string.IsNullOrEmpty(_subComponentType)) return;
            int camp = _camp == 0 ? ctx.entity.Camp : _camp;
            var pos = ctx.entity.Movement.Position;
            var ents = EntityManager.Manager.EntitySelector_Radius(
                (pos.x, pos.y), camp, _sameCamp, _radius, false);
            for (int i = 0; i < ents.Count; i++)
            {
                var sub = ComponentFactory.Create(_subComponentType);
                if (sub == null) continue;
                var subCtx = new AbilityContext
                {
                    entity = ents[i],
                    currentEvent = ctx.currentEvent,
                    sharedBlackboard = ctx.sharedBlackboard
                };
                sub.OnInit(subCtx, _subParameters);
                sub.OnTrigger(subCtx);
                sub.OnTeardown(subCtx);
            }
        }

        public void OnTick(AbilityContext ctx, float dt) { }
        public void OnTeardown(AbilityContext ctx) { }
    }
}
