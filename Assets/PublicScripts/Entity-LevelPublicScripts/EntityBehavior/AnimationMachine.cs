using System.Collections.Generic;
using UnityEngine;
using Spine.Unity;
using Spine;
using DG.Tweening;
using System;

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

public class AnimationMachine : MonoBehaviour, IPoolOperation
{
    private EntityState currentState;
    private HashSet<EntityState> states_ban;
    private SkeletonAnimation skeleton;
    private Entity thisEntity;
    private EventData event_attack;
    private EventData event_start;
    private (bool left, bool up) _direction;
    private Action _attackAction;
    private int _attackAnimationIndex;
    private float _attackStaticWaitTime;
    private AnimationReferenceAsset[] Attack;
    private AnimationReferenceAsset _currentAttackBegin;
    private AnimationReferenceAsset _currentAttackEnd;
    private AttackPhase _attackPhase;
    private AttackAnimationBranch _attackBranch;
    private AnimationResources _animationResources;
    private AnimationSet _baseAnimations;
    private AnimationSet _activeAnimations;
    private readonly List<OverrideEntry> _overrides = new List<OverrideEntry>();
    private int _nextOverrideId;

    public delegate void OperationsOnAttackAnimationBegin();

    /// <summary>
    /// Fired at the very start of the attack animation.
    /// Use for windowed checks (e.g. skill parry windows) that need to begin at the
    /// first frame of the attack animation.
    /// </summary>
    public event OperationsOnAttackAnimationBegin OnAttackAnimationBegin;

    /// <summary>
    /// Current state. Returns the <see cref="EntityState"/> enum value directly —
    /// no second int remapping.
    /// Attack sub-phases are tracked privately and are not exposed as entity states.
    /// </summary>
    public EntityState CurrentState
    {
        get { return currentState; }
    }

    public (bool left, bool up) CurrentDirection { get { return _direction; } }

    private void FixedUpdate()
    {
        if (_attackPhase == AttackPhase.ComboWindow)
        {
            if (_attackStaticWaitTime > 0)
            {
                _attackStaticWaitTime -= Time.fixedDeltaTime;
            }
            else
            {
                FinishComboWindow();
            }
        }
    }

    /// <summary>
    /// Tints the entity sprite.
    /// </summary>
    /// <param name="effect">Which tint effect to play</param>
    /// <param name="duration">Tween duration in seconds</param>
    private void SetColor(ColorEffect effect, float duration)
    {
        switch (effect)
        {
            case ColorEffect.FadeIn:
                DOTween.To(FadeAlpha, 0, 1, duration);
                break;
            case ColorEffect.FadeOut:
                DOTween.To(FadeAlpha, 1, 0, duration).OnComplete(() =>
                {
                    thisEntity.thisEntityPool.Return(thisEntity);
                });
                break;
            case ColorEffect.FlashRed:
                _flashRedBaselineG = skeleton.skeleton.GetColor().g;
                DOTween.To(ApplyFlashRed, 0, 2, duration);
                break;
        }
    }

    // Snapshotted g-channel at the moment FlashRed starts; the tween body
    // reads this each frame instead of capturing a closure.
    private float _flashRedBaselineG;

    // FlashRed tween body: a 0→2 ramp mapped to a red→white→red pulse that
    // returns to the baseline green channel captured at tween start.
    private void ApplyFlashRed(float value)
    {
        if (value < 1)
        {
            value = Math.Max(-value + _flashRedBaselineG, 0);
        }
        else
        {
            value = value - 1;
        }
        skeleton.skeleton.SetColor(new Color(1, value, value));
    }

    // Applies a greyscale-with-double-alpha curve used by fade-in / fade-out.
    private void FadeAlpha(float value)
    {
        skeleton.skeleton.SetColor(new Color(value, value, value, Math.Min(value * 2, 1)));
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

    /// <summary>
    /// Adds states to the transition ban list.
    /// </summary>
    /// <param name="statesToBan">States to ban (see <see cref="EntityState"/>)</param>
    public void AddStateToBan(EntityState[] statesToBan)
    {
        foreach (EntityState state in statesToBan)
        {
            states_ban.Add(state);
        }
    }

    /// <summary>
    /// Removes states from the transition ban list.
    /// </summary>
    /// <param name="statesfromBan">States to unban</param>
    public void RemoveStateFromBan(EntityState[] statesfromBan)
    {
        foreach (var state in statesfromBan)
        {
            states_ban.Remove(state);
        }
    }

    /// <summary>
    /// Attempts to transition to the given state.
    /// State declaration order defines transition priority.
    /// </summary>
    /// <param name="state">Target state</param>
    /// <param name="forceChange">If true, ignores priority; if false, only forward transitions are allowed</param>
    /// <returns>True if the transition succeeded</returns>
    public bool TrySetState(
        EntityState state,
        bool forceChange,
        MoveAnimationBranch moveBranch = MoveAnimationBranch.Normal)
    {
        bool canContinueCombo = state == EntityState.Attack &&
            currentState == EntityState.Attack &&
            _attackPhase == AttackPhase.ComboWindow;
        if (!forceChange && (state > currentState || canContinueCombo) && !states_ban.Contains(state))
        {
            SetState(state, moveBranch);
            return true;
        }
        else if (forceChange && currentState != EntityState.Die && !states_ban.Contains(state))
        {
            SetState(state, moveBranch);
            return true;
        }
        else
        {
            return false;
        }
    }

    public bool TrySetMoveState(bool forceChange, MoveAnimationBranch branch = MoveAnimationBranch.Normal)
    {
        return TrySetState(EntityState.Move, forceChange, branch);
    }

    public bool TrySetAttackState(bool forceChange, Action attackAction, AttackAnimationBranch branch = AttackAnimationBranch.Normal)
    {
        AttackAnimationBranch previousBranch = _attackBranch;
        _attackBranch = branch;
        if (!TrySetState(EntityState.Attack, forceChange))
        {
            _attackBranch = previousBranch;
            return false;
        }
        _attackAction = attackAction;
        return true;
    }

    /// <summary>
    /// Sets the entity facing direction based on a world-space target.
    /// </summary>
    /// <param name="target">World-space target position</param>
    public void SetDirection(Vector2 target)
    {
        // Note: the trailing `return;` on the !left branch is intentional and preserved.
        // Removing it would change rotation behavior for that branch.
        void SetDirectionBase()
        {
            float ry = skeleton.transform.rotation.y;
            if (_direction.left)
            {
                if (ry != 1) skeleton.transform.Rotate(new Vector3(0, (1 - ry) * 180, 0));
            }
            else
            {
                if (ry != 0) skeleton.transform.Rotate(new Vector3(0, -ry * 180, 0)); return;
            }
        }
        float dx = target.x - transform.position.x;
        float dy = target.y - transform.position.y;
        if (dy > 0)
        {
            _direction.up = true;
        }
        else if (dy < 0)
        {
            _direction.up = false;
        }
        if (dx > 0)
        {
            _direction.left = false;
            SetDirectionBase();
        }
        else if (dx < 0)
        {
            _direction.left = true;
            SetDirectionBase();
        }
    }

    public void ArriveEnd()
    {
        SetColor(ColorEffect.FadeOut, 0.2f);
    }

    private void AddSpineAnimation(AnimationReferenceAsset animation, bool loop, float timeScale, float delay)
    {
        skeleton.state.AddAnimation(0, animation, loop, delay).TimeScale = timeScale;
    }

    private void SetSpineAnimation(AnimationReferenceAsset animation, bool loop, float timeScale)
    {
        skeleton.state.SetAnimation(0, animation, loop).TimeScale = timeScale;
    }

    private void FinishComboWindow()
    {
        _attackAnimationIndex = 0;
        _attackPhase = AttackPhase.End;
        if (_currentAttackEnd)
        {
            skeleton.state.SetAnimation(0, _currentAttackEnd, false);
            ConsumeOneShots(_attackBranch == AttackAnimationBranch.Charge
                ? AnimationSlot.ChargeEnd
                : AnimationSlot.AttackEnd);
        }
        else
        {
            skeleton.state.SetAnimation(0, _activeAnimations.GetSingle(AnimationSlot.Idle), false);
            ConsumeOneShots(AnimationSlot.Idle);
        }
    }

    private void PlayAttackAnimation(bool continueCombo)
    {
        AnimationSlot beginSlot, groupSlot;
        if (_attackBranch == AttackAnimationBranch.Charge)
        {
            _currentAttackBegin = _activeAnimations.GetSingle(AnimationSlot.ChargeBegin);
            Attack = _activeAnimations.GetGroup(AnimationSlot.Charge);
            _currentAttackEnd = _activeAnimations.GetSingle(AnimationSlot.ChargeEnd);
            beginSlot = AnimationSlot.ChargeBegin;
            groupSlot = AnimationSlot.Charge;
        }
        else
        {
            _currentAttackBegin = _activeAnimations.GetSingle(AnimationSlot.AttackBegin);
            Attack = (thisEntity.Movement.ResistList.Count == 0) ? _activeAnimations.GetGroup(AnimationSlot.AttackRemote) : _activeAnimations.GetGroup(AnimationSlot.AttackClose);
            _currentAttackEnd = _activeAnimations.GetSingle(AnimationSlot.AttackEnd);
            beginSlot = AnimationSlot.AttackBegin;
            groupSlot = (thisEntity.Movement.ResistList.Count == 0) ? AnimationSlot.AttackRemote : AnimationSlot.AttackClose;
        }
        int length = Attack.Length;
        AnimationReferenceAsset attack = (_attackAnimationIndex < length)
            ? Attack[_attackAnimationIndex]
            : Attack[length - 1];
        float scale = attack.Animation.Duration / thisEntity.Stats.BaseAttackTimeS;
        if (_currentAttackBegin == null && length == 1)
        {
            SetSpineAnimation(attack, false, scale > 1 ? scale : 1);
            ConsumeOneShots(groupSlot);
        }
        else if (continueCombo || _currentAttackBegin == null)
        {
            _attackPhase = AttackPhase.Active;
            SetSpineAnimation(attack, false, scale);
            ConsumeOneShots(groupSlot);
        }
        else
        {
            _attackPhase = AttackPhase.Begin;
            float scaleB = _currentAttackBegin.Animation.Duration / thisEntity.Stats.BaseAttackTimeS;
            SetSpineAnimation(_currentAttackBegin, false, scaleB > 1 ? scaleB : 1);
            AddSpineAnimation(attack, false, scale, 0);
            ConsumeOneShots(beginSlot, groupSlot);
        }
        // Keep this after assigning the Spine track; currentState updates on Spine Start.
        OnAttackAnimationBegin?.Invoke();
    }

    private void SetState(
        EntityState setState,
        MoveAnimationBranch moveBranch = MoveAnimationBranch.Normal)
    {
        bool continueCombo = setState == EntityState.Attack && _attackPhase == AttackPhase.ComboWindow;
        _activeAnimations = ResolveAnimations();
        if (setState != EntityState.Attack)
        {
            _attackPhase = AttackPhase.None;
        }
        switch (setState)
        {
            case EntityState.Default:
                SetSpineAnimation(_activeAnimations.GetSingle(AnimationSlot.Default), false, 1);
                ConsumeOneShots(AnimationSlot.Default);
                break;
            case EntityState.Idle:
                SetSpineAnimation(_activeAnimations.GetSingle(AnimationSlot.Idle), true, 1);
                ConsumeOneShots(AnimationSlot.Idle);
                break;
            case EntityState.Move:
                SetSpineAnimation(_activeAnimations.GetSingle(AnimationSet.MoveBranchSlot(moveBranch)), true, 1);
                ConsumeOneShots(AnimationSet.MoveBranchSlot(moveBranch));
                break;
            case EntityState.Attack:
                PlayAttackAnimation(continueCombo);
                break;
            case EntityState.Start:
                SetSpineAnimation(_activeAnimations.GetSingle(AnimationSlot.Start), false, 1);
                AddSpineAnimation(_activeAnimations.GetSingle(AnimationSlot.Idle), true, 1, 0);
                ConsumeOneShots(AnimationSlot.Start, AnimationSlot.Idle);
                break;
            case EntityState.Die:
                SetSpineAnimation(_activeAnimations.GetSingle(AnimationSlot.Die), false, 1);
                ConsumeOneShots(AnimationSlot.Die);
                break;
            default: break;
        }
    }

    private void HandleAnimationStateEvent(Spine.TrackEntry trackEntry, Spine.Event e)
    {
        if (e.Data == event_attack)
        {
            _attackAction?.Invoke();
            _attackAction = null;
        }
        else if (e.Data == event_start)
        {
        }
        else
        {
            Debug.LogWarning($"Unregistered animation event: {e.Data.Name}");
        }
    }

    private void HandleAnimationStateStart(Spine.TrackEntry trackEntry)
    {
        AnimKind kind = ClassifyAnimation(trackEntry);
        switch (kind)
        {
            case AnimKind.DefaultAnim:
                currentState = EntityState.Default;
                break;
            case AnimKind.IdleAnim:
                currentState = EntityState.Idle;
                _attackPhase = AttackPhase.None;
                break;
            case AnimKind.MoveAnim:
                currentState = EntityState.Move;
                break;
            case AnimKind.AttackAnim:
                currentState = EntityState.Attack;
                _attackPhase = AttackPhase.Active;
                if (_currentAttackEnd == null && Attack.Length == 1)
                {
                    AddSpineAnimation(_activeAnimations.GetSingle(AnimationSlot.Idle), true, 1, 0);
                    ConsumeOneShots(AnimationSlot.Idle);
                }
                break;
            case AnimKind.AttackEndAnim:
                _attackPhase = AttackPhase.End;
                AddSpineAnimation(_activeAnimations.GetSingle(AnimationSlot.Idle), true, 1, 0);
                ConsumeOneShots(AnimationSlot.Idle);
                break;
            case AnimKind.StartAnim:
                currentState = EntityState.Start;
                break;
            case AnimKind.DieAnim:
                currentState = EntityState.Die;
                // FadeOut is deferred to HandleAnimationStateComplete so the
                // death animation plays at full opacity, then fades + returns
                // to pool only after Spine signals the Die track has finished.
                break;
        }
    }

    // Identifies which logical animation just started, preserving the original
    // if-else evaluation order exactly.
    private AnimKind ClassifyAnimation(Spine.TrackEntry entry)
    {
        if (_activeAnimations.GetSingle(AnimationSlot.Default) && entry.Animation == _activeAnimations.GetSingle(AnimationSlot.Default).Animation) return AnimKind.DefaultAnim;
        if (_activeAnimations.GetSingle(AnimationSlot.Idle) && entry.Animation == _activeAnimations.GetSingle(AnimationSlot.Idle).Animation) return AnimKind.IdleAnim;
        if (IsMoveAnimation(entry.Animation)) return AnimKind.MoveAnim;
        if (Attack != null)
        {
            bool inRange = _attackAnimationIndex < Attack.Length;
            bool matchesInRange = inRange && entry.Animation == Attack[_attackAnimationIndex].Animation;
            bool matchesLast = !inRange && entry.Animation == Attack[Attack.Length - 1].Animation;
            if (matchesInRange || matchesLast) return AnimKind.AttackAnim;
        }
        if (_currentAttackEnd && entry.Animation == _currentAttackEnd.Animation) return AnimKind.AttackEndAnim;
        if (_activeAnimations.GetSingle(AnimationSlot.Start) && entry.Animation == _activeAnimations.GetSingle(AnimationSlot.Start).Animation) return AnimKind.StartAnim;
        if (_activeAnimations.GetSingle(AnimationSlot.Die) && entry.Animation == _activeAnimations.GetSingle(AnimationSlot.Die).Animation) return AnimKind.DieAnim;
        return AnimKind.None;
    }

    private bool IsMoveAnimation(Spine.Animation animation)
    {
        return Matches(_activeAnimations.GetSingle(AnimationSlot.Move), animation)
            || Matches(_activeAnimations.GetSingle(AnimationSlot.JumpBegin), animation)
            || Matches(_activeAnimations.GetSingle(AnimationSlot.JumpLoop), animation)
            || Matches(_activeAnimations.GetSingle(AnimationSlot.JumpEnd), animation);
    }

    private static bool Matches(AnimationReferenceAsset reference, Spine.Animation animation)
    {
        return reference != null && reference.Animation == animation;
    }

    private enum AnimKind
    {
        None,
        DefaultAnim,
        IdleAnim,
        MoveAnim,
        AttackAnim,
        AttackEndAnim,
        StartAnim,
        DieAnim
    }

    private enum AttackPhase
    {
        None,
        Begin,
        Active,
        ComboWindow,
        End,
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

    private enum ColorEffect
    {
        FadeIn,
        FadeOut,
        FlashRed,
    }

    private void HandleAnimationStateComplete(Spine.TrackEntry trackEntry)
    {
        if ((_currentAttackEnd || (Attack != null && Attack.Length > 1)) && currentState == EntityState.Attack && _attackPhase == AttackPhase.Active && trackEntry.Animation == Attack[_attackAnimationIndex].Animation)
        {
            _attackPhase = AttackPhase.ComboWindow;
            _attackStaticWaitTime = 0.05f;
            if (Attack.Length > 1)
            {
                _attackAnimationIndex = (_attackAnimationIndex + 1) % Attack.Length;
            }
            return;
        }
        // Die animation finished → kick off the fade-out. The tween's OnComplete
        // returns the entity to the pool. currentState guard keeps Dormancy-reset
        // (which replaces the Die track with Default) from re-triggering fade-out.
        if (_activeAnimations.GetSingle(AnimationSlot.Die) != null && currentState == EntityState.Die && trackEntry.Animation == _activeAnimations.GetSingle(AnimationSlot.Die).Animation)
        {
            SetColor(ColorEffect.FadeOut, 0.2f);
            return;
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
        states_ban = new HashSet<EntityState>();
        _direction = (false, false);
        thisEntity = this.GetComponent<Entity>();
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
        skeleton.AnimationState.Start += HandleAnimationStateStart;
        skeleton.AnimationState.Complete += HandleAnimationStateComplete;
        skeleton.AnimationState.Data.DefaultMix = 0.1f;
        RegisterMixes(_baseAnimations);
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

    // Helper: zero mix time from a single animation to Start.
    private void SetMixToStart(AnimationReferenceAsset animation)
    {
        if (animation != null && _baseAnimations.GetSingle(AnimationSlot.Start) != null)
        {
            skeleton.AnimationState.Data.SetMix(animation, _baseAnimations.GetSingle(AnimationSlot.Start), 0);
        }
    }

    public void Initialize()
    {
        SetColor(ColorEffect.FadeIn, 0.2f);
        thisEntity.OnAfterHurt += HandleAfterHurt;
        _attackAnimationIndex = 0;
        _attackPhase = AttackPhase.None;
        _attackBranch = AttackAnimationBranch.Normal;
        _currentAttackBegin = null;
        _currentAttackEnd = null;
    }

    // Method group (cached, no per-checkout closure) subscribed to OnAfterHurt
    // in Initialize. Entity.Dormancy() nulls the event, so no -=/manual cleanup
    // is required here.
    private void HandleAfterHurt(Entity origin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly)
    {
        if (applyType != 2)
        {
            SetColor(ColorEffect.FlashRed, 0.2f);
        }
    }

    public void Dormancy()
    {
        OnAttackAnimationBegin = null;
        // _attackAction may still be set if Dormancy fires between
        // TrySetAttackState and the OnAttack Spine event (e.g. a death
        // that pre-empts an in-flight attack). Null it so pool reuse
        // doesn't re-fire a stale callback on the next deploy.
        _attackAction = null;
        _attackPhase = AttackPhase.None;
        states_ban.Clear();
        _overrides.Clear();
        SetState(EntityState.Default);
    }
}
