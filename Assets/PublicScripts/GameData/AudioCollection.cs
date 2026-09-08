using UnityEngine;

/// <summary>
/// 通用音效集合（纯存储 SO）。
/// 取代 Resources/Audios/GeneralAudioPath.json（逻辑名→Resources 路径字符串）：
/// 条目直接引用 AudioClip，改名/移动资源安全，缺失引用在 Inspector 可见。
/// 逻辑名与素材文件名无关（get_cost → b_ui_getcast），Name 是调用方使用的键，显式维护。
/// </summary>
[CreateAssetMenu(menuName = "TD/Static Data/Audio Collection", fileName = "AudioCollection")]
public class AudioCollection : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        public string Name;
        public AudioClip Clip;
    }

    [SerializeField] private Entry[] _entries;

    /// <summary>原始数据数组（只读视图）。</summary>
    public Entry[] Entries => _entries;
}
