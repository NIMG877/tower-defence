using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

/// <summary>
/// 引用节: MapPrefab / EnvironmentalControlDevice (单 GameObject)。
/// MapPrefab 除手填外支持一键生成/重建(见 MapPrefabGenerator)。
/// </summary>
public static class ReferencesSection
{
    public static VisualElement Build(SerializedObject so)
    {
        var section = new VisualElement();
        section.AddToClassList("level-editor-section");

        var title = new Label("▸ 引用");
        title.AddToClassList("level-editor-section-title");
        section.Add(title);

        section.Add(MakeSingleRow(so.FindProperty("MapPrefab")));
        section.Add(MapPrefabGenerator.Build(so));
        section.Add(MakeSingleRow(so.FindProperty("EnvironmentalControlDevice")));

        return section;
    }

    static VisualElement MakeSingleRow(SerializedProperty prop)
    {
        var field = new PropertyField(prop);
        field.BindProperty(prop);
        field.style.marginBottom = 4;
        return field;
    }
}
