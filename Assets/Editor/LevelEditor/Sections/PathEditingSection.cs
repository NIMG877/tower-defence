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
        root.Add(BuildToolbar(so, state, root));
        // Split: canvas (弹性宽) + 右侧固定宽栏
        var split = new VisualElement();
        split.style.flexDirection = FlexDirection.Row;
        split.style.marginTop = 6;
        split.style.flexShrink = 0;

        var canvas = MapCanvasView.Build(so, state);
        // 画布:flexGrow=1 占满剩余宽度,最小宽 320(避免太挤),高度固定 400
        canvas.style.flexGrow = 1;
        canvas.style.flexShrink = 1;
        canvas.style.minWidth = 320;
        split.Add(canvas);

        // 右侧栏:固定宽 220,高度 = 画布高度
        var right = new VisualElement();
        right.style.flexDirection = FlexDirection.Column;
        right.style.flexShrink = 0;
        right.style.flexGrow = 0;
        right.style.width = 220;
        right.style.marginLeft = 8;
        right.style.height = ViewTransform.CanvasHeight;
        right.style.overflow = Overflow.Hidden;

        var listView = CheckpointListView.Build(so, state);
        listView.style.flexGrow = 1;
        listView.style.flexShrink = 1;
        listView.style.minHeight = 60;
        listView.style.overflow = Overflow.Hidden;
        right.Add(listView);

        var detailView = CheckpointDetailView.Build(so, state);
        detailView.style.flexShrink = 0;
        detailView.style.marginTop = 4;
        right.Add(detailView);

        split.Add(right);
        root.Add(split);

        root.style.overflow = Overflow.Hidden;

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

        // 工具:用 path 索引拼显示文本(Name 优先,空时退化为 "Path {i}")
        string MakeChoice(int i)
        {
            var nameProp = pathsProp.GetArrayElementAtIndex(i).FindPropertyRelative("Name");
            var cps = pathsProp.GetArrayElementAtIndex(i).FindPropertyRelative("CheckPoints");
            var name = nameProp != null && !string.IsNullOrEmpty(nameProp.stringValue)
                ? nameProp.stringValue
                : $"Path {i}";
            return $"{name} (#{i}, {cps.arraySize} cp)";
        }

        var choices = new List<string>();
        for (int i = 0; i < pathsProp.arraySize; i++) choices.Add(MakeChoice(i));
        if (choices.Count == 0) choices.Add("(暂无路径)");

        int initialIdx = Mathf.Clamp(state.SelectedPathIdx, 0, Mathf.Max(0, choices.Count - 1));
        var popup = new PopupField<string>(choices, initialIdx);
        // popup 限宽 — text 越长越可能被压;给一个 flexBasis 避免完全占满
        popup.style.flexGrow = 0;
        popup.style.flexShrink = 1;
        popup.style.minWidth = 0;
        popup.style.maxWidth = 240;
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

        // 路径名 TextField — 改这里直接改当前 path 的 Name
        var nameLabel = new Label("名:");
        nameLabel.style.fontSize = 11;
        nameLabel.style.color = new Color(0.706f, 0.706f, 0.706f);
        nameLabel.style.marginLeft = 6;
        row.Add(nameLabel);

        var nameField = new TextField { value = "" };
        nameField.style.flexGrow = 1;
        nameField.style.flexShrink = 1;
        nameField.style.minWidth = 0;
        nameField.tooltip = "当前路径的显示名(可空)";
        nameField.RegisterValueChangedCallback(evt =>
        {
            if (state.SelectedPathIdx < 0) return;
            var arr = so.FindProperty("Paths");
            if (arr == null || state.SelectedPathIdx >= arr.arraySize) return;
            var el = arr.GetArrayElementAtIndex(state.SelectedPathIdx);
            var nameProp = el.FindPropertyRelative("Name");
            if (nameProp == null) return;
            Undo.RecordObject(so.targetObject, "Rename Path");
            nameProp.stringValue = evt.newValue ?? "";
            so.ApplyModifiedProperties();
            state.NotifyChanged();
        });
        // 跟随 state 重绑 — SelectedPathIdx 切换时刷新显示
        void SyncNameField()
        {
            if (state.SelectedPathIdx < 0)
            {
                nameField.SetValueWithoutNotify("");
                nameField.SetEnabled(false);
                return;
            }
            nameField.SetEnabled(true);
            var arr = so.FindProperty("Paths");
            if (arr == null || state.SelectedPathIdx >= arr.arraySize) return;
            var nameProp = arr.GetArrayElementAtIndex(state.SelectedPathIdx).FindPropertyRelative("Name");
            nameField.SetValueWithoutNotify(nameProp != null ? nameProp.stringValue : "");
        }
        state.Changed += SyncNameField;
        SyncNameField();
        row.Add(nameField);

        // 让 row 不被父级 split 占用的列宽挤压,确保 popup 拿到合理空间
        row.style.flexShrink = 0;

        // 路径变化时也要同步刷新 popup 文本(因为 Name 改了 choice 文字会变)
        void RebuildPopup()
        {
            choices.Clear();
            for (int i = 0; i < pathsProp.arraySize; i++) choices.Add(MakeChoice(i));
            if (choices.Count == 0) choices.Add("(暂无路径)");
            int cur = Mathf.Clamp(state.SelectedPathIdx, 0, Mathf.Max(0, choices.Count - 1));
            popup.SetValueWithoutNotify(choices[cur]);
        }
        state.Changed += RebuildPopup;

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

    static VisualElement BuildToolbar(SerializedObject so, PathEditingState state, VisualElement root)
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
            if (state.SelectedPathIdx < 0 || state.SelectedCheckpointIdx < 0) return;
            var pathsProp = so.FindProperty("Paths");
            if (pathsProp == null || state.SelectedPathIdx >= pathsProp.arraySize) return;
            var pathEl = pathsProp.GetArrayElementAtIndex(state.SelectedPathIdx);
            var cpsProp = pathEl.FindPropertyRelative("CheckPoints");
            var wtsProp = pathEl.FindPropertyRelative("WaitTimes");
            if (cpsProp == null || state.SelectedCheckpointIdx >= cpsProp.arraySize) return;
            Undo.RecordObject(so.targetObject, "Delete Checkpoint");
            cpsProp.DeleteArrayElementAtIndex(state.SelectedCheckpointIdx);
            // WaitTimes 同步缩短(保持两条数组等长)
            if (wtsProp != null && wtsProp.arraySize > state.SelectedCheckpointIdx)
                wtsProp.DeleteArrayElementAtIndex(state.SelectedCheckpointIdx);
            so.ApplyModifiedProperties();
            // 选中态:如果删的是最后一项就前移,否则保持 idx(让后面 cp 滑上来成为新的"当前")
            if (state.SelectedCheckpointIdx >= cpsProp.arraySize)
                state.SelectedCheckpointIdx = cpsProp.arraySize - 1;
            state.NotifyChanged();
        }) { text = "✕ 删除选中点" };
        delCpBtn.style.marginLeft = 4;
        delCpBtn.style.fontSize = 11;
        // 跟随 state 启用/禁用
        void SyncDelEnabled()
        {
            delCpBtn.SetEnabled(state.SelectedCheckpointIdx >= 0);
        }
        state.Changed += SyncDelEnabled;
        SyncDelEnabled();
        bar.Add(delCpBtn);

        // 格点吸附 toggle(off 时自由坐标,on 时 0.25 粒度)
        var snapBtn = new Button(() =>
        {
            state.Snap = !state.Snap;
            state.NotifyChanged();
        }) { text = state.Snap ? "◉ 格点吸附 (.25)" : "○ 格点吸附 (.25)" };
        snapBtn.style.marginLeft = 4;
        snapBtn.style.fontSize = 11;
        snapBtn.tooltip = "开启后,新建/移动 checkpoint 都会吸附到 0.25 粒度格点(每格 4 个点)";
        void SyncSnapVisual()
        {
            snapBtn.text = state.Snap ? "◉ 格点吸附 (.25)" : "○ 格点吸附 (.25)";
            snapBtn.style.color = state.Snap ? new Color(0.306f, 0.788f, 0.627f) : new Color(0.706f, 0.706f, 0.706f);
        }
        state.Changed += SyncSnapVisual;
        SyncSnapVisual();
        bar.Add(snapBtn);

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
