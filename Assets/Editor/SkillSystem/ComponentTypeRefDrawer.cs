// Inspector drawer for string fields marked with [ComponentTypeRef].
// Renders an EditorGUI.Popup sourced from ComponentFactory.RegisteredTypes,
// which is populated at runtime by SkillSystemBootstrap and, in editor cold-start,
// by ComponentAutoRegistry.EnsureRegistered() invoked below.

using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AbilitySystem
{
    [CustomPropertyDrawer(typeof(ComponentTypeRefAttribute))]
    public class ComponentTypeRefDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // Bootstrap ([RuntimeInitializeOnLoadMethod]) only runs in play mode / builds.
            // In edit mode the registry may be empty, so populate it on demand.
            ComponentAutoRegistry.EnsureRegistered();

            var types = ComponentFactory.RegisteredTypes.OrderBy(s => s).ToArray();

            if (types.Length == 0)
            {
                EditorGUI.LabelField(position, label.text, "(no components registered)");
                return;
            }

            // EditorGUI.Popup has no built-in "(None)" option — the popup shows only
            // registered component type names. Empty or orphan values (which are not
            // in the registry) are surfaced via the label and forced to a default index.
            var display = new GUIContent[types.Length];
            for (int i = 0; i < types.Length; i++)
                display[i] = new GUIContent(types[i]);

            int currentIdx;
            GUIContent popupLabel = label;

            if (string.IsNullOrEmpty(property.stringValue))
            {
                // Empty value: no registered name matches. Default the popup to index 0
                // and warn so the user can see the field is unset.
                currentIdx = 0;
                popupLabel = new GUIContent($"{label.text}  (!) empty");
            }
            else
            {
                int found = System.Array.IndexOf(types, property.stringValue);
                if (found >= 0)
                {
                    currentIdx = found;
                }
                else
                {
                    // Orphan / legacy value (component removed or renamed).
                    // Surface it in the label so the broken reference is visible;
                    // the popup defaults to the first registered type until the user picks.
                    currentIdx = 0;
                    popupLabel = new GUIContent(
                        $"{label.text}  (!) \"{property.stringValue}\" not registered",
                        $"\"{property.stringValue}\" is not in ComponentFactory.RegisteredTypes. " +
                        "Pick a value to replace it.");
                }
            }

            int newIdx = EditorGUI.Popup(position, popupLabel, currentIdx, display);
            if (newIdx < 0 || newIdx >= types.Length) return; // sanity guard

            string newVal = types[newIdx];
            if (property.stringValue != newVal)
                property.stringValue = newVal;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }
    }
}
