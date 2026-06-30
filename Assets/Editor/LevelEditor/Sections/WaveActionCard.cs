using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Action 卡片渲染 + 拖拽手柄。
/// 中段拖拽改 TriggerTime,右沿拖拽改持续时间(spawner = 按比例缩放 GapsFromLastRepeat,dialog = 改 DurationTime,其它不可拖)。
/// 锁定轨道(pickingMode=Ignore)不接收鼠标。
/// </summary>
public static class WaveActionCard
{
    public class State
    {
        public List<Button> Cards = new();
        public int LastActionCount = -1;
    }

    static float PixelsPerSecond() => 24f * WaveTimelineSection.Zoom;
    static Color CommandTypeColor(int cmd) => WaveTimelineSection.CommandTypeColor(cmd);

    public static void Render(
        VisualElement container,
        SerializedProperty actionsProp,
        int waveIdx,
        int trackIdx,
        Action<int, int, int> onActionSelected,
        Func<bool> isLocked,
        Action rebuild)
    {
        var state = container.userData as State;
        if (state == null)
        {
            state = new State();
            container.userData = state;
        }

        // 结构性变化(add/delete)整树重建;字段级变化走下面的增量更新
        if (state.LastActionCount != actionsProp.arraySize)
        {
            container.Clear();
            state.Cards.Clear();

            if (actionsProp.arraySize == 0)
            {
                var empty = new Label("(空)");
                empty.style.color = new Color(0.4f, 0.4f, 0.4f);
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                empty.style.position = Position.Absolute;
                empty.style.left = 8;
                empty.style.top = 26;
                container.Add(empty);
            }
            else
            {
                for (int i = 0; i < actionsProp.arraySize; i++)
                {
                    int actionIdx = i;
                    var card = new Button(() => onActionSelected?.Invoke(waveIdx, trackIdx, actionIdx))
                    { text = $"A{i}" };
                    card.style.position = Position.Absolute;
                    card.style.top = 22;
                    card.style.height = 36;
                    card.style.width = 60;  // 起始宽度,Render 阶段按 Duration 调整
                    card.style.color = new Color(0, 0, 0);
                    card.style.fontSize = 9;
                    card.style.paddingLeft = 2;
                    card.style.paddingRight = 2;
                    container.Add(card);
                    state.Cards.Add(card);

                    // 拖拽手柄:中段 → TriggerTime,右沿 → Duration
                    var drag = new ActionCardDragManipulator(card, actionsProp, actionIdx, isLocked, rebuild);
                    card.AddManipulator(drag);
                }
            }

            state.LastActionCount = actionsProp.arraySize;
        }

        if (actionsProp.arraySize == 0) return;

        // 增量更新:位置 + 宽度 + 颜色
        float pxPerSec = PixelsPerSecond();
        for (int i = 0; i < actionsProp.arraySize; i++)
        {
            var a = actionsProp.GetArrayElementAtIndex(i);
            var card = state.Cards[i];

            float triggerTime = a.FindPropertyRelative("TriggerTime").floatValue;
            card.style.left = triggerTime * pxPerSec;

            int cmd = a.FindPropertyRelative("CommandType").intValue;
            float duration = ComputeEndTime(a, cmd) - triggerTime;
            float width = Mathf.Max(20f, duration * pxPerSec);
            card.style.width = width;
            card.style.backgroundColor = CommandTypeColor(cmd);

            // 锁定轨道:卡片描边变色提示
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderTopWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftColor = isLocked() ? new Color(0.8f, 0.6f, 0.2f) : new Color(0.3f, 0.3f, 0.3f);
            card.style.borderRightColor = card.style.borderLeftColor;
            card.style.borderTopColor = card.style.borderLeftColor;
            card.style.borderBottomColor = card.style.borderLeftColor;
        }
    }

    /// <summary>
    /// 计算 Action 右端时间(TriggerTime + 占用时长)。
    /// 见 spec §2.4 派生表。
    /// </summary>
    public static float ComputeEndTime(SerializedProperty actionProp, int commandType)
    {
        float triggerTime = actionProp.FindPropertyRelative("TriggerTime").floatValue;
        switch (commandType)
        {
            case 0: // spawner:TriggerTime + sum(GapsFromLastRepeat)
                var gapsProp = actionProp.FindPropertyRelative("GapsFromLastRepeat");
                float sum = 0f;
                for (int i = 0; i < gapsProp.arraySize; i++)
                    sum += Mathf.Max(0f, gapsProp.GetArrayElementAtIndex(i).floatValue);
                return triggerTime + sum;
            case 2:
            case 3:
            case 4: // path preview:GapsFromLastRepeat 硬编码 = {0, printerLifeTime}
                var gapsProp2 = actionProp.FindPropertyRelative("GapsFromLastRepeat");
                float sum2 = 0f;
                for (int i = 0; i < gapsProp2.arraySize; i++)
                    sum2 += Mathf.Max(0f, gapsProp2.GetArrayElementAtIndex(i).floatValue);
                return triggerTime + sum2;
            case 5: // dialog:TriggerTime + DurationTime
                return triggerTime + Mathf.Max(0f, actionProp.FindPropertyRelative("DurationTime").floatValue);
            case 1:
            case 6: // 静态 / 剧情:无右端,返回 triggerTime(纯点)
            default:
                return triggerTime;
        }
    }
}

/// <summary>
/// ActionCard 的拖拽 Manipulator:
/// - 中段按下:水平拖动改 TriggerTime(吸附 0.1s,Shift 关闭)
/// - 右沿:Phase 3 基础版未实现(留作 follow-up),所有拖动都按 TriggerTime 处理
/// </summary>
public class ActionCardDragManipulator : MouseManipulator
{
    const float RightEdgeWidth = 6f;
    const float DragThresholdPx = 4f;  // 超过此距离才视为"拖动",否则当作点击(避免鼠标抖动造成 TriggerTime 跳变)

    readonly SerializedProperty _actionsProp;
    readonly int _actionIdx;
    readonly Func<bool> _isLocked;
    readonly Action _rebuild;

    Vector2 _startMouse;
    float _startTriggerTime;
    bool _dragging;  // 鼠标是否已越过门槛进入拖动状态

    public ActionCardDragManipulator(VisualElement target, SerializedProperty actionsProp, int actionIdx, Func<bool> isLocked, Action rebuild)
    {
        this.target = target;
        _actionsProp = actionsProp;
        _actionIdx = actionIdx;
        _isLocked = isLocked;
        _rebuild = rebuild;
        activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
    }

    static float PixelsPerSecond() => 24f * WaveTimelineSection.Zoom;

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

    void OnMouseDown(MouseDownEvent evt)
    {
        if (_isLocked()) return;
        var actionProp = _actionsProp.GetArrayElementAtIndex(_actionIdx);
        _startMouse = evt.mousePosition;
        _startTriggerTime = actionProp.FindPropertyRelative("TriggerTime").floatValue;
        _dragging = false;  // 起始未拖动
        target.CaptureMouse();
        // 不 StopPropagation,让 Button 的 click 也触发(选中 Action)
    }

    void OnMouseMove(MouseMoveEvent evt)
    {
        if (!target.HasMouseCapture()) return;

        float dx = evt.mousePosition.x - _startMouse.x;

        // 门槛检查:鼠标移动距离 < 4px 时视为点击,不应用 TriggerTime 改动
        if (!_dragging)
        {
            if (Mathf.Abs(dx) < DragThresholdPx) return;
            _dragging = true;
            evt.StopPropagation();  // 进入拖动后吃掉事件,避免触发 Button click
        }

        var actionProp = _actionsProp.GetArrayElementAtIndex(_actionIdx);
        float pxPerSec = PixelsPerSecond();

        if (evt.shiftKey)
        {
            // 关闭吸附
        }
        else
        {
            float rawDelta = dx / pxPerSec;
            float snapped = Mathf.Round(rawDelta * 10f) / 10f;
            dx = snapped * pxPerSec;
        }

        float newTrigger = Mathf.Max(0f, _startTriggerTime + dx / pxPerSec);
        actionProp.FindPropertyRelative("TriggerTime").floatValue = newTrigger;
        _actionsProp.serializedObject.ApplyModifiedProperties();
        _rebuild?.Invoke();
    }

    void OnMouseUp(MouseUpEvent evt)
    {
        if (target.HasMouseCapture())
        {
            target.ReleaseMouse();
            // 只有真正拖动过才 StopPropagation,避免误吞点击事件
            if (_dragging) evt.StopPropagation();
            _dragging = false;
        }
    }
}