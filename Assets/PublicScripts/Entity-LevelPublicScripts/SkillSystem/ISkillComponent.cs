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
        // 字段名保留 'skill' (legacy 组件可能会反射访问),但类型是 AbilityRuntime。
        // AbilityRuntime.MakeContext 里 `skill = this` 把 AbilityRuntime 自己填进来。
        public AbilityRuntime skill;
        public ISkillComponent component;
        public SkillEvent currentEvent;
        // The only blackboard. Per-Entity, set by EntitySkillRunner.PrepareContext
        // from the per-Entity sharedBlackboard field on the runner. Components
        // read this directly; there is no per-skill blackboard.
        public Blackboard sharedBlackboard;
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
