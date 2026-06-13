using System;
using System.Collections.Generic;
using AbilitySystem.Components;

namespace AbilitySystem
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class RegisterComponentAttribute : Attribute
    {
        public string TypeName;
        public RegisterComponentAttribute(string typeName) { TypeName = typeName; }
    }

    public static class ComponentFactory
    {
        private static readonly Dictionary<string, Func<AbilityComponentBase>> _registry =
            new Dictionary<string, Func<AbilityComponentBase>>();

        public static void Register(string typeName, Func<AbilityComponentBase> ctor)
        {
            if (string.IsNullOrEmpty(typeName))
                throw new ArgumentException("typeName must not be empty", nameof(typeName));
            if (ctor == null)
                throw new ArgumentNullException(nameof(ctor));
            _registry[typeName] = ctor;
        }

        public static AbilityComponentBase Create(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            return _registry.TryGetValue(typeName, out var ctor) ? ctor() : null;
        }

        public static bool IsRegistered(string typeName) =>
            !string.IsNullOrEmpty(typeName) && _registry.ContainsKey(typeName);

        public static IEnumerable<string> RegisteredTypes => _registry.Keys;

        public static void Clear() => _registry.Clear();
    }
}
