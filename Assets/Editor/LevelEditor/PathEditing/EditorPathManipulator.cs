using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class EditorPathManipulator : MouseManipulator
{
    SerializedObject _so;
    PathEditingState _state;
    VisualElement _canvas;
    VisualElement _layer;

    int _dragCpIdx = -1;
    bool _rightDragging;        // 右键按下进入缩放拖拽模式
    Vector2 _rightDownLocal;    // 右键按下时鼠标位置,缩放以该点为锚
    Vector2 _dragStartWorld;    // 左键拖 cp 起始时 cp 的世界坐标(累加基点)
    Vector2 _dragAccumWorld;    // 左键拖 cp 期间累计的世界 delta(每帧 mouseDelta 累加到这里)

    public EditorPathManipulator(SerializedObject so, PathEditingState state, VisualElement canvas, VisualElement layer)
    {
        _so = so; _state = state; _canvas = canvas; _layer = layer;
        activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        activators.Add(new ManipulatorActivationFilter { button = MouseButton.MiddleMouse });
        activators.Add(new ManipulatorActivationFilter { button = MouseButton.RightMouse });
    }

    protected override void RegisterCallbacksOnTarget()
    {
        target.RegisterCallback<MouseDownEvent>(OnMouseDown);
        target.RegisterCallback<MouseMoveEvent>(OnMouseMove);
        target.RegisterCallback<MouseUpEvent>(OnMouseUp);
        // 不再监听 WheelEvent — 缩放改用右键拖拽
    }

    protected override void UnregisterCallbacksFromTarget()
    {
        target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
        target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
        target.UnregisterCallback<MouseUpEvent>(OnMouseUp);
    }

    void OnMouseDown(MouseDownEvent evt)
    {
        if (evt.button == 0) OnLeftDown(evt);
        else if (evt.button == 1) OnRightDown(evt);
        else if (evt.button == 2) OnMiddleDown(evt);
    }

    void OnLeftDown(MouseDownEvent evt)
    {
        var local = evt.localMousePosition;
        var hitIdx = HitTestCheckpoint(local);

        if (hitIdx >= 0)
        {
            _state.SelectedCheckpointIdx = hitIdx;
            _dragCpIdx = hitIdx;
            // 记下 cp 原始世界坐标 —— 拖动时用 delta 累加(每帧 mouseDelta 累加到
            // _dragAccumWorld),而不是追踪鼠标绝对位置。避免"按下时鼠标偏 cp 中心 →
            // cp 立刻跳到鼠标位置"的突兀感。松手时也用 _dragAccumWorld,跟 Move 阶段同源。
            var cpProp = _so.FindProperty("Paths").GetArrayElementAtIndex(_state.SelectedPathIdx)
                .FindPropertyRelative("CheckPoints").GetArrayElementAtIndex(hitIdx);
            _dragStartWorld = cpProp.vector2Value;
            _dragAccumWorld = Vector2.zero;
            _state.NotifyChanged();
            target.CaptureMouse();
        }
        else
        {
            // 在空白处新增 checkpoint(snap 由 state.Snap 决定)
            // 不调 CaptureMouse:新增是原子动作,不需要进入拖拽态;capture 持续到下次
            // ReleaseMouse 之前会让 canvas 偷走所有后续 mouse 事件(包括 canvas 外的),
            // 而本路径 _dragCpIdx 永远是 -1,OnMouseUp 左键分支进不去,capture 不会释放。
            AddCheckpointAt(local);
        }
    }

    void OnRightDown(MouseDownEvent evt)
    {
        _rightDragging = true;
        _rightDownLocal = evt.localMousePosition;
        target.CaptureMouse();
    }

    void OnMiddleDown(MouseDownEvent evt)
    {
        target.CaptureMouse();
    }

    void OnMouseMove(MouseMoveEvent evt)
    {
        // cursor readout
        var readout = _canvas.Q<Label>("cursor-readout");
        if (readout != null && _state.Cache != null)
        {
            var world = _state.View.ScreenToWorld(evt.localMousePosition, _state.Cache.ISize, _state.Cache.JSize);
            readout.text = $"({world.x:F2}, {world.y:F2})";
        }

        // 中键拖拽 = 平移(屏幕像素 1:1)
        if (evt.pressedButtons == (1 << (int)MouseButton.MiddleMouse) && _state.Cache != null)
        {
            float unitX = ViewTransform.CanvasWidth / _state.Cache.JSize;
            float unitY = ViewTransform.CanvasHeight / _state.Cache.ISize;
            _state.View.Offset -= new Vector2(
                evt.mouseDelta.x / (_state.View.Zoom * unitX),
                -evt.mouseDelta.y / (_state.View.Zoom * unitY));
            _state.NotifyChanged();
        }

        // 右键拖拽 = 缩放(以按下时鼠标位置为锚,垂直位移 1 像素 = zoom * 1.01)
        if (_rightDragging && _state.Cache != null)
        {
            float delta = -evt.mouseDelta.y; // 鼠标上 = 放大(zoom 增加)
            float newZoom = Mathf.Clamp(_state.View.Zoom * Mathf.Pow(1.01f, delta), 0.25f, 16f);
            if (!Mathf.Approximately(newZoom, _state.View.Zoom))
            {
                // 以右键按下位置为锚缩放
                var worldBefore = _state.View.ScreenToWorld(_rightDownLocal, _state.Cache.ISize, _state.Cache.JSize);
                _state.View.Zoom = newZoom;
                var worldAfter = _state.View.ScreenToWorld(_rightDownLocal, _state.Cache.ISize, _state.Cache.JSize);
                _state.View.Offset += worldBefore - worldAfter;
                _state.NotifyChanged();
            }
        }

        // 左键拖拽 = 移动 checkpoint(delta 累加模式,不是追踪鼠标绝对位置)
        if (_dragCpIdx >= 0 && evt.pressedButtons == (1 << (int)MouseButton.LeftMouse) && _state.Cache != null)
        {
            // 累加本帧的世界 delta 到 _dragAccumWorld
            float unitX = ViewTransform.CanvasWidth / _state.Cache.JSize;
            float unitY = ViewTransform.CanvasHeight / _state.Cache.ISize;
            _dragAccumWorld += new Vector2(
                evt.mouseDelta.x / (_state.View.Zoom * unitX),
                -evt.mouseDelta.y / (_state.View.Zoom * unitY)); // Y 翻转
            var world = _dragStartWorld + _dragAccumWorld;
            var writePos = _state.Snap ? ViewTransform.SnapToGrid(world) : world;
            UpdateCheckpointVisual(_dragCpIdx, writePos);
        }
    }

    void OnMouseUp(MouseUpEvent evt)
    {
        if (evt.button == 0 && _dragCpIdx >= 0 && _state.Cache != null)
        {
            // 用 OnMouseMove 阶段累加的 _dragAccumWorld,不再重新反推屏幕 delta。
            // 跟 Move 阶段完全同源(都是把 mouseDelta 累加),保证松手时 cp 落在
            // Move 阶段最后显示的位置,不会有"最后 N 像素漂移"。
            var world = _dragStartWorld + _dragAccumWorld;
            var writePos = _state.Snap ? ViewTransform.SnapToGrid(world) : world;
            CommitCheckpointPosition(_dragCpIdx, writePos);
            _dragCpIdx = -1;
            target.ReleaseMouse();
        }
        else if (evt.button == 1 && _rightDragging)
        {
            _rightDragging = false;
            target.ReleaseMouse();
        }
        else if (evt.button == 2)
        {
            target.ReleaseMouse();
        }
    }

    int HitTestCheckpoint(Vector2 local)
    {
        for (int k = 0; k < _layer.childCount; k++)
        {
            var dot = _layer.ElementAt(k);
            var rect = new Rect(dot.layout.x, dot.layout.y, dot.layout.width, dot.layout.height);
            if (rect.Contains(local)) return k;
        }
        return -1;
    }

    void AddCheckpointAt(Vector2 local)
    {
        var world = _state.View.ScreenToWorld(local, _state.Cache.ISize, _state.Cache.JSize);
        var writePos = _state.Snap ? ViewTransform.SnapToGrid(world) : world;
        var pathProp = _so.FindProperty("Paths");
        var pathEl = pathProp.GetArrayElementAtIndex(_state.SelectedPathIdx);
        var cpsProp = pathEl.FindPropertyRelative("CheckPoints");
        var wtsProp = pathEl.FindPropertyRelative("WaitTimes");

        Undo.RecordObject(_so.targetObject, "Add Checkpoint");
        cpsProp.arraySize++;
        cpsProp.GetArrayElementAtIndex(cpsProp.arraySize - 1).vector2Value = writePos;
        wtsProp.arraySize++;
        wtsProp.GetArrayElementAtIndex(wtsProp.arraySize - 1).floatValue = 0f;
        _so.ApplyModifiedProperties();
        _state.SelectedCheckpointIdx = cpsProp.arraySize - 1;
        _state.NotifyChanged();
    }

    void UpdateCheckpointVisual(int idx, Vector2 worldPos)
    {
        if (_layer.ElementAt(idx) is VisualElement dot && _state.Cache != null)
        {
            var screen = _state.View.WorldToScreen(worldPos, _state.Cache.ISize, _state.Cache.JSize);
            float d = dot.layout.width;
            dot.style.left = screen.x - d / 2f;
            dot.style.top = screen.y - d / 2f;
        }
    }

    void CommitCheckpointPosition(int idx, Vector2 worldPos)
    {
        var pathProp = _so.FindProperty("Paths");
        var pathEl = pathProp.GetArrayElementAtIndex(_state.SelectedPathIdx);
        var cpsProp = pathEl.FindPropertyRelative("CheckPoints");
        Undo.RecordObject(_so.targetObject, "Move Checkpoint");
        cpsProp.GetArrayElementAtIndex(idx).vector2Value = worldPos;
        _so.ApplyModifiedProperties();
        _state.NotifyChanged();
    }
}
