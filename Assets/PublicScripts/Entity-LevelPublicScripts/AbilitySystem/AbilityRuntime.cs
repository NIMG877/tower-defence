using System;
using System.Collections.Generic;
using AbilitySystem.Components;
using UnityEngine;

namespace AbilitySystem
{
    public class AbilityRuntime
    {
        public AbilityConfig config;
        public AbilityKind Kind => config.Kind;
        public string runtimeId;
        public SPEngine spEngine;
        public bool isActive;

        public List<AbilityComponentBase> components = new List<AbilityComponentBase>();
        public List<ParamList> componentParams = new List<ParamList>();

        // Executable rule graph. components/componentParams above remain a
        // flattened compatibility index for existing UI and component ticks.
        public List<AbilityRuleRuntime> ruleRuntimes = new List<AbilityRuleRuntime>();
        public Dictionary<TriggerEvent, List<(AbilityRuleRuntime rule, List<ConditionGroup> groups)>>
            rulesByTrigger
            = new Dictionary<TriggerEvent, List<(AbilityRuleRuntime, List<ConditionGroup>)>>();

        public bool isInitialized;
        internal bool isCancellingStepExecutions;

        // Wire/UnwireRuntime 存放在这里;EntityAbilityRunner 负责 set/clear 这个字段。
        public Action _wireTeardown;

        public AbilityContext MakeContext(
            AbilityComponentBase component,
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
            // Every state transition starts a new lifetime. Cancel work owned
            // by the old active/inactive lifetime before changing state and
            // dispatching the corresponding Begin/End rules.
            CancelStepExecutions();
            isActive = value;
            if (value) OnAbilityBegin?.Invoke();
            else OnAbilityEnd?.Invoke();
        }

        public void BuildRules(AbilityRuleConfig[] configs)
        {
            CancelStepExecutions();
            ruleRuntimes.Clear();
            rulesByTrigger.Clear();
            components.Clear();
            componentParams.Clear();

            if (configs == null) return;
            for (int i = 0; i < configs.Length; i++)
            {
                AbilityRuleConfig configRule = configs[i];
                if (configRule == null) continue;
                var runtimeRule = new AbilityRuleRuntime(this, configRule);
                ruleRuntimes.Add(runtimeRule);

                int triggerCount = 0;
                if (configRule.triggers != null)
                {
                    for (int t = 0; t < configRule.triggers.Length; t++)
                    {
                        ConditionConfig trigger = configRule.triggers[t];
                        if (trigger == null) continue;
                        if (!rulesByTrigger.TryGetValue(trigger.triggerEvent, out var bucket))
                        {
                            bucket = new List<(AbilityRuleRuntime, List<ConditionGroup>)>();
                            rulesByTrigger[trigger.triggerEvent] = bucket;
                        }
                        bucket.Add((runtimeRule, trigger.groups));
                        triggerCount++;
                    }
                }

                if (triggerCount == 0)
                {
                    Debug.LogWarning(
                        $"[AbilityRuntime] Rule {i} in ability {config?.abilityId} " +
                        "declares no triggers; it will never execute.");
                }
            }
        }

        public void TickStepExecutions(float deltaTime)
        {
            var snapshot = new List<AbilityStepExecution>();
            AppendStepExecutionSnapshot(snapshot);
            for (int i = 0; i < snapshot.Count; i++)
                snapshot[i].Tick(deltaTime);
            PruneCompletedStepExecutions();
        }

        internal void AppendStepExecutionSnapshot(List<AbilityStepExecution> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            for (int i = 0; i < ruleRuntimes.Count; i++)
                ruleRuntimes[i].AppendExecutionSnapshot(destination);
        }

        internal void PruneCompletedStepExecutions()
        {
            for (int i = 0; i < ruleRuntimes.Count; i++)
                ruleRuntimes[i].PruneCompletedExecutions();
        }

        public void CancelStepExecutions()
        {
            if (isCancellingStepExecutions) return;
            isCancellingStepExecutions = true;
            try
            {
                for (int i = 0; i < ruleRuntimes.Count; i++)
                    ruleRuntimes[i].CancelAll();
            }
            finally
            {
                isCancellingStepExecutions = false;
            }
        }

        public event Action OnAbilityBegin;
        public event Action OnAbilityEnd;
    }
}
