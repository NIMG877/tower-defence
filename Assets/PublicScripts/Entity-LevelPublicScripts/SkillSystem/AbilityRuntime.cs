using System;
using System.Collections.Generic;

namespace SkillSystem
{
    public class AbilityRuntime
    {
        public AbilityConfig config;
        public AbilityKind Kind => config.Kind;
        public string runtimeId;
        public SPEngine spEngine;
        public bool isActive;

        public List<ISkillComponent> components = new List<ISkillComponent>();
        public List<ITickingComponent> tickingComponents = new List<ITickingComponent>();
        public List<ParamList> componentParams = new List<ParamList>();

        // Trigger 分桶:BuildAbilityRuntime 一次性填充,与 SkillRuntime 的 bucket 形状一致。
        public Dictionary<TriggerEvent, List<(ISkillComponent comp, List<ConditionGroup> groups)>>
            componentsByTrigger
            = new Dictionary<TriggerEvent, List<(ISkillComponent, List<ConditionGroup>)>>();

        public bool isInitialized;

        // Wire/UnwireRuntime 存放在这里;EntitySkillRunner 负责 set/clear 这个字段。
        // 见 spec §3.2。
        public Action _wireTeardown;

        public SkillContext MakeContext(ISkillComponent component, SkillEvent evt = null)
        {
            return new SkillContext
            {
                skill = this,   // 字段名保留 'skill' (SkillContext 兼容旧组件)
                component = component,
                currentEvent = evt,
                // sharedBlackboard 由 EntitySkillRunner.PrepareContext 注入。
            };
        }

        public void SetActive(bool value)
        {
            if (isActive == value) return;
            isActive = value;
            if (value) OnAbilityBegin?.Invoke();
            else OnAbilityEnd?.Invoke();
        }

        public event Action OnAbilityBegin;
        public event Action OnAbilityEnd;
    }
}
