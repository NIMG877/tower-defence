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

    // PLACEHOLDER — replaced by Task 2.3's real implementation.
    // Task 2.3 should DELETE this stub and provide the full SkillRuntime in its own file.
    // Kept here so the project still compiles between Task 1.6 and Task 2.3.
    public class SkillRuntime
    {
        public SkillConfig config;
    }

    public interface ISkillComponent
    {
        void OnInit(SkillContext ctx, ParamList parameters);
        void OnTrigger(SkillContext ctx);
        void OnTick(SkillContext ctx, float dt);
        void OnTeardown(SkillContext ctx);
    }

    public interface ITickingComponent : ISkillComponent { }
}
