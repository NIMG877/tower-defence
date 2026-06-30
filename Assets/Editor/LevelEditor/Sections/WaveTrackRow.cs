using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 单条轨道行:左侧轨道头(Name / 激活 / + / ×),右侧时间轴 + Action 卡片。
/// 构造时只建一次结构,字段级变更走增量更新(通过 TrackPropertyValue 触发 Rebuild)。
/// </summary>
public static class WaveTrackRow
{
    public class State
    {
        public TextField NameField;
        public Button LockButton;
        public Button DelActionBtn;            // 删除 Action 按钮(供 WaveTimelineSection 刷新 enabled)
        public VisualElement CardsContainer;  // 时间轴容器
        public ScrollView TimelineScroll;      // 时间轴 ScrollView(供 WaveTimelineSection 同步横向滚动)
        public VisualElement DetailContainer;  // 详情容器(选中 Action 时把 ActionDetailSection 塞这里)
        public Func<float> GetPxPerSec;        // 拿本 Wave 当前 pxPerSec(每 Wave 独立缩放)
        public SerializedProperty ActionsProp; // Actions 数组属性(供 RefreshDelActionBtnStates 判断 selA 越界)
        public int WaveIdx;                    // 所属 Wave(供 WaveTimelineSection 刷新 enabled 时匹配 selW)
        public int TrackIdx;                   // 所属 Track(同上,匹配 selT)
        public Func<bool> IsLocked;            // 本 Track 是否 Locked(供 WaveActionCard.Render 判断未激活描边)
        public Action<int, int, int> OnActionSelected;  // 卡片点击回调(供 RefreshActionCardBorders 重渲染)
        public Func<(int, int, int)> GetCurrentSelection;  // 当前选中(供 WaveActionCard.Render 判断高亮)
    }

    public static VisualElement Build(
        int waveIdx,
        int trackIdx,
        SerializedProperty tracksProp,
        SerializedProperty trackProp,
        SerializedObject so,
        Action<int, int, int> onActionSelected,
        Func<(int, int, int)> getCurrentSelection,
        Func<float> getPxPerSec)
    {
        var row = new VisualElement();
        row.AddToClassList("level-editor-section");
        row.style.flexDirection = FlexDirection.Column;  // 上下两行:contentRow(× + header + timeline) + detailContainer
        row.style.marginBottom = 4;

        // contentRow:承载 × 删除 / header / timelineScroll 三栏(原 Row 布局)
        var contentRow = new VisualElement();
        contentRow.style.flexDirection = FlexDirection.Row;
        row.Add(contentRow);

        // === 最左:× 删除 Track (与 header / timelineScroll 按 2:13:85 分总宽) ===
        var delTrackBtn = new Button(() =>
        {
            if (EditorUtility.DisplayDialog("删除 Track", $"确认删除 Track {trackIdx}?", "删除", "取消"))
            {
                Undo.RecordObject(so.targetObject, "Delete Track");
                tracksProp.DeleteArrayElementAtIndex(trackIdx);
                so.ApplyModifiedProperties();
                onActionSelected?.Invoke(-1, -1, -1);  // 选中必然失效
            }
        })
        { text = "×" };
        delTrackBtn.style.flexGrow = 1;
        delTrackBtn.style.flexBasis = 0;
        delTrackBtn.style.flexShrink = 0;
        delTrackBtn.style.height = 40;
        contentRow.Add(delTrackBtn);

        // === 中部:轨道头 (13% 总宽) ===
        var header = new VisualElement();
        header.style.flexGrow = 20;
        header.style.flexBasis = 0;
        header.style.flexShrink = 0;
        header.style.flexDirection = FlexDirection.Column;
        header.style.backgroundColor = new Color(0.13f, 0.13f, 0.16f);
        header.style.height = 40;
        header.style.borderTopLeftRadius = 3;
        header.style.borderBottomLeftRadius = 3;
        contentRow.Add(header);

        // === 右栏:时间轴 (80% 总宽) ===
        // timelineScroll 在下方单独设置 width=80%

        // 第一行:Name 输入框 (100% header 宽)
        var nameRow = new VisualElement();
        nameRow.style.flexDirection = FlexDirection.Row;
        nameRow.style.alignItems = Align.Center;
        nameRow.style.marginRight = 4;
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

        // 第二行:激活开关 40% / + 25% / × 25%
        var btnRow = new VisualElement();
        btnRow.style.flexDirection = FlexDirection.Row;
        btnRow.style.marginRight = 4;
        btnRow.style.alignItems = Align.Center;
        header.Add(btnRow);

        // 激活开关 40%(Locked=true → 未激活,运行时该 Track 的 Action 不被加载)
        var lockProp = trackProp.FindPropertyRelative("Locked");
        bool initialLocked = lockProp.boolValue;
        var lockBtn = new Button { text = initialLocked ? "激活" : "不激活" };
        lockBtn.clicked += () =>
        {
            Undo.RecordObject(so.targetObject, "Toggle Track Active");
            lockProp.boolValue = !lockProp.boolValue;
            so.ApplyModifiedProperties();
            lockBtn.text = lockProp.boolValue ? "激活" : "不激活";
        };
        // 用 flexGrow(2/1/1) + flexBasis(0) 实现"扣除 margin 后按 50/25/25 分剩余宽度"
        // 比直接 width% + marginRight 更精确——margin 不挤压按钮视觉宽度
        lockBtn.style.flexGrow = 2;
        lockBtn.style.flexBasis = 0;
        lockBtn.style.flexShrink = 0;
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
        btnRow.Add(addActionBtn);

        // × 25%:删除选中的 Action(没选中 → disabled)
        var actionsProp0 = trackProp.FindPropertyRelative("Actions");
        var delActionBtn = new Button(() =>
        {
            var (selW, selT, selA) = getCurrentSelection();
            if (selW != waveIdx || selT != trackIdx || selA < 0 || selA >= actionsProp0.arraySize) return;  // 防御
            if (EditorUtility.DisplayDialog("删除 Action", $"确认删除 Track {trackIdx} 的 Action {selA}?", "删除", "取消"))
            {
                Undo.RecordObject(so.targetObject, "Delete Action");
                actionsProp0.DeleteArrayElementAtIndex(selA);
                so.ApplyModifiedProperties();
                onActionSelected?.Invoke(-1, -1, -1);  // 删除后无选中 → 触发 RefreshDelActionBtnStates 全 disabled
            }
        }) { text = "×" };
        delActionBtn.style.flexGrow = 1;
        delActionBtn.style.flexBasis = 0;
        delActionBtn.style.flexShrink = 0;
        delActionBtn.style.marginRight = 0;
        // 初始 enabled 状态(创建时按当前 _selectedAction 判断)
        var (initSelW, initSelT, initSelA) = getCurrentSelection();
        bool initiallyEnabled = initSelW == waveIdx && initSelT == trackIdx && initSelA >= 0 && initSelA < actionsProp0.arraySize;
        delActionBtn.SetEnabled(initiallyEnabled);
        // userData 和 RegisterDelActionBtn 移到下面 state 实例化之后(state 还未声明)
        btnRow.Add(delActionBtn);

        // 占位:删除 Wave / Wave 块删除按钮已由外层 WaveTimelineSection 提供

        header.Add(btnRow);

        // === 右侧:时间轴 ===
        var timelineScroll = new ScrollView(ScrollViewMode.Horizontal);
        timelineScroll.style.flexGrow = 90;
        timelineScroll.style.flexBasis = 0;
        timelineScroll.style.flexShrink = 0;
        timelineScroll.style.height = 40;
        timelineScroll.style.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
        timelineScroll.style.borderTopRightRadius = 3;
        timelineScroll.style.borderBottomRightRadius = 3;
        timelineScroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
        timelineScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        contentRow.Add(timelineScroll);

        var cardsContainer = new VisualElement();
        cardsContainer.style.height = 30;
        cardsContainer.style.position = Position.Relative;
        cardsContainer.style.overflow = Overflow.Visible;
        timelineScroll.Add(cardsContainer);

        // 详情容器:选中 Action 时显示(在 contentRow 下方),默认折叠
        var detailContainer = new VisualElement();
        detailContainer.style.display = DisplayStyle.None;
        detailContainer.style.marginTop = 2;
        detailContainer.style.width = Length.Percent(100);
        row.Add(detailContainer);

        // Track 状态对象(后续 Phase 3 增量更新用)
        var state = new State
        {
            NameField = nameField,
            LockButton = lockBtn,
            DelActionBtn = delActionBtn,
            CardsContainer = cardsContainer,
            TimelineScroll = timelineScroll,
            DetailContainer = detailContainer,
            GetPxPerSec = getPxPerSec,
            ActionsProp = actionsProp0,
            WaveIdx = waveIdx,
            TrackIdx = trackIdx,
            IsLocked = () => lockProp.boolValue,
            OnActionSelected = onActionSelected,
            GetCurrentSelection = getCurrentSelection,
        };
        delActionBtn.userData = state;  // WaveTimelineSection.RefreshDelActionBtnStates 通过 userData 拿 WaveIdx/TrackIdx/ActionsProp
        WaveTimelineSection.RegisterDelActionBtn(delActionBtn);  // 注册到全局供选中变化时刷新
        WaveTimelineSection.RegisterTrackDetailContainer(waveIdx, trackIdx, detailContainer);  // 注册 detail 容器,ShowDetail 时按 (waveIdx, trackIdx) 找
        WaveTimelineSection.RegisterTrackState(state);  // 注册到全局,选中变化时刷新所有卡片的边框高亮
        cardsContainer.userData = state;
        row.userData = state;   // 暴露给 WaveTimelineSection 用于横向滚动同步

        // 卡片渲染(委托给 WaveActionCard)
        var actionsProp = trackProp.FindPropertyRelative("Actions");
        WaveActionCard.Render(cardsContainer, actionsProp, waveIdx, trackIdx, onActionSelected, () => lockProp.boolValue, getPxPerSec, getCurrentSelection);
        cardsContainer.TrackPropertyValue(actionsProp, _ =>
            WaveActionCard.Render(cardsContainer, actionsProp, waveIdx, trackIdx, onActionSelected, () => lockProp.boolValue, getPxPerSec, getCurrentSelection));

        return row;
    }
}