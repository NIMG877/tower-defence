using System;
using UnityEngine;

namespace AbilitySystem
{
    /// <summary>Marks a serialized op string for the registered-op dropdown.</summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class StepOpRefAttribute : PropertyAttribute { }
}
