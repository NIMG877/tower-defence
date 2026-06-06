using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 音频路径表（纯存储 SO）。
/// 取代旧的 GeneralAudioPath.json（read via File.ReadAllText(Application.dataPath + ...)——build 后崩）。
/// 通过 Inspector 维护，构建时随 Resources 一起打包，运行时由 <see cref="GameDataService"/> 加载。
/// </summary>
[CreateAssetMenu(menuName = "TD/Static Data/Audio Path Collection", fileName = "AudioPathCollection")]
public class AudioPathCollection : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public string audioName;
        public string audioPath;
    }

    [SerializeField] private Entry[] _entries;

    public IReadOnlyList<Entry> Entries => _entries;

    /// <summary>一次性转成 Dictionary，给 <see cref="AudioManager"/> 用。</summary>
    public Dictionary<string, string> ToDictionary()
    {
        var d = new Dictionary<string, string>(_entries?.Length ?? 0);
        if (_entries == null) return d;
        foreach (var e in _entries)
        {
            if (string.IsNullOrEmpty(e.audioName)) continue;
            d[e.audioName] = e.audioPath ?? string.Empty;
        }
        return d;
    }
}
