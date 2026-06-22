using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 横向波次时间线: 列出 Waves, 每条 Wave 一行; 每条 Action 渲染为卡片 (Task 12 加入)。
/// 通过事件 ActionSelected 派发"用户选中"信号给 ActionDetailSection。
/// </summary>
public static class WaveTimelineSection
{
    public static VisualElement Build(SerializedObject so, Action<int, int> onActionSelected)
    {
        var section = new VisualElement();
        section.AddToClassList("level-editor-section");

        var title = new Label("▸ 波次时间线");
        title.AddToClassList("level-editor-section-title");
        section.Add(title);

        var wavesProp = so.FindProperty("Waves");
        var wavesListContainer = new VisualElement();
        section.Add(wavesListContainer);

        // 重建: 在 onCreate / wave 数组变化时调用
        Action rebuild = () => RebuildWaves(wavesListContainer, wavesProp, so, onActionSelected);
        rebuild();

        // 监听 Waves 数组变化 (Undo/Redo, 外部 mutation)
        wavesProp.TrackPropertyValue(wavesProp, _ => rebuild());

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

    static void RebuildWaves(VisualElement container, SerializedProperty wavesProp, SerializedObject so, Action<int, int> onActionSelected)
    {
        container.Clear();
        for (int w = 0; w < wavesProp.arraySize; w++)
        {
            container.Add(BuildWaveRow(w, wavesProp.GetArrayElementAtIndex(w), so, onActionSelected));
        }
    }

    static VisualElement BuildWaveRow(int waveIdx, SerializedProperty waveProp, SerializedObject so, Action<int, int> onActionSelected)
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

        // Action 卡片容器 (Task 12 填充)
        var cardsContainer = new VisualElement();
        cardsContainer.style.height = 32;
        cardsContainer.style.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
        cardsContainer.style.borderTopLeftRadius = 3;
        cardsContainer.style.borderTopRightRadius = 3;
        cardsContainer.style.borderBottomLeftRadius = 3;
        cardsContainer.style.borderBottomRightRadius = 3;
        row.Add(cardsContainer);

        // + 新增 Action 按钮
        var addActionBtn = new Button(() =>
        {
            Undo.RecordObject(so.targetObject, "Add Action");
            actionsProp.InsertArrayElementAtIndex(actionsProp.arraySize);
            actionsProp.GetArrayElementAtIndex(actionsProp.arraySize - 1).FindPropertyRelative("CommandType").intValue = 0;
            so.ApplyModifiedProperties();
        })
        { text = "+ 新增 Action" };
        addActionBtn.style.marginTop = 4;
        row.Add(addActionBtn);

        // 删除 Wave 按钮
        var delBtn = new Button(() =>
        {
            if (EditorUtility.DisplayDialog("删除 Wave", $"确认删除 Wave {waveIdx}?", "删除", "取消"))
            {
                Undo.RecordObject(so.targetObject, "Delete Wave");
                waveProp.serializedObject.FindProperty("Waves").DeleteArrayElementAtIndex(waveIdx);
                so.ApplyModifiedProperties();
            }
        })
        { text = "× 删除 Wave" };
        delBtn.style.marginTop = 4;
        row.Add(delBtn);

        return row;
    }
}
