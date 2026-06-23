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
    Vector2 _dragVisualOffset; // 拖动期间,圆点的视觉偏移(只更新 style)

    public EditorPathManipulator(SerializedObject so, PathEditingState state, VisualElement canvas, VisualElement layer)
    {
        _so = so; _state = state; _canvas = canvas; _layer = layer;
        activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        activators.Add(new ManipulatorActivationFilter { button = MouseButton.MiddleMouse });
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

    void OnMouseDown(MouseDownEvent evt)
    {
        if (evt.button == 0) OnLeftDown(evt);
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
            _dragVisualOffset = Vector2.zero;
            _state.NotifyChanged();
            target.CaptureMouse();
        }
        else
        {
            // 在空白处新增 checkpoint
            AddCheckpointAt(local);
            target.CaptureMouse();
        }
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
            readout.text = $"({world.x:F1}, {world.y:F1})";
        }

        // 中键拖拽 = 平移(屏幕像素 1:1,鼠标 N px = 视角 N px)
        if (evt.pressedButtons == (1 << (int)MouseButton.MiddleMouse) && _state.Cache != null)
        {
            float unitX = ViewTransform.CanvasWidth / _state.Cache.JSize;
            float unitY = ViewTransform.CanvasHeight / _state.Cache.ISize;
            // 鼠标 Y 向下 → grid y 减小(让"鼠标下,图也下"的自然手感)
            _state.View.Offset -= new Vector2(
                evt.mouseDelta.x / (_state.View.Zoom * unitX),
                -evt.mouseDelta.y / (_state.View.Zoom * unitY));
            _state.NotifyChanged();
        }

        // 左键拖拽 = 移动 checkpoint (视觉跟随,不写 SerializedProperty;不再 snap)
        if (_dragCpIdx >= 0 && evt.pressedButtons == (1 << (int)MouseButton.LeftMouse))
        {
            var world = _state.View.ScreenToWorld(evt.localMousePosition, _state.Cache.ISize, _state.Cache.JSize);
            UpdateCheckpointVisual(_dragCpIdx, world);
        }
    }

    void OnMouseUp(MouseUpEvent evt)
    {
        if (evt.button == 0 && _dragCpIdx >= 0)
        {
            // 松手一次性写回(自由坐标,不再 snap)
            var world = _state.View.ScreenToWorld(evt.localMousePosition, _state.Cache.ISize, _state.Cache.JSize);
            CommitCheckpointPosition(_dragCpIdx, world);
            _dragCpIdx = -1;
            target.ReleaseMouse();
        }
        else if (evt.button == 2)
        {
            target.ReleaseMouse();
        }
    }

    void OnWheel(WheelEvent evt)
    {
        float oldZoom = _state.View.Zoom;
        float newZoom = Mathf.Clamp(oldZoom * (1f - evt.delta.y * 0.05f), 0.25f, 4f);
        // 以鼠标位置为中心缩放
        if (_state.Cache != null)
        {
            var worldBefore = _state.View.ScreenToWorld(evt.localMousePosition, _state.Cache.ISize, _state.Cache.JSize);
            _state.View.Zoom = newZoom;
            var worldAfter = _state.View.ScreenToWorld(evt.localMousePosition, _state.Cache.ISize, _state.Cache.JSize);
            _state.View.Offset += worldBefore - worldAfter;
        }
        _state.NotifyChanged();
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
        var snapped = ViewTransform.SnapToGrid(world);
        var pathProp = _so.FindProperty("Paths");
        var pathEl = pathProp.GetArrayElementAtIndex(_state.SelectedPathIdx);
        // 实际路径:Paths[i].CheckPoints;由于 Unity 序列化数组嵌套,正确路径是
        //   pathEl.FindPropertyRelative("CheckPoints")
        var cpsProp = pathEl.FindPropertyRelative("CheckPoints");
        var wtsProp = pathEl.FindPropertyRelative("WaitTimes");

        Undo.RecordObject(_so.targetObject, "Add Checkpoint");
        cpsProp.arraySize++;
        cpsProp.GetArrayElementAtIndex(cpsProp.arraySize - 1).vector2Value = snapped;
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
