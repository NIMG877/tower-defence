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

        section.Add(MakeRow(so.FindProperty("LevelName")));
        section.Add(MakeRow(so.FindProperty("LevelCode")));
        section.Add(MakeRow(so.FindProperty("LevelDescription")));
        section.Add(MakeRow(so.FindProperty("CameraSize")));
        section.Add(MakeRow(so.FindProperty("CameraPos")));
        section.Add(MakeRow(so.FindProperty("CutToLevelTexture")));

        return section;
    }

    static VisualElement MakeRow(SerializedProperty prop)
    {
        var field = new PropertyField(prop);
        field.BindProperty(prop);
        field.style.marginBottom = 2;
        return field;
    }
}
