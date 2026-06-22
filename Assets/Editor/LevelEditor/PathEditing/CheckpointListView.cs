using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class CheckpointListView
{
    public static VisualElement Build(SerializedObject so, PathEditingState state)
    {
        var root = new VisualElement();
        root.style.backgroundColor = new Color(0.118f, 0.118f, 0.133f);
        root.style.borderTopLeftRadius = 3;
        root.style.borderTopRightRadius = 3;
        root.style.borderBottomLeftRadius = 3;
        root.style.borderBottomRightRadius = 3;
        root.style.borderLeftWidth = 1; root.style.borderRightWidth = 1;
        root.style.borderTopWidth = 1; root.style.borderBottomWidth = 1;
        root.style.borderLeftColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.borderRightColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.borderTopColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.borderBottomColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.paddingTop = 4; root.style.paddingBottom = 4;
        root.style.paddingLeft = 6; root.style.paddingRight = 6;
        root.style.marginBottom = 6;

        var header = new Label("▸ Checkpoints");
        header.style.color = new Color(0.611f, 0.863f, 0.996f);
        header.style.fontSize = 11;
        header.style.marginBottom = 4;
        root.Add(header);

        var list = new VisualElement();
        list.name = "cp-list";
        root.Add(list);

        void Rebuild()
        {
            list.Clear();
            if (state.SelectedPathIdx < 0) return;
            var pathProp = so.FindProperty("Paths").GetArrayElementAtIndex(state.SelectedPathIdx);
            var cpsProp = pathProp.FindPropertyRelative("CheckPoints");
            var wtsProp = pathProp.FindPropertyRelative("WaitTimes");
            if (cpsProp == null) return;

            header.text = $"▸ Checkpoints ({cpsProp.arraySize})";

            for (int k = 0; k < cpsProp.arraySize; k++)
            {
                int captured = k;
                var pos = cpsProp.GetArrayElementAtIndex(k).vector2Value;
                var wait = wtsProp != null && k < wtsProp.arraySize
                    ? wtsProp.GetArrayElementAtIndex(k).floatValue : 0f;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.justifyContent = Justify.SpaceBetween;
                row.style.paddingTop = 3; row.style.paddingBottom = 3;
                row.style.paddingLeft = 6; row.style.paddingRight = 6;
                row.style.marginBottom = 1;
                row.style.backgroundColor = (k == state.SelectedCheckpointIdx)
                    ? new Color(0.275f, 0.353f, 0.294f)
                    : new Color(0.196f, 0.196f, 0.216f);
                if (k == state.SelectedCheckpointIdx)
                {
                    row.style.borderLeftWidth = 2;
                    row.style.borderLeftColor = new Color(1f, 0.784f, 0.314f);
                }

                var label = new Label($"#{k} ({pos.x:F2}, {pos.y:F2})");
                label.style.fontSize = 11;
                row.Add(label);

                var waitLbl = new Label($"wait {wait:F1}s");
                waitLbl.style.fontSize = 11;
                waitLbl.style.color = new Color(0.706f, 0.706f, 0.706f);
                row.Add(waitLbl);

                row.RegisterCallback<MouseDownEvent>(_ =>
                {
                    state.SelectedCheckpointIdx = captured;
                    state.NotifyChanged();
                });
                list.Add(row);
            }
        }

        state.Changed += Rebuild;
        so.Update();
        Undo.undoRedoPerformed += Rebuild;
        Rebuild();
        return root;
    }
}
