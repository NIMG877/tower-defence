using System.Collections.Generic;
using Spine;
using Spine.Unity;
using UnityEngine;

public enum MoveAnimationBranch
{
    Normal,
    JumpBegin,
    JumpLoop,
    JumpEnd,
}

public enum AttackAnimationBranch
{
    Normal,
    Charge,
}

public sealed class AnimationOverrideHandle
{
    internal int Id;
    internal AnimationMachine Machine;
}

/// <summary>
/// 动画表现层：订阅 EntityStateMachine 的状态/相位事件，解析槽位（含覆盖优先级）并驱动 Spine。
/// 不持有逻辑状态；动画时机（OnAttack 帧、各段播完）经 Notify* 上报回状态机。
/// 攻击序列编排（前摇→主动段→后摇→Idle）与 TimeScale 缩放逻辑自旧实现原样搬运。
/// </summary>
public class AnimationMachine : MonoBehaviour, IPoolOperation
{
    private SkeletonAnimation skeleton;
    private Entity thisEntity;
    private EntityStateMachine _sm;
    private EventData event_attack;
    // OnStart 是数据侧存在但逻辑不消费的事件（多个骨骼的 Start 动画时间轴挂有它，部署必播）——显式静默，见 HandleAnimationStateEvent
    private EventData event_start;
    private AnimationResources _animationResources;
    private AnimationSet _baseAnimations;
    private AnimationSet _activeAnimations;
    private readonly List<OverrideEntry> _overrides = new List<OverrideEntry>();
    private int _nextOverrideId;
    private MoveAnimationBranch _moveBranch;
    private AttackAnimationBranch _attackBranch;

    // 当前攻击编排段（播完上报以 逻辑状态/相位 + Animation 对象身份 双重判定，与旧实现同防线；
    // 跨槽位复用同一资产是既有惯用法（曾有用同一资产填 AttackClose/AttackRemote 两槽的配置），身份单义不可靠）
    private AnimationReferenceAsset[] _attackGroup;
    private AnimationReferenceAsset _currentAttackBegin;
    private AnimationReferenceAsset _currentAttackEnd;
    private Spine.Animation _activeAttackAnim;
    private Spine.Animation _endAnim;
    private Spine.Animation _dieAnim;
    private Spine.Animation _startAnim;
    private Spine.Animation _castAnim;

    public delegate void OperationsOnAttackAnimationBegin();

    /// <summary>
    /// Fired at the very start of the attack animation.
    /// Use for windowed checks (e.g. skill parry windows) that need to begin at the
    /// first frame of the attack animation.
    /// </summary>
    public event OperationsOnAttackAnimationBegin OnAttackAnimationBegin;

    /// <summary>
    /// Move 分支（走/跳三段）为纯表现分支：逻辑方先设分支再 TrySetState(Move)，
    /// 分支在转换失败时无害残留（下一次设置覆盖）。
    /// </summary>
    public void SetMoveBranch(MoveAnimationBranch branch)
    {
        _moveBranch = branch;
    }

    /// <summary>
    /// Attack 分支（普攻/蓄力）：每个 TrySetAttackState 调用点先设分支；
    /// 蓄力转换失败时调用方须回设 Normal。
    /// </summary>
    public void SetAttackBranch(AttackAnimationBranch branch)
    {
        _attackBranch = branch;
    }

    public float ResolveAnimationDuration(AnimationSlot slot)
    {
        AnimationReferenceAsset animation;
        if (AnimationSet.IsGroup(slot))
        {
            AnimationReferenceAsset[] group = ResolveAnimations().GetGroup(slot);
            animation = (group != null && group.Length > 0) ? group[0] : null;
        }
        else
        {
            animation = ResolveAnimations().GetSingle(slot);
        }
        return animation != null ? animation.Animation.Duration : 0;
    }

    public float ResolveNamedAnimationDuration(string resourceName)
    {
        AnimationReferenceAsset animation = _animationResources.GetAnimation(resourceName);
        return animation != null ? animation.Animation.Duration : 0;
    }

    public AnimationOverrideHandle AddOverride(object owner, AnimationOverride animations, int priority = 0)
    {
        var handle = new AnimationOverrideHandle { Id = ++_nextOverrideId, Machine = this };
        _overrides.Add(new OverrideEntry(handle.Id, owner, animations, priority));
        RegisterMixes(animations);
        return handle;
    }

    public void RemoveOverride(AnimationOverrideHandle handle)
    {
        if (handle == null || handle.Machine != this) return;
        _overrides.RemoveAll(entry => entry.Id == handle.Id);
    }

    public void RemoveOverrides(object owner)
    {
        _overrides.RemoveAll(entry => ReferenceEquals(entry.Owner, owner));
    }

    /// <summary>
    /// Registers a one-shot override: it layers into animation resolution like a
    /// persistent override (priority order), and is consumed whole the first
    /// time the machine plays any of its covered slots. No state transition is
    /// performed — the override waits for the machine's natural flow.
    /// </summary>
    public AnimationOverrideHandle AddOneShotOverride(object owner, AnimationOverride animations, int priority = 0)
    {
        var handle = new AnimationOverrideHandle { Id = ++_nextOverrideId, Machine = this };
        _overrides.Add(new OverrideEntry(handle.Id, owner, animations, priority, animations.GetCoveredSlots()));
        RegisterMixes(animations);
        return handle;
    }

    // One-shot consumption: called wherever a slot's animation is actually
    // played. The whole entry goes on its FIRST use — covering slots that may
    // never play (e.g. AttackClose on a ranged attacker) must not make the
    // entry immortal. Removal only affects the next ResolveAnimations; the
    // already-resolved active set keeps playing the override through the
    // current animation cycle.
    private void ConsumeOneShots(params AnimationSlot[] slots)
    {
        for (int i = _overrides.Count - 1; i >= 0; i--)
        {
            HashSet<AnimationSlot> covered = _overrides[i].CoveredSlots;
            if (covered == null) continue;
            for (int s = 0; s < slots.Length; s++)
            {
                if (covered.Contains(slots[s]))
                {
                    _overrides.RemoveAt(i);
                    break;
                }
            }
        }
    }

    public void PreWarm()
    {
        if (!this.transform.GetChild(0).TryGetComponent(out SkeletonAnimation skeletonAnimation))
        {
            Debug.LogError("SkeletonAnimation not found on child 0");
            return;
        }

        skeleton = skeletonAnimation;
        thisEntity = this.GetComponent<Entity>();
        _sm = thisEntity.StateMachine;
        _animationResources = thisEntity.EntityData.AnimationResources;
        if (_animationResources == null)
        {
            Debug.LogError($"AnimationResources is not configured for entity '{thisEntity.EntityData.ID}'.", this);
            return;
        }

        _baseAnimations = AnimationSet.From(_animationResources);
        _activeAnimations = ResolveAnimations();
        event_attack = skeleton.Skeleton.Data.FindEvent("OnAttack");
        event_start = skeleton.Skeleton.Data.FindEvent("OnStart");
        skeleton.AnimationState.Event += HandleAnimationStateEvent;
        skeleton.AnimationState.Complete += HandleAnimationStateComplete;
        skeleton.AnimationState.Data.DefaultMix = 0.1f;
        RegisterMixes(_baseAnimations);
        _sm.StateChanged += OnStateChanged;
        _sm.AttackStarted += OnAttackStarted;
        _sm.AttackPhaseChanged += OnAttackPhaseChanged;
    }

    public void Initialize()
    {
        // 仅复位分支：Initialize 逆序晚于 Entity.Initialize 的 Start 播放，
        // 此处若清播完追踪字段会抹掉刚记录的 _startAnim（追踪字段在 OnStateChanged(Default) 清）
        _moveBranch = MoveAnimationBranch.Normal;
        _attackBranch = AttackAnimationBranch.Normal;
    }

    public void Dormancy()
    {
        OnAttackAnimationBegin = null;
        _overrides.Clear();
        Initialize();
    }

    private AnimationSet ResolveAnimations()
    {
        AnimationSet resolved = _baseAnimations.Copy();
        _overrides.Sort((left, right) =>
        {
            int priority = left.Priority.CompareTo(right.Priority);
            return priority != 0 ? priority : left.Id.CompareTo(right.Id);
        });
        foreach (OverrideEntry entry in _overrides)
        {
            resolved.Apply(entry.Animations, _animationResources);
        }
        return resolved;
    }

    // ===== 状态机事件 → 播放 =====

    private void OnStateChanged(EntityState previous, EntityState next)
    {
        _activeAnimations = ResolveAnimations();
        switch (next)
        {
            case EntityState.Default:
                // 回池复位态：清攻击编排与播完追踪字段（Initialize 里不清，见其注释）
                _attackGroup = null;
                _currentAttackBegin = null;
                _currentAttackEnd = null;
                _activeAttackAnim = null;
                _endAnim = null;
                _dieAnim = null;
                _startAnim = null;
                _castAnim = null;
                SetSpineAnimation(_activeAnimations.GetSingle(AnimationSlot.Default), false, 1);
                ConsumeOneShots(AnimationSlot.Default);
                break;
            case EntityState.Idle:
                SetSpineAnimation(_activeAnimations.GetSingle(AnimationSlot.Idle), true, 1);
                ConsumeOneShots(AnimationSlot.Idle);
                break;
            case EntityState.Move:
                AnimationSlot moveSlot = AnimationSet.MoveBranchSlot(_moveBranch);
                SetSpineAnimation(_activeAnimations.GetSingle(moveSlot), true, 1);
                ConsumeOneShots(moveSlot);
                break;
            case EntityState.Start:
                _startAnim = PlaySingle(AnimationSlot.Start, false, 1);
                ConsumeOneShots(AnimationSlot.Start);
                break;
            case EntityState.Cast:
                // 演出态：不循环播放，播完经 HandleAnimationStateComplete 上报回 Idle
                _castAnim = PlaySingle(AnimationSlot.Cast, false, 1);
                ConsumeOneShots(AnimationSlot.Cast);
                break;
            case EntityState.Die:
                _dieAnim = PlaySingle(AnimationSlot.Die, false, 1);
                ConsumeOneShots(AnimationSlot.Die);
                break;
            case EntityState.Attack:
                break; // 攻击编排由 OnAttackStarted 负责
        }
    }

    private Spine.Animation PlaySingle(AnimationSlot slot, bool loop, float timeScale)
    {
        AnimationReferenceAsset animation = _activeAnimations.GetSingle(slot);
        SetSpineAnimation(animation, loop, timeScale);
        return animation != null ? animation.Animation : null;
    }

    private void OnAttackStarted(bool continueCombo)
    {
        bool charge = _attackBranch == AttackAnimationBranch.Charge;
        AnimationSlot beginSlot, groupSlot;
        if (charge)
        {
            _currentAttackBegin = _activeAnimations.GetSingle(AnimationSlot.ChargeBegin);
            _currentAttackEnd = _activeAnimations.GetSingle(AnimationSlot.ChargeEnd);
            _attackGroup = _activeAnimations.GetGroup(AnimationSlot.Charge);
            beginSlot = AnimationSlot.ChargeBegin;
            groupSlot = AnimationSlot.Charge;
        }
        else
        {
            _currentAttackBegin = _activeAnimations.GetSingle(AnimationSlot.AttackBegin);
            _currentAttackEnd = _activeAnimations.GetSingle(AnimationSlot.AttackEnd);
            bool ranged = thisEntity.Movement.ResistList.Count == 0;
            _attackGroup = _activeAnimations.GetGroup(ranged ? AnimationSlot.AttackRemote : AnimationSlot.AttackClose);
            beginSlot = AnimationSlot.AttackBegin;
            groupSlot = ranged ? AnimationSlot.AttackRemote : AnimationSlot.AttackClose;
        }
        int length = _attackGroup.Length;
        AnimationReferenceAsset attack = (_sm.AttackComboIndex < length)
            ? _attackGroup[_sm.AttackComboIndex]
            : _attackGroup[length - 1];
        float scale = attack.Animation.Duration / thisEntity.Stats.BaseAttackTimeS;
        _endAnim = null;
        if (_currentAttackBegin == null && length == 1)
        {
            _activeAttackAnim = attack.Animation;
            SetSpineAnimation(attack, false, scale > 1 ? scale : 1);
            ConsumeOneShots(groupSlot);
        }
        else if (continueCombo || _currentAttackBegin == null)
        {
            _activeAttackAnim = attack.Animation;
            SetSpineAnimation(attack, false, scale);
            ConsumeOneShots(groupSlot);
        }
        else
        {
            float scaleB = _currentAttackBegin.Animation.Duration / thisEntity.Stats.BaseAttackTimeS;
            SetSpineAnimation(_currentAttackBegin, false, scaleB > 1 ? scaleB : 1);
            AddSpineAnimation(attack, false, scale, 0);
            _activeAttackAnim = attack.Animation;
            ConsumeOneShots(beginSlot, groupSlot);
        }
        // Fire after tracks are queued (旧实现在轨道排定后触发).
        OnAttackAnimationBegin?.Invoke();
    }

    private void OnAttackPhaseChanged(AttackPhase phase)
    {
        if (phase != AttackPhase.End) return;
        if (_currentAttackEnd != null)
        {
            _endAnim = _currentAttackEnd.Animation;
            skeleton.state.SetAnimation(0, _currentAttackEnd, false);
            ConsumeOneShots(_attackBranch == AttackAnimationBranch.Charge
                ? AnimationSlot.ChargeEnd
                : AnimationSlot.AttackEnd);
        }
        else
        {
            // 无后摇资产：攻击就此收尾，直接请求回 Idle
            _sm.NotifyAttackEndCompleted();
        }
    }

    // ===== Spine 时机 → 状态机上报 =====

    private void HandleAnimationStateEvent(Spine.TrackEntry trackEntry, Spine.Event e)
    {
        if (e.Data == event_attack)
        {
            _sm.NotifyAttackFrame();
        }
        else if (e.Data == event_start)
        {
            // 已注册但不消费（见字段注释）
        }
        else
        {
            Debug.LogWarning($"Unregistered animation event: {e.Data.Name}");
        }
    }

    private void HandleAnimationStateComplete(Spine.TrackEntry trackEntry)
    {
        // 以逻辑状态/相位为路由判据（与旧实现同防线）：跨槽位复用同一资产时
        // 仅凭 Animation 对象身份无法区分是哪一段播完（如攻击中死亡且 Die 槽
        // 复用攻击组资产——无状态守卫会把 Die 播完误路由给攻击分支，实体永不回池）。
        if (_sm.CurrentState == EntityState.Attack)
        {
            if (_activeAttackAnim != null && trackEntry.Animation == _activeAttackAnim
                && _sm.CurrentAttackPhase == AttackPhase.Active)
            {
                // 与旧实现同判据：有后摇或多段组才开连击窗口；单发无后摇直接收尾
                if (_currentAttackEnd != null || _attackGroup.Length > 1)
                {
                    _sm.NotifyAttackActiveCompleted(_attackGroup.Length);
                }
                else
                {
                    _sm.NotifyAttackEndCompleted();
                }
                return;
            }
            if (_endAnim != null && trackEntry.Animation == _endAnim
                && _sm.CurrentAttackPhase == AttackPhase.End)
            {
                _sm.NotifyAttackEndCompleted();
                return;
            }
        }
        if (_sm.CurrentState == EntityState.Die && _dieAnim != null && trackEntry.Animation == _dieAnim)
        {
            _sm.NotifyDieAnimationCompleted();
            return;
        }
        if (_sm.CurrentState == EntityState.Start && _startAnim != null && trackEntry.Animation == _startAnim)
        {
            _sm.NotifyStartAnimationCompleted();
            return;
        }
        if (_sm.CurrentState == EntityState.Cast && _castAnim != null && trackEntry.Animation == _castAnim)
        {
            _sm.NotifyCastAnimationCompleted();
        }
    }

    // ===== Spine 基础操作 =====

    private void AddSpineAnimation(AnimationReferenceAsset animation, bool loop, float timeScale, float delay)
    {
        skeleton.state.AddAnimation(0, animation, loop, delay).TimeScale = timeScale;
    }

    private void SetSpineAnimation(AnimationReferenceAsset animation, bool loop, float timeScale)
    {
        skeleton.state.SetAnimation(0, animation, loop).TimeScale = timeScale;
    }

    // 从任何动画切入 Start（部署）一律零混合（旧 SetMixToStart 语义）。
    private void RegisterMixes(AnimationSet set)
    {
        if (set == null || skeleton == null || _baseAnimations == null) return;
        AnimationReferenceAsset start = _baseAnimations.GetSingle(AnimationSlot.Start);
        if (start == null) return;
        foreach (AnimationReferenceAsset animation in set.EnumerateSingles())
        {
            SetMixToStart(animation);
        }
        foreach (AnimationReferenceAsset[] group in set.EnumerateGroups())
        {
            for (int i = 0; i < group.Length; i++)
            {
                SetMixToStart(group[i]);
            }
        }
    }

    private void RegisterMixes(AnimationOverride animations)
    {
        if (animations == null || skeleton == null || _animationResources == null) return;
        var resolved = new AnimationSet();
        resolved.Apply(animations, _animationResources);
        RegisterMixes(resolved);
    }

    private void SetMixToStart(AnimationReferenceAsset animation)
    {
        if (animation != null && _baseAnimations.GetSingle(AnimationSlot.Start) != null)
        {
            skeleton.AnimationState.Data.SetMix(animation, _baseAnimations.GetSingle(AnimationSlot.Start), 0);
        }
    }

    private sealed class OverrideEntry
    {
        public readonly int Id;
        public readonly object Owner;
        public readonly AnimationOverride Animations;
        public readonly int Priority;
        // Slots a one-shot entry covers (null = persistent entry). The entry is
        // consumed whole the first time any covered slot is played.
        public readonly HashSet<AnimationSlot> CoveredSlots;

        public OverrideEntry(int id, object owner, AnimationOverride animations, int priority,
            HashSet<AnimationSlot> coveredSlots = null)
        {
            Id = id;
            Owner = owner;
            Animations = animations;
            Priority = priority;
            CoveredSlots = coveredSlots;
        }
    }
}
