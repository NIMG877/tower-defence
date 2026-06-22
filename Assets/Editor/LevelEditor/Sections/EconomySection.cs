using UnityEditor;
using UnityEngine.UIElements;

/// <summary>
/// 经济节: LevelHp / Cost0 / MaxCost / CanSetNum / CostRecoverSpeed。
/// </summary>
public static class EconomySection
{
    public static VisualElement Build(SerializedObject so)
    {
        var section = new VisualElement();
        section.AddToClassList("level-editor-section");

        var title = new Label("▸ 经济");
        title.AddToClassList("level-editor-section-title");
        section.Add(title);

        section.Add(MakeRow("LevelHp",           so.FindProperty("LevelHp")));
        section.Add(MakeRow("Cost0",             so.FindProperty("Cost0")));
        section.Add(MakeRow("MaxCost",           so.FindProperty("MaxCost")));
        section.Add(MakeRow("CanSetNum",         so.FindProperty("CanSetNum")));
        section.Add(MakeRow("CostRecoverSpeed",  so.FindProperty("CostRecoverSpeed")));

        return section;
    }

    static VisualElement MakeRow(string label, SerializedProperty prop)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginBottom = 2;

        var lab = new Label(label);
        lab.style.width = 160;
        lab.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
        row.Add(lab);

        var field = new PropertyField(prop);
        field.style.flexGrow = 1;
        field.BindProperty(prop);
        row.Add(field);

        return row;
    }
}
