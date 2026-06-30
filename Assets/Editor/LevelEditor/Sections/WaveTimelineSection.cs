using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 横向波次时间线:列出 Waves, 每条 Wave 包含 N 条 Track; 每条 Track 是一行(WaveTrackRow)。
/// 通过事件 ActionSelected 派发"用户选中"信号给 ActionDetailSection。
/// </summary>
public static class WaveTimelineSection
{
    // 每个 Wave 独立的缩放(在 BuildWaveBlock 内定义为局部变量 _zoom,各 Wave 互不影响)
// 所有 Track 的删除 Action 按钮(供选中状态变化时刷新 enabled)
    static readonly List<Button> _allDelActionBtns = new();

    public static VisualElement Build(
        SerializedObject so,
        Action<int, int, int> onActionSelected,
        Func<(int, int, int)> getCurrentSelection)
    {
        var section = new VisualElement();
        section.AddToClassList("level-editor-section");
        section.style.width = Length.Percent(100);

        var title = new Label("▸ 波次时间线");
        title.AddToClassList("level-editor-section-title");
        section.Add(title);

        var wavesProp = so.FindProperty("Waves");
        var wavesListContainer = new VisualElement();
        section.Add(wavesListContainer);

        Action rebuild = () => RebuildWaves(wavesListContainer, wavesProp, so, onActionSelected, getCurrentSelection);
        rebuild();

        // 顶层按钮行:[ + 新增 Wave | × 删除 Wave ] (flexGrow 1:1,同宽)
        var topBtnRow = new VisualElement();
        topBtnRow.style.flexDirection = FlexDirection.Row;
        topBtnRow.style.marginTop = 6;

        var addWaveBtn = new Button(() =>
        {
            Undo.RecordObject(so.targetObject, "Add Wave");
            wavesProp.InsertArrayElementAtIndex(wavesProp.arraySize);
            var newWave = wavesProp.GetArrayElementAtIndex(wavesProp.arraySize - 1);
            newWave.FindPropertyRelative("Tracks").arraySize = 0;
            so.ApplyModifiedProperties();
        })
        { text = "+ 新增 Wave" };
        addWaveBtn.style.flexGrow = 1;
        addWaveBtn.style.flexBasis = 0;
        addWaveBtn.style.flexShrink = 0;
        addWaveBtn.style.marginRight = 4;
        topBtnRow.Add(addWaveBtn);

        // 顶层删除 Wave:删 selW(选中 Action 所属的 Wave)
        var delWaveBtn = new Button(() =>
        {
            var (selW, _, _) = getCurrentSelection();
            if (selW < 0 || selW >= wavesProp.arraySize)
            {
                EditorUtility.DisplayDialog("删除 Wave", "请先选中一个 Wave 内的 Action 再删除。", "确定");
                return;
            }
            if (EditorUtility.DisplayDialog("删除 Wave", $"确认删除 Wave {selW}?", "删除", "取消"))
            {
                Undo.RecordObject(so.targetObject, "Delete Wave");
                wavesProp.DeleteArrayElementAtIndex(selW);
                so.ApplyModifiedProperties();
                onActionSelected?.Invoke(-1, -1, -1);  // 选中必然失效
            }
        })
        { text = "× 删除 Wave" };
        delWaveBtn.style.flexGrow = 1;
        delWaveBtn.style.flexBasis = 0;
        delWaveBtn.style.flexShrink = 0;
        topBtnRow.Add(delWaveBtn);

        section.Add(topBtnRow);

        int lastWaveCount = wavesProp.arraySize;
        section.TrackPropertyValue(wavesProp, _ =>
        {
            if (wavesProp.arraySize != lastWaveCount)
            {
                lastWaveCount = wavesProp.arraySize;
                rebuild();
            }
        });

        return section;
    }

    static void RebuildWaves(VisualElement container, SerializedProperty wavesProp, SerializedObject so, Action<int, int, int> onActionSelected, Func<(int, int, int)> getCurrentSelection)
    {
        container.Clear();
        for (int w = 0; w < wavesProp.arraySize; w++)
        {
            container.Add(BuildWaveBlock(w, wavesProp.GetArrayElementAtIndex(w), so, onActionSelected, getCurrentSelection));
        }
    }

    static VisualElement BuildWaveBlock(int waveIdx, SerializedProperty waveProp, SerializedObject so, Action<int, int, int> onActionSelected, Func<(int, int, int)> getCurrentSelection)
    {
        var block = new VisualElement();
        block.style.backgroundColor = new Color(0.15f, 0.15f, 0.18f);
        block.style.borderTopLeftRadius = 3;
        block.style.borderTopRightRadius = 3;
        block.style.borderBottomLeftRadius = 3;
        block.style.borderBottomRightRadius = 3;
        block.style.paddingTop = 4;
        block.style.paddingBottom = 4;
        block.style.paddingLeft = 6;
        block.style.paddingRight = 6;
        block.style.marginBottom = 8;

        // Wave 标签
        var tracksProp = waveProp.FindPropertyRelative("Tracks");
        float _zoom = 1f;  // 本 Wave 独立的缩放,Slider 改它只影响本 Wave
        var label = new Label($"Wave {waveIdx} · {tracksProp.arraySize} tracks");
        label.style.color = new Color(0.8f, 0.8f, 0.8f);
        label.style.fontSize = 11;
        label.style.marginBottom = 4;
        block.Add(label);

        // === 顶部共享刻度尺 ScrollView (sticky 不随竖滚,横向滚动与所有 Track 同步) ===
        // 镜像 Track 行的 15:85 flex 布局:左侧 15% header 列(放 "时间轴" Label),右侧 85% 放 scaleScroll
        var scaleRow = new VisualElement();
        scaleRow.style.flexDirection = FlexDirection.Row;
        scaleRow.style.marginBottom = 2;
        block.Add(scaleRow);

        // 左侧 15% - "时间轴" label (与 Track 行的 header 列对齐)
        var scaleHeader = new VisualElement();
        scaleHeader.style.flexGrow = 15;
        scaleHeader.style.flexBasis = 0;
        scaleHeader.style.flexShrink = 0;
        scaleHeader.style.flexDirection = FlexDirection.Column;
        scaleHeader.style.borderTopLeftRadius = 3;
        scaleHeader.style.borderBottomLeftRadius = 3;
        scaleRow.Add(scaleHeader);

        var timeAxisLabel = new Label("时间轴");
        timeAxisLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
        timeAxisLabel.style.fontSize = 10;
        timeAxisLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        timeAxisLabel.style.marginTop = 4;
        scaleHeader.Add(timeAxisLabel);

        // 右侧 85% - scaleScroll (与 Track 行的 timelineScroll 对齐)
        var scaleScroll = new ScrollView(ScrollViewMode.Horizontal);
        scaleScroll.style.flexGrow = 85;
        scaleScroll.style.flexBasis = 0;
        scaleScroll.style.flexShrink = 0;
        scaleScroll.style.height = 22;
        scaleScroll.style.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
        scaleScroll.style.borderTopRightRadius = 3;
        scaleScroll.style.borderBottomRightRadius = 3;
        scaleScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;  // 顶部不显示滚动条 (滚动靠 Track)
        scaleScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        scaleRow.Add(scaleScroll);

        var scaleContent = new VisualElement();
        scaleContent.style.height = 22;
        scaleContent.style.position = Position.Relative;
        scaleScroll.Add(scaleContent);

        // 各 Track 行
        var tracksContainer = new VisualElement();
        block.Add(tracksContainer);

        // 同步横向滚动:scaleScroll + 所有 Track 的 timelineScroll 共享 scrollOffset
        var syncedScrollViews = new List<ScrollView> { scaleScroll };
        bool syncing = false;
        void WireScrollSync()
        {
            foreach (var sv in syncedScrollViews)
            {
                sv.horizontalScroller.valueChanged += value =>
                {
                    if (syncing) return;
                    syncing = true;
                    try
                    {
                        foreach (var other in syncedScrollViews)
                        {
                            if (other == sv) continue;
                            other.horizontalScroller.value = value;
                        }
                    }
                    finally { syncing = false; }
                };
            }
        }

        float lastMaxTime = float.NaN;  // 缓存:避免 maxTime 没变时重绘刻度 + 重设 minWidth(拖动 Action 时最常触发)
        void RescaleContentWidthsAndScale()
        {
            float maxTime = ComputeMaxTimeAcrossTracks(tracksProp);
            if (maxTime == lastMaxTime) return;  // 没变,跳过(只读操作便宜,但 minWidth setter + WaveScaleBar.Render 会触发 layout)
            lastMaxTime = maxTime;
            float pxPerSec = 24f * _zoom;
            float minW = Mathf.Max(200f, maxTime * pxPerSec);
            scaleContent.style.minWidth = minW;
            for (int i = 1; i < syncedScrollViews.Count; i++)  // [0] = scaleScroll,已设过
            {
                syncedScrollViews[i].contentContainer.style.minWidth = minW;
            }
            WaveScaleBar.Render(scaleContent, maxTime, pxPerSec);
        }

        Action rebuildTracks = () =>
        {
            tracksContainer.Clear();
            // 清空旧的 Track ScrollView 引用 (保留 scaleScroll)
            while (syncedScrollViews.Count > 1) syncedScrollViews.RemoveAt(syncedScrollViews.Count - 1);

            for (int t = 0; t < tracksProp.arraySize; t++)
            {
                var row = WaveTrackRow.Build(
                    waveIdx, t,
                    tracksProp.GetArrayElementAtIndex(t),
                    so, onActionSelected, getCurrentSelection,
                    () => 24f * _zoom);
                tracksContainer.Add(row);

                if (row.userData is WaveTrackRow.State rowState && rowState.TimelineScroll != null)
                    syncedScrollViews.Add(rowState.TimelineScroll);
            }

            // 强制重置:新 Track 的 cardsContainer 是新元素,minWidth 起始 0,需要重新设置
            lastMaxTime = float.NaN;
            WireScrollSync();
            RescaleContentWidthsAndScale();
        };
        rebuildTracks();

        int lastTracksCount = tracksProp.arraySize;
        tracksContainer.TrackPropertyValue(tracksProp, _ =>
        {
            if (tracksProp.arraySize != lastTracksCount)
            {
                lastTracksCount = tracksProp.arraySize;
                rebuildTracks();
            }
            else
            {
                // Track 数不变但子字段变化 (例如拖动卡片改 TriggerTime) → maxTime 可能变,重绘刻度
                RescaleContentWidthsAndScale();
            }
        });

        // 按钮行:[ + 新增 Track | 删除 Track | 缩放条 ] (flexGrow 1:1:1,同宽)
        var btnRow = new VisualElement();
        btnRow.style.flexDirection = FlexDirection.Row;
        btnRow.style.marginTop = 4;

        // 1. + 新增 Track
        var addTrackBtn = new Button(() =>
        {
            Undo.RecordObject(so.targetObject, "Add Track");
            tracksProp.InsertArrayElementAtIndex(tracksProp.arraySize);
            var newTrack = tracksProp.GetArrayElementAtIndex(tracksProp.arraySize - 1);
            newTrack.FindPropertyRelative("Name").stringValue = $"Track {tracksProp.arraySize - 1}";
            newTrack.FindPropertyRelative("Locked").boolValue = false;
            newTrack.FindPropertyRelative("Actions").arraySize = 0;
            so.ApplyModifiedProperties();
        })
        { text = "+ 新增 Track" };
        addTrackBtn.style.flexGrow = 1;
        addTrackBtn.style.flexBasis = 0;
        addTrackBtn.style.flexShrink = 0;
        addTrackBtn.style.marginRight = 4;
        btnRow.Add(addTrackBtn);

        // 2. 删除 Track (删当前 Wave 的最后一个 Track)
        var delTrackBtn = new Button(() =>
        {
            if (tracksProp.arraySize == 0) return;
            int lastIdx = tracksProp.arraySize - 1;
            if (EditorUtility.DisplayDialog("删除 Track", $"确认删除 Track {lastIdx}?", "删除", "取消"))
            {
                Undo.RecordObject(so.targetObject, "Delete Track");
                tracksProp.DeleteArrayElementAtIndex(lastIdx);
                so.ApplyModifiedProperties();
                var (selW, selT, _) = getCurrentSelection();
                if (selW == waveIdx && selT == lastIdx) onActionSelected?.Invoke(-1, -1, -1);
            }
        })
        { text = "删除 Track" };
        delTrackBtn.style.flexGrow = 1;
        delTrackBtn.style.flexBasis = 0;
        delTrackBtn.style.flexShrink = 0;
        delTrackBtn.style.marginRight = 4;
        btnRow.Add(delTrackBtn);

        // 3. 缩放条:Label "缩放 X.Xx" + Slider (本 Wave 独立 _zoom,改它只影响本 Wave)
        var zoomContainer = new VisualElement();
        zoomContainer.style.flexGrow = 1;
        zoomContainer.style.flexBasis = 0;
        zoomContainer.style.flexShrink = 0;
        zoomContainer.style.flexDirection = FlexDirection.Row;
        zoomContainer.style.alignItems = Align.Center;
        zoomContainer.style.paddingLeft = 6;
        zoomContainer.style.paddingRight = 6;
        btnRow.Add(zoomContainer);

        var zoomLabel = new Label($"缩放 {_zoom:0.0}x");
        zoomLabel.style.fontSize = 11;
        zoomLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
        zoomLabel.style.width = 50;
        zoomLabel.style.flexShrink = 0;
        zoomContainer.Add(zoomLabel);

        var zoomSlider = new Slider(0.1f, 5f) { value = _zoom };
        zoomSlider.style.flexGrow = 1;
        zoomSlider.style.flexBasis = 0;
        zoomSlider.style.minWidth = 0;
        zoomSlider.showInputField = false;
        zoomSlider.RegisterValueChangedCallback(evt =>
        {
            _zoom = evt.newValue;
            zoomLabel.text = $"缩放 {_zoom:0.0}x";
            ForceZoomRescale();
        });
        zoomContainer.Add(zoomSlider);

        // 缩放刷新:_zoom 变了 → pxPerSec 变了,刻度条 + 卡片 left/width 都要用新 pxPerSec 重算
        void ForceZoomRescale()
        {
            lastMaxTime = float.NaN;  // 让 RescaleContentWidthsAndScale 不跳过(否则 maxTime 没变会 return)
            RescaleContentWidthsAndScale();
            // TrackPropertyValue 只在 SerializedProperty 变化时触发,_zoom 改不会触发 → 手动重算所有卡片
            for (int t = 0; t < tracksProp.arraySize && t < tracksContainer.childCount; t++)
            {
                int tIdx = t;  // for 循环闭包陷阱:拷贝 t 给 lambda
                var row = tracksContainer[t];
                if (row.userData is WaveTrackRow.State rs && rs.CardsContainer != null)
                {
                    var actionsProp = tracksProp.GetArrayElementAtIndex(tIdx).FindPropertyRelative("Actions");
                    if (actionsProp != null)
                        WaveActionCard.Render(rs.CardsContainer, actionsProp, waveIdx, tIdx, onActionSelected,
                            () => tracksProp.GetArrayElementAtIndex(tIdx).FindPropertyRelative("Locked").boolValue,
                            () => 24f * _zoom);
                }
            }
        }

        block.Add(btnRow);

        return block;
    }

    /// <summary>
    /// 注册 Track 的删除 Action 按钮(供选中状态变化时刷新 enabled)。
    /// </summary>
    public static void RegisterDelActionBtn(Button btn) => _allDelActionBtns.Add(btn);

    /// <summary>
    /// 根据当前选中 (selW, selT, selA) 刷新所有 Track 删除 Action 按钮的 enabled 状态。
    /// 没选中 (selA < 0) → 全 disabled;selA 越界 (≥ ActionsProp.arraySize) → disabled;否则仅 selW/selT 匹配的按钮 enabled。
    /// 自动剔除 detached (rebuild 后旧元素) 的按钮。
    /// </summary>
    public static void RefreshDelActionBtnStates(int selW, int selT, int selA)
    {
        _allDelActionBtns.RemoveAll(b => b.parent == null);  // 清理已 detach 的旧按钮
        foreach (var btn in _allDelActionBtns)
        {
            if (btn.userData is WaveTrackRow.State rs && rs.ActionsProp != null)
            {
                bool enabled = selW >= 0
                    && rs.WaveIdx == selW && rs.TrackIdx == selT
                    && selA >= 0 && selA < rs.ActionsProp.arraySize;
                btn.SetEnabled(enabled);
            }
        }
    }

    /// <summary>
    /// 计算整 Wave 的最大时间(所有 Track / 所有 Action 中 TriggerTime + DurationTime 的最大值)。
    /// 刻度尺按这个数延伸。
    /// </summary>
    public static float ComputeMaxTimeAcrossTracks(SerializedProperty tracksProp)
    {
        float max = 0f;
        if (tracksProp == null) return 1f;
        for (int t = 0; t < tracksProp.arraySize; t++)
        {
            var actionsProp = tracksProp.GetArrayElementAtIndex(t).FindPropertyRelative("Actions");
            if (actionsProp == null) continue;
            for (int a = 0; a < actionsProp.arraySize; a++)
            {
                var act = actionsProp.GetArrayElementAtIndex(a);
                float trig = act.FindPropertyRelative("TriggerTime").floatValue;
                int cmd = act.FindPropertyRelative("CommandType").intValue;
                float end = WaveActionCard.ComputeEndTime(act, cmd);
                if (end > max) max = end;
            }
        }
        // 至少 1s,避免空 Wave 时宽度为 0
        return Mathf.Max(1f, max);
    }

    /// <summary>
    /// 选 tick 间隔:目标间距约 80px,从 {0.1, 0.2, 0.5, 1, 2, 5, ...} 里挑第一个 ≥ ideal。
    /// zoom 越大刻度越细。
    /// </summary>
    public static float ChooseTickInterval(float pxPerSec)
    {
        const float targetPxBetweenTicks = 80f;
        float ideal = targetPxBetweenTicks / Mathf.Max(1f, pxPerSec);
        float[] candidates = { 0.1f, 0.2f, 0.5f, 1f, 2f, 5f, 10f, 15f, 30f, 60f, 120f, 300f, 600f };
        foreach (var c in candidates)
        {
            if (c >= ideal) return c;
        }
        return candidates[candidates.Length - 1];
    }

    public static Color CommandTypeColor(int cmd)
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
}
