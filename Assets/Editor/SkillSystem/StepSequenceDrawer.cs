using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace AbilitySystem
{
    /// <summary>
    /// Reorderable editor for nested managed-reference step arrays.
    /// Unity inserts null when a SerializeReference array grows; this drawer's
    /// Add action always creates a concrete StepConfig so it is immediately
    /// authorable and serializable.
    /// </summary>
    [CustomPropertyDrawer(typeof(StepSequenceAttribute))]
    public sealed class StepSequenceDrawer : PropertyDrawer
    {
        private const float ElementPadding = 4f;
        private const float MultiObjectHelpHeight = 38f;

        private sealed class ListState
        {
            public ReorderableList List;
            public GUIContent Label;
        }

        // ReorderableList owns selection and drag state across GUI events. A
        // fresh instance per OnGUI/GetPropertyHeight would make reordering and
        // the remove button unreliable, so keep one per target/property path.
        private readonly Dictionary<string, ListState> _lists =
            new Dictionary<string, ListState>();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!property.isArray || property.propertyType == SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "StepSequence requires an array field");
                return;
            }

            bool isMultiObject = property.serializedObject.isEditingMultipleObjects;

            EditorGUI.BeginProperty(position, label, property);
            if (isMultiObject)
            {
                EditorGUI.HelpBox(
                    position,
                    $"{label.text}: nested steps are read-only while multiple objects are selected. Edit one ability at a time.",
                    MessageType.Info);
                EditorGUI.EndProperty();
                return;
            }

            GetList(property, label).DoList(position);
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isArray || property.propertyType == SerializedPropertyType.String)
                return EditorGUIUtility.singleLineHeight;

            if (property.serializedObject.isEditingMultipleObjects)
                return MultiObjectHelpHeight;
            return GetList(property, label).GetHeight();
        }

        private ReorderableList GetList(SerializedProperty property, GUIContent label)
        {
            int targetId = property.serializedObject.targetObject != null
                ? property.serializedObject.targetObject.GetInstanceID()
                : 0;
            string key = targetId + ":" + property.propertyPath;

            if (!_lists.TryGetValue(key, out ListState state) ||
                state.List.serializedProperty == null ||
                state.List.serializedProperty.serializedObject != property.serializedObject)
            {
                state = CreateListState(property, label);
                _lists[key] = state;
            }
            else
            {
                // SerializedProperty wrappers are tied to their SerializedObject.
                // Refresh the cached list with the property from this GUI event.
                state.List.serializedProperty = property;
                state.Label = new GUIContent(label);
            }

            return state.List;
        }

        private static ListState CreateListState(SerializedProperty property, GUIContent label)
        {
            var state = new ListState { Label = new GUIContent(label) };
            state.List = new ReorderableList(
                property.serializedObject,
                property,
                true,
                true,
                true,
                true);

            ReorderableList list = state.List;

            list.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(rect, state.Label);
            };

            list.elementHeightCallback = index =>
            {
                SerializedProperty array = state.List.serializedProperty;
                if (array == null || index < 0 || index >= array.arraySize)
                    return EditorGUIUtility.singleLineHeight + ElementPadding;

                SerializedProperty element = array.GetArrayElementAtIndex(index);
                return GetElementHeight(element, index) + ElementPadding;
            };

            list.drawElementCallback = (rect, index, active, focused) =>
            {
                SerializedProperty array = state.List.serializedProperty;
                if (array == null || index < 0 || index >= array.arraySize) return;

                SerializedProperty element = array.GetArrayElementAtIndex(index);
                rect.y += ElementPadding * 0.5f;
                rect.height = GetElementHeight(element, index);

                if (IsNullManagedReference(element))
                {
                    if (GUI.Button(rect, $"Create Step {index}"))
                        AssignNewStep(array, element, "Create Ability Step");
                    return;
                }

                EditorGUI.PropertyField(
                    rect,
                    element,
                    new GUIContent($"Step {index}"),
                    includeChildren: true);
            };

            list.onAddCallback = reorderableList =>
            {
                SerializedProperty array = reorderableList.serializedProperty;
                SerializedObject serializedObject = array.serializedObject;
                Undo.RecordObjects(serializedObject.targetObjects, "Add Ability Step");

                int newIndex = array.arraySize;
                // This is Unity's own append path for ReorderableList. A
                // SerializeReference element is null after growing the array;
                // immediately replace it with a fresh concrete step.
                array.arraySize = newIndex + 1;
                SerializedProperty element = array.GetArrayElementAtIndex(newIndex);
                element.managedReferenceValue = new StepConfig();
                serializedObject.ApplyModifiedProperties();

                reorderableList.index = newIndex;
                GUI.changed = true;
            };

            list.onRemoveCallback = reorderableList =>
            {
                SerializedProperty array = reorderableList.serializedProperty;
                int index = reorderableList.index;
                if (index < 0 || index >= array.arraySize) return;

                SerializedObject serializedObject = array.serializedObject;
                Undo.RecordObjects(serializedObject.targetObjects, "Remove Ability Step");

                int previousSize = array.arraySize;
                array.DeleteArrayElementAtIndex(index);
                // Object-like array elements can require one call to clear the
                // reference and a second to remove the now-null slot.
                if (array.arraySize == previousSize)
                    array.DeleteArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();

                reorderableList.index = Mathf.Clamp(index - 1, -1, array.arraySize - 1);
                GUI.changed = true;
            };

            list.onReorderCallback = reorderableList =>
            {
                reorderableList.serializedProperty.serializedObject.ApplyModifiedProperties();
                GUI.changed = true;
            };

            return state;
        }

        private static float GetElementHeight(SerializedProperty element, int index)
        {
            if (IsNullManagedReference(element))
                return EditorGUIUtility.singleLineHeight;

            return EditorGUI.GetPropertyHeight(
                element,
                new GUIContent($"Step {index}"),
                includeChildren: true);
        }

        private static bool IsNullManagedReference(SerializedProperty property)
        {
            return property.propertyType == SerializedPropertyType.ManagedReference &&
                   property.managedReferenceValue == null;
        }

        private static void AssignNewStep(
            SerializedProperty array,
            SerializedProperty element,
            string undoName)
        {
            SerializedObject serializedObject = array.serializedObject;
            Undo.RecordObjects(serializedObject.targetObjects, undoName);
            element.managedReferenceValue = new StepConfig();
            serializedObject.ApplyModifiedProperties();
            GUI.changed = true;
        }
    }
}
