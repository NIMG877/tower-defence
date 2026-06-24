using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Map-paint tab. See spec §4.3. Single-field brush with portal two-click mode.
/// Writes to <c>LevelData.MapData</c> via SerializedProperty + Undo.
/// Layout matches PathEditTab: green collapsible header, path-picker-style size row, toolbar, canvas left, brush right.
/// </summary>
public static class MapEditTab
{
    enum PortalMode { Off, SetPortalOut }

    class BrushState
    {
        public enum Tool { Brush, Eraser }
        public Tool tool = Tool.Brush;
        public bool highland;
        public bool canSet;
        public int  passableType;
        public bool deadly;
        public PortalMode portalMode = PortalMode.Off;
        public (int i, int j)? pendingPortalSource;
    }

    public static VisualElement Build(SerializedObject so, BlockMapCache cache, PathEditingState state)
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
        var header = new Label("▸ Map Editing");
        header.style.color = new Color(0.306f, 0.788f, 0.627f);
        header.style.fontSize = 12;
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginBottom = 6;
        root.Add(header);

        // 1) Size row (path-picker 风格)
        root.Add(BuildMapSizeRow(so));

        // 2) Toolbar (path 工具栏风格:删除 + 工具切换 + 重置)
        // brush 实例在 Build 内部创建,toolbar 需要持有它以同步 active 视觉
        var brush = new BrushState();
        var canvasContainer = BuildCanvasContainer(so, cache, brush, state);

        // Build brush panel first so toolbar can grab a direct reference for visibility toggling
        var brushPanel = BuildBrushPanel(brush, state);
        brushPanel.name = "brush-panel-container";

        root.Add(BuildToolbar(so, brush, state, canvasContainer, brushPanel));

        // 3) Split: canvas + 右侧栏
        var split = new VisualElement();
        split.style.flexDirection = FlexDirection.Row;
        split.style.marginTop = 6;
        split.style.flexShrink = 0;

        // 画布容器(flexGrow=1 占满剩余宽度,最小宽 320,高度固定 400)
        canvasContainer.style.flexGrow = 1;
        canvasContainer.style.flexShrink = 1;
        canvasContainer.style.minWidth = 320;
        split.Add(canvasContainer);

        // 右侧栏:固定宽 220,高度 = 画布高度
        var right = new VisualElement();
        right.style.flexDirection = FlexDirection.Column;
        right.style.flexShrink = 0;
        right.style.flexGrow = 0;
        right.style.width = 220;
        right.style.marginLeft = 8;
        right.style.height = ViewTransform.CanvasHeight;
        right.style.overflow = Overflow.Hidden;

        // Brush panel (top, flexGrow, 仅 Brush 工具时可见)
        brushPanel.style.flexGrow = 1;
        brushPanel.style.flexShrink = 1;
        brushPanel.style.minHeight = 60;
        brushPanel.style.overflow = Overflow.Hidden;
        right.Add(brushPanel);

        // Selected cell panel (bottom, flexShrink 0)
        var cellPanel = BuildSelectedCellPanel(state);
        cellPanel.style.flexShrink = 0;
        cellPanel.style.marginTop = 4;
        right.Add(cellPanel);

        split.Add(right);
        root.Add(split);

        root.style.overflow = Overflow.Hidden;

        // 初始 fit 视图 (在 BuildCanvasContainer 之后调用,canvas 已就绪)
        if (state.Cache != null)
            state.View = ViewTransform.Fit(state.Cache.ISize, state.Cache.JSize);

        state.NotifyChanged();

        return root;
    }

    static VisualElement BuildMapSizeRow(SerializedObject so)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 4;
        row.style.flexShrink = 0;

        var label = new Label("▸ Map size");
        label.style.color = new Color(0.611f, 0.863f, 0.996f);
        label.style.minWidth = 90;
        label.style.fontSize = 11;
        label.style.flexShrink = 0;
        row.Add(label);

        // 用 Unity 自带 label 的 IntegerField,Unity 内部已经处理好 label + 输入框的排版
        // flexGrow:1 + minWidth:0 让两个 field 在剩余空间里均分;label 宽度不算进去
        var iSizeField = new IntegerField("iSize (rows)") { value = so.FindProperty("iSize").intValue };
        iSizeField.style.flexGrow = 1;
        iSizeField.style.flexShrink = 1;
        iSizeField.style.minWidth = 0;
        iSizeField.style.marginLeft = 4;
        var iProp = so.FindProperty("iSize");
        iSizeField.RegisterValueChangedCallback(evt =>
        {
            Undo.RecordObject(so.targetObject, "Change iSize");
            iProp.intValue = Mathf.Max(0, evt.newValue);
            so.ApplyModifiedProperties();
        });
        row.Add(iSizeField);

        var jSizeField = new IntegerField("jSize (cols)") { value = so.FindProperty("jSize").intValue };
        jSizeField.style.flexGrow = 1;
        jSizeField.style.flexShrink = 1;
        jSizeField.style.minWidth = 0;
        jSizeField.style.marginLeft = 4;
        var jProp = so.FindProperty("jSize");
        jSizeField.RegisterValueChangedCallback(evt =>
        {
            Undo.RecordObject(so.targetObject, "Change jSize");
            jProp.intValue = Mathf.Max(0, evt.newValue);
            so.ApplyModifiedProperties();
        });
        row.Add(jSizeField);

        return row;
    }

    static VisualElement BuildToolbar(SerializedObject so, BrushState brush, PathEditingState state, VisualElement canvasContainer, VisualElement brushPanel)
    {
        var bar = new VisualElement();
        bar.style.flexDirection = FlexDirection.Row;
        bar.style.backgroundColor = new Color(0.157f, 0.157f, 0.157f);
        bar.style.paddingTop = 4; bar.style.paddingBottom = 4;
        bar.style.paddingLeft = 6; bar.style.paddingRight = 6;
        bar.style.alignItems = Align.Center;
        bar.style.borderTopLeftRadius = 3; bar.style.borderTopRightRadius = 3;
        bar.style.borderBottomLeftRadius = 3; bar.style.borderBottomRightRadius = 3;

        // ✕ 删除选中点(当前悬停格的 MapData 条目)
        var delCellBtn = new Button(() =>
        {
            if (state.HoverCell.HasValue) EraseEntry(so, state, canvasContainer, state.HoverCell.Value);
        }) { text = "✕ 删除选中点" };
        delCellBtn.style.marginLeft = 4;
        delCellBtn.style.fontSize = 11;
        void SyncDelEnabled()
        {
            delCellBtn.SetEnabled(state.HoverCell.HasValue);
        }
        state.Changed += SyncDelEnabled;
        SyncDelEnabled();
        bar.Add(delCellBtn);

        var sep1 = new VisualElement();
        sep1.style.width = 1; sep1.style.height = 16;
        sep1.style.backgroundColor = new Color(0.314f, 0.314f, 0.314f);
        sep1.style.marginLeft = 6; sep1.style.marginRight = 6;
        bar.Add(sep1);

        var toolLabel = new Label("工具:");
        toolLabel.style.fontSize = 11;
        toolLabel.style.color = new Color(0.706f, 0.706f, 0.706f);
        bar.Add(toolLabel);

        // 工具按钮:圆形符号 + 文字色(跟格点吸附的 ◉/○ 视觉一致)
        //   active  → ◉ 画刷/笔擦 + 绿色字 (0.306, 0.788, 0.627)
        //   inactive→ ○ 画刷/笔擦 + 灰色字 (0.706, 0.706, 0.706)
        var tools = new[] { "画刷", "笔擦" };
        var toolButtons = new Button[tools.Length];
        for (int k = 0; k < tools.Length; k++)
        {
            int captured = k;
            var btn = new Button { text = $"○ {tools[k]}" };
            btn.clicked += () =>
            {
                brush.tool = (BrushState.Tool)captured;
                state.NotifyChanged();
                // 工具切换后,canvas 视觉也需要刷新(光标形状、提示文本等)
                canvasContainer.MarkDirtyRepaint();
            };
            btn.style.marginLeft = 4;
            btn.style.fontSize = 11;
            toolButtons[k] = btn;
            bar.Add(btn);
        }
        void SyncToolVisual()
        {
            int active = (int)brush.tool;
            for (int k = 0; k < toolButtons.Length; k++)
            {
                bool on = (k == active);
                toolButtons[k].text = on ? $"◉ {tools[k]}" : $"○ {tools[k]}";
                toolButtons[k].style.color = on
                    ? new Color(0.306f, 0.788f, 0.627f)
                    : new Color(0.706f, 0.706f, 0.706f);
            }
            // Brush 工具隐藏时,brush 面板也同步隐藏(避免空白占位)
            brushPanel.style.display = brush.tool == BrushState.Tool.Brush ? DisplayStyle.Flex : DisplayStyle.None;
        }
        state.Changed += SyncToolVisual;
        SyncToolVisual();

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

    static VisualElement BuildCanvasContainer(SerializedObject so, BlockMapCache cache, BrushState brush, PathEditingState state)
    {
        var canvas = new VisualElement();
        canvas.style.width = ViewTransform.CanvasWidth;
        canvas.style.height = ViewTransform.CanvasHeight;
        canvas.style.backgroundColor = new Color(0.078f, 0.078f, 0.094f);
        canvas.style.borderTopLeftRadius = 3;
        canvas.style.borderTopRightRadius = 3;
        canvas.style.borderBottomLeftRadius = 3;
        canvas.style.borderBottomRightRadius = 3;
        canvas.style.borderLeftWidth = 1;
        canvas.style.borderRightWidth = 1;
        canvas.style.borderTopWidth = 1;
        canvas.style.borderBottomWidth = 1;
        canvas.style.borderLeftColor = new Color(0.235f, 0.235f, 0.275f);
        canvas.style.borderRightColor = new Color(0.235f, 0.235f, 0.275f);
        canvas.style.borderTopColor = new Color(0.235f, 0.235f, 0.275f);
        canvas.style.borderBottomColor = new Color(0.235f, 0.235f, 0.275f);
        canvas.style.overflow = Overflow.Hidden;
        canvas.style.position = Position.Relative;

        // Status label (bottom-left)
        var status = new Label("(i, j): -");
        status.name = "canvas-status";
        status.style.position = Position.Absolute;
        status.style.bottom = 4; status.style.left = 8;
        status.style.fontSize = 10;
        status.style.color = new Color(0.55f, 0.55f, 0.55f);
        canvas.Add(status);

        // Hint label (bottom-right)
        var hint = new Label("左键拖:画/擦 · 右键拖:缩放 · 中键拖:平移");
        hint.name = "canvas-hint";
        hint.style.position = Position.Absolute;
        hint.style.bottom = 4; hint.style.right = 8;
        hint.style.fontSize = 10;
        hint.style.color = new Color(0.55f, 0.55f, 0.55f);
        canvas.Add(hint);

        // Draw blocks
        canvas.generateVisualContent = ctx =>
        {
            if (state.Cache == null) return;
            MapCanvasView.DrawBlocks(ctx, state);
        };

        // Manipulator
        var manip = new MapEditManipulator(so, cache, brush, status, state, () => canvas.MarkDirtyRepaint());
        canvas.AddManipulator(manip);

        // Repaint on state change / undo
        state.Changed += () => canvas.MarkDirtyRepaint();
        Undo.undoRedoPerformed += () => canvas.MarkDirtyRepaint();

        // Cleanup on detach
        canvas.RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            state.HoverCell = null;
            state.Changed -= () => canvas.MarkDirtyRepaint();
            Undo.undoRedoPerformed -= () => canvas.MarkDirtyRepaint();
        });

        return canvas;
    }

    static VisualElement BuildBrushPanel(BrushState brush, PathEditingState state)
    {
        var panel = new VisualElement();
        panel.style.flexDirection = FlexDirection.Column;
        panel.style.backgroundColor = new Color(0.118f, 0.118f, 0.118f);
        panel.style.borderTopLeftRadius = 3; panel.style.borderTopRightRadius = 3;
        panel.style.borderBottomLeftRadius = 3; panel.style.borderBottomRightRadius = 3;
        panel.style.borderLeftWidth = 1; panel.style.borderRightWidth = 1;
        panel.style.borderTopWidth = 1; panel.style.borderBottomWidth = 1;
        panel.style.borderLeftColor = new Color(0.235f, 0.235f, 0.275f);
        panel.style.borderRightColor = new Color(0.235f, 0.235f, 0.275f);
        panel.style.borderTopColor = new Color(0.235f, 0.235f, 0.275f);
        panel.style.borderBottomColor = new Color(0.235f, 0.235f, 0.275f);
        panel.style.paddingTop = 4; panel.style.paddingBottom = 4;
        panel.style.paddingLeft = 6; panel.style.paddingRight = 6;
        panel.style.marginBottom = 6;

        // Header row: ▸ + title
        var headerRow = new VisualElement();
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.alignItems = Align.Center;
        headerRow.style.marginBottom = 4;
        headerRow.style.flexShrink = 0;

        var prefix = new Label("▸");
        prefix.style.color = new Color(0.611f, 0.863f, 0.996f);
        prefix.style.fontSize = 11;
        prefix.style.flexShrink = 0;
        prefix.style.marginRight = 4;
        headerRow.Add(prefix);

        var title = new Label("画刷 (highland, canSet, passable, deadly, portal)");
        title.style.color = new Color(0.611f, 0.863f, 0.996f);
        title.style.fontSize = 11;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.flexShrink = 1;
        headerRow.Add(title);
        panel.Add(headerRow);

        panel.Add(MakeToggle("Highland", brush.highland, v => brush.highland = v));
        panel.Add(MakeToggle("CanSet",   brush.canSet,   v => brush.canSet = v));

        // PassableType:行内布局(Label + 裸 IntegerField)与 MakeToggle 一致
        // 注意 row 必须 flexShrink:0 — 在 Column 父容器里,flexGrow:1 会让子节点垂直拉伸
        // 横向拉伸靠 IntegerField 自己的 flexGrow:1(在 row 的主轴 = 水平)
        var passableRow = new VisualElement();
        passableRow.style.flexDirection = FlexDirection.Row;
        passableRow.style.alignItems = Align.Center;
        passableRow.style.flexShrink = 0;
        var passableLbl = new Label("PassableType");
        passableLbl.style.minWidth = 90;
        passableLbl.style.fontSize = 11;
        passableLbl.style.flexShrink = 0;
        passableRow.Add(passableLbl);
        var passableField = new IntegerField { value = brush.passableType };
        passableField.style.flexGrow = 1;
        passableField.style.flexShrink = 1;
        passableField.style.minWidth = 0;
        passableField.RegisterValueChangedCallback(evt => brush.passableType = Mathf.Clamp(evt.newValue, 0, 3));
        passableRow.Add(passableField);
        panel.Add(passableRow);

        panel.Add(MakeToggle("Deadly", brush.deadly, v => brush.deadly = v));

        // Portal:同样行内布局,row flexShrink:0 防垂直拉伸
        var portalRow = new VisualElement();
        portalRow.style.flexDirection = FlexDirection.Row;
        portalRow.style.alignItems = Align.Center;
        portalRow.style.flexShrink = 0;
        var portalLbl = new Label("Portal");
        portalLbl.style.minWidth = 90;
        portalLbl.style.fontSize = 11;
        portalLbl.style.flexShrink = 0;
        portalRow.Add(portalLbl);
        var portalEnum = new EnumField(brush.portalMode);
        portalEnum.style.flexGrow = 1;
        portalEnum.style.flexShrink = 1;
        portalEnum.style.minWidth = 0;
        portalEnum.RegisterValueChangedCallback(evt => brush.portalMode = (PortalMode)evt.newValue);
        portalRow.Add(portalEnum);
        panel.Add(portalRow);

        var hint = new Label("提示:左键按住拖动可连续画/擦;portal 两段式;右键拖=缩放;中键拖=平移。");
        hint.style.fontSize = 10;
        hint.style.color = new Color(0.55f, 0.55f, 0.55f);
        hint.style.marginTop = 8;
        hint.style.whiteSpace = WhiteSpace.Normal;
        panel.Add(hint);

        return panel;
    }

    static VisualElement BuildSelectedCellPanel(PathEditingState state)
    {
        var panel = new VisualElement();
        panel.style.flexDirection = FlexDirection.Column;
        panel.style.backgroundColor = new Color(0.118f, 0.118f, 0.118f);
        panel.style.borderTopLeftRadius = 3; panel.style.borderTopRightRadius = 3;
        panel.style.borderBottomLeftRadius = 3; panel.style.borderBottomRightRadius = 3;
        panel.style.borderLeftWidth = 1; panel.style.borderRightWidth = 1;
        panel.style.borderTopWidth = 1; panel.style.borderBottomWidth = 1;
        panel.style.borderLeftColor = new Color(0.235f, 0.235f, 0.275f);
        panel.style.borderRightColor = new Color(0.235f, 0.235f, 0.275f);
        panel.style.borderTopColor = new Color(0.235f, 0.235f, 0.275f);
        panel.style.borderBottomColor = new Color(0.235f, 0.235f, 0.275f);
        panel.style.paddingTop = 4; panel.style.paddingBottom = 4;
        panel.style.paddingLeft = 6; panel.style.paddingRight = 6;

        var header = new Label("▸ Cell");
        header.style.color = new Color(0.611f, 0.863f, 0.996f);
        header.style.fontSize = 11;
        header.style.marginBottom = 4;
        panel.Add(header);

        var posLabel = new Label("(i, j): -");
        posLabel.name = "cell-pos";
        posLabel.style.fontSize = 10;
        panel.Add(posLabel);

        var highlandLbl = new Label("highland: -");
        highlandLbl.name = "cell-highland";
        highlandLbl.style.fontSize = 10;
        panel.Add(highlandLbl);

        var canSetLbl = new Label("canSet: -");
        canSetLbl.name = "cell-canset";
        canSetLbl.style.fontSize = 10;
        panel.Add(canSetLbl);

        var passableLbl = new Label("passable: -");
        passableLbl.name = "cell-passable";
        passableLbl.style.fontSize = 10;
        panel.Add(passableLbl);

        var deadlyLbl = new Label("deadly: -");
        deadlyLbl.name = "cell-deadly";
        deadlyLbl.style.fontSize = 10;
        panel.Add(deadlyLbl);

        var portalLbl = new Label("portal: -");
        portalLbl.name = "cell-portal";
        portalLbl.style.fontSize = 10;
        panel.Add(portalLbl);

        // 同步悬停格的内容:有 entry 时显示字段值,无 entry 或悬停为 null 时显示 (empty)
        void Bind()
        {
            if (!state.HoverCell.HasValue || state.Cache == null || state.Cache.Blocks == null)
            {
                posLabel.text = "(i, j): -";
                highlandLbl.text = "highland: -";
                canSetLbl.text = "canSet: -";
                passableLbl.text = "passable: -";
                deadlyLbl.text = "deadly: -";
                portalLbl.text = "portal: -";
                return;
            }
            var (i, j) = state.HoverCell.Value;
            if (i < 0 || j < 0 || i >= state.Cache.ISize || j >= state.Cache.JSize ||
                state.Cache.HasEntry == null || !state.Cache.HasEntry[i, j])
            {
                posLabel.text = $"(i, j): ({i}, {j})";
                highlandLbl.text = "highland: (empty)";
                canSetLbl.text = "canSet: (empty)";
                passableLbl.text = "passable: (empty)";
                deadlyLbl.text = "deadly: (empty)";
                portalLbl.text = "portal: (empty)";
                return;
            }
            var e = state.Cache.Blocks[i, j];
            posLabel.text = $"(i, j): ({i}, {j})";
            highlandLbl.text = $"highland: {e.highland}";
            canSetLbl.text = $"canSet: {e.canSet}";
            passableLbl.text = $"passable: {e.passableType}";
            deadlyLbl.text = $"deadly: {e.deadly}";
            if (e.portalOutI >= 0 && e.portalOutJ >= 0)
                portalLbl.text = $"portal: -> ({e.portalOutI}, {e.portalOutJ})";
            else
                portalLbl.text = "portal: -";
        }
        state.Changed += Bind;
        Bind();

        return panel;
    }

    static VisualElement MakeToggle(string label, bool initial, System.Action<bool> onChange)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;

        var lbl = new Label(label);
        lbl.style.minWidth = 90;
        lbl.style.fontSize = 11;
        row.Add(lbl);

        var t = new Toggle { value = initial };
        t.style.marginLeft = 0;
        t.RegisterValueChangedCallback(evt => onChange(evt.newValue));
        row.Add(t);

        return row;
    }

    static void EraseEntry(SerializedObject so, PathEditingState state, VisualElement canvasContainer, (int i, int j) cell)
    {
        Undo.RecordObject(so.targetObject, "Erase Block");
        var mapData = so.FindProperty("MapData");
        int idx = MapEditManipulator.FindEntryIndex(mapData, cell);
        if (idx < 0) return; // 已为空,no-op
        mapData.DeleteArrayElementAtIndex(idx);
        so.ApplyModifiedProperties();
        MapEditManipulator.RefreshCacheFromSOStatic(so, state.Cache);
        state.NotifyChanged();
        canvasContainer.MarkDirtyRepaint();
    }

    // === Manipulator ===
    class MapEditManipulator : MouseManipulator
    {
        readonly SerializedObject _so;
        readonly BlockMapCache _cache;
        readonly BrushState _brush;
        readonly Label _status;
        readonly PathEditingState _state;
        readonly System.Action _repaint;

        public MapEditManipulator(SerializedObject so, BlockMapCache cache,
            BrushState brush, Label status, PathEditingState state, System.Action repaint)
        {
            _so = so; _cache = cache; _brush = brush;
            _status = status; _state = state; _repaint = repaint;
        }

        bool _rightDragging;
        bool _midDragging;
        bool _leftDragging;          // 画刷/笔擦 拖动绘制中
        Vector2 _rightDownLocal;
        Vector2 _midDownLocal;
        (int i, int j)? _lastPaintedCell;  // 拖动期间上次应用的格子(去重)
        int _undoGroup;              // 拖动开始时的 undo group,MouseUp 时 collapse 整段

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<MouseDownEvent>(OnMouseDown);
            target.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            target.RegisterCallback<MouseUpEvent>(OnMouseUp);
            target.RegisterCallback<MouseLeaveEvent>(OnMouseLeave);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
            target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
            target.UnregisterCallback<MouseUpEvent>(OnMouseUp);
            target.UnregisterCallback<MouseLeaveEvent>(OnMouseLeave);
        }

        (int i, int j)? ScreenToCell(Vector2 local)
        {
            if (_cache.Blocks == null) return null;
            var world = _state.View.ScreenToWorld(local, _cache.ISize, _cache.JSize);
            int i = (int)(world.y + 0.5f);
            int j = (int)(world.x + 0.5f);
            if (i < 0 || j < 0 || i >= _cache.ISize || j >= _cache.JSize) return null;
            return (i, j);
        }

        void OnMouseMove(MouseMoveEvent evt)
        {
            var cell = ScreenToCell(evt.localMousePosition);
            _status.text = cell.HasValue ? $"(i, j): ({cell.Value.i}, {cell.Value.j})" : "(i, j): -";
            // 同步 hover 状态(供 toolbar 的删除按钮使用)
            if (!NullableEquals(cell, _state.HoverCell))
            {
                _state.HoverCell = cell;
                _state.NotifyChanged();
            }

            // Right-drag: zoom (anchor at right-down cursor)
            if (_rightDragging && _state.Cache != null)
            {
                float delta = -evt.mouseDelta.y;
                float newZoom = Mathf.Clamp(_state.View.Zoom * Mathf.Pow(1.01f, delta), 0.25f, 16f);
                if (!Mathf.Approximately(newZoom, _state.View.Zoom))
                {
                    var worldBefore = _state.View.ScreenToWorld(_rightDownLocal, _state.Cache.ISize, _state.Cache.JSize);
                    _state.View.Zoom = newZoom;
                    var worldAfter = _state.View.ScreenToWorld(_rightDownLocal, _state.Cache.ISize, _state.Cache.JSize);
                    _state.View.Offset += worldBefore - worldAfter;
                    _repaint();
                }
            }

            // Middle-drag: pan (delta form, matches EditorPathManipulator)
            if (_midDragging && _state.Cache != null)
            {
                float unitX = ViewTransform.CanvasWidth / _state.Cache.JSize;
                float unitY = ViewTransform.CanvasHeight / _state.Cache.ISize;
                _state.View.Offset -= new Vector2(
                    evt.mouseDelta.x / (_state.View.Zoom * unitX),
                    -evt.mouseDelta.y / (_state.View.Zoom * unitY));
                _repaint();
            }

            // Left-drag: 连续画/擦(去重:只在新进入一个 cell 时才应用)
            if (_leftDragging && cell.HasValue && !NullableEquals(cell, _lastPaintedCell))
            {
                if (_brush.tool == BrushState.Tool.Eraser) EraseEntry(cell.Value);
                else                                       ApplyBrush(cell.Value);
                _lastPaintedCell = cell;
            }
        }

        void OnMouseLeave(MouseLeaveEvent evt)
        {
            if (_state.HoverCell.HasValue)
            {
                _state.HoverCell = null;
                _state.NotifyChanged();
            }
        }

        static bool NullableEquals((int i, int j)? a, (int i, int j)? b)
        {
            if (a.HasValue != b.HasValue) return false;
            if (!a.HasValue) return true;
            return a.Value.i == b.Value.i && a.Value.j == b.Value.j;
        }

        void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.button == (int)MouseButton.RightMouse)
            {
                _rightDragging = true;
                _rightDownLocal = evt.localMousePosition;
                target.CaptureMouse();
                return;
            }
            if (evt.button == (int)MouseButton.MiddleMouse)
            {
                _midDragging = true;
                _midDownLocal = evt.localMousePosition;
                target.CaptureMouse();
                return;
            }
            if (evt.button != 0) return;

            var c = ScreenToCell(evt.localMousePosition);
            if (!c.HasValue) return;

            // Eraser 模式:擦除该格的 MapData 条目(支持按住拖动连续擦)
            if (_brush.tool == BrushState.Tool.Eraser)
            {
                _leftDragging = true;
                target.CaptureMouse();
                _undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Erase Blocks");
                EraseEntry(c.Value);
                _lastPaintedCell = c;
                return;
            }

            // Brush 模式:portal 两段式(单次点击,不进入拖动)
            if (_brush.portalMode == PortalMode.SetPortalOut)
            {
                if (!_brush.pendingPortalSource.HasValue)
                {
                    _brush.pendingPortalSource = c;
                }
                else
                {
                    var src = _brush.pendingPortalSource.Value;
                    var dst = c.Value;
                    SetPortalOut(src, dst);
                    _brush.pendingPortalSource = null;
                }
                _repaint();
                return;
            }

            // Brush 默认模式:按住左键可连续画
            _leftDragging = true;
            target.CaptureMouse();
            _undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Paint Blocks");
            ApplyBrush(c.Value);
            _lastPaintedCell = c;
        }

        void OnMouseUp(MouseUpEvent evt)
        {
            if (evt.button == (int)MouseButton.RightMouse && _rightDragging)
            {
                _rightDragging = false;
                target.ReleaseMouse();
                return;
            }
            if (evt.button == (int)MouseButton.MiddleMouse && _midDragging)
            {
                _midDragging = false;
                target.ReleaseMouse();
                return;
            }
            if (evt.button == 0 && _leftDragging)
            {
                _leftDragging = false;
                target.ReleaseMouse();
                // 把整段拖动合并成一个 undo 条目(默认每格一个 undo,体感割裂)
                Undo.CollapseUndoOperations(_undoGroup);
                _lastPaintedCell = null;
            }
        }

        // === SerializedProperty writes ===
        SerializedProperty MapDataProp() => _so.FindProperty("MapData");

        void EraseEntry((int i, int j) cell)
        {
            Undo.RecordObject(_so.targetObject, "Erase Block");
            var mapData = MapDataProp();
            int idx = FindEntryIndex(mapData, cell);
            if (idx < 0) return; // 已为空,no-op
            mapData.DeleteArrayElementAtIndex(idx);
            _so.ApplyModifiedProperties();
            RefreshCacheFromSO();
            _state.NotifyChanged();
        }

        void ApplyBrush((int i, int j) cell)
        {
            Undo.RecordObject(_so.targetObject, "Paint Block");
            var mapData = MapDataProp();
            int idx = FindEntryIndex(mapData, cell);
            if (idx < 0)
            {
                idx = mapData.arraySize;
                mapData.InsertArrayElementAtIndex(idx);
            }
            var entry = mapData.GetArrayElementAtIndex(idx);
            entry.FindPropertyRelative("i").intValue = cell.i;
            entry.FindPropertyRelative("j").intValue = cell.j;
            entry.FindPropertyRelative("highland").boolValue = _brush.highland;
            entry.FindPropertyRelative("canSet").boolValue = _brush.canSet;
            entry.FindPropertyRelative("passableType").intValue = _brush.passableType;
            entry.FindPropertyRelative("deadly").boolValue = _brush.deadly;
            // portalOutI/J unchanged on regular paint
            _so.ApplyModifiedProperties();
            RefreshCacheFromSO();
            _state.NotifyChanged();
        }

        void SetPortalOut((int i, int j) src, (int i, int j) dst)
        {
            Undo.RecordObject(_so.targetObject, "Set Portal");
            var mapData = MapDataProp();
            int idx = FindEntryIndex(mapData, src);
            if (idx < 0)
            {
                idx = mapData.arraySize;
                mapData.InsertArrayElementAtIndex(idx);
                var e = mapData.GetArrayElementAtIndex(idx);
                e.FindPropertyRelative("i").intValue = src.i;
                e.FindPropertyRelative("j").intValue = src.j;
            }
            var entry = mapData.GetArrayElementAtIndex(idx);
            entry.FindPropertyRelative("portalOutI").intValue = dst.i;
            entry.FindPropertyRelative("portalOutJ").intValue = dst.j;
            _so.ApplyModifiedProperties();
            RefreshCacheFromSO();
            _state.NotifyChanged();
        }

        public static int FindEntryIndex(SerializedProperty mapData, (int i, int j) cell)
        {
            for (int k = 0; k < mapData.arraySize; k++)
            {
                var e = mapData.GetArrayElementAtIndex(k);
                if (e.FindPropertyRelative("i").intValue == cell.i &&
                    e.FindPropertyRelative("j").intValue == cell.j) return k;
            }
            return -1;
        }

        void RefreshCacheFromSO()
        {
            RefreshCacheFromSOStatic(_so, _cache);
        }

        public static void RefreshCacheFromSOStatic(SerializedObject so, BlockMapCache cache)
        {
            // Rebuild Blocks[,] from the SO. Simple: re-read each entry.
            if (cache.Blocks == null) return;
            // Reset to default first
            for (int i = 0; i < cache.ISize; i++)
            for (int j = 0; j < cache.JSize; j++)
            {
                cache.Blocks[i, j] = default;
                if (cache.HasEntry != null) cache.HasEntry[i, j] = false;
            }

            var mapData = so.FindProperty("MapData");
            for (int k = 0; k < mapData.arraySize; k++)
            {
                var e = mapData.GetArrayElementAtIndex(k);
                int i = e.FindPropertyRelative("i").intValue;
                int j = e.FindPropertyRelative("j").intValue;
                if (i < 0 || j < 0 || i >= cache.ISize || j >= cache.JSize) continue;
                cache.Blocks[i, j] = new BlockDataEntry
                {
                    i = i, j = j,
                    highland = e.FindPropertyRelative("highland").boolValue,
                    canSet = e.FindPropertyRelative("canSet").boolValue,
                    passableType = e.FindPropertyRelative("passableType").intValue,
                    deadly = e.FindPropertyRelative("deadly").boolValue,
                    portalOutI = e.FindPropertyRelative("portalOutI").intValue,
                    portalOutJ = e.FindPropertyRelative("portalOutJ").intValue,
                    portalColor = e.FindPropertyRelative("portalColor").colorValue,
                };
                if (cache.HasEntry != null) cache.HasEntry[i, j] = true;
            }
        }
    }
}
