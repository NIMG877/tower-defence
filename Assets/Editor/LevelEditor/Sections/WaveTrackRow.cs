using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 单条轨道行:左侧轨道头(Name / Color / Locked / 排序手柄 / + / ×),右侧时间轴 + Action 卡片。
/// 构造时只建一次结构,字段级变更走增量更新(通过 TrackPropertyValue 触发 Rebuild)。
/// </summary>
public static class WaveTrackRow
{
    public class State
    {
        public TextField NameField;
        public VisualElement ColorSwatch;
        public Button LockButton;
        public VisualElement DragHandle;
        public VisualElement CardsContainer;  // 时间轴容器
    }

    public static VisualElement Build(
        int waveIdx,
        int trackIdx,
        SerializedProperty trackProp,
        SerializedObject so,
        Action<int, int, int> onActionSelected,
        Func<(int, int, int)> getCurrentSelection,
        Action rebuild)
    {
        var row = new VisualElement();
        row.AddToClassList("level-editor-section");
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginBottom = 4;

        // === 左侧:轨道头 ===
        var header = new VisualElement();
        header.style.width = 180;
        header.style.flexShrink = 0;
        header.style.backgroundColor = new Color(0.13f, 0.13f, 0.16f);
        header.style.paddingTop = 4;
        header.style.paddingBottom = 4;
        header.style.paddingLeft = 6;
        header.style.paddingRight = 6;
        header.style.borderTopLeftRadius = 3;
        header.style.borderBottomLeftRadius = 3;
        row.Add(header);

        // 排序手柄(占位,Phase 3 内简化版只显示不动)
        var dragHandle = new Label("≡");
        dragHandle.style.fontSize = 14;
        dragHandle.style.color = new Color(0.6f, 0.6f, 0.6f);
        dragHandle.style.unityTextAlign = TextAnchor.MiddleCenter;
        dragHandle.style.width = 16;
        header.Add(dragHandle);

        // Name(可编辑)
        var nameField = new TextField { value = trackProp.FindPropertyRelative("Name").stringValue };
        nameField.style.flexGrow = 1;
        nameField.style.marginLeft = 2;
        nameField.style.marginRight = 2;
        nameField.RegisterValueChangedCallback(evt =>
        {
            Undo.RecordObject(so.targetObject, "Rename Track");
            trackProp.FindPropertyRelative("Name").stringValue = evt.newValue;
            so.ApplyModifiedProperties();
        });
        header.Add(nameField);

        // Color swatch + 颜色选择按钮
        var colorProp = trackProp.FindPropertyRelative("TrackColor");
        var swatch = new VisualElement();
        swatch.style.width = 18;
        swatch.style.height = 18;
        swatch.style.borderTopLeftRadius = 2;
        swatch.style.borderTopRightRadius = 2;
        swatch.style.borderBottomLeftRadius = 2;
        swatch.style.borderBottomRightRadius = 2;
        swatch.style.marginRight = 2;
        swatch.style.backgroundColor = colorProp.colorValue;
        swatch.style.borderLeftWidth = 1;
        swatch.style.borderRightWidth = 1;
        swatch.style.borderTopWidth = 1;
        swatch.style.borderBottomWidth = 1;
        swatch.style.borderLeftColor = new Color(0.3f, 0.3f, 0.3f);
        swatch.style.borderRightColor = new Color(0.3f, 0.3f, 0.3f);
        swatch.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f);
        swatch.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);
        var colorPickerBtn = new Button(() =>
        {
            // 简化:打开系统 ColorField 弹窗
            EditorGUI.BeginChangeCheck();
            Color newColor = EditorGUILayout.ColorField("Track Color", colorProp.colorValue);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(so.targetObject, "Change Track Color");
                colorProp.colorValue = newColor;
                so.ApplyModifiedProperties();
                swatch.style.backgroundColor = newColor;
                rebuild?.Invoke();
            }
        }) { text = "🎨" };
        colorPickerBtn.style.width = 22;
        colorPickerBtn.style.height = 18;
        colorPickerBtn.style.fontSize = 9;
        header.Add(colorPickerBtn);
        header.Add(swatch);

        // Lock button
        var lockProp = trackProp.FindPropertyRelative("Locked");
        bool initialLocked = lockProp.boolValue;
        var lockBtn = new Button { text = initialLocked ? "🔒" : "🔓" };
        lockBtn.clicked += () =>
        {
            Undo.RecordObject(so.targetObject, "Toggle Track Lock");
            lockProp.boolValue = !lockProp.boolValue;
            so.ApplyModifiedProperties();
            lockBtn.text = lockProp.boolValue ? "🔒" : "🔓";
        };
        lockBtn.style.width = 22;
        lockBtn.style.height = 18;
        lockBtn.style.fontSize = 10;
        header.Add(lockBtn);

        // + / × 行内按钮
        var btnRow = new VisualElement();
        btnRow.style.flexDirection = FlexDirection.Row;
        btnRow.style.marginTop = 4;

        var addActionBtn = new Button(() =>
        {
            var actionsProp = trackProp.FindPropertyRelative("Actions");
            Undo.RecordObject(so.targetObject, "Add Action");
            actionsProp.InsertArrayElementAtIndex(actionsProp.arraySize);
            var newAction = actionsProp.GetArrayElementAtIndex(actionsProp.arraySize - 1);
            newAction.FindPropertyRelative("CommandType").intValue = 0;
            // TriggerTime = 同 Track 内最大值 + 1s(空 Track 则 0)
            float maxTrig = 0f;
            for (int i = 0; i < actionsProp.arraySize - 1; i++)
            {
                float t = actionsProp.GetArrayElementAtIndex(i).FindPropertyRelative("TriggerTime").floatValue;
                if (t > maxTrig) maxTrig = t;
            }
            newAction.FindPropertyRelative("TriggerTime").floatValue = maxTrig + 1f;
            newAction.FindPropertyRelative("GapsFromLastRepeat").arraySize = 0;
            so.ApplyModifiedProperties();
        }) { text = "+" };
        addActionBtn.style.flexGrow = 1;
        addActionBtn.style.marginRight = 2;
        btnRow.Add(addActionBtn);

        var delActionBtn = new Button(() =>
        {
            var actionsProp = trackProp.FindPropertyRelative("Actions");
            if (actionsProp.arraySize == 0) return;
            var (selW, selT, selA) = getCurrentSelection();
            bool willInvalidate = selW == waveIdx && selT == trackIdx && selA == actionsProp.arraySize - 1;
            if (EditorUtility.DisplayDialog("删除 Action", $"确认删除 Track {trackIdx} 的最后一个 Action?", "删除", "取消"))
            {
                Undo.RecordObject(so.targetObject, "Delete Action");
                actionsProp.DeleteArrayElementAtIndex(actionsProp.arraySize - 1);
                so.ApplyModifiedProperties();
                if (willInvalidate) onActionSelected?.Invoke(-1, -1, -1);
            }
        }) { text = "×" };
        delActionBtn.style.flexGrow = 1;
        btnRow.Add(delActionBtn);

        header.Add(btnRow);

        // === 右侧:时间轴 ===
        var timelineScroll = new ScrollView(ScrollViewMode.Horizontal);
        timelineScroll.style.flexGrow = 1;
        timelineScroll.style.height = 70;
        timelineScroll.style.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
        timelineScroll.style.borderTopRightRadius = 3;
        timelineScroll.style.borderBottomRightRadius = 3;
        timelineScroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
        timelineScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        row.Add(timelineScroll);

        var cardsContainer = new VisualElement();
        cardsContainer.style.height = 70;
        cardsContainer.style.position = Position.Relative;
        cardsContainer.style.overflow = Overflow.Visible;
        timelineScroll.Add(cardsContainer);

        // Track 状态对象(后续 Phase 3 增量更新用)
        var state = new State
        {
            NameField = nameField,
            ColorSwatch = swatch,
            LockButton = lockBtn,
            DragHandle = dragHandle,
            CardsContainer = cardsContainer,
        };
        cardsContainer.userData = state;

        // 卡片渲染(委托给 WaveActionCard)
        var actionsProp = trackProp.FindPropertyRelative("Actions");
        WaveActionCard.Render(cardsContainer, actionsProp, waveIdx, trackIdx, onActionSelected, () => lockProp.boolValue, rebuild);
        cardsContainer.TrackPropertyValue(actionsProp, _ =>
            WaveActionCard.Render(cardsContainer, actionsProp, waveIdx, trackIdx, onActionSelected, () => lockProp.boolValue, rebuild));

        return row;
    }
}