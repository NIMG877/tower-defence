using System;
using System.Collections.Generic;

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
        private static readonly Dictionary<string, Func<IAbilityComponent>> _registry =
            new Dictionary<string, Func<IAbilityComponent>>();

        public static void Register(string typeName, Func<IAbilityComponent> ctor)
        {
            if (string.IsNullOrEmpty(typeName))
                throw new ArgumentException("typeName must not be empty", nameof(typeName));
            if (ctor == null)
                throw new ArgumentNullException(nameof(ctor));
            _registry[typeName] = ctor;
        }

        public static IAbilityComponent Create(string typeName)
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
