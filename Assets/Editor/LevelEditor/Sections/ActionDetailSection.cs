using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Action 详情面板: 接收 waveIdx/actionIdx, 绑定对应 SerializedProperty 子路径。
/// 基础字段: CommandType / GapFromLastAction / OnBeforeAction。
/// 条件字段 (按 CommandType 显隐): 后续任务添加。
/// </summary>
public static class ActionDetailSection
{
    public static VisualElement Build(SerializedObject so, int waveIdx, int actionIdx)
    {
        var section = new VisualElement();
        section.style.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
        section.style.paddingTop = 8;
        section.style.paddingBottom = 8;
        section.style.paddingLeft = 8;
        section.style.paddingRight = 8;
        section.style.borderTopLeftRadius = 3;
        section.style.borderTopRightRadius = 3;
        section.style.borderBottomLeftRadius = 3;
        section.style.borderBottomRightRadius = 3;
        section.style.borderLeftWidth = 1;
        section.style.borderRightWidth = 1;
        section.style.borderTopWidth = 1;
        section.style.borderBottomWidth = 1;
        section.style.borderLeftColor = new Color(0.3f, 0.8f, 0.6f);
        section.style.borderRightColor = new Color(0.3f, 0.8f, 0.6f);
        section.style.borderTopColor = new Color(0.3f, 0.8f, 0.6f);
        section.style.borderBottomColor = new Color(0.3f, 0.8f, 0.6f);

        var actionsProp = so.FindProperty($"Waves.Array.data[{waveIdx}].Actions");
        if (actionsProp == null)
        {
            section.Add(new Label($"Wave {waveIdx} not found"));
            return section;
        }
        var actionProp = actionsProp.GetArrayElementAtIndex(actionIdx);
        if (actionProp == null)
        {
            section.Add(new Label($"Action {actionIdx} not found"));
            return section;
        }

        // 顶部信息条
        var header = new Label($"▸ 已选 Wave[{waveIdx}].Action[{actionIdx}]");
        header.style.color = new Color(0.3f, 0.8f, 0.6f);
        header.style.fontSize = 10;
        header.style.marginBottom = 4;
        section.Add(header);

        // 基础字段 (始终显示)
        section.Add(MakeRow("CommandType",        actionProp.FindPropertyRelative("CommandType")));
        section.Add(MakeRow("GapFromLastAction",  actionProp.FindPropertyRelative("GapFromLastAction")));
        section.Add(MakeUnityEventRow("OnBeforeAction", actionProp.FindPropertyRelative("OnBeforeAction")));

        return section;
    }

    static VisualElement MakeRow(string label, SerializedProperty prop)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginBottom = 2;

        var lab = new Label(label);
        lab.style.width = 160;
        lab.style.color = new Color(0.5f, 0.5f, 0.5f);
        row.Add(lab);

        var field = new PropertyField(prop);
        field.style.flexGrow = 1;
        field.BindProperty(prop);
        row.Add(field);

        return row;
    }

    /// <summary>
    /// UnityEvent 字段: UI Toolkit 的 PropertyField 对 UnityEvent 渲染有限, 这里用 IMGUIContainer 包一层 IMGUI。
    /// </summary>
    static VisualElement MakeUnityEventRow(string label, SerializedProperty prop)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginBottom = 2;

        var lab = new Label(label);
        lab.style.width = 160;
        lab.style.color = new Color(0.5f, 0.5f, 0.5f);
        row.Add(lab);

        var imgui = new IMGUIContainer(() =>
        {
            if (prop != null) EditorGUILayout.PropertyField(prop);
        });
        imgui.style.flexGrow = 1;
        row.Add(imgui);

        return row;
    }
}