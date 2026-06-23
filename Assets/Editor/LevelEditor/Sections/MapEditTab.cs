using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Map-paint tab. See spec §4.3. Single-field brush with portal two-click mode.
/// Writes to <c>LevelData.MapData</c> via SerializedProperty + Undo.
/// Layout matches PathEditTab: green collapsible header, canvas left, brush right.
/// </summary>
public static class MapEditTab
{
    enum PortalMode { Off, SetPortalOut }

    class BrushState
    {
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

        // Toolbar
        root.Add(BuildToolbar(state));

        // Split: canvas (弹性宽) + 右侧固定宽栏
        // 初始 fit 视图 (BEFORE BuildCanvasContainer so canvas reads a valid Zoom)
        if (state.Cache != null)
            state.View = ViewTransform.Fit(state.Cache.ISize, state.Cache.JSize);

        var split = new VisualElement();
        split.style.flexDirection = FlexDirection.Row;
        split.style.marginTop = 6;
        split.style.flexShrink = 0;

        // 画布容器(flexGrow=1 占满剩余宽度,最小宽 320,高度固定 400)
        var canvasContainer = BuildCanvasContainer(so, cache, state);
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

        // Right panel order: Size (top, fixed) + Brush (middle, flexGrow) + Cell (bottom, fixed)
        var sizePanel = BuildSizePanel(so);
        sizePanel.style.flexShrink = 0;
        sizePanel.style.marginBottom = 4;
        right.Add(sizePanel);

        // Brush panel (middle, flexGrow)
        var brush = new BrushState();
        var brushPanel = BuildBrushPanel(brush, state, canvasContainer);
        brushPanel.style.flexGrow = 1;
        brushPanel.style.flexShrink = 1;
        brushPanel.style.minHeight = 100;
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

        state.NotifyChanged();

        return root;
    }

    static VisualElement BuildToolbar(PathEditingState state)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 4;

        var resetBtn = new Button(() =>
        {
            if (state.Cache != null)
                state.View = ViewTransform.Fit(state.Cache.ISize, state.Cache.JSize);
            state.NotifyChanged();
        }) { text = "↻ 重置视图" };
        resetBtn.style.fontSize = 11;
        row.Add(resetBtn);

        return row;
    }

    static VisualElement BuildCanvasContainer(SerializedObject so, BlockMapCache cache, PathEditingState state)
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
        var hint = new Label("左键:画刷 / portal源 · 右键拖:缩放 · 中键拖:平移");
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
        var brush = new BrushState();
        var manip = new MapEditManipulator(so, cache, brush, status, state, () => canvas.MarkDirtyRepaint());
        canvas.AddManipulator(manip);

        // Repaint on state change / undo
        state.Changed += () => canvas.MarkDirtyRepaint();
        Undo.undoRedoPerformed += () => canvas.MarkDirtyRepaint();

        // Cleanup on detach
        canvas.RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            state.Changed -= () => canvas.MarkDirtyRepaint();
            Undo.undoRedoPerformed -= () => canvas.MarkDirtyRepaint();
        });

        return canvas;
    }

    static VisualElement BuildBrushPanel(BrushState brush, PathEditingState state, VisualElement canvasContainer)
    {
        var panel = new VisualElement();
        panel.style.flexDirection = FlexDirection.Column;
        panel.style.backgroundColor = new Color(0.078f, 0.078f, 0.094f);
        panel.style.borderTopLeftRadius = 3;
        panel.style.borderTopRightRadius = 3;
        panel.style.borderBottomLeftRadius = 3;
        panel.style.borderBottomRightRadius = 3;
        panel.style.paddingTop = 4; panel.style.paddingBottom = 4;
        panel.style.paddingLeft = 6; panel.style.paddingRight = 6;

        var title = new Label("画刷");
        title.style.color = new Color(0.306f, 0.788f, 0.627f);
        title.style.fontSize = 11;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginBottom = 4;
        panel.Add(title);

        panel.Add(MakeToggle("Highland", brush.highland, v => brush.highland = v));
        panel.Add(MakeToggle("CanSet",   brush.canSet,   v => brush.canSet = v));

        var passableRow = new VisualElement();
        passableRow.style.flexDirection = FlexDirection.Row;
        passableRow.style.alignItems = Align.Center;
        var passableLbl = new Label("PassableType");
        passableLbl.style.minWidth = 90;
        passableLbl.style.fontSize = 11;
        passableRow.Add(passableLbl);
        var passableField = new IntegerField { value = brush.passableType };
        passableField.style.flexGrow = 1;
        passableField.RegisterValueChangedCallback(evt => brush.passableType = Mathf.Clamp(evt.newValue, 0, 3));
        passableRow.Add(passableField);
        panel.Add(passableRow);

        panel.Add(MakeToggle("Deadly", brush.deadly, v => brush.deadly = v));

        var portalRow = new VisualElement();
        portalRow.style.flexDirection = FlexDirection.Row;
        portalRow.style.alignItems = Align.Center;
        var portalLbl = new Label("Portal");
        portalLbl.style.minWidth = 90;
        portalLbl.style.fontSize = 11;
        portalRow.Add(portalLbl);
        var portalEnum = new EnumField(brush.portalMode);
        portalEnum.style.flexGrow = 1;
        portalEnum.RegisterValueChangedCallback(evt => brush.portalMode = (PortalMode)evt.newValue);
        portalRow.Add(portalEnum);
        panel.Add(portalRow);

        var hint = new Label("提示:左键应用画刷;portal 两段式;右键拖=缩放;中键拖=平移。");
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
        panel.style.backgroundColor = new Color(0.078f, 0.078f, 0.094f);
        panel.style.borderTopLeftRadius = 3;
        panel.style.borderTopRightRadius = 3;
        panel.style.borderBottomLeftRadius = 3;
        panel.style.borderBottomRightRadius = 3;
        panel.style.paddingTop = 4; panel.style.paddingBottom = 4;
        panel.style.paddingLeft = 6; panel.style.paddingRight = 6;

        var title = new Label("▸ Cell");
        title.style.color = new Color(0.611f, 0.863f, 0.996f);
        title.style.fontSize = 11;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginBottom = 4;
        panel.Add(title);

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

        return panel;
    }

    static VisualElement BuildSizePanel(SerializedObject so)
    {
        var panel = new VisualElement();
        panel.style.flexDirection = FlexDirection.Column;
        panel.style.backgroundColor = new Color(0.078f, 0.078f, 0.094f);
        panel.style.borderTopLeftRadius = 3;
        panel.style.borderTopRightRadius = 3;
        panel.style.borderBottomLeftRadius = 3;
        panel.style.borderBottomRightRadius = 3;
        panel.style.paddingTop = 4; panel.style.paddingBottom = 4;
        panel.style.paddingLeft = 6; panel.style.paddingRight = 6;

        var title = new Label("▸ Map size");
        title.style.color = new Color(0.611f, 0.863f, 0.996f);
        title.style.fontSize = 11;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginBottom = 4;
        panel.Add(title);

        panel.Add(MakeLabeledIntField("iSize (rows)", so, "iSize"));
        panel.Add(MakeLabeledIntField("jSize (cols)", so, "jSize"));

        return panel;
    }

    static VisualElement MakeLabeledIntField(string label, SerializedObject so, string propName)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        var lbl = new Label(label);
        lbl.style.minWidth = 90;
        lbl.style.fontSize = 11;
        row.Add(lbl);
        var prop = so.FindProperty(propName);
        var field = new IntegerField { value = prop.intValue };
        field.style.flexGrow = 1;
        field.RegisterValueChangedCallback(evt =>
        {
            Undo.RecordObject(so.targetObject, $"Change {propName}");
            prop.intValue = Mathf.Max(0, evt.newValue);
            so.ApplyModifiedProperties();
        });
        row.Add(field);
        return row;
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
        Vector2 _rightDownLocal;
        Vector2 _midDownLocal;

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<MouseDownEvent>(OnMouseDown);
            target.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            target.RegisterCallback<MouseUpEvent>(OnMouseUp);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
            target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
            target.UnregisterCallback<MouseUpEvent>(OnMouseUp);
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
                _state.View.Offset = new Vector2(
                    _state.View.Offset.x + evt.mouseDelta.x / (_state.View.Zoom * unitX),
                    _state.View.Offset.y - evt.mouseDelta.y / (_state.View.Zoom * unitY)
                );
                _repaint();
            }
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
            ApplyBrush(c.Value);
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
        }

        // === SerializedProperty writes ===
        SerializedProperty MapDataProp() => _so.FindProperty("MapData");

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

        static int FindEntryIndex(SerializedProperty mapData, (int i, int j) cell)
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
            // Rebuild Blocks[,] from the SO. Simple: re-read each entry.
            if (_cache.Blocks == null) return;
            // Reset to default first
            for (int i = 0; i < _cache.ISize; i++)
            for (int j = 0; j < _cache.JSize; j++)
                _cache.Blocks[i, j] = default;

            var mapData = MapDataProp();
            for (int k = 0; k < mapData.arraySize; k++)
            {
                var e = mapData.GetArrayElementAtIndex(k);
                int i = e.FindPropertyRelative("i").intValue;
                int j = e.FindPropertyRelative("j").intValue;
                if (i < 0 || j < 0 || i >= _cache.ISize || j >= _cache.JSize) continue;
                _cache.Blocks[i, j] = new BlockDataEntry
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
            }
        }
    }
}
