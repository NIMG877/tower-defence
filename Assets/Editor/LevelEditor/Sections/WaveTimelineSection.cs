using System;
using UnityEditor;
using UnityEditor.UIElements;
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

    static void RenderActionCards(VisualElement container, SerializedProperty actionsProp, int waveIdx, Action<int, int> onActionSelected)
    {
        container.Clear();
        if (actionsProp.arraySize == 0)
        {
            var empty = new Label("(空)");
            empty.style.color = new Color(0.4f, 0.4f, 0.4f);
            empty.style.unityTextAlign = TextAnchor.MiddleCenter;
            empty.style.flexGrow = 1;
            container.Add(empty);
            return;
        }

        // 计算总时长 (累加所有 Gap + 每条 Action 占 1s 占位宽度)
        float totalUnits = 0f;
        for (int i = 0; i < actionsProp.arraySize; i++)
        {
            var a = actionsProp.GetArrayElementAtIndex(i);
            totalUnits += 1f; // Action 自身占 1 单位
            totalUnits += Mathf.Max(0f, a.FindPropertyRelative("GapFromLastAction").floatValue);
        }
        if (totalUnits <= 0f) totalUnits = 1f;

        // 渲染
        float cursorUnits = 0f;
        for (int i = 0; i < actionsProp.arraySize; i++)
        {
            var a = actionsProp.GetArrayElementAtIndex(i);
            float gap = Mathf.Max(0f, a.FindPropertyRelative("GapFromLastAction").floatValue);
            int cmd = a.FindPropertyRelative("CommandType").intValue;

            // 间隔标记
            if (gap > 0f)
            {
                var gapEl = new VisualElement();
                gapEl.style.position = Position.Absolute;
                gapEl.style.left = Length.Percent(cursorUnits / totalUnits * 100f);
                gapEl.style.width = Length.Percent(gap / totalUnits * 100f);
                gapEl.style.height = Length.Percent(100f);
                gapEl.style.backgroundColor = new Color(0.23f, 0.23f, 0.27f);
                gapEl.style.flexDirection = FlexDirection.Row;
                gapEl.style.alignItems = Align.Center;
                gapEl.style.justifyContent = Justify.Center;
                var gapLabel = new Label($"gap {gap:0.0}s");
                gapLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                gapLabel.style.fontSize = 9;
                gapEl.Add(gapLabel);
                container.Add(gapEl);
                cursorUnits += gap;
            }

            // 卡片
            var card = new Button(() => onActionSelected?.Invoke(waveIdx, i))
            {
                text = $"A{i} {CommandTypeShort(cmd)}"
            };
            card.style.position = Position.Absolute;
            card.style.left = Length.Percent(cursorUnits / totalUnits * 100f);
            card.style.width = Length.Percent(1f / totalUnits * 100f);
            card.style.height = Length.Percent(100f);
            card.style.backgroundColor = CommandTypeColor(cmd);
            card.style.color = new Color(0, 0, 0);
            card.style.fontSize = 9;
            card.style.paddingLeft = 2;
            card.style.paddingRight = 2;
            container.Add(card);
            cursorUnits += 1f;
        }
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

        RenderActionCards(cardsContainer, actionsProp, waveIdx, onActionSelected);
        actionsProp.TrackPropertyValue(actionsProp, _ => RenderActionCards(cardsContainer, actionsProp, waveIdx, onActionSelected));

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
