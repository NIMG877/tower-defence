using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class PathEditTab
{
    public static VisualElement Build(SerializedObject so, PathEditingState state)
    {
        var root = new VisualElement();
        root.style.backgroundColor = EditorTheme.TabBg;
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
        root.style.borderLeftColor = EditorTheme.AccentGreen;
        root.style.borderRightColor = EditorTheme.AccentGreen;
        root.style.borderTopColor = EditorTheme.AccentGreen;
        root.style.borderBottomColor = EditorTheme.AccentGreen;

        // Header
        var header = new Label("▸ Path Editing");
        header.style.color = EditorTheme.AccentGreen;
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

        var queryView = PathLengthQueryView.Build(so, state);
        queryView.style.flexShrink = 0;
        queryView.style.marginTop = 4;
        right.Add(queryView);

        var detailView = CheckpointDetailView.Build(so, state);
        detailView.style.flexShrink = 0;
        detailView.style.marginTop = 4;
        right.Add(detailView);

        split.Add(right);
        root.Add(split);

        root.style.overflow = Overflow.Hidden;

        // 视图 Fit 由 MapEditorSection.Build 在 inspector 首次打开时统一处理,
        // 这里不再 Fit — 切到本 tab 时保留用户已调整的视图。

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
        label.style.color = EditorTheme.SubHeader;
        label.style.minWidth = 90;
        label.style.fontSize = 11;
        row.Add(label);

        var pathsProp = so.FindProperty("Paths");

        // 工具:用 path 索引拼显示文本(格式 "#{i}: {Name}",空 Name 退化为 "Path {i}")
        string MakeChoice(int i)
        {
            var nameProp = pathsProp.GetArrayElementAtIndex(i).FindPropertyRelative("Name");
            var name = nameProp != null && !string.IsNullOrEmpty(nameProp.stringValue)
                ? nameProp.stringValue
                : $"Path {i}";
            return $"#{i}: {name}";
        }

        var choices = new List<string>();
        for (int i = 0; i < pathsProp.arraySize; i++) choices.Add(MakeChoice(i));
        if (choices.Count == 0) choices.Add("(暂无路径)");

        int initialIdx = Mathf.Clamp(state.SelectedPathIdx, 0, Mathf.Max(0, choices.Count - 1));
        var popup = new PopupField<string>(choices, initialIdx);
        // 占满 label 与按钮之间的剩余空间;minWidth=0 让 flex 允许压缩到比文本自然宽更小
        popup.style.flexGrow = 1;
        popup.style.flexShrink = 1;
        popup.style.minWidth = 0;
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

        // 让 row 不被父级 split 占用的列宽挤压,确保 popup 拿到合理空间
        row.style.flexShrink = 0;

        // 路径名 TextField 已迁到 CheckpointListView 标题行;此处保留 popup 文字刷新
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
            // 不要 RemoveFromHierarchy:会破坏事件订阅,UI 立即消失,只能等 inspector 重建才恢复
            // state.NotifyChanged() 已经触发 RebuildPopup,popup 会刷新到新 path
        }) { text = "+ 新建" };
        newBtn.style.marginLeft = 4;
        row.Add(newBtn);

        var delBtn = new Button(() =>
        {
            if (state.SelectedPathIdx < 0 || state.SelectedPathIdx >= pathsProp.arraySize) return;
            string pathName = MakeChoice(state.SelectedPathIdx);
            if (!EditorUtility.DisplayDialog("删除 Path", $"确认删除 {pathName}?该路径下的所有 Checkpoint / WaitTime 都会丢失。", "删除", "取消"))
                return;
            Undo.RecordObject(so.targetObject, "Delete Path");
            pathsProp.DeleteArrayElementAtIndex(state.SelectedPathIdx);
            so.ApplyModifiedProperties();
            state.SelectedPathIdx = Mathf.Max(0, state.SelectedPathIdx - 1);
            state.SelectedCheckpointIdx = -1;
            state.NotifyChanged();
        }) { text = "删除" };
        delBtn.style.marginLeft = 4;
        delBtn.style.backgroundColor = EditorTheme.Danger;
        row.Add(delBtn);

        return row;
    }

    static VisualElement BuildToolbar(SerializedObject so, PathEditingState state, VisualElement root)
    {
        var bar = new VisualElement();
        bar.style.flexDirection = FlexDirection.Row;
        bar.style.backgroundColor = EditorTheme.ToolbarBg;
        bar.style.paddingTop = 4; bar.style.paddingBottom = 4;
        bar.style.paddingLeft = 6; bar.style.paddingRight = 6;
        bar.style.alignItems = Align.Center;
        bar.style.borderTopLeftRadius = 3; bar.style.borderTopRightRadius = 3;
        bar.style.borderBottomLeftRadius = 3; bar.style.borderBottomRightRadius = 3;

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

        // 格点吸附 toggle(off 时自由坐标,on 时 entityR 粒度,与实体半径对齐)
        float entityR = state.Cache.EntityR;
        var snapBtn = EditorTabShell.MakeToggleButton(state, $"格点吸附 ({entityR:0.##})",
            isActive: () => state.Snap,
            onClick: () => { state.Snap = !state.Snap; state.NotifyChanged(); });
        snapBtn.style.marginLeft = 4;
        snapBtn.tooltip = $"开启后,新建/移动 checkpoint 都会吸附到 {entityR:0.##} 粒度格点(与实体半径对齐)";
        bar.Add(snapBtn);

        bar.Add(EditorTabShell.MakeVerticalSeparator());

        var moveLabel = new Label("moveMethod:");
        moveLabel.style.fontSize = 11;
        moveLabel.style.color = EditorTheme.MutedText;
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
                btn.style.backgroundColor = EditorTheme.AccentGreen;
                btn.style.color = Color.black;
            };
            btn.style.marginLeft = 2;
            btn.style.fontSize = 11;
            if (k == state.MoveMethod)
            {
                btn.style.backgroundColor = EditorTheme.AccentGreen;
                btn.style.color = Color.black;
            }
            bar.Add(btn);
        }

        bar.Add(EditorTabShell.MakeVerticalSeparator());

        bar.Add(EditorTabShell.MakeResetButton(state));

        return bar;
    }
}