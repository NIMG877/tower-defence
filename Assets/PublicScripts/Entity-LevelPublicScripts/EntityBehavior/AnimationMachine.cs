using System.Collections.Generic;
using UnityEngine;
using Spine.Unity;
using Spine;
using DG.Tweening;
using System;

/// <summary>
/// 动画资源槽位。
/// 用于解析动画资源，以及在 <see cref="AnimationOverride"/> 中显式清空某个覆盖槽。
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
    Die,
    AttackRemote,
    AttackClose,
    AttackBegin,
    AttackEnd,
    ChargeBegin,
    Charge,
    ChargeEnd,
}

public sealed class AnimationOverride
{
    public string Default;
    public string Idle;
    public string Move;
    public string JumpBegin;
    public string JumpLoop;
    public string JumpEnd;
    public string Start;
    public string Die;
    public string AttackBegin;
    public string AttackEnd;
    public string AttackRemote;
    public string AttackClose;
    public string ChargeBegin;
    public string Charge;
    public string ChargeEnd;
    internal readonly HashSet<AnimationSlot> ClearedSlots = new HashSet<AnimationSlot>();

    public AnimationOverride Clear(params AnimationSlot[] slots)
    {
        foreach (AnimationSlot slot in slots)
        {
            ClearedSlots.Add(slot);
        }
        return this;
    }
}

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
        AnimationReferenceAsset animation = ResolveAnimations(null).GetSingle(slot);
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
        AnimationOverride once = null,
        MoveAnimationBranch moveBranch = MoveAnimationBranch.Normal)
    {
        bool canContinueCombo = state == EntityState.Attack &&
            currentState == EntityState.Attack &&
            _attackPhase == AttackPhase.ComboWindow;
        if (!forceChange && (state > currentState || canContinueCombo) && !states_ban.Contains(state))
        {
            SetState(state, once, moveBranch);
            return true;
        }
        else if (forceChange && currentState != EntityState.Die && !states_ban.Contains(state))
        {
            SetState(state, once, moveBranch);
            return true;
        }
        else
        {
            return false;
        }
    }

    public bool TrySetMoveState(bool forceChange, MoveAnimationBranch branch = MoveAnimationBranch.Normal, AnimationOverride once = null)
    {
        return TrySetState(EntityState.Move, forceChange, once, branch);
    }

    public bool TrySetAttackState(bool forceChange, Action attackAction, AttackAnimationBranch branch = AttackAnimationBranch.Normal, AnimationOverride once = null)
    {
        AttackAnimationBranch previousBranch = _attackBranch;
        _attackBranch = branch;
        if (!TrySetState(EntityState.Attack, forceChange, once))
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
        Debug.Log($"{thisEntity} enter attack end-wait stage");
        _attackAnimationIndex = 0;
        _attackPhase = AttackPhase.End;
        if (_currentAttackEnd)
        {
            skeleton.state.SetAnimation(0, _currentAttackEnd, false);
        }
        else
        {
            skeleton.state.SetAnimation(0, _activeAnimations.Idle, false);
        }
    }

    private void PlayAttackAnimation(bool continueCombo)
    {
        if (_attackBranch == AttackAnimationBranch.Charge)
        {
            _currentAttackBegin = _activeAnimations.ChargeBegin;
            Attack = _activeAnimations.Charge;
            _currentAttackEnd = _activeAnimations.ChargeEnd;
        }
        else
        {
            _currentAttackBegin = _activeAnimations.AttackBegin;
            Attack = (thisEntity.Movement.ResistList.Count == 0) ? _activeAnimations.AttackRemote : _activeAnimations.AttackClose;
            _currentAttackEnd = _activeAnimations.AttackEnd;
        }
        int length = Attack.Length;
        AnimationReferenceAsset attack = (_attackAnimationIndex < length)
            ? Attack[_attackAnimationIndex]
            : Attack[length - 1];
        float scale = attack.Animation.Duration / thisEntity.Stats.BaseAttackTimeS;
        if (_currentAttackBegin == null && length == 1)
        {
            SetSpineAnimation(attack, false, 1);
        }
        else if (continueCombo || _currentAttackBegin == null)
        {
            _attackPhase = AttackPhase.Active;
            SetSpineAnimation(attack, false, scale);
        }
        else
        {
            _attackPhase = AttackPhase.Begin;
            float scaleB = _currentAttackBegin.Animation.Duration / thisEntity.Stats.BaseAttackTimeS;
            SetSpineAnimation(_currentAttackBegin, false, scaleB > 1 ? scaleB : 1);
            AddSpineAnimation(attack, false, scale, 0);
        }
        // Keep this after assigning the Spine track; currentState updates on Spine Start.
        OnAttackAnimationBegin?.Invoke();
    }

    private void SetState(
        EntityState setState,
        AnimationOverride once = null,
        MoveAnimationBranch moveBranch = MoveAnimationBranch.Normal)
    {
        bool continueCombo = setState == EntityState.Attack && _attackPhase == AttackPhase.ComboWindow;
        RegisterMixes(once);
        _activeAnimations = ResolveAnimations(once);
        if (setState != EntityState.Attack)
        {
            _attackPhase = AttackPhase.None;
        }
        switch (setState)
        {
            case EntityState.Default:
                SetSpineAnimation(_activeAnimations.Default, false, 1);
                break;
            case EntityState.Idle:
                SetSpineAnimation(_activeAnimations.Idle, true, 1);
                break;
            case EntityState.Move:
                SetSpineAnimation(_activeAnimations.GetMove(moveBranch), true, 1);
                break;
            case EntityState.Attack:
                PlayAttackAnimation(continueCombo);
                break;
            case EntityState.Start:
                SetSpineAnimation(_activeAnimations.Start, false, 1);
                AddSpineAnimation(_activeAnimations.Idle, true, 1, 0);
                break;
            case EntityState.Die:
                SetSpineAnimation(_activeAnimations.Die, false, 1);
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
            Debug.Log("Start!");
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
                    AddSpineAnimation(_activeAnimations.Idle, true, 1, 0);
                }
                break;
            case AnimKind.AttackEndAnim:
                _attackPhase = AttackPhase.End;
                AddSpineAnimation(_activeAnimations.Idle, true, 1, 0);
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
        if (_activeAnimations.Default && entry.Animation == _activeAnimations.Default.Animation) return AnimKind.DefaultAnim;
        if (_activeAnimations.Idle && entry.Animation == _activeAnimations.Idle.Animation) return AnimKind.IdleAnim;
        if (_activeAnimations.IsMoveAnimation(entry.Animation)) return AnimKind.MoveAnim;
        if (Attack != null)
        {
            bool inRange = _attackAnimationIndex < Attack.Length;
            bool matchesInRange = inRange && entry.Animation == Attack[_attackAnimationIndex].Animation;
            bool matchesLast = !inRange && entry.Animation == Attack[Attack.Length - 1].Animation;
            if (matchesInRange || matchesLast) return AnimKind.AttackAnim;
        }
        if (_currentAttackEnd && entry.Animation == _currentAttackEnd.Animation) return AnimKind.AttackEndAnim;
        if (_activeAnimations.Start && entry.Animation == _activeAnimations.Start.Animation) return AnimKind.StartAnim;
        if (_activeAnimations.Die && entry.Animation == _activeAnimations.Die.Animation) return AnimKind.DieAnim;
        return AnimKind.None;
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

        public OverrideEntry(int id, object owner, AnimationOverride animations, int priority)
        {
            Id = id;
            Owner = owner;
            Animations = animations;
            Priority = priority;
        }
    }

    private sealed class AnimationSet
    {
        public AnimationReferenceAsset Default;
        public AnimationReferenceAsset Idle;
        public AnimationReferenceAsset Move;
        public AnimationReferenceAsset JumpBegin;
        public AnimationReferenceAsset JumpLoop;
        public AnimationReferenceAsset JumpEnd;
        public AnimationReferenceAsset Start;
        public AnimationReferenceAsset Die;
        public AnimationReferenceAsset AttackBegin;
        public AnimationReferenceAsset AttackEnd;
        public AnimationReferenceAsset[] AttackRemote;
        public AnimationReferenceAsset[] AttackClose;
        public AnimationReferenceAsset ChargeBegin;
        public AnimationReferenceAsset[] Charge;
        public AnimationReferenceAsset ChargeEnd;

        public static AnimationSet From(AnimationResources resources)
        {
            AnimationResources.DefaultAnimationTemplate defaults = resources.Defaults;
            AnimationResources.MovementAnimationGroup movement = resources.Movement;
            AnimationResources.AttackAnimationGroup attack = resources.Attack;
            return new AnimationSet
            {
                Default = defaults.Default,
                Idle = defaults.Idle,
                Move = movement.Move,
                JumpBegin = movement.Jump.Begin,
                JumpLoop = movement.Jump.Loop,
                JumpEnd = movement.Jump.End,
                Start = defaults.Start,
                Die = defaults.Die,
                AttackBegin = attack.AttackBegin,
                AttackEnd = attack.AttackEnd,
                AttackRemote = attack.AttackRemote,
                AttackClose = attack.AttackClose,
                ChargeBegin = attack.Charge.ChargeBegin,
                Charge = attack.Charge.Charge,
                ChargeEnd = attack.Charge.ChargeEnd,
            };
        }

        public AnimationSet Copy()
        {
            return new AnimationSet
            {
                Default = Default,
                Idle = Idle,
                Move = Move,
                JumpBegin = JumpBegin,
                JumpLoop = JumpLoop,
                JumpEnd = JumpEnd,
                Start = Start,
                Die = Die,
                AttackBegin = AttackBegin,
                AttackEnd = AttackEnd,
                AttackRemote = AttackRemote,
                AttackClose = AttackClose,
                ChargeBegin = ChargeBegin,
                Charge = Charge,
                ChargeEnd = ChargeEnd,
            };
        }

        public AnimationReferenceAsset GetSingle(AnimationSlot slot)
        {
            switch (slot)
            {
                case AnimationSlot.Default: return Default;
                case AnimationSlot.Idle: return Idle;
                case AnimationSlot.Move: return Move;
                case AnimationSlot.JumpBegin: return JumpBegin;
                case AnimationSlot.JumpLoop: return JumpLoop;
                case AnimationSlot.JumpEnd: return JumpEnd;
                case AnimationSlot.Start: return Start;
                case AnimationSlot.Die: return Die;
                case AnimationSlot.AttackBegin: return AttackBegin;
                case AnimationSlot.AttackEnd: return AttackEnd;
                case AnimationSlot.ChargeBegin: return ChargeBegin;
                case AnimationSlot.ChargeEnd: return ChargeEnd;
                default: return null;
            }
        }

        public AnimationReferenceAsset[] GetGroup(AnimationSlot slot)
        {
            switch (slot)
            {
                case AnimationSlot.AttackRemote: return AttackRemote;
                case AnimationSlot.AttackClose: return AttackClose;
                case AnimationSlot.Charge: return Charge;
                default: return null;
            }
        }

        public AnimationReferenceAsset GetMove(MoveAnimationBranch branch)
        {
            switch (branch)
            {
                case MoveAnimationBranch.JumpBegin: return JumpBegin;
                case MoveAnimationBranch.JumpLoop: return JumpLoop;
                case MoveAnimationBranch.JumpEnd: return JumpEnd;
                default: return Move;
            }
        }

        public bool IsMoveAnimation(Spine.Animation animation)
        {
            return Matches(Move, animation) ||
                Matches(JumpBegin, animation) ||
                Matches(JumpLoop, animation) ||
                Matches(JumpEnd, animation);
        }

        private static bool Matches(AnimationReferenceAsset reference, Spine.Animation animation)
        {
            return reference != null && reference.Animation == animation;
        }

        public void Apply(AnimationOverride animations, AnimationResources resources)
        {
            if (animations == null) return;
            foreach (AnimationSlot slot in animations.ClearedSlots)
            {
                Clear(slot);
            }
            ApplySingle(animations.Default, resources, value => Default = value);
            ApplySingle(animations.Idle, resources, value => Idle = value);
            ApplySingle(animations.Move, resources, value => Move = value);
            ApplySingle(animations.JumpBegin, resources, value => JumpBegin = value);
            ApplySingle(animations.JumpLoop, resources, value => JumpLoop = value);
            ApplySingle(animations.JumpEnd, resources, value => JumpEnd = value);
            ApplySingle(animations.Start, resources, value => Start = value);
            ApplySingle(animations.Die, resources, value => Die = value);
            ApplySingle(animations.AttackBegin, resources, value => AttackBegin = value);
            ApplySingle(animations.AttackEnd, resources, value => AttackEnd = value);
            ApplyGroup(animations.AttackRemote, resources, value => AttackRemote = value);
            ApplyGroup(animations.AttackClose, resources, value => AttackClose = value);
            ApplySingle(animations.ChargeBegin, resources, value => ChargeBegin = value);
            ApplyGroup(animations.Charge, resources, value => Charge = value);
            ApplySingle(animations.ChargeEnd, resources, value => ChargeEnd = value);
        }

        private static void ApplySingle(
            string resourceName,
            AnimationResources resources,
            Action<AnimationReferenceAsset> apply)
        {
            if (!string.IsNullOrEmpty(resourceName))
            {
                apply(resources.GetAnimation(resourceName));
            }
        }

        private static void ApplyGroup(
            string resourceName,
            AnimationResources resources,
            Action<AnimationReferenceAsset[]> apply)
        {
            if (!string.IsNullOrEmpty(resourceName))
            {
                apply(resources.GetAnimationGroup(resourceName));
            }
        }

        private void Clear(AnimationSlot slot)
        {
            switch (slot)
            {
                case AnimationSlot.Default: Default = null; break;
                case AnimationSlot.Idle: Idle = null; break;
                case AnimationSlot.Move: Move = null; break;
                case AnimationSlot.JumpBegin: JumpBegin = null; break;
                case AnimationSlot.JumpLoop: JumpLoop = null; break;
                case AnimationSlot.JumpEnd: JumpEnd = null; break;
                case AnimationSlot.Start: Start = null; break;
                case AnimationSlot.Die: Die = null; break;
                case AnimationSlot.AttackBegin: AttackBegin = null; break;
                case AnimationSlot.AttackEnd: AttackEnd = null; break;
                case AnimationSlot.AttackRemote: AttackRemote = null; break;
                case AnimationSlot.AttackClose: AttackClose = null; break;
                case AnimationSlot.ChargeBegin: ChargeBegin = null; break;
                case AnimationSlot.Charge: Charge = null; break;
                case AnimationSlot.ChargeEnd: ChargeEnd = null; break;
            }
        }
    }

    private AnimationSet ResolveAnimations(AnimationOverride once)
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
        resolved.Apply(once, _animationResources);
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
        if (_activeAnimations.Die != null && currentState == EntityState.Die && trackEntry.Animation == _activeAnimations.Die.Animation)
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
        _activeAnimations = ResolveAnimations(null);
        event_attack = skeleton.Skeleton.Data.FindEvent("OnAttack");
        event_start = skeleton.Skeleton.Data.FindEvent("OnStart");
        skeleton.AnimationState.Event += HandleAnimationStateEvent;
        skeleton.AnimationState.Start += HandleAnimationStateStart;
        skeleton.AnimationState.Complete += HandleAnimationStateComplete;
        skeleton.AnimationState.Data.DefaultMix = 0.1f;
        SetMixToStart(_baseAnimations.Default);
        SetMixToStart(_baseAnimations.Idle);
        SetMixToStart(_baseAnimations.Move);
        SetMixToStart(_baseAnimations.JumpBegin);
        SetMixToStart(_baseAnimations.JumpLoop);
        SetMixToStart(_baseAnimations.JumpEnd);
        SetMixToStart(_baseAnimations.AttackBegin);
        SetMixToStart(_baseAnimations.AttackEnd);
        SetMixToStartAll(_baseAnimations.AttackRemote);
        SetMixToStartAll(_baseAnimations.AttackClose);
        SetMixToStart(_baseAnimations.ChargeBegin);
        SetMixToStartAll(_baseAnimations.Charge);
        SetMixToStart(_baseAnimations.ChargeEnd);
        SetMixToStart(_baseAnimations.Die);
    }

    // Helper: zero mix time from a single animation to Start.
    private void SetMixToStart(AnimationReferenceAsset animation)
    {
        if (animation != null && _baseAnimations.Start != null)
        {
            skeleton.AnimationState.Data.SetMix(animation, _baseAnimations.Start, 0);
        }
    }

    // Helper: zero mix time from every animation in the array to Start.
    private void SetMixToStartAll(AnimationReferenceAsset[] animations)
    {
        if (animations == null) return;
        for (int i = 0; i < animations.Length; i++)
        {
            SetMixToStart(animations[i]);
        }
    }

    private void RegisterMixes(AnimationOverride animations)
    {
        if (animations == null || skeleton == null || _animationResources == null) return;
        var resolved = new AnimationSet();
        resolved.Apply(animations, _animationResources);
        SetMixToStart(resolved.Default);
        SetMixToStart(resolved.Idle);
        SetMixToStart(resolved.Move);
        SetMixToStart(resolved.JumpBegin);
        SetMixToStart(resolved.JumpLoop);
        SetMixToStart(resolved.JumpEnd);
        SetMixToStart(resolved.AttackBegin);
        SetMixToStart(resolved.AttackEnd);
        SetMixToStartAll(resolved.AttackRemote);
        SetMixToStartAll(resolved.AttackClose);
        SetMixToStart(resolved.ChargeBegin);
        SetMixToStartAll(resolved.Charge);
        SetMixToStart(resolved.ChargeEnd);
        SetMixToStart(resolved.Die);
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
