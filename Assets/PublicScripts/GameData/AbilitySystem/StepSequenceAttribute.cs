using System;
using UnityEngine;

namespace AbilitySystem
{
    /// <summary>
    /// Marks a managed-reference StepConfig array for the nested sequence editor.
    /// The drawer creates concrete StepConfig instances instead of leaving the
    /// null elements produced by Unity's default SerializeReference array UI.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class StepSequenceAttribute : PropertyAttribute { }
}
