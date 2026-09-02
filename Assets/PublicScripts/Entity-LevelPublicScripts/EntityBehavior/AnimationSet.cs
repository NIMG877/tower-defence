using System;
using System.Collections.Generic;
using Spine.Unity;

/// <summary>
/// 一套已解析的动画资源（槽位→AnimationReferenceAsset）。
/// 单动画槽与组动画槽分两个字典；槽位属于哪类由 <see cref="GroupSlots"/> 静态定义。
/// 替代旧版 15 个具名字段 + 7 组镜像 switch 的形态：新增槽位=枚举加一项 + From 填一行。
/// </summary>
public sealed class AnimationSet
{
    /// <summary>组动画槽位（一对多）。其余槽位均为单动画。</summary>
    public static readonly AnimationSlot[] GroupSlots =
    {
        AnimationSlot.AttackRemote,
        AnimationSlot.AttackClose,
        AnimationSlot.Charge,
    };

    private readonly Dictionary<AnimationSlot, AnimationReferenceAsset> _singles = new Dictionary<AnimationSlot, AnimationReferenceAsset>();
    private readonly Dictionary<AnimationSlot, AnimationReferenceAsset[]> _groups = new Dictionary<AnimationSlot, AnimationReferenceAsset[]>();

    public static bool IsGroup(AnimationSlot slot)
    {
        return Array.IndexOf(GroupSlots, slot) >= 0;
    }

    public static AnimationSet From(AnimationResources resources)
    {
        AnimationResources.DefaultAnimationTemplate defaults = resources.Defaults;
        AnimationResources.MovementAnimationGroup movement = resources.Movement;
        AnimationResources.AttackAnimationGroup attack = resources.Attack;
        var set = new AnimationSet();
        set.SetSingle(AnimationSlot.Default, defaults.Default);
        set.SetSingle(AnimationSlot.Idle, defaults.Idle);
        set.SetSingle(AnimationSlot.Start, defaults.Start);
        set.SetSingle(AnimationSlot.Cast, defaults.Cast);
        set.SetSingle(AnimationSlot.Die, defaults.Die);
        set.SetSingle(AnimationSlot.Move, movement.Move);
        set.SetSingle(AnimationSlot.JumpBegin, movement.Jump.Begin);
        set.SetSingle(AnimationSlot.JumpLoop, movement.Jump.Loop);
        set.SetSingle(AnimationSlot.JumpEnd, movement.Jump.End);
        set.SetSingle(AnimationSlot.AttackBegin, attack.AttackBegin);
        set.SetSingle(AnimationSlot.AttackEnd, attack.AttackEnd);
        set.SetGroup(AnimationSlot.AttackRemote, attack.AttackRemote);
        set.SetGroup(AnimationSlot.AttackClose, attack.AttackClose);
        set.SetSingle(AnimationSlot.ChargeBegin, attack.Charge.ChargeBegin);
        set.SetGroup(AnimationSlot.Charge, attack.Charge.Charge);
        set.SetSingle(AnimationSlot.ChargeEnd, attack.Charge.ChargeEnd);
        return set;
    }

    public AnimationReferenceAsset GetSingle(AnimationSlot slot)
    {
        return _singles.TryGetValue(slot, out AnimationReferenceAsset value) ? value : null;
    }

    public AnimationReferenceAsset[] GetGroup(AnimationSlot slot)
    {
        return _groups.TryGetValue(slot, out AnimationReferenceAsset[] value) ? value : null;
    }

    public void SetSingle(AnimationSlot slot, AnimationReferenceAsset animation)
    {
        if (animation != null) _singles[slot] = animation;
        else _singles.Remove(slot);
    }

    public void SetGroup(AnimationSlot slot, AnimationReferenceAsset[] animations)
    {
        if (animations != null) _groups[slot] = animations;
        else _groups.Remove(slot);
    }

    public AnimationSet Copy()
    {
        var copy = new AnimationSet();
        foreach (KeyValuePair<AnimationSlot, AnimationReferenceAsset> kv in _singles) copy._singles[kv.Key] = kv.Value;
        foreach (KeyValuePair<AnimationSlot, AnimationReferenceAsset[]> kv in _groups) copy._groups[kv.Key] = kv.Value;
        return copy;
    }

    /// <summary>按覆盖申请就地修改本集合（含清槽）。资源解析失败（名字未登记）会抛 KeyNotFoundException——让配置错误当场暴露。</summary>
    public void Apply(AnimationOverride animations, AnimationResources resources)
    {
        Apply(animations, resources.GetAnimation, resources.GetAnimationGroup);
    }

    /// <summary>解析器注入重载：单测无 SO 资产时用 lambda 提供资源。</summary>
    public void Apply(AnimationOverride animations, Func<string, AnimationReferenceAsset> resolveSingle, Func<string, AnimationReferenceAsset[]> resolveGroup)
    {
        if (animations == null) return;
        foreach (AnimationSlot slot in animations.ClearedSlots)
        {
            if (IsGroup(slot)) _groups.Remove(slot);
            else _singles.Remove(slot);
        }
        foreach (KeyValuePair<AnimationSlot, string> kv in animations.Entries)
        {
            if (IsGroup(kv.Key)) SetGroup(kv.Key, resolveGroup(kv.Value));
            else SetSingle(kv.Key, resolveSingle(kv.Value));
        }
    }

    public IEnumerable<AnimationReferenceAsset> EnumerateSingles()
    {
        return _singles.Values;
    }

    public IEnumerable<AnimationReferenceAsset[]> EnumerateGroups()
    {
        return _groups.Values;
    }

    /// <summary>移动分支（走/跳三段）→ 对应单动画槽。表现层专用。</summary>
    public static AnimationSlot MoveBranchSlot(MoveAnimationBranch branch)
    {
        switch (branch)
        {
            case MoveAnimationBranch.JumpBegin: return AnimationSlot.JumpBegin;
            case MoveAnimationBranch.JumpLoop: return AnimationSlot.JumpLoop;
            case MoveAnimationBranch.JumpEnd: return AnimationSlot.JumpEnd;
            default: return AnimationSlot.Move;
        }
    }
}
