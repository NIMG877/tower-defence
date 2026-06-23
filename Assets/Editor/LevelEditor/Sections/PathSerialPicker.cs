using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// PathSerial 下拉框:选项源是当前 LevelData.Paths[],文案跟 PathEditTab
/// 的 popup 一致(#{i}: {Name},空 Name 退化 "Path {i}")。
///
/// PathSerial 是 int(索引),直接读写 prop.intValue;无 "-1 / 空" 选项 ——
/// CommandType 2/3/4 强制需要有效路径,UI 上给 "(无路径)" 提示而不允许选空。
/// </summary>
public static class PathSerialPicker
{
    public static VisualElement Build(SerializedProperty prop)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 2;

        // 字段名 Label(固定 width=120,跟 EntityIdPicker 对齐)
        var fieldLabel = new Label(prop.displayName) { tooltip = prop.tooltip };
        fieldLabel.style.width = 120;
        fieldLabel.style.marginRight = 4;
        fieldLabel.style.flexShrink = 0;
        fieldLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        row.Add(fieldLabel);

        // 找根 SerializedObject 的 Paths
        var pathsProp = prop.serializedObject.FindProperty("Paths");
        int pathCount = pathsProp != null ? pathsProp.arraySize : 0;

        if (pathCount == 0)
        {
            var placeholder = new Label("(无路径 — 请先在 Path Editing 里新建)");
            placeholder.style.flexGrow = 1;
            placeholder.style.color = new Color(0.706f, 0.4f, 0.4f);
            placeholder.style.unityFontStyleAndWeight = FontStyle.Italic;
            row.Add(placeholder);
            return row;
        }

        // build (idx -> display) 列表
        var idxs = new List<int>();
        var display = new List<string>();
        for (int i = 0; i < pathCount; i++)
        {
            idxs.Add(i);
            var nameProp = pathsProp.GetArrayElementAtIndex(i).FindPropertyRelative("Name");
            string name = nameProp != null && !string.IsNullOrEmpty(nameProp.stringValue)
                ? nameProp.stringValue
                : $"Path {i}";
            display.Add($"#{i}: {name}");
        }

        // 当前值:clamp 到 [0, pathCount-1],越界时落回 0
        int current = Mathf.Clamp(prop.intValue, 0, pathCount - 1);
        // 如果 prop 越界,clamp 后会改实际值,下面立即修正
        if (prop.intValue != current)
        {
            // 不在这里写回 — 仅用于显示,用户编辑时再写
        }

        var popup = new PopupField<string>(display, current);
        popup.style.flexGrow = 1;
        popup.style.flexShrink = 1;
        popup.style.minWidth = 0;
        popup.tooltip = "选当前 LevelData.Paths 里的路径索引";
        popup.RegisterValueChangedCallback(evt =>
        {
            int newIdx = idxs[display.IndexOf(evt.newValue)];
            if (newIdx == prop.intValue) return;
            Undo.RecordObject(prop.serializedObject.targetObject, "Change PathSerial");
            prop.intValue = newIdx;
            prop.serializedObject.ApplyModifiedProperties();
        });
        row.Add(popup);

        return row;
    }
}
