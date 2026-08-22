using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AbilitySystem
{
    /// <summary>
    /// Inspector dropdown for <see cref="StepOpRefAttribute"/> strings.
    /// Empty and no-longer-registered values remain visible and are never
    /// replaced merely because the Inspector was drawn.
    /// </summary>
    [CustomPropertyDrawer(typeof(StepOpRefAttribute))]
    public sealed class StepOpRefDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "StepOpRef requires a string field");
                return;
            }

            string currentValue = property.stringValue ?? string.Empty;
            string[] registeredOps = AbilityStepOpRegistry.RegisteredOps
                .Where(op => !string.IsNullOrEmpty(op))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(op => op, StringComparer.Ordinal)
                .ToArray();

            int registeredIndex = Array.IndexOf(registeredOps, currentValue);
            bool hasPreservedValue = registeredIndex < 0;
            var displayOptions = new List<GUIContent>(registeredOps.Length + (hasPreservedValue ? 1 : 0));

            if (hasPreservedValue)
            {
                displayOptions.Add(string.IsNullOrEmpty(currentValue)
                    ? new GUIContent("(empty)", "No op has been selected.")
                    : new GUIContent(
                        currentValue + " (unregistered)",
                        "This serialized value is not currently registered. It is preserved until you choose another op."));
            }

            for (int i = 0; i < registeredOps.Length; i++)
                displayOptions.Add(new GUIContent(registeredOps[i]));

            if (displayOptions.Count == 0)
            {
                // The only possible case is an empty field and an empty registry.
                displayOptions.Add(new GUIContent("(empty; no ops registered)"));
                hasPreservedValue = true;
            }

            int currentIndex = hasPreservedValue ? 0 : registeredIndex;

            EditorGUI.BeginProperty(position, label, property);
            bool previousMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int selectedIndex = EditorGUI.Popup(position, label, currentIndex, displayOptions.ToArray());
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = previousMixedValue;
            EditorGUI.EndProperty();

            // A synthetic first option represents the exact serialized empty/orphan
            // value. Selecting it is intentionally a no-op; only a registered op
            // explicitly chosen by the user is written back.
            if (!changed || selectedIndex < 0 || selectedIndex >= displayOptions.Count)
                return;
            if (hasPreservedValue)
            {
                if (selectedIndex == 0) return;
                selectedIndex--;
            }

            if (selectedIndex >= 0 && selectedIndex < registeredOps.Length)
                property.stringValue = registeredOps[selectedIndex];
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }
    }
}
