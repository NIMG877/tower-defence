using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Map-paint tab. See spec §4.3. Single-field brush with portal two-click mode.
/// Writes to <c>LevelData.MapData</c> via SerializedProperty + Undo.
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
        root.style.flexDirection = FlexDirection.Row;

        // === Brush panel (left) ===
        var panel = new VisualElement();
        panel.style.width = 200;
        panel.style.paddingRight = 8;
        panel.style.borderRightWidth = 1;
        panel.style.borderRightColor = new Color(0.3f, 0.3f, 0.3f);
        root.Add(panel);

        var brush = new BrushState();
        panel.Add(MakeToggle("Highland", brush.highland, v => brush.highland = v));
        panel.Add(MakeToggle("CanSet",   brush.canSet,   v => brush.canSet = v));

        var passableRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
        passableRow.Add(new Label("PassableType") { style = { minWidth = 100 } });
        var passableField = new IntegerField { value = brush.passableType };
        passableField.style.flexGrow = 1;
        passableField.RegisterValueChangedCallback(evt => brush.passableType = Mathf.Clamp(evt.newValue, 0, 3));
        passableRow.Add(passableField);
        panel.Add(passableRow);

        panel.Add(MakeToggle("Deadly", brush.deadly, v => brush.deadly = v));

        var portalLabel = new Label("Portal mode");
        panel.Add(portalLabel);
        var portalEnum = new EnumField(brush.portalMode);
        portalEnum.RegisterValueChangedCallback(evt => brush.portalMode = (PortalMode)evt.newValue);
        panel.Add(portalEnum);

        panel.Add(new Label("提示:点击格子应用画刷;portal 模式两段式。右键 = 清除。"));

        // === Canvas (right) ===
        var canvasContainer = new VisualElement();
        canvasContainer.style.flexGrow = 1;
        canvasContainer.style.minHeight = 400;
        root.Add(canvasContainer);

        var view = new ViewTransform();
        state.View = view;
        var canvas = new VisualElement();
        canvas.style.flexGrow = 1;
        canvasContainer.Add(canvas);

        var status = new Label("(i, j): -");
        status.style.paddingTop = 4;
        canvasContainer.Add(status);

        void Repaint()
        {
            canvas.generateVisualContent = null;
            canvas.generateVisualContent = ctx => MapCanvasView.DrawBlocks(ctx, state);
            canvas.MarkDirtyRepaint();
        }
        state.Changed += Repaint;
        Repaint();

        // === Manipulator ===
        var manip = new MapEditManipulator(so, cache, view, brush, status, state, Repaint);
        canvas.AddManipulator(manip);

        // Repaint when undo/redo changes MapData
        Undo.undoRedoPerformed += Repaint;

        // Cleanup on detach
        canvas.RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            state.Changed -= Repaint;
            Undo.undoRedoPerformed -= Repaint;
        });

        return root;
    }

    static Toggle MakeToggle(string label, bool initial, System.Action<bool> onChange)
    {
        var t = new Toggle(label) { value = initial };
        t.RegisterValueChangedCallback(evt => onChange(evt.newValue));
        return t;
    }

    // === Manipulator ===
    class MapEditManipulator : MouseManipulator
    {
        readonly SerializedObject _so;
        readonly BlockMapCache _cache;
        readonly ViewTransform _view;
        readonly BrushState _brush;
        readonly Label _status;
        readonly PathEditingState _state;
        readonly System.Action _repaint;

        public MapEditManipulator(SerializedObject so, BlockMapCache cache, ViewTransform view,
            BrushState brush, Label status, PathEditingState state, System.Action repaint)
        {
            _so = so; _cache = cache; _view = view; _brush = brush;
            _status = status; _state = state; _repaint = repaint;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<MouseDownEvent>(OnMouseDown);
            target.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            target.RegisterCallback<MouseUpEvent>(OnMouseUp);
            target.RegisterCallback<WheelEvent>(OnWheel);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
            target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
            target.UnregisterCallback<MouseUpEvent>(OnMouseUp);
            target.UnregisterCallback<WheelEvent>(OnWheel);
        }

        void OnWheel(WheelEvent evt)
        {
            float delta = -evt.delta.y;
            _view.Zoom = Mathf.Clamp(_view.Zoom * Mathf.Pow(1.01f, delta), 0.25f, 16f);
            _repaint();
        }

        (int i, int j)? ScreenToCell(Vector2 local)
        {
            if (_cache.Blocks == null) return null;
            var world = _view.ScreenToWorld(local, _cache.ISize, _cache.JSize);
            int i = (int)(world.y + 0.5f);
            int j = (int)(world.x + 0.5f);
            if (i < 0 || j < 0 || i >= _cache.ISize || j >= _cache.JSize) return null;
            return (i, j);
        }

        void OnMouseMove(MouseMoveEvent evt)
        {
            var cell = ScreenToCell(evt.localMousePosition);
            _status.text = cell.HasValue ? $"(i, j): ({cell.Value.i}, {cell.Value.j})" : "(i, j): -";
        }

        void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.button == 1) // right-click: clear
            {
                var cell = ScreenToCell(evt.localMousePosition);
                if (cell.HasValue) ClearCell(cell.Value);
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

        void OnMouseUp(MouseUpEvent evt) { /* drag-to-paint handled by repeated MouseDown if you want; future extension */ }

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

        void ClearCell((int i, int j) cell)
        {
            Undo.RecordObject(_so.targetObject, "Clear Block");
            var mapData = MapDataProp();
            int idx = FindEntryIndex(mapData, cell);
            if (idx >= 0)
            {
                mapData.DeleteArrayElementAtIndex(idx);
                _so.ApplyModifiedProperties();
            }
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