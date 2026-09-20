using System;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// MapPrefab 一键生成:按 LevelData.MapData 重建 Map.prefab 的地块方块。
///
/// 运行时契约见 MapDataManager.MapInitialize —— Map 根的直接子物体按整数格点
/// (x=j, y=i) 收 MeshRenderer 材质进 TileMaterials。因此:
///   - 每个画过的格子生成一个 Quad 子物体,位置 (j, i, 0.01),材质按
///     deadly → 地穴槽 / highland → 高台槽 / 其余 → 地面槽 选择;
///   - 再生成时只删除"会被运行时当成地块"的子物体(整数格点 + MeshRenderer,
///     与 MapInitialize 的消费判据同源),用户放的手工装饰(SpriteRenderer
///     出生点标记、非格点装饰物)原样保留 —— 生成是可迭代的,装饰不必重摆。
///
/// 材质槽留空时该类格子仍生成 Quad(无材质呈紫红),补材质即可,不阻断生成。
/// </summary>
public static class MapPrefabGenerator
{
    /// <summary>地块 quad 的 z。地图 z 序约定:地块 0.01 最底,实体 0 在上。</summary>
    const float TileZ = 0.01f;

    // 预填默认材质(现有主题素材);域重载后首次访问时加载一次,用户清空后不再回填
    const string DefaultGroundMatPath = "Assets/Resources/Prefabs/Levels/Main/MainImgs/地面.mat";
    const string DefaultHighlandMatPath = "Assets/Resources/Prefabs/Levels/Main/MainImgs/高台.mat";

    static Material _groundMat;
    static Material _highlandMat;
    static Material _deadlyMat;
    static bool _defaultsLoaded;

    public static VisualElement Build(SerializedObject so)
    {
        EnsureDefaultMaterials();

        var root = new VisualElement();
        root.style.marginBottom = 6;
        root.style.paddingTop = 4;
        root.style.paddingBottom = 4;
        root.style.paddingLeft = 6;
        root.style.paddingRight = 6;
        root.style.backgroundColor = EditorTheme.PanelBg;
        root.style.borderTopLeftRadius = 3;
        root.style.borderTopRightRadius = 3;
        root.style.borderBottomLeftRadius = 3;
        root.style.borderBottomRightRadius = 3;
        root.style.borderLeftWidth = 1;
        root.style.borderRightWidth = 1;
        root.style.borderTopWidth = 1;
        root.style.borderBottomWidth = 1;
        root.style.borderLeftColor = EditorTheme.Border;
        root.style.borderRightColor = EditorTheme.Border;
        root.style.borderTopColor = EditorTheme.Border;
        root.style.borderBottomColor = EditorTheme.Border;

        var matRow = new VisualElement();
        matRow.style.flexDirection = FlexDirection.Row;
        matRow.style.marginBottom = 4;
        matRow.Add(MakeMatField("地面", () => _groundMat, v => _groundMat = v,
            "非 highland 且非地穴格子的材质。留空则该类格子无材质(紫红占位)。"));
        matRow.Add(MakeMatField("高台", () => _highlandMat, v => _highlandMat = v,
            "highland 格子的材质。留空则该类格子无材质(紫红占位)。"));
        matRow.Add(MakeMatField("地穴", () => _deadlyMat, v => _deadlyMat = v,
            "deadly 格子的材质。留空时退用地面材质。"));
        root.Add(matRow);

        var btnRow = new VisualElement();
        btnRow.style.flexDirection = FlexDirection.Row;

        var genBtn = new Button(() => Generate(so, (LevelData)so.targetObject, _groundMat, _highlandMat, _deadlyMat))
        { text = "生成 MapPrefab" };
        genBtn.style.flexGrow = 1;
        genBtn.style.flexShrink = 1;
        genBtn.style.fontSize = 11;
        btnRow.Add(genBtn);

        var editBtn = new Button(() =>
        {
            var prefab = ((LevelData)so.targetObject).MapPrefab;
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("编辑 MapPrefab", "MapPrefab 尚未生成/指定。先点\"生成 MapPrefab\"。", "OK");
                return;
            }
            AssetDatabase.OpenAsset(prefab);
        })
        { text = "编辑" };
        editBtn.style.width = 50;
        editBtn.style.marginLeft = 4;
        editBtn.style.fontSize = 11;
        btnRow.Add(editBtn);

        root.Add(btnRow);
        return root;
    }

    static void EnsureDefaultMaterials()
    {
        if (_defaultsLoaded) return;
        _defaultsLoaded = true;
        _groundMat = AssetDatabase.LoadAssetAtPath<Material>(DefaultGroundMatPath);
        _highlandMat = AssetDatabase.LoadAssetAtPath<Material>(DefaultHighlandMatPath);
    }

    static VisualElement MakeMatField(string label, Func<Material> get, Action<Material> set, string tooltip)
    {
        var field = new ObjectField(label) { objectType = typeof(Material), value = get() };
        field.tooltip = tooltip;
        // 默认主题给字段标签 min-width:120px,"地面"两个字远撑不满,余下全成空白,
        // 视觉上像"输入框贴着下一个标签" —— 归零让标签收缩到文字自然宽,输入框紧随其后
        field.labelElement.style.minWidth = 0;
        field.style.flexGrow = 1;
        field.style.flexShrink = 1;
        field.style.minWidth = 0;
        field.RegisterValueChangedCallback(evt => set((Material)evt.newValue));
        return field;
    }

    /// <summary>
    /// 目标路径:已引用的 prefab 原地重建(保 GUID,引用不断);
    /// 未引用则放在 LevelData 同目录 Map.prefab。
    /// </summary>
    public static void Generate(SerializedObject so, LevelData levelData,
        Material groundMat, Material highlandMat, Material deadlyMat)
    {
        if (levelData.iSize <= 0 || levelData.jSize <= 0 || levelData.MapData.Count == 0)
        {
            EditorUtility.DisplayDialog("生成 MapPrefab", "MapData 为空 —— 请先在\"地图\"编辑器里画格子。", "OK");
            return;
        }

        string path;
        if (levelData.MapPrefab != null)
        {
            path = AssetDatabase.GetAssetPath(levelData.MapPrefab);
        }
        else
        {
            string assetPath = AssetDatabase.GetAssetPath(levelData);
            if (string.IsNullOrEmpty(assetPath))
            {
                EditorUtility.DisplayDialog("生成 MapPrefab",
                    "LevelData 资产尚未保存到磁盘,无法决定 prefab 落盘位置。", "OK");
                return;
            }
            path = $"{Path.GetDirectoryName(assetPath).Replace('\\', '/')}/Map.prefab";
        }

        bool existed = File.Exists(path);
        if (existed && !EditorUtility.DisplayDialog("生成 MapPrefab",
                $"将覆盖:\n{path}\n\n· 地块方块按当前 MapData 重建\n· 非地块子物体(如出生点标记)保留\n· 对手工地块方块的调整会被替换",
                "覆盖生成", "取消"))
            return;

        if (existed)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int kept = RemoveTileQuads(root);
                int quads = BuildTileQuads(root.transform, levelData, groundMat, highlandMat, deadlyMat);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Finish(so, path, quads, kept);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        else
        {
            var root = new GameObject("Map");
            int quads = BuildTileQuads(root.transform, levelData, groundMat, highlandMat, deadlyMat);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            Finish(so, path, quads, 0);
        }
    }

    /// <summary>
    /// 删除会被 MapInitialize 当成地块消费的子物体(整数格点 + MeshRenderer)。
    /// 判据与消费判据同源,所以无论旧 prefab 是本工具生成的还是手工搭的,
    /// 都不会留下与新生成方块叠在同一格点的残影。
    /// </summary>
    static int RemoveTileQuads(GameObject root)
    {
        int removed = 0;
        for (int k = root.transform.childCount - 1; k >= 0; k--)
        {
            var child = root.transform.GetChild(k);
            if (child.GetComponent<MeshRenderer>() == null) continue;
            if (!IsInteger(child.localPosition.x) || !IsInteger(child.localPosition.y)) continue;
            UnityEngine.Object.DestroyImmediate(child.gameObject);
            removed++;
        }
        return removed;
    }

    static bool IsInteger(float v) => Mathf.Approximately(v, Mathf.Round(v));

    static int BuildTileQuads(Transform parent, LevelData levelData,
        Material groundMat, Material highlandMat, Material deadlyMat)
    {
        int count = 0;
        foreach (var entry in levelData.MapData)
        {
            if (entry.i < 0 || entry.i >= levelData.iSize) continue;
            if (entry.j < 0 || entry.j >= levelData.jSize) continue;

            Material mat;
            string kind;
            if (entry.deadly)
            {
                mat = deadlyMat != null ? deadlyMat : groundMat;
                kind = "Den";
            }
            else if (entry.highland)
            {
                mat = highlandMat;
                kind = "Highland";
            }
            else
            {
                mat = groundMat;
                kind = "Ground";
            }

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"{kind} ({entry.j}, {entry.i})";
            var collider = quad.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = new Vector3(entry.j, entry.i, TileZ);
            quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
            count++;
        }
        return count;
    }

    /// <summary>把生成结果写回 MapPrefab 引用(Undo 可撤销)并汇报。</summary>
    static void Finish(SerializedObject so, string path, int quads, int kept)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null)
        {
            var prop = so.FindProperty("MapPrefab");
            if (prop != null)
            {
                Undo.RecordObject(so.targetObject, "Assign Generated MapPrefab");
                prop.objectReferenceValue = prefab;
                so.ApplyModifiedProperties();
            }
        }
        Debug.Log($"[MapPrefabGenerator] {quads} 个地块方块 → {path}(保留自定义子物体 {kept} 个)");
        EditorUtility.DisplayDialog("生成 MapPrefab",
            $"{quads} 个地块方块已写入:\n{path}\n\n保留自定义子物体 {kept} 个。\nMapPrefab 引用已自动填入。", "OK");
    }
}
