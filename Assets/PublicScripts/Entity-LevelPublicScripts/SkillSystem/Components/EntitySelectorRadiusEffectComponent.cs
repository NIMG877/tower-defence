namespace SkillSystem.Components
{
    [RegisterComponent("EntitySelectorRadiusEffect")]
    public class EntitySelectorRadiusEffectComponent : ISkillComponent
    {
        private float _radius = 1f;
        private bool _sameCamp = true;
        private int _camp;
        private string _subComponentType;
        private ParamList _subParameters = new ParamList();

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _radius = p.GetFloat("radius", 1f);
            _sameCamp = p.GetBool("sameCamp", true);
            _camp = p.GetInt("camp", 0);
            _subComponentType = p.GetString("subComponentType", "");
            // Pass-through: the sub-component reads from the same ParamList as the parent.
            _subParameters = p;
        }

        public void OnTrigger(SkillContext ctx)
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
                var subCtx = new SkillContext
                {
                    entity = ents[i],
                    currentEvent = ctx.currentEvent,
                    blackboard = ctx.blackboard,
                    sharedBlackboard = ctx.sharedBlackboard
                };
                sub.OnInit(subCtx, _subParameters);
                sub.OnTrigger(subCtx);
                sub.OnTeardown(subCtx);
            }
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
