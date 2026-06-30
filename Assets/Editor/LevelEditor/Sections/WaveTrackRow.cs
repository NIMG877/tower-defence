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
        header.style.width = new Length(240, LengthUnit.Pixel);   // 显式宽度,容纳 ≡ + Name + 4 按钮 + 色块
        header.style.minWidth = new Length(240, LengthUnit.Pixel); // 防止被 flex 容器挤压
        header.style.flexShrink = 0;                                // 不参与横向 flex 收缩
        header.style.flexDirection = FlexDirection.Column;
        header.style.backgroundColor = new Color(0.13f, 0.13f, 0.16f);
        header.style.paddingTop = 4;
        header.style.paddingBottom = 4;
        header.style.paddingLeft = 6;
        header.style.paddingRight = 6;
        header.style.borderTopLeftRadius = 3;
        header.style.borderBottomLeftRadius = 3;
        row.Add(header);

        // 第一行:拖拽手柄 + Name
        var nameRow = new VisualElement();
        nameRow.style.flexDirection = FlexDirection.Row;
        nameRow.style.alignItems = Align.Center;
        header.Add(nameRow);

        // 排序手柄(占位,Phase 3 内简化版只显示不动)
        var dragHandle = new Label("≡");
        dragHandle.style.fontSize = 14;
        dragHandle.style.color = new Color(0.6f, 0.6f, 0.6f);
        dragHandle.style.unityTextAlign = TextAnchor.MiddleCenter;
        dragHandle.style.width = 16;
        nameRow.Add(dragHandle);

        // Name(可编辑)
        var nameField = new TextField { value = trackProp.FindPropertyRelative("Name").stringValue };
        nameField.label = "";   // 隐藏内置 Label,留出全部宽度给输入区
        nameField.style.flexGrow = 1;
        nameField.style.flexBasis = 0;  // 配合 flexGrow,允许收缩到 0,避免被内部 padding 挤
        nameField.style.minWidth = 0;
        nameField.style.marginLeft = 2;
        nameField.style.marginRight = 2;
        nameField.RegisterValueChangedCallback(evt =>
        {
            Undo.RecordObject(so.targetObject, "Rename Track");
            trackProp.FindPropertyRelative("Name").stringValue = evt.newValue;
            so.ApplyModifiedProperties();
        });
        nameRow.Add(nameField);

        // 第二行:颜色 / 锁 / + / × 按钮行
        var btnRow = new VisualElement();
        btnRow.style.flexDirection = FlexDirection.Row;
        btnRow.style.marginTop = 4;
        btnRow.style.alignItems = Align.Center;
        header.Add(btnRow);

        // Lock button(纯文字)
        var lockProp = trackProp.FindPropertyRelative("Locked");
        bool initialLocked = lockProp.boolValue;
        var lockBtn = new Button { text = initialLocked ? "解锁" : "锁定" };
        lockBtn.clicked += () =>
        {
            Undo.RecordObject(so.targetObject, "Toggle Track Lock");
            lockProp.boolValue = !lockProp.boolValue;
            so.ApplyModifiedProperties();
            lockBtn.text = lockProp.boolValue ? "解锁" : "锁定";
        };
        lockBtn.style.marginLeft = 18;  // 对齐 Name(跳过 drag handle 宽度)
        lockBtn.style.marginRight = 4;
        btnRow.Add(lockBtn);

        // + 按钮(纯文字)
        var addActionBtn = new Button(() =>
        {
            var actionsProp = trackProp.FindPropertyRelative("Actions");
            Undo.RecordObject(so.targetObject, "Add Action");
            actionsProp.InsertArrayElementAtIndex(actionsProp.arraySize);
            var newAction = actionsProp.GetArrayElementAtIndex(actionsProp.arraySize - 1);
            newAction.FindPropertyRelative("CommandType").intValue = 0;
            float maxTrig = 0f;
            for (int i = 0; i < actionsProp.arraySize - 1; i++)
            {
                float t = actionsProp.GetArrayElementAtIndex(i).FindPropertyRelative("TriggerTime").floatValue;
                if (t > maxTrig) maxTrig = t;
            }
            newAction.FindPropertyRelative("TriggerTime").floatValue = maxTrig + 1f;
            newAction.FindPropertyRelative("GapsFromLastRepeat").arraySize = 0;
            so.ApplyModifiedProperties();
        }) { text = "+ Action" };
        addActionBtn.style.flexGrow = 1;
        addActionBtn.style.marginRight = 2;
        btnRow.Add(addActionBtn);

        // × 按钮(纯文字)
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
        delActionBtn.style.width = 28;
        btnRow.Add(delActionBtn);

        // 占位:删除 Wave / Wave 块删除按钮已由外层 WaveTimelineSection 提供

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