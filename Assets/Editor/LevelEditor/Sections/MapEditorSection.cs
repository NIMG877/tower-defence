using System;
using System.Collections.Generic;
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
    static readonly Color WarningColor = new Color(0.95f, 0.7f, 0.3f);

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

        void RefreshWarnings()
        {
            warningsContainer.Clear();
            var warnings = cache.GetWarnings();
            if (warnings.Count == 0) return;

            // Group by kind (preserving spec §7 order: OutOfRange, Duplicate)
            var byKind = new Dictionary<BlockMapCache.WarningKind, List<BlockMapCache.CacheWarning>>();
            foreach (var w in warnings)
            {
                if (!byKind.TryGetValue(w.Kind, out var list))
                {
                    list = new List<BlockMapCache.CacheWarning>();
                    byKind[w.Kind] = list;
                }
                list.Add(w);
            }

            if (byKind.TryGetValue(BlockMapCache.WarningKind.OutOfRange, out var oorList))
            {
                int iSize = cache.ISize;
                int jSize = cache.JSize;
                AddCountedFoldout(
                    warningsContainer,
                    $"⚠ {oorList.Count} entries out of range (grid {iSize}×{jSize})",
                    oorList,
                    cleanTooltip: $"Remove all MapData entries with (i, j) outside 0..{iSize - 1} × 0..{jSize - 1}. Undoable.",
                    onClean: () =>
                    {
                        int removed = BlockMapCache.CleanOutOfRangeEntries(so, cache);
                        if (removed > 0)
                        {
                            cache.Reload();
                            state.NotifyChanged();
                        }
                    });
            }

            if (byKind.TryGetValue(BlockMapCache.WarningKind.Duplicate, out var dupList))
            {
                AddCountedFoldout(
                    warningsContainer,
                    $"⚠ {dupList.Count} duplicate entries",
                    dupList,
                    cleanTooltip: null,
                    onClean: null);
            }
        }

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

    /// <summary>
    /// 一行 foldout + 可选 Clean 按钮。foldout 展开后能看到每条 warning 明细。
    /// </summary>
    static void AddCountedFoldout(VisualElement parent, string summary,
        List<BlockMapCache.CacheWarning> items, string cleanTooltip, Action onClean)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.FlexStart;

        var foldout = new Foldout { text = summary, value = false };
        foldout.style.flexGrow = 1;
        foldout.style.color = WarningColor;
        foldout.style.unityFontStyleAndWeight = FontStyle.Bold;
        foreach (var w in items)
        {
            var lbl = new Label(w.Message);
            lbl.style.fontSize = 10;
            lbl.style.marginLeft = 12;
            lbl.style.color = WarningColor;
            lbl.style.whiteSpace = WhiteSpace.Normal;
            foldout.Add(lbl);
        }
        row.Add(foldout);

        if (onClean != null)
        {
            var cleanBtn = new Button(onClean) { text = "✕ Clean" };
            cleanBtn.style.marginLeft = 4;
            cleanBtn.style.marginTop = 2; // align with foldout toggle
            cleanBtn.style.fontSize = 10;
            if (cleanTooltip != null) cleanBtn.tooltip = cleanTooltip;
            row.Add(cleanBtn);
        }

        parent.Add(row);
    }
}
