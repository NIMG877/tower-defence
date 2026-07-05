using System.Collections.Generic;
using UnityEngine;
using Unity.Plastic.Newtonsoft.Json;

/// <summary>
/// 静态数据门面。取代旧的 <c>GameDataManager.EntityDataCollection</c> / <c>GameDataManager.AudioData</c>。
///
/// 设计：
/// - 纯静态（不挂 GameObject）—— 所有 SOs 通过 <c>Resources.Load</c> 获取，build 后正常可用
/// - 懒构建：第一次访问 <see cref="EntityRepository"/> / <see cref="AudioPaths"/> 时才加载
/// - 生命周期由 Unity 资源系统管理（Resources 加载的资产随场景切换持久）
///
/// 调用方迁移：
/// - <c>GameDataManager.EntityDataCollection.GetEntityData(id)</c> → <c>GameDataService.EntityRepository.Get(id)</c>
/// - <c>GameDataManager.EntityDataCollection.GetEntityDataByIDC("c")</c> → <c>GameDataService.EntityRepository.GetByCategory("c")</c>
/// - <c>GameDataManager.AudioData</c> → <c>GameDataService.AudioPaths</c>
/// </summary>
public static class GameDataService
{
    // 资源路径常量
    const string EntityCollectionPath = "GameDatas/EntityDataCollection";
    const string GeneralAudioTextPath  = "Audios/GeneralAudioPath";   // 资源文件为 .json，Load<TextAsset> 时不带扩展名

    // 缓存
    static EntityDataCollection   _entityCollection;
    static EntityDataRepository   _entityRepository;
    static IReadOnlyDictionary<string, string> _audioPaths;

    /// <summary>实体数据仓库（懒加载 SO + 懒构建索引；首次访问后缓存，ResetCache 显式失效）</summary>
    public static EntityDataRepository EntityRepository
    {
        get
        {
            if (_entityRepository != null) return _entityRepository;
            if (_entityCollection == null)
                _entityCollection = Resources.Load<EntityDataCollection>(EntityCollectionPath);
            _entityRepository = EntityDataRepository.Build(_entityCollection != null ? _entityCollection.EntityBasicDatas : null);
            return _entityRepository;
        }
    }

    /// <summary>
    /// 音频路径表（懒加载）。
    /// 数据源仍是 <c>Resources/Audios/GeneralAudioPath.json</c>，但通过 <c>Resources.Load&lt;TextAsset&gt;</c>
    /// 读取，build 后也能正常加载（解决了旧的 <c>File.ReadAllText(Application.dataPath + …)</c> build 崩溃问题）。
    /// </summary>
    public static IReadOnlyDictionary<string, string> AudioPaths
    {
        get
        {
            if (_audioPaths != null) return _audioPaths;
            var textAsset = Resources.Load<TextAsset>(GeneralAudioTextPath);
            if (textAsset == null)
            {
                Debug.LogError($"[GameDataService] Resources/{GeneralAudioTextPath}.json 加载失败");
                _audioPaths = new Dictionary<string, string>();
                return _audioPaths;
            }
            try
            {
                _audioPaths = JsonConvert.DeserializeObject<Dictionary<string, string>>(textAsset.text)
                              ?? new Dictionary<string, string>();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GameDataService] JSON 解析失败: {ex.Message}");
                _audioPaths = new Dictionary<string, string>();
            }
            return _audioPaths;
        }
    }

    /// <summary>重置缓存（仅供测试用 / 资源热重载后手动调）。</summary>
    public static void ResetCache()
    {
        _entityCollection = null;
        _entityRepository = null;
        _audioPaths = null;
    }
}
