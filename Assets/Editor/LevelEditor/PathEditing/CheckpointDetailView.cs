using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public static class CheckpointDetailView
{
    public static VisualElement Build(SerializedObject so, PathEditingState state)
    {
        var root = new VisualElement();
        root.style.backgroundColor = new Color(0.118f, 0.118f, 0.133f);
        root.style.borderTopLeftRadius = 3; root.style.borderTopRightRadius = 3;
        root.style.borderBottomLeftRadius = 3; root.style.borderBottomRightRadius = 3;
        root.style.borderLeftWidth = 1; root.style.borderRightWidth = 1;
        root.style.borderTopWidth = 1; root.style.borderBottomWidth = 1;
        root.style.borderLeftColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.borderRightColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.borderTopColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.borderBottomColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.paddingTop = 4; root.style.paddingBottom = 4;
        root.style.paddingLeft = 6; root.style.paddingRight = 6;

        var header = new Label("▸ Checkpoint detail");
        header.style.color = new Color(0.611f, 0.863f, 0.996f);
        header.style.fontSize = 11;
        header.style.marginBottom = 4;
        root.Add(header);

        var posXField = new FloatField("Position X") { value = 0f };
        var posYField = new FloatField("Position Y") { value = 0f };
        var waitField = new FloatField("WaitTime") { value = 0f };
        posXField.style.marginBottom = 2;
        posYField.style.marginBottom = 2;
        waitField.style.marginBottom = 2;

        var cpProp = new SerializedProperty[1]; // SerializedProperty 没有 public ctor,改用数组容器
        void Bind()
        {
            if (state.SelectedCheckpointIdx < 0)
            {
                header.text = "▸ Checkpoint detail (未选中)";
                posXField.SetEnabled(false); posYField.SetEnabled(false); waitField.SetEnabled(false);
                return;
            }
            var pathsArr = so.FindProperty("Paths");
            if (pathsArr == null || state.SelectedPathIdx >= pathsArr.arraySize)
            {
                header.text = "▸ Checkpoint detail (路径为空)";
                posXField.SetEnabled(false); posYField.SetEnabled(false); waitField.SetEnabled(false);
                return;
            }
            var pathProp = pathsArr.GetArrayElementAtIndex(state.SelectedPathIdx);
            var cpsProp = pathProp.FindPropertyRelative("CheckPoints");
            var wtsProp = pathProp.FindPropertyRelative("WaitTimes");
            if (cpsProp == null || state.SelectedCheckpointIdx >= cpsProp.arraySize) return;

            header.text = $"▸ Checkpoint #{state.SelectedCheckpointIdx}";
            posXField.SetEnabled(true); posYField.SetEnabled(true); waitField.SetEnabled(true);

            cpProp[0] = cpsProp.GetArrayElementAtIndex(state.SelectedCheckpointIdx);
            posXField.BindProperty(cpProp[0].FindPropertyRelative("x"));
            posYField.BindProperty(cpProp[0].FindPropertyRelative("y"));
            if (wtsProp != null && state.SelectedCheckpointIdx < wtsProp.arraySize)
                waitField.BindProperty(wtsProp.GetArrayElementAtIndex(state.SelectedCheckpointIdx));
        }

        root.Add(posXField); root.Add(posYField); root.Add(waitField);

        state.Changed += Bind;
        Bind();
        return root;
    }
}
