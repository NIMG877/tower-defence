using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 实体数据集合（纯存储 SO）。
/// 只负责把 <see cref="EntityData"/> 数组序列化成 .asset；
/// 所有查询由 <see cref="EntityDataRepository"/> 负责（懒构建索引）。
/// 写入请用 <see cref="SetEntityBasicDatas"/>（JSON2DataAsset 工具用），不要反射写私有字段。
/// </summary>
[CreateAssetMenu(menuName = "TD/Static Data/Entity Data Collection", fileName = "EntityDataCollection")]
public class EntityDataCollection : ScriptableObject, ISerializationCallbackReceiver
{
    [SerializeField] private EntityData[] _entityBasicDatas;

    /// <summary>原始数据数组（只读视图）。</summary>
    public EntityData[] EntityBasicDatas => _entityBasicDatas;

    /// <summary>
    /// 写入入口。由 JSON2DataAsset 工具调用。
    /// 自动 EditorUtility.SetDirty 标记，调用方仍需 AssetDatabase.SaveAssets 落盘。
    /// </summary>
    public void SetEntityBasicDatas(EntityData[] data)
    {
        _entityBasicDatas = data;
#if UNITY_EDITOR
        if (this != null) EditorUtility.SetDirty(this);
#endif
    }

    // —— ISerializationCallbackReceiver ——
    // 不在这里构建索引。索引归 Repository，懒构建。
    // 这里只放"反序列化后必须立即修的数据"。目前 YAML 失同步的迁移在 OnValidate 里做（见下）。

    void ISerializationCallbackReceiver.OnBeforeSerialize() { }
    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
        // 留空：不要在序列化 hook 里改字段，会触发循环
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor 校验 + YAML 失同步兜底。
    /// 触发时机：字段被改、资产重新反序列化、进入 Play 模式。
    /// 注意：不修改资产本身（避免循环），只打 Warning + 提示修复菜单。
    /// </summary>
    void OnValidate()
    {
        if (_entityBasicDatas == null) return;

        var seenIds = new HashSet<string>();
        for (int i = 0; i < _entityBasicDatas.Length; i++)
        {
            var data = _entityBasicDatas[i];
            // 校验 1: ID_C 不应为空
            if (string.IsNullOrEmpty(data.ID.ID_C))
            {
                Debug.LogWarning(
                    $"[EntityDataCollection] {name} index={i} has empty ID.ID_C. " +
                    $"请跑 Tools > Static Data > Rebuild Entity Collection 重新生成 .asset。",
                    this);
                continue;
            }
            // 校验 2: ID 唯一
            var key = $"{data.ID.ID_C}-{data.ID.ID_N}";
            if (!seenIds.Add(key))
            {
                Debug.LogWarning(
                    $"[EntityDataCollection] {name} duplicate ID: {key} (index={i}).",
                    this);
            }
        }
    }
#endif
}
