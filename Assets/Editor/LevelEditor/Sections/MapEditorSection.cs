using System;
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

        // Warnings strip (spec §7). Re-populates whenever the editing state changes.
        var warningsContainer = new VisualElement();
        warningsContainer.style.paddingTop = 4;
        warningsContainer.style.paddingBottom = 4;
        root.Add(warningsContainer);

        void RefreshWarnings()
        {
            warningsContainer.Clear();
            foreach (var msg in cache.GetWarnings())
            {
                var lbl = new Label("⚠ " + msg);
                lbl.style.color = new Color(0.95f, 0.7f, 0.3f);
                lbl.style.paddingTop = 2;
                warningsContainer.Add(lbl);
            }
        }
        // RefreshWarnings is wired to state.Changed below, after `state` is created.

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

        state.Changed += RefreshWarnings;
        RefreshWarnings();

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
}
