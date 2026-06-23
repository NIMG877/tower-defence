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
            _state.NotifyChanged();
            target.CaptureMouse();
        }
        else
        {
            // 在空白处新增 checkpoint(snap 由 state.Snap 决定)
            AddCheckpointAt(local);
            target.CaptureMouse();
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

        // 左键拖拽 = 移动 checkpoint
        if (_dragCpIdx >= 0 && evt.pressedButtons == (1 << (int)MouseButton.LeftMouse))
        {
            var world = _state.View.ScreenToWorld(evt.localMousePosition, _state.Cache.ISize, _state.Cache.JSize);
            var writePos = _state.Snap ? ViewTransform.SnapToGrid(world) : world;
            UpdateCheckpointVisual(_dragCpIdx, writePos);
        }
    }

    void OnMouseUp(MouseUpEvent evt)
    {
        if (evt.button == 0 && _dragCpIdx >= 0)
        {
            var world = _state.View.ScreenToWorld(evt.localMousePosition, _state.Cache.ISize, _state.Cache.JSize);
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
