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
    // 整体缩放 (所有 Wave 共享, static)
    static float _zoom = 1f;
    public static float Zoom => _zoom;

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

        section.Add(BuildZoomControls(rebuild));

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
            var newWave = wavesProp.GetArrayElementAtIndex(wavesProp.arraySize - 1);
            newWave.FindPropertyRelative("Tracks").arraySize = 0;
            so.ApplyModifiedProperties();
        })
        { text = "+ 新增 Wave" };
        addWaveBtn.style.marginTop = 6;
        section.Add(addWaveBtn);

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
        var label = new Label($"Wave {waveIdx} · {tracksProp.arraySize} tracks");
        label.style.color = new Color(0.8f, 0.8f, 0.8f);
        label.style.fontSize = 11;
        label.style.marginBottom = 4;
        block.Add(label);

        // 重建闭包(供 TrackRow 调)
        Action rebuild = () =>
        {
            var parent = block.parent;
            if (parent == null) return;
            int idx = parent.IndexOf(block);
            parent.RemoveAt(idx);
            parent.Insert(idx, BuildWaveBlock(waveIdx, waveProp, so, onActionSelected, getCurrentSelection));
        };

        // 各 Track 行
        var tracksContainer = new VisualElement();
        block.Add(tracksContainer);

        Action rebuildTracks = () =>
        {
            tracksContainer.Clear();
            for (int t = 0; t < tracksProp.arraySize; t++)
            {
                tracksContainer.Add(WaveTrackRow.Build(
                    waveIdx, t,
                    tracksProp.GetArrayElementAtIndex(t),
                    so, onActionSelected, getCurrentSelection,
                    rebuild));
            }
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
        });

        // 按钮行
        var btnRow = new VisualElement();
        btnRow.style.flexDirection = FlexDirection.Row;
        btnRow.style.marginTop = 4;

        var addTrackBtn = new Button(() =>
        {
            Undo.RecordObject(so.targetObject, "Add Track");
            tracksProp.InsertArrayElementAtIndex(tracksProp.arraySize);
            var newTrack = tracksProp.GetArrayElementAtIndex(tracksProp.arraySize - 1);
            newTrack.FindPropertyRelative("Name").stringValue = $"Track {tracksProp.arraySize - 1}";
            newTrack.FindPropertyRelative("TrackColor").colorValue = WaveMigrator.DefaultTrackColor(tracksProp.arraySize - 1);
            newTrack.FindPropertyRelative("Locked").boolValue = false;
            newTrack.FindPropertyRelative("Actions").arraySize = 0;
            so.ApplyModifiedProperties();
        })
        { text = "+ 新增 Track" };
        addTrackBtn.style.flexGrow = 1;
        addTrackBtn.style.marginRight = 4;
        btnRow.Add(addTrackBtn);

        var delWaveBtn = new Button(() =>
        {
            var (selW, _, _) = getCurrentSelection();
            bool willInvalidate = selW >= 0 && waveIdx <= selW;
            if (EditorUtility.DisplayDialog("删除 Wave", $"确认删除 Wave {waveIdx}?", "删除", "取消"))
            {
                Undo.RecordObject(so.targetObject, "Delete Wave");
                waveProp.serializedObject.FindProperty("Waves").DeleteArrayElementAtIndex(waveIdx);
                so.ApplyModifiedProperties();
                if (willInvalidate) onActionSelected?.Invoke(-1, -1, -1);
            }
        })
        { text = "× 删除 Wave" };
        btnRow.Add(delWaveBtn);

        block.Add(btnRow);

        return block;
    }

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
        slider.style.flexBasis = 0;
        slider.style.minWidth = 0;
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
