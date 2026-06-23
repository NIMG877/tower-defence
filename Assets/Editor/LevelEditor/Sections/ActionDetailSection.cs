using UnityEditor;
using UnityEditor.UIElements;
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
        section.Add(MakeRow(actionProp.FindPropertyRelative("CommandType")));
        section.Add(MakeRow(actionProp.FindPropertyRelative("GapFromLastAction")));
        section.Add(MakeUnityEventRow(actionProp.FindPropertyRelative("OnBeforeAction")));

        // 条件字段容器 (按 CommandType 显隐)
        var conditionalContainer = new VisualElement();
        section.Add(conditionalContainer);

        // CommandType 变化时重建条件容器
        var commandTypeProp = actionProp.FindPropertyRelative("CommandType");
        RebuildConditional(conditionalContainer, actionProp, commandTypeProp.intValue, section);
        section.TrackPropertyValue(commandTypeProp, _ => RebuildConditional(conditionalContainer, actionProp, commandTypeProp.intValue, section));

        return section;
    }

    static VisualElement MakeRow(SerializedProperty prop)
    {
        var field = new PropertyField(prop);
        field.BindProperty(prop);
        field.style.marginBottom = 2;
        return field;
    }

    /// <summary>
    /// UnityEvent 字段: UI Toolkit 的 PropertyField 对 UnityEvent 渲染有限, 这里用 IMGUIContainer 包一层 IMGUI。
    /// </summary>
    static VisualElement MakeUnityEventRow(SerializedProperty prop)
    {
        var imgui = new IMGUIContainer(() =>
        {
            if (prop != null) EditorGUILayout.PropertyField(prop);
        });
        imgui.style.marginBottom = 2;
        return imgui;
    }

    static void RebuildConditional(VisualElement container, SerializedProperty actionProp, int commandType, VisualElement section)
    {
        container.Clear();

        // CommandType 0/1: 召唤/静止实体
        if (commandType == 0 || commandType == 1)
        {
            var title = new Label("▸ 召唤/静止参数");
            title.style.color = new Color(0.3f, 0.8f, 0.6f);
            title.style.fontSize = 10;
            title.style.marginTop = 4;
            title.style.marginBottom = 4;
            container.Add(title);

            container.Add(EntityIdPicker.Build(actionProp.FindPropertyRelative("EntityPrefabID")));
            container.Add(MakeRow(actionProp.FindPropertyRelative("Camp")));
            container.Add(MakeUnityEventRow(actionProp.FindPropertyRelative("OnActionRepeat")));
        }

        // CommandType 0/2/3/4: 路径
        if (commandType == 0 || commandType == 2 || commandType == 3 || commandType == 4)
        {
            container.Add(MakeRow(actionProp.FindPropertyRelative("PathSerial")));
        }

        // CommandType 0: 重复召唤
        if (commandType == 0)
        {
            var t = new Label("▸ 重复召唤 (仅 CommandType 0)");
            t.style.color = new Color(0.3f, 0.8f, 0.6f);
            t.style.fontSize = 10;
            t.style.marginTop = 4;
            t.style.marginBottom = 4;
            container.Add(t);

            container.Add(MakeRow(actionProp.FindPropertyRelative("GapsFromLastRepeat")));
            container.Add(MakeRow(actionProp.FindPropertyRelative("ModifyAttributes")));

            var modifyProp = actionProp.FindPropertyRelative("ModifyAttributes");
            if (modifyProp.boolValue)
            {
                container.Add(MakeRow(actionProp.FindPropertyRelative("ModifyLevelHpConsume")));
                container.Add(MakeRow(actionProp.FindPropertyRelative("ModifyPrimary")));
                container.Add(MakeRow(actionProp.FindPropertyRelative("ModifyCountOperate")));
            }
            section.TrackPropertyValue(modifyProp, _ =>
            {
                // ModifyAttributes 切换时重建 (展开/收起 ModifyLevelHpConsume 等)
                RebuildConditional(container, actionProp, commandType, section);
            });
        }

        // CommandType 1: 静止目标位置
        if (commandType == 1)
        {
            container.Add(MakeRow(actionProp.FindPropertyRelative("Destination")));
            container.Add(MakeRow(actionProp.FindPropertyRelative("Orientation")));
        }

        // CommandType 5: 对话框
        if (commandType == 5)
        {
            var t = new Label("▸ 对话框 (仅 CommandType 5)");
            t.style.color = new Color(0.3f, 0.8f, 0.6f);
            t.style.fontSize = 10;
            t.style.marginTop = 4;
            t.style.marginBottom = 4;
            container.Add(t);

            container.Add(MakeRow(actionProp.FindPropertyRelative("HeadImage")));
            container.Add(MakeRow(actionProp.FindPropertyRelative("Content")));
            container.Add(MakeRow(actionProp.FindPropertyRelative("DurationTime")));
        }

        // CommandType 6: 面板
        if (commandType == 6)
        {
            var t = new Label("▸ 面板 (仅 CommandType 6)");
            t.style.color = new Color(0.3f, 0.8f, 0.6f);
            t.style.fontSize = 10;
            t.style.marginTop = 4;
            t.style.marginBottom = 4;
            container.Add(t);

            container.Add(MakeRow(actionProp.FindPropertyRelative("Contents")));
        }
    }
}