using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 时间刻度尺:在传入的 scaleContent 内 (Position.Absolute, 顶部 0..18px) 渲染 tick 竖线 + 时间标签。
/// scaleContent 是 Wave 顶部共享刻度 ScrollView 的 contentContainer,横向滚动由外层 ScrollView 处理。
/// 复用 tick / label 元素,只增删到所需数量。
/// </summary>
public static class WaveScaleBar
{
    public class State
    {
        public List<VisualElement> TickLines = new();
        public List<Label> TickLabels = new();
    }

    public static void Render(VisualElement scaleContent, float maxTime, float pxPerSec)
    {
        var state = scaleContent.userData as State;
        if (state == null)
        {
            state = new State();
            scaleContent.userData = state;
        }

        float tickInterval = WaveTimelineSection.ChooseTickInterval(pxPerSec);
        int requiredTickCount = 0;
        for (float t = 0f; t <= maxTime + 0.001f; t += tickInterval) requiredTickCount++;

        // 增删 tick / label 元素 (复用,只调整 left + text)
        while (state.TickLines.Count < requiredTickCount)
        {
            var tick = new VisualElement();
            tick.style.position = Position.Absolute;
            tick.style.top = 18;
            tick.style.bottom = 0;
            tick.style.width = 1;
            tick.style.backgroundColor = new Color(0.25f, 0.25f, 0.3f);
            tick.pickingMode = PickingMode.Ignore;
            scaleContent.Add(tick);
            state.TickLines.Add(tick);

            var label = new Label();
            label.style.position = Position.Absolute;
            label.style.top = 0;
            label.style.fontSize = 9;
            label.style.color = new Color(0.55f, 0.55f, 0.55f);
            label.pickingMode = PickingMode.Ignore;
            scaleContent.Add(label);
            state.TickLabels.Add(label);
        }
        while (state.TickLines.Count > requiredTickCount)
        {
            state.TickLines[state.TickLines.Count - 1].RemoveFromHierarchy();
            state.TickLines.RemoveAt(state.TickLines.Count - 1);
            state.TickLabels[state.TickLabels.Count - 1].RemoveFromHierarchy();
            state.TickLabels.RemoveAt(state.TickLabels.Count - 1);
        }

        // 设置位置 + 文案
        int ti = 0;
        for (float t = 0f; t <= maxTime + 0.001f; t += tickInterval)
        {
            float x = t * pxPerSec;
            state.TickLines[ti].style.left = x;
            state.TickLabels[ti].style.left = x + 2;
            state.TickLabels[ti].text = $"{t:0.#}s";
            ti++;
        }
    }
}