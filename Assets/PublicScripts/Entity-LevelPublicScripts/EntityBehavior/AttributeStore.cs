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
    private struct AttrState
    {
        public float baseValue;
        public List<Modifier> modifiers;
        public bool dirty;
        public float cached;
    }

    private readonly Dictionary<string, AttrState> _states = new Dictionary<string, AttrState>();

    /// <summary>设置属性基础值。EntityStats.AttributesCaculateFirst 调用。</summary>
    public void SetBase(string attribute, float value)
    {
        AttrState s = EnsureState(attribute);
        s.baseValue = value;
        s.dirty = true;
        _states[attribute] = s;   // struct: 写回（EnsureState 返回的是副本，必须写回）
    }

    /// <summary>加入一组 modifier（buff 创建/更新时）。分桶到各属性，置脏。</summary>
    public void AddModifiers(Modifier[] modifiers)
    {
        if (modifiers == null) return;
        for (int i = 0; i < modifiers.Length; i++)
        {
            AttrState s = EnsureState(modifiers[i].attribute);
            s.modifiers.Add(modifiers[i]);
            s.dirty = true;
            _states[modifiers[i].attribute] = s;   // struct: 写回
        }
    }

    /// <summary>移除一组 modifier（buff 销毁时）。按值匹配移除，置脏。</summary>
    public void RemoveModifiers(Modifier[] modifiers)
    {
        if (modifiers == null) return;
        for (int i = 0; i < modifiers.Length; i++)
        {
            if (!_states.TryGetValue(modifiers[i].attribute, out AttrState s)) continue;
            s.modifiers.RemoveAll(m => m.attribute == modifiers[i].attribute
                                    && m.op == modifiers[i].op
                                    && m.magnitude == modifiers[i].magnitude);
            s.dirty = true;
            _states[modifiers[i].attribute] = s;   // struct: 写回
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
            _states[attribute] = s;   // struct: 写回
        }
        return s.cached;
    }

    /// <summary>池回收：清所有 modifier + 置脏 + 缓存归零。不清 baseValue（属性固有值）。</summary>
    public void Clear()
    {
        // 先快照 key 集合再写回 value，避免对字典 value 的 foreach 内赋值引发的结构性歧义。
        // （技术上改 value 不改 key 结构在 C# 是安全的，但快照写法语义更清晰且无隐患。）
        var keys = new List<string>(_states.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            string key = keys[i];
            AttrState s = _states[key];
            if (s.modifiers != null) s.modifiers.Clear();
            s.dirty = true;
            s.cached = 0f;
            _states[key] = s;   // struct: 写回
        }
    }

    private AttrState EnsureState(string attribute)
    {
        if (!_states.TryGetValue(attribute, out AttrState s))
        {
            s = new AttrState { modifiers = new List<Modifier>(), dirty = true };
            _states[attribute] = s;
        }
        return _states[attribute];
    }

    private float Compute(AttrState s)
    {
        float F = 0f, M = 0f, G = 0f, P = 1f;
        var list = s.modifiers;
        if (list != null)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Modifier m = list[i];
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
