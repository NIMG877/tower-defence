using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot migration from the legacy BlockData-on-prefab format to
/// LevelData.MapData. See spec §6.
///
/// Uses reflection to find <c>BlockData</c> by type name because the
/// <c>BlockData.cs</c> source file has been deleted (Task 2.6) — but legacy
/// <c>.prefab</c> YAML files may still reference the type. The migration
/// therefore can be compiled and run in current builds, but only finds
/// data on prefabs whose YAML still serializes a <c>BlockData</c> component.
/// </summary>
public static class MapAutoMigrator
{
    const string BlockDataTypeName = "BlockData";
    const string BlockDataAssemblyName = "BasicScripts";

    /// <summary>
    /// Read all <c>BlockData</c> MBs from a prefab root's children and produce
    /// a list of <see cref="Tile"/>. Coordinates are taken from
    /// each child's transform.position (integer part — y maps to i, x maps to j).
    /// Returns an empty list when <paramref name="prefabRoot"/> is null or
    /// when the legacy <c>BlockData</c> type is not loaded in the current
    /// build (post-deletion state).
    /// </summary>
    public static List<Tile> ReadFromPrefab(GameObject prefabRoot)
    {
        var result = new List<Tile>();
        if (prefabRoot == null) return result;

        var blockDataType = System.Type.GetType($"{BlockDataTypeName}, {BlockDataAssemblyName}");
        if (blockDataType == null) return result; // legacy type not loaded

        for (int k = 0; k < prefabRoot.transform.childCount; k++)
        {
            var child = prefabRoot.transform.GetChild(k);
            var bd = child.GetComponent(blockDataType);
            if (bd == null) continue;
            int i = (int)child.position.y;
            int j = (int)child.position.x;

            // Read fields via reflection (legacy private field names).
            int passableType = (int)GetField(bd, "_passableType", 0);
            bool highland    = (bool)GetField(bd, "_highland", false);
            bool canSet      = (bool)GetField(bd, "_canSet", false);
            bool deadly      = (bool)GetProp(bd, "Deadly", false);

            // Portal target lookup via reflection (legacy property name).
            var portalOut = GetProp(bd, "ProtalOutBlock", null) as Component;
            int portalOutI = -1, portalOutJ = -1;
            if (portalOut != null)
            {
                portalOutI = (int)portalOut.transform.position.y;
                portalOutJ = (int)portalOut.transform.position.x;
            }

            result.Add(new Tile
            {
                i            = i,
                j            = j,
                highland     = highland,
                canSet       = canSet,
                passableType = passableType,
                deadly       = deadly,
                portalOutI   = portalOutI,
                portalOutJ   = portalOutJ,
            });
        }
        return result;
    }

    static object GetField(object obj, string name, object defaultValue)
    {
        var f = obj.GetType().GetField(name,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return f != null ? f.GetValue(obj) : defaultValue;
    }

    static object GetProp(object obj, string name, object defaultValue)
    {
        var p = obj.GetType().GetProperty(name);
        return p != null ? p.GetValue(obj) : defaultValue;
    }

    /// <summary>
    /// Strip <c>BlockData</c> MBs from a prefab root's children in-place and
    /// save the prefab asset. Returns the number of components removed.
    /// Uses <see cref="PrefabUtility.LoadPrefabContents"/> so the change can
    /// be saved back to the asset.
    /// </summary>
    public static int StripBlockDataFromPrefab(string assetPath)
    {
        var blockDataType = System.Type.GetType($"{BlockDataTypeName}, {BlockDataAssemblyName}");
        if (blockDataType == null) return 0;

        var contents = PrefabUtility.LoadPrefabContents(assetPath);
        int removed = 0;
        for (int k = 0; k < contents.transform.childCount; k++)
        {
            var child = contents.transform.GetChild(k);
            var comps = child.GetComponents(blockDataType);
            foreach (var c in comps)
            {
                Object.DestroyImmediate(c as Object, true);
                removed++;
            }
        }
        PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
        PrefabUtility.UnloadPrefabContents(contents);
        return removed;
    }

    /// <summary>
    /// Run the full migration on a LevelData: read legacy MBs into MapData,
    /// strip MBs from prefab, save both. Idempotent: if MapData is non-empty,
    /// does nothing. Returns true when migration actually ran.
    /// </summary>
    public static bool MigrateLevelData(LevelData levelData)
    {
        if (levelData == null) return false;
        if (levelData.MapData == null) levelData.MapData = new List<Tile>();
        if (levelData.MapData.Count > 0) return false; // already migrated

        if (levelData.MapPrefab == null) return false;

        var prefabPath = AssetDatabase.GetAssetPath(levelData.MapPrefab);
        if (string.IsNullOrEmpty(prefabPath)) return false;

        var temp = Object.Instantiate(levelData.MapPrefab);
        try
        {
            var entries = ReadFromPrefab(temp);
            foreach (var e in entries) levelData.MapData.Add(e);
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }

        EditorUtility.SetDirty(levelData);
        AssetDatabase.SaveAssets();
        StripBlockDataFromPrefab(prefabPath);
        AssetDatabase.SaveAssets();
        return true;
    }

    /// <summary>
    /// Escape-hatch menu item — re-run migration on the currently selected
    /// LevelData asset. Useful when the user wants to retry or when
    /// <c>LevelDataEditor.OnEnable</c> did not auto-migrate (e.g. user
    /// manually clears <c>MapData</c>).
    /// </summary>
    [MenuItem("Tools/Level Editor/Migrate Old Map (active LevelData)")]
    public static void MigrateMenuItem()
    {
        var sel = Selection.activeObject as LevelData;
        if (sel == null)
        {
            EditorUtility.DisplayDialog("Migrate", "请先在 Project 窗口选中一个 LevelData 资产。", "OK");
            return;
        }
        if (MigrateLevelData(sel))
        {
            EditorUtility.DisplayDialog("Migrate", $"已迁移 {sel.MapData.Count} 个地块。", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Migrate", "无需迁移 (MapData 已存在,或 prefab 为空)。", "OK");
        }
    }
}