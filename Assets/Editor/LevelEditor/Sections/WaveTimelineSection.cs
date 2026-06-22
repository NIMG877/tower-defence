using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 横向波次时间线: 列出 Waves, 每条 Wave 一行; 每条 Action 按"绝对时间"渲染为卡片,
/// 无 gap 占位标记, 顶部有刻度 + 时间标签, 顶部有缩放滑块 (整体共享)。
/// 通过事件 ActionSelected 派发"用户选中"信号给 ActionDetailSection。
/// </summary>
public static class WaveTimelineSection
{
    // 整体缩放 (所有 Wave 共享, static)
    static float _zoom = 1f;

    public static VisualElement Build(
        SerializedObject so,
        Action<int, int> onActionSelected,
        Func<(int, int)> getCurrentSelection)
    {
        var section = new VisualElement();
        section.AddToClassList("level-editor-section");
        // 关键: 强制 section 宽度 = 父级 (body.content) 宽度,
        // 防止内部 cardsContainer (minWidth = maxTime*pxPerSec) 通过 ScrollView 把 section 撑大,
        // 进而让 zoom bar 的 slider 也跟着溢出。
        section.style.width = Length.Percent(100);

        var title = new Label("▸ 波次时间线");
        title.AddToClassList("level-editor-section-title");
        section.Add(title);

        var wavesProp = so.FindProperty("Waves");
        var wavesListContainer = new VisualElement();
        section.Add(wavesListContainer);

        // 重建: 在 zoom 变化 / wave 数组变化时调用
        Action rebuild = () => RebuildWaves(wavesListContainer, wavesProp, so, onActionSelected, getCurrentSelection);
        rebuild();

        // 缩放控件 (位于标题下、Wave 列表之上)
        section.Add(BuildZoomControls(rebuild));

        // 监听 Waves 数组变化 (Undo/Redo, 外部 mutation)
        section.TrackPropertyValue(wavesProp, _ => rebuild());

        // + 新增 Wave 按钮
        var addWaveBtn = new Button(() =>
        {
            Undo.RecordObject(so.targetObject, "Add Wave");
            wavesProp.InsertArrayElementAtIndex(wavesProp.arraySize);
            wavesProp.GetArrayElementAtIndex(wavesProp.arraySize - 1).FindPropertyRelative("Actions").arraySize = 0;
            so.ApplyModifiedProperties();
        })
        { text = "+ 新增 Wave" };
        addWaveBtn.style.marginTop = 6;
        section.Add(addWaveBtn);

        return section;
    }

    static void RebuildWaves(VisualElement container, SerializedProperty wavesProp, SerializedObject so, Action<int, int> onActionSelected, Func<(int, int)> getCurrentSelection)
    {
        container.Clear();
        for (int w = 0; w < wavesProp.arraySize; w++)
        {
            container.Add(BuildWaveRow(w, wavesProp.GetArrayElementAtIndex(w), so, onActionSelected, getCurrentSelection));
        }
    }

    static void RenderActionCards(VisualElement container, SerializedProperty actionsProp, int waveIdx, Action<int, int> onActionSelected)
    {
        container.Clear();
        if (actionsProp.arraySize == 0)
        {
            var empty = new Label("(空 · 0s)");
            empty.style.color = new Color(0.4f, 0.4f, 0.4f);
            empty.style.unityTextAlign = TextAnchor.MiddleCenter;
            empty.style.position = Position.Absolute;
            empty.style.left = 0;
            empty.style.right = 0;
            empty.style.top = 0;
            empty.style.bottom = 0;
            container.Add(empty);
            return;
        }

        float maxTime = ComputeMaxTime(actionsProp);
        float pxPerSec = PixelsPerSecond();
        float tickInterval = ChooseTickInterval(pxPerSec);
        float containerWidth = Mathf.Max(200f, maxTime * pxPerSec);
        container.style.minWidth = containerWidth;

        // 渲染刻度 (tick + label)
        for (float t = 0f; t <= maxTime + 0.001f; t += tickInterval)
        {
            float x = t * pxPerSec;
            var tick = new VisualElement();
            tick.style.position = Position.Absolute;
            tick.style.left = x;
            tick.style.top = 18;
            tick.style.bottom = 0;
            tick.style.width = 1;
            tick.style.backgroundColor = new Color(0.25f, 0.25f, 0.3f);
            container.Add(tick);

            var label = new Label($"{t:0.#}s");
            label.style.position = Position.Absolute;
            label.style.left = x + 2;
            label.style.top = 0;
            label.style.fontSize = 9;
            label.style.color = new Color(0.55f, 0.55f, 0.55f);
            container.Add(label);
        }

        // 渲染 action 卡片 (按绝对时间定位, 无 gap 标记)
        // action[i] 在 cumulativeTime 位置。
        // 下一个 action 的位置 = 当前 action 位置 + 下一个 action 的 GapFromLastAction
        // (因为 gap[i] 表示 "和前一个 action 的距离", 即 action[i] 距离 action[i-1] 的时间)
        float cumulativeTime = 0f;
        for (int i = 0; i < actionsProp.arraySize; i++)
        {
            var a = actionsProp.GetArrayElementAtIndex(i);
            int cmd = a.FindPropertyRelative("CommandType").intValue;
            float myGap = Mathf.Max(0f, a.FindPropertyRelative("GapFromLastAction").floatValue);

            float x = cumulativeTime * pxPerSec;
            int actionIndex = i; // 闭包按值捕获, 避免循环结束后 i 越界
            var card = new Button(() => onActionSelected?.Invoke(waveIdx, actionIndex))
            {
                text = $"A{i}"
            };
            card.style.position = Position.Absolute;
            card.style.left = x;
            card.style.top = 22;  // 位于刻度标签之下
            card.style.height = 28;
            card.style.width = 36;  // 固定宽度
            card.style.backgroundColor = CommandTypeColor(cmd);
            card.style.color = new Color(0, 0, 0);
            card.style.fontSize = 9;
            card.style.paddingLeft = 2;
            card.style.paddingRight = 2;
            container.Add(card);

            // 推进 cumulativeTime: 用下一个 action 的 gap (它表示"和前一个 action 的距离")
            // 即下一个 action 距离 action[i] 的时间
            if (i + 1 < actionsProp.arraySize)
            {
                var next = actionsProp.GetArrayElementAtIndex(i + 1);
                float nextGap = Mathf.Max(0f, next.FindPropertyRelative("GapFromLastAction").floatValue);
                cumulativeTime += nextGap;
            }
        }
    }

    static float ComputeMaxTime(SerializedProperty actionsProp)
    {
        float total = 0f;
        // Skip gap[0] (meaningless under "from previous" interpretation)
        for (int i = 1; i < actionsProp.arraySize; i++)
        {
            var a = actionsProp.GetArrayElementAtIndex(i);
            total += Mathf.Max(0f, a.FindPropertyRelative("GapFromLastAction").floatValue);
        }
        return Mathf.Max(1f, total);
    }

    static float ChooseTickInterval(float pxPerSec)
    {
        // 目标: 刻度之间约 80 像素 (zoom 越大刻度越细)
        const float targetPxBetweenTicks = 80f;
        float ideal = targetPxBetweenTicks / Mathf.Max(1f, pxPerSec);
        float[] candidates = { 0.1f, 0.2f, 0.5f, 1f, 2f, 5f, 10f, 15f, 30f, 60f, 120f, 300f, 600f };
        foreach (var c in candidates)
        {
            if (c >= ideal) return c;
        }
        return candidates[candidates.Length - 1];
    }

    static float PixelsPerSecond() => 24f * _zoom;  // 基础刻度 × zoom

    static VisualElement BuildZoomControls(Action onZoomChanged)
    {
        var bar = new VisualElement();
        bar.style.flexDirection = FlexDirection.Row;
        bar.style.alignItems = Align.Center;
        bar.style.marginBottom = 8;
        bar.style.paddingLeft = 6;
        bar.style.paddingRight = 6;
        bar.style.maxWidth = Length.Percent(100);  // 防止 bar 超出父级宽度

        var zoomLabel = new Label($"缩放 {_zoom:0.0}x");
        zoomLabel.style.fontSize = 11;
        zoomLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
        zoomLabel.style.width = 70;
        bar.Add(zoomLabel);

        var slider = new Slider(0.5f, 5f) { value = _zoom };
        slider.style.flexGrow = 1;
        slider.showInputField = false;
        slider.RegisterValueChangedCallback(evt =>
        {
            _zoom = evt.newValue;
            zoomLabel.text = $"缩放 {_zoom:0.0}x";
            onZoomChanged?.Invoke();
        });
        bar.Add(slider);

        var resetBtn = new Button(() =>
        {
            _zoom = 1f;
            slider.SetValueWithoutNotify(1f);
            zoomLabel.text = "缩放 1.0x";
            onZoomChanged?.Invoke();
        }) { text = "重置" };
        resetBtn.style.marginLeft = 6;
        bar.Add(resetBtn);

        return bar;
    }

    static string CommandTypeShort(int cmd)
    {
        switch (cmd)
        {
            case 0: return "spawn";
            case 1: return "static";
            case 2: return "path↑";
            case 3: return "path↗";
            case 4: return "path→";
            case 5: return "dialog";
            case 6: return "panel";
            default: return $"c{cmd}";
        }
    }

    static Color CommandTypeColor(int cmd)
    {
        switch (cmd)
        {
            case 0: return new Color(0.3f, 0.8f, 0.6f);   // 绿
            case 1: return new Color(0.3f, 0.6f, 0.9f);   // 蓝
            case 2:
            case 3:
            case 4: return new Color(0.86f, 0.8f, 0.66f);  // 黄
            case 5: return new Color(0.77f, 0.52f, 0.75f); // 紫
            case 6: return new Color(0.95f, 0.5f, 0.5f);   // 红
            default: return new Color(0.5f, 0.5f, 0.5f);
        }
    }

    static VisualElement BuildWaveRow(int waveIdx, SerializedProperty waveProp, SerializedObject so, Action<int, int> onActionSelected, Func<(int, int)> getCurrentSelection)
    {
        var row = new VisualElement();
        row.style.backgroundColor = new Color(0.15f, 0.15f, 0.18f);
        row.style.borderTopLeftRadius = 3;
        row.style.borderTopRightRadius = 3;
        row.style.borderBottomLeftRadius = 3;
        row.style.borderBottomRightRadius = 3;
        row.style.paddingTop = 4;
        row.style.paddingBottom = 4;
        row.style.paddingLeft = 6;
        row.style.paddingRight = 6;
        row.style.marginBottom = 6;

        // Wave 标签
        var actionsProp = waveProp.FindPropertyRelative("Actions");
        var label = new Label($"Wave {waveIdx} · {actionsProp.arraySize} actions");
        label.style.color = new Color(0.8f, 0.8f, 0.8f);
        label.style.fontSize = 11;
        label.style.marginBottom = 4;
        row.Add(label);

        // 横向 ScrollView (timeline 超出 inspector 宽度时可滚动)
        var timelineScroll = new ScrollView(ScrollViewMode.Horizontal);
        timelineScroll.style.height = 66;
        timelineScroll.style.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
        timelineScroll.style.borderTopLeftRadius = 3;
        timelineScroll.style.borderTopRightRadius = 3;
        timelineScroll.style.borderBottomLeftRadius = 3;
        timelineScroll.style.borderBottomRightRadius = 3;
        timelineScroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
        timelineScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;  // 仅水平滚动
        row.Add(timelineScroll);

        var cardsContainer = new VisualElement();
        cardsContainer.style.height = 66;
        cardsContainer.style.position = Position.Relative;
        cardsContainer.style.overflow = Overflow.Visible;
        timelineScroll.Add(cardsContainer);  // ScrollView 自动添加到 contentContainer

        RenderActionCards(cardsContainer, actionsProp, waveIdx, onActionSelected);
        cardsContainer.TrackPropertyValue(actionsProp, _ => RenderActionCards(cardsContainer, actionsProp, waveIdx, onActionSelected));

        // 按钮行 (新增 Action / 删除 Action / 删除 Wave 横向排列)
        var buttonRow = new VisualElement();
        buttonRow.style.flexDirection = FlexDirection.Row;
        buttonRow.style.marginTop = 4;

        // + 新增 Action 按钮
        var addActionBtn = new Button(() =>
        {
            Undo.RecordObject(so.targetObject, "Add Action");
            actionsProp.InsertArrayElementAtIndex(actionsProp.arraySize);
            actionsProp.GetArrayElementAtIndex(actionsProp.arraySize - 1).FindPropertyRelative("CommandType").intValue = 0;
            so.ApplyModifiedProperties();
        })
        { text = "+ 新增 Action" };
        addActionBtn.style.flexGrow = 1;
        addActionBtn.style.marginRight = 4;
        buttonRow.Add(addActionBtn);

        // × 删除 Action 按钮 (删除最后一个 action)
        var delActionBtn = new Button(() =>
        {
            if (actionsProp.arraySize == 0) return;
            // 判定: 被删的是 arraySize-1, 若当前选中 === (waveIdx, arraySize-1) 则会失效
            var (selWave, selAction) = getCurrentSelection();
            bool willInvalidate = selWave == waveIdx && selAction == actionsProp.arraySize - 1;
            if (EditorUtility.DisplayDialog("删除 Action", $"确认删除最后一个 Action? (当前 {actionsProp.arraySize} 个)", "删除", "取消"))
            {
                Undo.RecordObject(so.targetObject, "Delete Action");
                actionsProp.DeleteArrayElementAtIndex(actionsProp.arraySize - 1);
                so.ApplyModifiedProperties();
                if (willInvalidate) onActionSelected?.Invoke(-1, -1);
            }
        })
        { text = "× 删除 Action" };
        delActionBtn.style.marginRight = 4;
        buttonRow.Add(delActionBtn);

        // × 删除 Wave 按钮
        var delBtn = new Button(() =>
        {
            // 判定: 删 waveIdx 会让 >= waveIdx 的所有 wave 索引下移, 当前选中在此范围内即失效
            var (selWave, _) = getCurrentSelection();
            bool willInvalidate = selWave >= 0 && waveIdx <= selWave;
            if (EditorUtility.DisplayDialog("删除 Wave", $"确认删除 Wave {waveIdx}?", "删除", "取消"))
            {
                Undo.RecordObject(so.targetObject, "Delete Wave");
                waveProp.serializedObject.FindProperty("Waves").DeleteArrayElementAtIndex(waveIdx);
                so.ApplyModifiedProperties();
                if (willInvalidate) onActionSelected?.Invoke(-1, -1);
            }
        })
        { text = "× 删除 Wave" };
        buttonRow.Add(delBtn);

        row.Add(buttonRow);

        return row;
    }
}
