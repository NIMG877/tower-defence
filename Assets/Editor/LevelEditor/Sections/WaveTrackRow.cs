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
        public Button LockButton;
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

        // === 左侧:轨道头 (15% 总宽) ===
        var header = new VisualElement();
        header.style.width = new Length(15, LengthUnit.Percent);
        header.style.flexShrink = 0;
        header.style.flexDirection = FlexDirection.Column;
        header.style.backgroundColor = new Color(0.13f, 0.13f, 0.16f);
        header.style.paddingTop = 4;
        header.style.paddingBottom = 4;
        header.style.paddingLeft = 6;
        header.style.paddingRight = 6;
        header.style.borderTopLeftRadius = 3;
        header.style.borderBottomLeftRadius = 3;
        row.Add(header);

        // === 右栏:时间轴 (85% 总宽) ===
        // timelineScroll 在下方单独设置 width=85%

        // 第一行:Name 输入框 (100% header 宽)
        var nameRow = new VisualElement();
        nameRow.style.flexDirection = FlexDirection.Row;
        nameRow.style.alignItems = Align.Center;
        header.Add(nameRow);

        // Name(可编辑)——占满 nameRow 全部宽度(用户要求 100%)
        var nameField = new TextField { value = trackProp.FindPropertyRelative("Name").stringValue };
        nameField.label = "";   // 隐藏内置 Label,留出全部宽度给输入区
        nameField.style.flexGrow = 1;
        nameField.style.flexBasis = 0;
        nameField.style.minWidth = 0;
        nameField.RegisterValueChangedCallback(evt =>
        {
            Undo.RecordObject(so.targetObject, "Rename Track");
            trackProp.FindPropertyRelative("Name").stringValue = evt.newValue;
            so.ApplyModifiedProperties();
        });
        nameRow.Add(nameField);

        // 第二行:锁定 40% / + 25% / × 25%
        var btnRow = new VisualElement();
        btnRow.style.flexDirection = FlexDirection.Row;
        btnRow.style.marginTop = 4;
        btnRow.style.alignItems = Align.Center;
        header.Add(btnRow);

        // 锁定 40%
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
        // 用 flexGrow(2/1/1) + flexBasis(0) 实现"扣除 margin 后按 50/25/25 分剩余宽度"
        // 比直接 width% + marginRight 更精确——margin 不挤压按钮视觉宽度
        lockBtn.style.flexGrow = 2;
        lockBtn.style.flexBasis = 0;
        lockBtn.style.flexShrink = 0;
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
        }) { text = "+" };
        addActionBtn.style.flexGrow = 1;
        addActionBtn.style.flexBasis = 0;
        addActionBtn.style.flexShrink = 0;
        addActionBtn.style.marginRight = 4;
        btnRow.Add(addActionBtn);

        // × 25%
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
        delActionBtn.style.flexBasis = 0;
        delActionBtn.style.flexShrink = 0;
        btnRow.Add(delActionBtn);

        // 占位:删除 Wave / Wave 块删除按钮已由外层 WaveTimelineSection 提供

        header.Add(btnRow);

        // === 右侧:时间轴 ===
        var timelineScroll = new ScrollView(ScrollViewMode.Horizontal);
        timelineScroll.style.width = new Length(85, LengthUnit.Percent);  // 右栏占总宽 85%
        timelineScroll.style.flexGrow = 0;
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
            LockButton = lockBtn,
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