using System.Collections.Generic;
using AbilitySystem;
using UnityEngine;

/// <summary>
/// 属性聚合引擎。纯 C# 类（非 MonoBehaviour），由 Entity 持有。
/// 按属性分桶存 List&lt;Modifier&gt;，lazy + 脏标记：modifier 增删置脏，
/// GetFinal 读取时才重算并缓存。读多写少高效，天然无漂移。
///
/// 聚合公式（阶段顺序固定，同类内部顺序无关）：
///   F = Σ AddFlat              直接加算
///   M = Σ AddPercent, clamp≥0  直接乘算累加和 &lt;0 补 0
///   G = Σ AddFlatFinal         最终加算
///   P = Π MulFinal_i, 单项&lt;0→1 最终乘算连乘，空桶→1（乘法单位元）
///   Final = ((base + F) * (1 + M) + G) * P
///
/// store 不持有 Buff 引用——buff 增删通过 BuffController 转译成 Add/RemoveModifiers。
/// </summary>
public class AttributeStore
{
    // 每个被加入的 modifier 带一个 group id（AddModifiers 时为整组分配同一个自增值）。
    // 移除时按 group 而非按值匹配——这样两个贡献相同 modifier 的不同 buff 不会互相误删
    // （各自身份由 group 区分）。这是 GAS "spec handle" 思路的简化版。
    private struct Entry
    {
        public Modifier mod;
        public int group;
    }

    private struct AttrState
    {
        public float baseValue;
        public List<Entry> entries;
        public bool dirty;
        public float cached;
    }

    private readonly Dictionary<string, AttrState> _states = new Dictionary<string, AttrState>();
    private int _nextGroup = 1;   // 0 表示"未加入"，自增从 1 起

    /// <summary>设置属性基础值。EntityStats.AttributesCaculateFirst 调用。</summary>
    public void SetBase(string attribute, float value)
    {
        AttrState s = EnsureState(attribute);
        s.baseValue = value;
        s.dirty = true;
        _states[attribute] = s;   // struct: 写回（EnsureState 返回的是副本，必须写回）
    }

    /// <summary>
    /// 加入一组 modifier（buff 创建/更新时）。整组分配同一 group id。
    /// 返回该 group id，调用方持有，移除时回传。
    /// </summary>
    public int AddModifiers(Modifier[] modifiers)
    {
        if (modifiers == null || modifiers.Length == 0) return 0;
        int group = _nextGroup++;
        for (int i = 0; i < modifiers.Length; i++)
        {
            AttrState s = EnsureState(modifiers[i].attribute);
            s.entries.Add(new Entry { mod = modifiers[i], group = group });
            s.dirty = true;
            _states[modifiers[i].attribute] = s;
        }
        return group;
    }

    /// <summary>移除某 group 的所有 modifier（buff 销毁时）。按 group 精确移除，不误删同值的其他 group。</summary>
    public void RemoveModifiers(int group)
    {
        if (group == 0) return;
        var keys = new List<string>(_states.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            string key = keys[i];
            AttrState s = _states[key];
            if (s.entries == null || s.entries.Count == 0) continue;
            int removed = s.entries.RemoveAll(e => e.group == group);
            if (removed > 0)
            {
                s.dirty = true;
                _states[key] = s;
            }
        }
    }

    /// <summary>读取最终值。dirty 则重算并缓存，否则返 cached。</summary>
    public float GetFinal(string attribute)
    {
        if (!_states.TryGetValue(attribute, out AttrState s))
        {
            // 未注册属性：返回 0 + 一次性 warn
            OneShotWarn.WarnOnce("attr-unknown:" + attribute,
                $"AttributeStore: 未注册属性 '{attribute}'，返回 0。");
            return 0f;
        }
        if (s.dirty)
        {
            s.cached = Compute(s);
            s.dirty = false;
            _states[attribute] = s;
        }
        return s.cached;
    }

    /// <summary>池回收：清所有 modifier + 置脏 + 缓存归零。不清 baseValue（属性固有值）。</summary>
    public void Clear()
    {
        var keys = new List<string>(_states.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            string key = keys[i];
            AttrState s = _states[key];
            if (s.entries != null) s.entries.Clear();
            s.dirty = true;
            s.cached = 0f;
            _states[key] = s;
        }
    }

    private AttrState EnsureState(string attribute)
    {
        if (!_states.TryGetValue(attribute, out AttrState s))
        {
            s = new AttrState { entries = new List<Entry>(), dirty = true };
            _states[attribute] = s;
        }
        return _states[attribute];
    }

    private float Compute(AttrState s)
    {
        float F = 0f, M = 0f, G = 0f, P = 1f;
        var list = s.entries;
        if (list != null)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Modifier m = list[i].mod;
                switch (m.op)
                {
                    case ModifierOp.AddFlat:      F += m.magnitude; break;
                    case ModifierOp.AddPercent:   M += m.magnitude; break;
                    case ModifierOp.AddFlatFinal: G += m.magnitude; break;
                    case ModifierOp.MulFinal:
                        P *= m.magnitude < 0f ? 1f : m.magnitude;  // 单项 <0 → 1
                        break;
                }
            }
        }
        if (M < 0f) M = 0f;   // 直接乘算累加和 <0 钳 0
        return ((s.baseValue + F) * (1f + M) + G) * P;
    }
}
