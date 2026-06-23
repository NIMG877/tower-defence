using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// EntityPrefabID 下拉框:从 EntityDataCollection 拉所有 EntityData,选项文案
/// "<c>-<n> (ChineseName)",首位 "(空)" 表示 EntityID.Null。
///
/// 写回:通过 SerializedProperty 的 ID_C / ID_N 子字段双写,支持 Undo。
/// 缓存:EntityDataCollection 整个编辑器生命周期只加载一次;若资产被外部
/// 替换,调 <see cref="InvalidateCache"/> 或重启 Editor。
/// </summary>
public static class EntityIdPicker
{
    const string ResourcePath = "GameDatas/EntityDataCollection";
    static EntityDataCollection _cache;
    static EntityData[] _cacheData;

    public static void InvalidateCache()
    {
        _cache = null;
        _cacheData = null;
    }

    static EntityData[] LoadAll()
    {
        if (_cacheData != null) return _cacheData;
        _cache = Resources.Load<EntityDataCollection>(ResourcePath);
        _cacheData = _cache != null ? (_cache.EntityBasicDatas ?? new EntityData[0]) : new EntityData[0];
        return _cacheData;
    }

    public static VisualElement Build(SerializedProperty prop)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 2;

        // 字段名 Label(模拟 PropertyField 的左侧标签)
        // 用 width(固定) 而非 minWidth:label 文本 "EntityPrefabID" 自然宽 ~110px,
        // 如果只设 minWidth,label 会按内容撑开,picker 虽然 flexGrow=1 也吃不准剩余空间
        var fieldLabel = new Label(prop.displayName) { tooltip = prop.tooltip };
        fieldLabel.style.width = 120;
        fieldLabel.style.marginRight = 4;
        fieldLabel.style.flexShrink = 0;
        fieldLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        row.Add(fieldLabel);

        // EntityPrefabID 是 struct,SerializedProperty 路径里能直接 FindPropertyRelative
        var idCProp = prop.FindPropertyRelative("ID_C");
        var idNProp = prop.FindPropertyRelative("ID_N");
        if (idCProp == null || idNProp == null) return row;

        // 加载所有 EntityData,build (id -> display) 列表
        var all = LoadAll();
        var ids = new List<EntityID>();
        var display = new List<string>();

        // 首位:空槽
        ids.Add(new EntityID(null, 0));
        display.Add("(空)");

        for (int i = 0; i < all.Length; i++)
        {
            var d = all[i];
            if (d == null || string.IsNullOrEmpty(d.ID.ID_C)) continue; // 跳过 OnValidate 报警的脏数据
            ids.Add(d.ID);
            string label = !string.IsNullOrEmpty(d.ChineseName)
                ? $"{d.ID.ID_C}-{d.ID.ID_N} ({d.ChineseName})"
                : $"{d.ID.ID_C}-{d.ID.ID_N}";
            display.Add(label);
        }

        // 当前值(注意:Unity 序列化 null string 后,stringValue 读回的是 "" 而非 null)
        var current = new EntityID(
            string.IsNullOrEmpty(idCProp.stringValue) ? null : idCProp.stringValue,
            idNProp.intValue);
        int initialIdx = 0;
        if (!current.IsNull)
        {
            bool found = false;
            for (int i = 1; i < ids.Count; i++) // skip (空)
            {
                if (ids[i].Equals(current)) { initialIdx = i; found = true; break; }
            }
            // 找不到(自定义/已删除):追加 "未知" 占位
            if (!found)
            {
                ids.Add(current);
                display.Add($"{current.ID_C}-{current.ID_N} (未知)");
                initialIdx = display.Count - 1;
            }
        }

        var popup = new PopupField<string>(display, initialIdx);
        popup.style.flexGrow = 1;
        popup.style.flexShrink = 1;
        popup.style.minWidth = 0;
        popup.RegisterValueChangedCallback(evt =>
        {
            int idx = display.IndexOf(evt.newValue);
            if (idx < 0) return;
            var picked = ids[idx];
            Undo.RecordObject(prop.serializedObject.targetObject, "Change EntityPrefabID");
            idCProp.stringValue = picked.ID_C; // null 表示空槽
            idNProp.intValue = picked.ID_N;
            prop.serializedObject.ApplyModifiedProperties();
        });
        row.Add(popup);

        return row;
    }
}
