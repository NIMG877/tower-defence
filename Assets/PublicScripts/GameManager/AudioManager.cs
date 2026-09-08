using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

/// <summary>全局音效管理。每次播放借出一个独立 AudioSource（声音池），
/// 同一片段的叠放由活跃声音表管理：并发达到上限时偷掉最旧的声音（voice steal），
/// 剩余活跃声音按剩余时长加权分摊音量——相同片段的副本相位高度相关，
/// 线性叠放峰值最多翻 N 倍，不加约束就会超出满量程削波失真。</summary>
public class AudioManager
{
    public static AudioManager Manager
    {
        get
        {
            if (_instance == null)
            {
                _instance = new AudioManager();
            }
            return _instance;
        }
    }
    private static AudioManager _instance;

    /// <summary>同一片段允许的最大并发声音数，超出即偷最旧</summary>
    private const int MaxVoicesPerClip = 2;

    /// <summary>一条借出的声音。权重用剩余时长而非均分：新触发的攻击段主导
    /// 重复播放的听感、保留更多动态，旧声音的衰减尾承受更多衰减。</summary>
    private sealed class Voice
    {
        public readonly AudioSource Source;
        public readonly float Length;
        public readonly double StartDspTime;

        public Voice(AudioSource source, float length)
        {
            Source = source;
            Length = length;
            StartDspTime = AudioSettings.dspTime;
        }

        /// <summary>剩余播放时长，钳到 0：片段播完到回收协程摘表之间有一帧
        /// 滞后窗口，此窗口内触发的叠放给垂死声音的权重自然归零；
        /// 总权重恒为正（新声音的权重 = 完整时长），不会除零</summary>
        public float Remaining => Mathf.Max(Length - (float)(AudioSettings.dspTime - StartDspTime), 0f);
    }

    private readonly Transform _voiceRoot;
    private readonly ObjectPool<AudioSource> _voicePool;
    /// <summary>片段名 -> 活跃声音，按触发顺序排列（最旧在前）</summary>
    private readonly Dictionary<string, List<Voice>> _activeVoices = new Dictionary<string, List<Voice>>();
    private Dictionary<string, AudioClip> _generalAudioClipDic;

    private AudioManager()
    {
        _voiceRoot = new GameObject("AudioVoices").transform;
        _voicePool = new ObjectPool<AudioSource>(
            () =>
            {
                AudioSource source = new GameObject("Voice").AddComponent<AudioSource>();
                // AddComponent 出来的 AudioSource 默认 playOnAwake=true，必须关掉
                source.playOnAwake = false;
                source.transform.SetParent(_voiceRoot);
                return source;
            },
            actionOnRelease: source => source.gameObject.SetActive(false),
            actionOnDestroy: source => Object.Destroy(source.gameObject),
            collectionCheck: true);
        _generalAudioClipDic = new Dictionary<string, AudioClip>();
        foreach (var item in GameDataService.AudioClips)
        {
            _generalAudioClipDic[item.Key] = item.Value;
        }
    }
    public void PlayAudio(string clipName)
    {
        if (!_generalAudioClipDic.TryGetValue(clipName, out AudioClip audioClip))
        {
            Debug.LogError("audio undefined");
            return;
        }

        if (!_activeVoices.TryGetValue(clipName, out List<Voice> voices))
        {
            voices = new List<Voice>();
            _activeVoices[clipName] = voices;
        }
        if (voices.Count >= MaxVoicesPerClip)
        {
            // 偷最旧：活跃表在这里立即摘除，本次播放的权重分摊才能按真实并发算；
            // 被偷声音的归还由它自己的回收协程完成
            Voice oldest = voices[0];
            voices.RemoveAt(0);
            oldest.Source.Stop();
        }

        AudioSource source = _voicePool.Get();
        // 归还时被 SetActive(false)，借出必须重新激活，否则 AudioSource 不出声
        source.gameObject.SetActive(true);
        source.clip = audioClip;
        Voice voice = new Voice(source, audioClip.length);
        voices.Add(voice);
        // 各声音音量之和恒为 1：同相叠放的总峰值仍被钳在单次播放的峰值内，
        // 这一抗削波性质对任何归一化权重都成立，与权重的具体取法无关
        float totalWeight = 0f;
        for (int i = 0; i < voices.Count; i++)
        {
            totalWeight += voices[i].Remaining;
        }
        for (int i = 0; i < voices.Count; i++)
        {
            voices[i].Source.volume = voices[i].Remaining / totalWeight;
        }
        source.Play();
        ReturnWhenFinished(voices, voice).Forget();
    }

    /// <summary>回收协程是归还池的唯一路径（双归还由池的 collectionCheck 抛出）。
    /// 活跃表摘除是幂等的：自然播完走这里，被偷的声音已在偷取路径先行摘除，
    /// 此处 List.Remove 返回 false 即空操作。</summary>
    private async UniTaskVoid ReturnWhenFinished(List<Voice> voices, Voice voice)
    {
        await UniTask.WaitUntil(() => !voice.Source.isPlaying);
        voices.Remove(voice);
        _voicePool.Release(voice.Source);
    }
}
