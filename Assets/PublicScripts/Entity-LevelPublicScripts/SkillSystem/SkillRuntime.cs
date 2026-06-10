using System.Collections.Generic;

namespace SkillSystem
{
    public class SkillRuntime
    {
        public SkillConfig config;
        public SPEngine spEngine;
        public Blackboard blackboard = new Blackboard();
        public List<ISkillComponent> components = new List<ISkillComponent>();
        public List<ITickingComponent> tickingComponents = new List<ITickingComponent>();
        // 与 components 并行：保存每个组件的初始参数，供 OnInitialize 时 re-OnInit。
        public List<ParamList> componentParams = new List<ParamList>();
        // Trigger 分桶：BuildSkillRuntime 一次性填充，OnInitialize/OnTeardown 不重建。
        // key 是 ConditionConfig.triggerEvent 的 enum，value 是按 config 声明顺序排好的组件列表。
        public Dictionary<TriggerEvent, List<ISkillComponent>> componentsByTrigger
            = new Dictionary<TriggerEvent, List<ISkillComponent>>();
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
                blackboard = blackboard,
            };
        }
    }
}
