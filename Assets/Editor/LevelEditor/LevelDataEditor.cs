using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// LevelData 的自定义 Inspector (UI Toolkit)。
/// 触发条件: 在 Project 窗口选中 LevelData 资产。
/// </summary>
[CustomEditor(typeof(LevelData))]
public class LevelDataEditor : Editor
{
    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        root.style.flexDirection = FlexDirection.Column;

        // Header
        var header = new VisualElement();
        header.AddToClassList("level-editor-header");
        var title = new Label(((LevelData)target).LevelName);
        title.AddToClassList("level-editor-title");
        header.Add(title);
        root.Add(header);

        // Body placeholder
        var body = new VisualElement();
        body.style.paddingLeft = 12;
        body.style.paddingRight = 12;
        body.style.paddingTop = 12;
        body.style.paddingBottom = 12;
        body.Add(MetadataSection.Build(serializedObject));
        body.Add(ReferencesSection.Build(serializedObject));
        body.Add(EconomySection.Build(serializedObject));
        root.Add(body);

        return root;
    }
}