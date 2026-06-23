using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 横向波次时间线: 列出 Waves, 每条 Wave 一行; 每条 Action 按"绝对时间"渲染为卡片,
/// 无 gap 占位标记, 顶部有刻度 + 时间标签, 顶部有缩放滑块 (整体共享, 拖到 1.0x 即复位)。
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
        // 防止内部 cardsContainer (minWidth = maxTime*pxPerSec) 通过 ScrollView 把 section 撑大。
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

        // 监听 Waves 数组结构变化 (add/delete wave, Undo/Redo) — 字段级变更不再触发整条 timeline 重建
        int lastWaveCount = wavesProp.arraySize;
        section.TrackPropertyValue(wavesProp, _ =>
        {
            if (wavesProp.arraySize != lastWaveCount)
            {
                lastWaveCount = wavesProp.arraySize;
                rebuild();
            }
        });

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

    // 存于 cardsContainer.userData — 增量更新用, 避免任何字段变更都重建整树
    class WaveTimelineState
    {
        public List<VisualElement> tickLines = new();
        public List<Label> tickLabels = new();
        public List<Button> cards = new();
        public VisualElement emptyLabel;
        public int lastActionCount = -1;
    }

    static void RenderActionCards(VisualElement container, SerializedProperty actionsProp, int waveIdx, Action<int, int> onActionSelected)
    {
        var state = container.userData as WaveTimelineState;
        if (state == null)
        {
            state = new WaveTimelineState();
            container.userData = state;
        }

        // 结构性变更 (add/delete action) 才做整树重建; 字段级变更走下面的增量更新
        if (state.lastActionCount != actionsProp.arraySize)
        {
            container.Clear();
            state.tickLines.Clear();
            state.tickLabels.Clear();
            state.cards.Clear();
            state.emptyLabel = null;

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
                state.emptyLabel = empty;
            }
            else
            {
                // 先把 card 元素都创建出来, 位置 / 颜色在下面增量更新里设
                for (int i = 0; i < actionsProp.arraySize; i++)
                {
                    int actionIndex = i; // 闭包按值捕获
                    var card = new Button(() => onActionSelected?.Invoke(waveIdx, actionIndex))
                    {
                        text = $"A{i}"
                    };
                    card.style.position = Position.Absolute;
                    card.style.top = 22;
                    card.style.height = 28;
                    card.style.width = 36;
                    card.style.color = new Color(0, 0, 0);
                    card.style.fontSize = 9;
                    card.style.paddingLeft = 2;
                    card.style.paddingRight = 2;
                    container.Add(card);
                    state.cards.Add(card);
                }
            }

            state.lastActionCount = actionsProp.arraySize;
        }

        if (actionsProp.arraySize == 0) return;

        // === 增量更新 (字段级变更走这里, O(actions + ticks) 次 style 赋值) ===

        float maxTime = ComputeMaxTime(actionsProp);
        float pxPerSec = PixelsPerSecond();
        float tickInterval = ChooseTickInterval(pxPerSec);
        float containerWidth = Mathf.Max(200f, maxTime * pxPerSec);
        container.style.minWidth = containerWidth;

        // ticks: 复用现有元素, 数量变化时再增删
        int requiredTickCount = 0;
        for (float t = 0f; t <= maxTime + 0.001f; t += tickInterval) requiredTickCount++;

        while (state.tickLines.Count < requiredTickCount)
        {
            var tick = new VisualElement();
            tick.style.position = Position.Absolute;
            tick.style.top = 18;
            tick.style.bottom = 0;
            tick.style.width = 1;
            tick.style.backgroundColor = new Color(0.25f, 0.25f, 0.3f);
            container.Add(tick);
            state.tickLines.Add(tick);

            var label = new Label();
            label.style.position = Position.Absolute;
            label.style.top = 0;
            label.style.fontSize = 9;
            label.style.color = new Color(0.55f, 0.55f, 0.55f);
            container.Add(label);
            state.tickLabels.Add(label);
        }
        while (state.tickLines.Count > requiredTickCount)
        {
            state.tickLines[state.tickLines.Count - 1].RemoveFromHierarchy();
            state.tickLines.RemoveAt(state.tickLines.Count - 1);
            state.tickLabels[state.tickLabels.Count - 1].RemoveFromHierarchy();
            state.tickLabels.RemoveAt(state.tickLabels.Count - 1);
        }
        int ti = 0;
        for (float t = 0f; t <= maxTime + 0.001f; t += tickInterval)
        {
            float x = t * pxPerSec;
            state.tickLines[ti].style.left = x;
            state.tickLabels[ti].style.left = x + 2;
            state.tickLabels[ti].text = $"{t:0.#}s";
            ti++;
        }

        // cards: 就地更新 left + CommandType 颜色
        float cumulativeTime = 0f;
        for (int i = 0; i < actionsProp.arraySize; i++)
        {
            var a = actionsProp.GetArrayElementAtIndex(i);
            var card = state.cards[i];
            card.style.left = cumulativeTime * pxPerSec;

            int cmd = a.FindPropertyRelative("CommandType").intValue;
            card.style.backgroundColor = CommandTypeColor(cmd);

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

        var zoomLabel = new Label($"缩放 {_zoom:0.0}x");
        zoomLabel.style.fontSize = 11;
        zoomLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
        zoomLabel.style.width = 70;
        bar.Add(zoomLabel);

        var slider = new Slider(0.1f, 5f) { value = _zoom };
        slider.style.flexGrow = 1;
        slider.style.flexBasis = 0;       // 关键: 从 0 开始 grow, 不让 slider 内部参考宽度污染 bar 的 intrinsic
        slider.style.minWidth = 0;       // 允许 shrink 到 0, 防止 Unity 默认 min-width 顶住
        slider.showInputField = false;
        slider.RegisterValueChangedCallback(evt =>
        {
            _zoom = evt.newValue;
            zoomLabel.text = $"缩放 {_zoom:0.0}x";
            onZoomChanged?.Invoke();
        });
        bar.Add(slider);

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
