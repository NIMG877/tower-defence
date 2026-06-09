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
