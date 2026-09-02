using System.Collections.Generic;

/// <summary>
/// 动画资源槽位。用于解析动画资源，以及在 <see cref="AnimationOverride"/> 中显式清空某个覆盖槽。
/// 注：与 <see cref="EntityState"/> 的语义不重合——本枚举标识动画资源槽位，
///     EntityState 标识逻辑动画状态。
/// </summary>
public enum AnimationSlot
{
    Default,
    Idle,
    Move,
    JumpBegin,
    JumpLoop,
    JumpEnd,
    Start,
    Cast,
    Die,
    AttackRemote,
    AttackClose,
    AttackBegin,
    AttackEnd,
    ChargeBegin,
    Charge,
    ChargeEnd,
}

/// <summary>
/// 一次动画覆盖申请：槽位→资源名。经索引器读写（空值=移除，不算覆盖槽）。
/// <see cref="Clear"/> 显式清空槽位（覆盖到"无动画"）。
/// </summary>
public sealed class AnimationOverride
{
    private readonly Dictionary<AnimationSlot, string> _entries = new Dictionary<AnimationSlot, string>();

    /// <summary>显式清空的槽位（覆盖为"无动画"），也算覆盖槽。</summary>
    internal readonly HashSet<AnimationSlot> ClearedSlots = new HashSet<AnimationSlot>();

    /// <summary>槽位→资源名。赋 null/空串=移除该槽。</summary>
    public string this[AnimationSlot slot]
    {
        get { return _entries.TryGetValue(slot, out string value) ? value : null; }
        set
        {
            if (string.IsNullOrEmpty(value)) _entries.Remove(slot);
            else _entries[slot] = value;
        }
    }

    internal Dictionary<AnimationSlot, string> Entries { get { return _entries; } }

    public AnimationOverride Clear(params AnimationSlot[] slots)
    {
        foreach (AnimationSlot slot in slots)
        {
            ClearedSlots.Add(slot);
        }
        return this;
    }

    /// <summary>
    /// Slots this override affects: every slot with a non-empty resource, plus
    /// cleared slots. One-shot entries seed their pending-consumption set from this.
    /// </summary>
    public HashSet<AnimationSlot> GetCoveredSlots()
    {
        var covered = new HashSet<AnimationSlot>(ClearedSlots);
        covered.UnionWith(_entries.Keys);
        return covered;
    }
}
