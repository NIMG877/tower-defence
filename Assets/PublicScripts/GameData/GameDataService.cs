using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 静态数据门面。取代旧的 <c>GameDataManager.EntityDataCollection</c> / <c>GameDataManager.AudioData</c>。
///
/// 设计：
/// - 纯静态（不挂 GameObject）—— 所有 SOs 通过 <c>Resources.Load</c> 获取，build 后正常可用
/// - 懒构建：第一次访问 <see cref="EntityRepository"/> / <see cref="AudioClips"/> 时才加载
/// - 生命周期由 Unity 资源系统管理（Resources 加载的资产随场景切换持久）
///
/// 调用方迁移：
/// - <c>GameDataManager.EntityDataCollection.GetEntityData(id)</c> → <c>GameDataService.EntityRepository.Get(id)</c>
/// - <c>GameDataManager.EntityDataCollection.GetEntityDataByIDC("c")</c> → <c>GameDataService.EntityRepository.GetByCategory("c")</c>
/// - <c>GameDataManager.AudioData</c> → <c>GameDataService.AudioClips</c>
/// </summary>
public static class GameDataService
{
    // 资源路径常量
    const string EntityCollectionPath = "GameDatas/EntityDataCollection";
    const string AudioCollectionPath  = "Audios/GeneralAudioCollection";

    // 缓存
    static EntityDataCollection   _entityCollection;
    static EntityDataRepository   _entityRepository;
    static AudioCollection        _audioCollection;
    static IReadOnlyDictionary<string, AudioClip> _audioClips;

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

    /// <summary>音频表（懒加载）：逻辑名 → AudioClip，来自 <see cref="AudioCollection"/> SO。</summary>
    public static IReadOnlyDictionary<string, AudioClip> AudioClips
    {
        get
        {
            if (_audioClips != null) return _audioClips;
            if (_audioCollection == null)
                _audioCollection = Resources.Load<AudioCollection>(AudioCollectionPath);
            var clips = new Dictionary<string, AudioClip>();
            if (_audioCollection == null)
            {
                Debug.LogError($"[GameDataService] Resources/{AudioCollectionPath}.asset 加载失败");
            }
            else
            {
                foreach (var entry in _audioCollection.Entries)
                {
                    // 引用丢失的条目在此响亮失败并跳过：进表会在播放时表现为无声
                    if (entry.Clip == null)
                    {
                        Debug.LogError($"[GameDataService] {AudioCollectionPath}.asset 条目 {entry.Name} 的 Clip 引用丢失");
                        continue;
                    }
                    clips[entry.Name] = entry.Clip;
                }
            }
            _audioClips = clips;
            return _audioClips;
        }
    }

    /// <summary>重置缓存（仅供测试用 / 资源热重载后手动调）。</summary>
    public static void ResetCache()
    {
            _entityCollection = null;
            _entityRepository = null;
            _audioCollection = null;
            _audioClips = null;
    }
}
