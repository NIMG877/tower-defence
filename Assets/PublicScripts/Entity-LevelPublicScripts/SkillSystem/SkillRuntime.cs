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

        public bool MatchesTrigger(TriggerEvent te)
        {
            if (config.globalConditions == null) return true;
            for (int i = 0; i < config.globalConditions.Length; i++)
            {
                if (config.globalConditions[i].triggerEvent == te) return true;
            }
            return config.globalConditions.Length == 0;
        }
    }
}
