using System;
using System.Collections.Generic;
using System.Reflection;

namespace AbilitySystem
{
    public static class ComponentAutoRegistry
    {
        private static bool _done = false;
        private static readonly object _lock = new object();

        public static void RegisterAll()
        {
            lock (_lock)
            {
                if (_done) return;
                var asm = Assembly.GetAssembly(typeof(IAbilityComponent));
                foreach (var type in asm.GetTypes())
                {
                    var attr = type.GetCustomAttribute<RegisterComponentAttribute>();
                    if (attr == null) continue;
                    if (!typeof(IAbilityComponent).IsAssignableFrom(type)) continue;
                    if (type.IsAbstract || type.IsInterface) continue;
                    ComponentFactory.Register(attr.TypeName, () => (IAbilityComponent)Activator.CreateInstance(type));
                }
                _done = true;
            }
        }

        public static void Reset() { lock (_lock) { _done = false; } }

        public static void EnsureRegistered()
        {
            if (!_done) RegisterAll();
        }
    }
}
