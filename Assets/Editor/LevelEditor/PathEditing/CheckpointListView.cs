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

        // Header:▸ [name TextField] (N cp)
        var headerRow = new VisualElement();
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.alignItems = Align.Center;
        headerRow.style.marginBottom = 4;
        headerRow.style.flexShrink = 0;

        // ▸ 前缀
        var prefix = new Label("▸");
        prefix.style.color = new Color(0.611f, 0.863f, 0.996f);
        prefix.style.fontSize = 11;
        prefix.style.flexShrink = 0;
        prefix.style.marginRight = 4;
        headerRow.Add(prefix);

        // name TextField(给当前 path 命名)
        var nameField = new TextField { value = "" };
        nameField.style.flexGrow = 1;
        nameField.style.flexShrink = 1;
        nameField.style.minWidth = 0;
        nameField.style.marginRight = 4;
        nameField.style.fontSize = 11;
        nameField.tooltip = "当前路径的显示名(可空)";
        nameField.RegisterValueChangedCallback(evt =>
        {
            if (state.SelectedPathIdx < 0) return;
            var arr = so.FindProperty("Paths");
            if (arr == null || state.SelectedPathIdx >= arr.arraySize) return;
            var nameProp = arr.GetArrayElementAtIndex(state.SelectedPathIdx).FindPropertyRelative("Name");
            if (nameProp == null) return;
            Undo.RecordObject(so.targetObject, "Rename Path");
            nameProp.stringValue = evt.newValue ?? "";
            so.ApplyModifiedProperties();
            state.NotifyChanged();
        });
        headerRow.Add(nameField);

        // (N cp) 计数
        var header = new Label("Checkpoints");
        header.style.color = new Color(0.611f, 0.863f, 0.996f);
        header.style.fontSize = 11;
        header.style.flexShrink = 0;
        headerRow.Add(header);

        root.Add(headerRow);

        // 用 ScrollView 包裹行列表 — 当 cp 多时只滚动这部分,不影响 detail
        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.name = "cp-list-scroll";
        scroll.style.flexGrow = 1;
        scroll.style.minHeight = 0;
        var list = new VisualElement();
        list.name = "cp-list";
        list.style.flexShrink = 0; // 行高度累加,允许撑出 ScrollView
        scroll.Add(list);
        root.Add(scroll);

        void Rebuild()
        {
            list.Clear();
            if (state.SelectedPathIdx < 0)
            {
                header.text = "(未选中)";
                nameField.SetValueWithoutNotify("");
                nameField.SetEnabled(false);
                return;
            }
            var pathsArr = so.FindProperty("Paths");
            if (pathsArr == null || state.SelectedPathIdx >= pathsArr.arraySize)
            {
                header.text = "(路径为空)";
                nameField.SetValueWithoutNotify("");
                nameField.SetEnabled(false);
                return;
            }
            nameField.SetEnabled(true);
            var pathProp = pathsArr.GetArrayElementAtIndex(state.SelectedPathIdx);
            var cpsProp = pathProp.FindPropertyRelative("CheckPoints");
            var wtsProp = pathProp.FindPropertyRelative("WaitTimes");
            var nameProp = pathProp.FindPropertyRelative("Name");
            if (cpsProp == null) return;

            header.text = $"({cpsProp.arraySize} cp)";
            // 同步 nameField(防止 state.Changed 触发时 TextField 显示与 SerializedProperty 不一致)
            if (nameProp != null) nameField.SetValueWithoutNotify(nameProp.stringValue ?? "");

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
