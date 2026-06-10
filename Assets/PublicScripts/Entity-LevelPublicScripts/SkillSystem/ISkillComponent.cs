using UnityEngine;

namespace SkillSystem
{
    public abstract class SkillEvent
    {
        // Every concrete SkillEvent must declare which TriggerEvent enum value
        // it routes to. Abstract (not virtual) so the compiler catches missing
        // overrides — that's the safety net for the dispatch bridge table.
        public abstract TriggerEvent TriggerEvent { get; }
    }

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
