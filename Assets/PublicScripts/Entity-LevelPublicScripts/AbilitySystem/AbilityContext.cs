using UnityEngine;
using AbilitySystem.Components;

namespace AbilitySystem
{
    public abstract class AbilityEvent
    {
        // Every concrete AbilityEvent must declare which TriggerEvent enum value
        // it routes to. Abstract (not virtual) so the compiler catches missing
        // overrides — that's the safety net for the dispatch bridge table.
        public abstract TriggerEvent TriggerEvent { get; }
    }

    public class AbilityContext
    {
        public Entity entity;
        // Filled by AbilityRuntime.MakeContext (the runtime passes itself as 'ability').
        public AbilityRuntime ability;
        public AbilityComponentBase component;
        public AbilityEvent currentEvent;
        // The only blackboard. Per-Entity, set by EntityAbilityRunner via MakeContext
        // (passed in as a parameter). Components read this directly; there is no
        // per-ability blackboard.
        public Blackboard sharedBlackboard;
    }

    /// <summary>
    /// 在场景加载前触发 <see cref="ComponentAutoRegistry.EnsureRegistered"/>,
    /// 让所有标了 <see cref="RegisterComponentAttribute"/> 的组件类被扫进工厂。
    /// </summary>
    public static class AbilitySystemBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Init()
        {
            ComponentAutoRegistry.EnsureRegistered();
        }
    }
}
