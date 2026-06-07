using UnityEngine;

namespace SkillSystem
{
    public abstract class SkillEvent { }

    public class SkillContext
    {
        public Entity entity;
        public SkillRuntime skill;
        public ISkillComponent component;
        public SkillEvent currentEvent;
        public Blackboard blackboard;          // per-skill
        public Blackboard sharedBlackboard;    // per-Entity SkillRunner
        public GameObject tempContainer;       // for spawn effects
    }

    public interface ISkillComponent
    {
        void OnInit(SkillContext ctx, ParamList parameters);
        void OnTrigger(SkillContext ctx);
        void OnTick(SkillContext ctx, float dt);
        void OnTeardown(SkillContext ctx);
    }

    public interface ITickingComponent : ISkillComponent { }

    public static class SkillSystemBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Init()
        {
            ComponentAutoRegistry.EnsureRegistered();
        }
    }
}
