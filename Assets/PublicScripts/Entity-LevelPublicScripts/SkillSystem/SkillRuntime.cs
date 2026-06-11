using System.Collections.Generic;

namespace SkillSystem
{
    public class SkillRuntime
    {
        public SkillConfig config;
        public SPEngine spEngine;
        public List<ISkillComponent> components = new List<ISkillComponent>();
        public List<ITickingComponent> tickingComponents = new List<ITickingComponent>();
        // 与 components 并行：保存每个组件的初始参数，供 OnInitialize 时 re-OnInit。
        public List<ParamList> componentParams = new List<ParamList>();
        // Trigger 分桶：BuildSkillRuntime 一次性填充，OnInitialize/OnTeardown 不重建。
        // key 是 ConditionConfig.triggerEvent 的 enum；value 是按 config 声明顺序排好的
        // (component, conditionExpression) 对，conditionExpression 是从 ConditionConfig
        // 投影出的 List<ConditionGroup>（见 §2.1 of the spec）。
        public Dictionary<TriggerEvent, List<(ISkillComponent comp, List<ConditionGroup> groups)>>
            componentsByTrigger
            = new Dictionary<TriggerEvent, List<(ISkillComponent, List<ConditionGroup>)>>();
        public bool isInitialized;
        public bool isActive; // true while skill is firing (SPEngine.IsActive)

        public void OpenActiveWindow()  { isActive = true;  }
        public void CloseActiveWindow() { isActive = false; }

        public SkillContext MakeContext(ISkillComponent component, SkillEvent evt = null)
        {
            return new SkillContext
            {
                skill = this,
                component = component,
                currentEvent = evt,
                // sharedBlackboard 由 EntitySkillRunner.PrepareContext 注入。
            };
        }
    }
}
