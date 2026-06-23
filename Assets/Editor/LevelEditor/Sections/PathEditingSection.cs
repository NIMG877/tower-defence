using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class PathEditingSection
{
    public static VisualElement Build(SerializedObject so, PathEditingState state)
    {
        var root = new VisualElement();
        root.style.backgroundColor = new Color(0.118f, 0.118f, 0.133f);
        root.style.paddingTop = 8; root.style.paddingBottom = 8;
        root.style.paddingLeft = 8; root.style.paddingRight = 8;
        root.style.borderTopLeftRadius = 3;
        root.style.borderTopRightRadius = 3;
        root.style.borderBottomLeftRadius = 3;
        root.style.borderBottomRightRadius = 3;
        root.style.borderLeftWidth = 1;
        root.style.borderRightWidth = 1;
        root.style.borderTopWidth = 1;
        root.style.borderBottomWidth = 1;
        root.style.borderLeftColor = new Color(0.306f, 0.788f, 0.627f);
        root.style.borderRightColor = new Color(0.306f, 0.788f, 0.627f);
        root.style.borderTopColor = new Color(0.306f, 0.788f, 0.627f);
        root.style.borderBottomColor = new Color(0.306f, 0.788f, 0.627f);

        // Header
        var header = new Label("▸ Path Editing");
        header.style.color = new Color(0.306f, 0.788f, 0.627f);
        header.style.fontSize = 12;
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginBottom = 6;
        root.Add(header);

        // Path picker
        root.Add(BuildPathPicker(so, state));
        // Toolbar
        root.Add(BuildToolbar(state, root));
        // Split: canvas + side panel
        var split = new VisualElement();
        split.style.flexDirection = FlexDirection.Row;
        split.style.marginTop = 6;

        var canvas = MapCanvasView.Build(so, state);
        split.Add(canvas);

        var right = new VisualElement();
        right.style.flexDirection = FlexDirection.Column;
        right.style.flexGrow = 1;
        right.style.marginLeft = 8;
        right.style.minWidth = 180;
        right.Add(CheckpointListView.Build(so, state));
        right.Add(CheckpointDetailView.Build(so, state));
        split.Add(right);

        root.Add(split);

        // 初始 fit 视图
        if (state.Cache != null) state.View = ViewTransform.Fit(state.Cache.ISize, state.Cache.JSize);
        state.NotifyChanged();

        return root;
    }

    static VisualElement BuildPathPicker(SerializedObject so, PathEditingState state)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 4;

        var label = new Label("当前编辑路径");
        label.style.color = new Color(0.611f, 0.863f, 0.996f);
        label.style.minWidth = 90;
        label.style.fontSize = 11;
        row.Add(label);

        var pathsProp = so.FindProperty("Paths");
        var choices = new List<string>();
        for (int i = 0; i < pathsProp.arraySize; i++)
        {
            var cps = pathsProp.GetArrayElementAtIndex(i).FindPropertyRelative("CheckPoints");
            choices.Add($"Path {i}: {(cps != null ? cps.arraySize : 0)} checkpoints");
        }
        if (choices.Count == 0) choices.Add("(暂无路径)");

        int initialIdx = Mathf.Clamp(state.SelectedPathIdx, 0, Mathf.Max(0, choices.Count - 1));
        var popup = new PopupField<string>(choices, initialIdx);
        popup.style.flexGrow = 1;
        popup.RegisterValueChangedCallback(evt =>
        {
            int idx = choices.IndexOf(evt.newValue);
            if (idx >= 0)
            {
                state.SelectedPathIdx = idx;
                state.SelectedCheckpointIdx = -1;
                state.NotifyChanged();
            }
        });
        row.Add(popup);

        var newBtn = new Button(() =>
        {
            Undo.RecordObject(so.targetObject, "Add Path");
            pathsProp.arraySize++;
            pathsProp.GetArrayElementAtIndex(pathsProp.arraySize - 1)
                .FindPropertyRelative("CheckPoints").arraySize = 0;
            pathsProp.GetArrayElementAtIndex(pathsProp.arraySize - 1)
                .FindPropertyRelative("WaitTimes").arraySize = 0;
            so.ApplyModifiedProperties();
            state.SelectedPathIdx = pathsProp.arraySize - 1;
            state.SelectedCheckpointIdx = -1;
            state.NotifyChanged();
            // Rebuild picker to show new path
            row.RemoveFromHierarchy();
            // Note: full rebuild deferred to caller; simpler: add a new path then trigger section refresh
            // — handled by caller (LevelDataEditor) re-calling Build() on Paths property change
        }) { text = "+ 新建" };
        newBtn.style.marginLeft = 4;
        row.Add(newBtn);

        var delBtn = new Button(() =>
        {
            if (state.SelectedPathIdx < 0 || state.SelectedPathIdx >= pathsProp.arraySize) return;
            Undo.RecordObject(so.targetObject, "Delete Path");
            pathsProp.DeleteArrayElementAtIndex(state.SelectedPathIdx);
            so.ApplyModifiedProperties();
            state.SelectedPathIdx = Mathf.Max(0, state.SelectedPathIdx - 1);
            state.SelectedCheckpointIdx = -1;
            state.NotifyChanged();
        }) { text = "删除" };
        delBtn.style.marginLeft = 4;
        delBtn.style.backgroundColor = new Color(0.471f, 0.235f, 0.235f);
        row.Add(delBtn);

        return row;
    }

    static VisualElement BuildToolbar(PathEditingState state, VisualElement root)
    {
        var bar = new VisualElement();
        bar.style.flexDirection = FlexDirection.Row;
        bar.style.backgroundColor = new Color(0.157f, 0.157f, 0.157f);
        bar.style.paddingTop = 4; bar.style.paddingBottom = 4;
        bar.style.paddingLeft = 6; bar.style.paddingRight = 6;
        bar.style.alignItems = Align.Center;
        bar.style.borderTopLeftRadius = 3; bar.style.borderTopRightRadius = 3;
        bar.style.borderBottomLeftRadius = 3; bar.style.borderBottomRightRadius = 3;

        var addCpBtn = new Button(() => { /* delegated to Manipulator left-click on empty */ })
        { text = "⊕ 新建点 (左键空白)" };
        addCpBtn.style.fontSize = 11;
        addCpBtn.SetEnabled(false); // 提示用法:实际通过左键操作
        bar.Add(addCpBtn);

        var delCpBtn = new Button(() =>
        {
            // 通过 SerializedObject 删除选中 cp
            var so = root.userData as SerializedObject;
            // see CheckpointDetailView for delete pattern; toolbar version delegated
        }) { text = "✕ 删除选中点" };
        delCpBtn.style.marginLeft = 4;
        delCpBtn.style.fontSize = 11;
        // Active when state.SelectedCheckpointIdx >= 0; bind later
        bar.Add(delCpBtn);

        var sep1 = new VisualElement();
        sep1.style.width = 1; sep1.style.height = 16;
        sep1.style.backgroundColor = new Color(0.314f, 0.314f, 0.314f);
        sep1.style.marginLeft = 6; sep1.style.marginRight = 6;
        bar.Add(sep1);

        var moveLabel = new Label("moveMethod:");
        moveLabel.style.fontSize = 11;
        moveLabel.style.color = new Color(0.706f, 0.706f, 0.706f);
        bar.Add(moveLabel);

        var methods = new[] { "地面", "近地", "飞行" };
        for (int k = 0; k < 3; k++)
        {
            int captured = k;
            // 先创建 Button 再注册 click,避免 lambda 在 btn 声明前捕获
            var btn = new Button { text = methods[k] };
            btn.clicked += () =>
            {
                state.MoveMethod = captured;
                state.NotifyChanged();
                // Update button visuals
                foreach (var child in bar.Children())
                    if (child is Button b && methods.Contains(b.text)) b.style.backgroundColor = StyleKeyword.Null;
                btn.style.backgroundColor = new Color(0.306f, 0.788f, 0.627f);
                btn.style.color = Color.black;
            };
            btn.style.marginLeft = 2;
            btn.style.fontSize = 11;
            if (k == state.MoveMethod)
            {
                btn.style.backgroundColor = new Color(0.306f, 0.788f, 0.627f);
                btn.style.color = Color.black;
            }
            bar.Add(btn);
        }

        var sep2 = new VisualElement();
        sep2.style.width = 1; sep2.style.height = 16;
        sep2.style.backgroundColor = new Color(0.314f, 0.314f, 0.314f);
        sep2.style.marginLeft = 6; sep2.style.marginRight = 6;
        bar.Add(sep2);

        var resetBtn = new Button(() =>
        {
            if (state.Cache != null)
                state.View = ViewTransform.Fit(state.Cache.ISize, state.Cache.JSize);
            state.NotifyChanged();
        }) { text = "↺ 重置视图" };
        resetBtn.style.fontSize = 11;
        bar.Add(resetBtn);

        return bar;
    }
}
