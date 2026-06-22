using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

/// <summary>
/// 引用节: MapPrefab / EnvironmentalControlDevice (单 GameObject) + CheckPoints / WaveEntityPrefabIDs (数组)。
/// </summary>
public static class ReferencesSection
{
    public static VisualElement Build(SerializedObject so)
    {
        var section = new VisualElement();
        section.AddToClassList("level-editor-section");

        var title = new Label("▸ 引用 (只填, 不做内部编辑)");
        title.AddToClassList("level-editor-section-title");
        section.Add(title);

        section.Add(MakeSingleRow(so.FindProperty("MapPrefab")));
        section.Add(MakeSingleRow(so.FindProperty("EnvironmentalControlDevice")));

        // 数组
        var checkpointsProp = so.FindProperty("CheckPoints");
        var idsProp = so.FindProperty("WaveEntityPrefabIDs");
        section.Add(MakeArrayRow(checkpointsProp));
        section.Add(MakeArrayRow(idsProp));

        return section;
    }

    static VisualElement MakeSingleRow(SerializedProperty prop)
    {
        var field = new PropertyField(prop);
        field.BindProperty(prop);
        field.style.marginBottom = 4;
        return field;
    }

    static VisualElement MakeArrayRow(SerializedProperty arrayProp)
    {
        // 直接 PropertyField 让 Unity 自己渲染 ListView (Unity 2022 ListView binding)
        var field = new PropertyField(arrayProp);
        field.BindProperty(arrayProp);
        field.style.marginBottom = 8;
        return field;
    }
}
