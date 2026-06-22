using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

/// <summary>
/// 元数据节: LevelName / LevelCode / LevelDescription / CameraSize / CameraPos / CutToLevelTexture。
/// </summary>
public static class MetadataSection
{
    public static VisualElement Build(SerializedObject so)
    {
        var section = new VisualElement();
        section.AddToClassList("level-editor-section");

        var title = new Label("▸ 元数据");
        title.AddToClassList("level-editor-section-title");
        section.Add(title);

        section.Add(MakeRow("LevelName",          so.FindProperty("LevelName")));
        section.Add(MakeRow("LevelCode",          so.FindProperty("LevelCode")));
        section.Add(MakeRow("LevelDescription",   so.FindProperty("LevelDescription")));
        section.Add(MakeRow("CameraSize",         so.FindProperty("CameraSize")));
        section.Add(MakeRow("CameraPos",          so.FindProperty("CameraPos")));
        section.Add(MakeRow("CutToLevelTexture",  so.FindProperty("CutToLevelTexture")));

        return section;
    }

    static VisualElement MakeRow(string label, SerializedProperty prop)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginBottom = 2;

        var lab = new Label(label);
        lab.style.width = 130;
        lab.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
        row.Add(lab);

        var field = new PropertyField(prop);
        field.style.flexGrow = 1;
        field.BindProperty(prop);
        row.Add(field);

        return row;
    }
}
