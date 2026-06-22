using UnityEditor;
using UnityEditor.UIElements;
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

        section.Add(MakeRow(so.FindProperty("LevelHp")));
        section.Add(MakeRow(so.FindProperty("Cost0")));
        section.Add(MakeRow(so.FindProperty("MaxCost")));
        section.Add(MakeRow(so.FindProperty("CanSetNum")));
        section.Add(MakeRow(so.FindProperty("CostRecoverSpeed")));

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
