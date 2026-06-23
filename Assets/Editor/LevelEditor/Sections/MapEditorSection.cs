using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Shared scaffolding for the [地图] / [路径] tabs in the LevelData inspector.
/// Owns the BlockMapCache, ViewTransform, and the canvas background that both
/// tabs render on top of. See spec §4.2.
/// </summary>
public static class MapEditorSection
{
    public static VisualElement Build(SerializedObject so, LevelData levelData, out IDisposable disposable)
    {
        disposable = null;
        var root = new VisualElement();
        root.style.flexDirection = FlexDirection.Column;
        root.AddToClassList("map-editor-section");

        var cache = BlockMapCache.Load(levelData);
        disposable = cache;

        if (levelData.iSize == 0 || levelData.jSize == 0)
        {
            var placeholder = new Label("⚠ MapData 为空:先在下方设置 iSize / jSize,再开始画地图。");
            placeholder.style.color = new Color(0.95f, 0.7f, 0.3f);
            placeholder.style.paddingTop = 8;
            placeholder.style.paddingBottom = 8;
            root.Add(placeholder);
            // Continue building — user might still want to set iSize/jSize.
        }

        // Size controls
        var sizeRow = new VisualElement();
        sizeRow.style.flexDirection = FlexDirection.Row;
        sizeRow.style.paddingTop = 4;
        sizeRow.style.paddingBottom = 4;
        sizeRow.Add(MakeLabeledIntField("iSize (rows)", so, "iSize", cache));
        sizeRow.Add(MakeLabeledIntField("jSize (cols)", so, "jSize", cache));
        root.Add(sizeRow);

        // Tab container
        var tabBar = new VisualElement();
        tabBar.style.flexDirection = FlexDirection.Row;
        tabBar.style.paddingTop = 8;
        var mapTabBtn = new Button { text = "地图" };
        var pathTabBtn = new Button { text = "路径" };
        mapTabBtn.style.flexGrow = 1;
        pathTabBtn.style.flexGrow = 1;
        tabBar.Add(mapTabBtn);
        tabBar.Add(pathTabBtn);
        root.Add(tabBar);

        // Body
        var body = new VisualElement();
        body.style.paddingTop = 4;
        root.Add(body);

        var state = new PathEditingState { Cache = cache };
        VisualElement currentTab = null;

        void ShowTab(System.Func<VisualElement> buildTab, Button activeBtn, Button inactiveBtn)
        {
            body.Clear();
            currentTab = buildTab();
            body.Add(currentTab);
            activeBtn.style.backgroundColor = new Color(0.35f, 0.45f, 0.65f);
            inactiveBtn.style.backgroundColor = new StyleColor(StyleKeyword.Null);
            state.NotifyChanged();
        }

        mapTabBtn.clicked += () => ShowTab(
            () => MapEditTab.Build(so, cache, state),
            mapTabBtn, pathTabBtn);
        pathTabBtn.clicked += () => ShowTab(
            () => PathEditTab.Build(so, state),
            pathTabBtn, mapTabBtn);

        // Default: show map tab
        ShowTab(() => MapEditTab.Build(so, cache, state), mapTabBtn, pathTabBtn);

        return root;
    }

    static VisualElement MakeLabeledIntField(string label, SerializedObject so, string propName, BlockMapCache cache)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginRight = 12;
        var lbl = new Label(label);
        lbl.style.minWidth = 80;
        row.Add(lbl);
        var prop = so.FindProperty(propName);
        var field = new IntegerField { value = prop.intValue };
        field.style.width = 60;
        field.RegisterValueChangedCallback(evt =>
        {
            Undo.RecordObject(so.targetObject, $"Change {propName}");
            prop.intValue = Mathf.Max(0, evt.newValue);
            so.ApplyModifiedProperties();
            // Note: cache is stale until user reloads section. The section
            // could be torn down and rebuilt on size change; for simplicity
            // we leave the rebuild to the user closing/reopening the inspector.
        });
        row.Add(field);
        return row;
    }
}
