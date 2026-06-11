using System;
using UnityEngine;

namespace AbilitySystem
{
    /// <summary>
    /// Marker for <c>string</c> fields whose value should be selected from
    /// <see cref="ComponentFactory.RegisteredTypes"/> via a dropdown in the Inspector.
    ///
    /// The runtime field stays a <c>string</c> (Unity-safe serialization, no enum ABI risk);
    /// the dropdown is purely an editor convenience wired up by
    /// <c>ComponentTypeRefDrawer</c> in the Editor assembly.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public class ComponentTypeRefAttribute : PropertyAttribute { }
}
