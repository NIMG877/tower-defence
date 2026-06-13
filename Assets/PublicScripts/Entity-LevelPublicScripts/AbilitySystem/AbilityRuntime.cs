using System;
using System.Collections.Generic;

namespace AbilitySystem
{
    public class AbilityRuntime
    {
        public AbilityConfig config;
        public AbilityKind Kind => config.Kind;
        public string runtimeId;
        public SPEngine spEngine;
        public bool isActive;

        public List<IAbilityComponent> components = new List<IAbilityComponent>();
        public List<ITickingComponent> tickingComponents = new List<ITickingComponent>();
        public List<ParamList> componentParams = new List<ParamList>();

        // Trigger 分桶:BuildAbilityRuntime 一次性填充,key 是 TriggerEvent,value 是
        // (component, condition groups) 对的列表。
        public Dictionary<TriggerEvent, List<(IAbilityComponent comp, List<ConditionGroup> groups)>>
            componentsByTrigger
            = new Dictionary<TriggerEvent, List<(IAbilityComponent, List<ConditionGroup>)>>();

        public bool isInitialized;

        // Wire/UnwireRuntime 存放在这里;EntityAbilityRunner 负责 set/clear 这个字段。
        // 见 spec §3.2。
        public Action _wireTeardown;

        public AbilityContext MakeContext(
            IAbilityComponent component,
            AbilityEvent evt = null,
            Blackboard sharedBlackboard = null,
            Entity entity = null)
        {
            return new AbilityContext
            {
                ability = this,
                component = component,
                currentEvent = evt,
                sharedBlackboard = sharedBlackboard,
                entity = entity,
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
