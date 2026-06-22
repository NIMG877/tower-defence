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

        section.Add(MakeSingleRow("MapPrefab",                 so.FindProperty("MapPrefab")));
        section.Add(MakeSingleRow("EnvironmentalControlDevice", so.FindProperty("EnvironmentalControlDevice")));

        // 数组
        var checkpointsProp = so.FindProperty("CheckPoints");
        var idsProp = so.FindProperty("WaveEntityPrefabIDs");
        section.Add(MakeArrayRow("CheckPoints (拖拽 GameObject)",        checkpointsProp));
        section.Add(MakeArrayRow("WaveEntityPrefabIDs (拖拽 EntityData)", idsProp));

        return section;
    }

    static VisualElement MakeSingleRow(string label, SerializedProperty prop)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginBottom = 4;

        var lab = new Label(label);
        lab.style.width = 200;
        lab.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
        row.Add(lab);

        var field = new PropertyField(prop);
        field.style.flexGrow = 1;
        field.BindProperty(prop);
        row.Add(field);

        return row;
    }

    static VisualElement MakeArrayRow(string label, SerializedProperty arrayProp)
    {
        var row = new VisualElement();
        row.style.marginBottom = 8;

        var lab = new Label(label);
        lab.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
        lab.style.marginBottom = 2;
        row.Add(lab);

        // 直接 PropertyField 让 Unity 自己渲染 ListView (Unity 2022 ListView binding)
        var field = new PropertyField(arrayProp);
        field.BindProperty(arrayProp);
        row.Add(field);

        return row;
    }
}
