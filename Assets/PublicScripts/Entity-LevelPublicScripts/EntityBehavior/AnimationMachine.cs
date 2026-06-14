using System.Collections.Generic;
using UnityEngine;
using Spine.Unity;
using Spine;
using DG.Tweening;
using System;

/// <summary>
/// 动画资源槽位码。
/// 替代 AnimationMachine 旧版 int[] 形参（0/1/2/30/31/32/33/4/5），
/// 用于 <see cref="AnimationMachine.ResetAnimation"/> 标识需要重置的动画资源槽。
/// 注：与 <see cref="EntityState"/> 的语义不重合——本枚举标识动画资源槽位，
///     EntityState 标识逻辑动画状态。
/// </summary>
public enum AnimationSlot
{
    Default = 0,
    Idle = 1,
    Move = 2,
    Start = 4,
    Die = 5,
    AttackRemote = 30,
    AttackClose = 31,
    AttackBegin = 32,
    AttackEnd = 33,
}

public class AnimationMachine : MonoBehaviour, IPoolOperation
{
    // Animation reference assets are serialized in two stages: original (o_*) for reset
    // and runtime (no prefix) which the gameplay code is allowed to swap via ResetAnimation.
    [SerializeField] private AnimationReferenceAsset o_default, o_idle, o_move, o_attack_begin, o_attack_end, o_start, o_die;
    [SerializeField] private AnimationReferenceAsset[] o_attack_remote, o_attack_close;
    [HideInInspector] public AnimationReferenceAsset Default, Idle, Move, Attack_Begin, Attack_End, Start, Die;
    [HideInInspector] public AnimationReferenceAsset[] Attack_Remote, Attack_Close;

    private EntityState currentState;
    private EntityState targetState;
    private List<EntityState> states_ban;
    private SkeletonAnimation skeleton;
    private Entity thisEntity;
    private EventData event_attack;
    private EventData event_start;
    private (bool left, bool up) _direction;
    private Action _attackAction;
    private int _attackAnimationIndex;
    private float _attackStaticWaitTime;
    private AnimationReferenceAsset[] Attack;

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
    /// The legacy version collapsed Attack/Attack_Wait to 3 and Start to 4 / Die to 5,
    /// which did not match the enum's actual indices (5/6). That mismatch is fixed.
    /// </summary>
    public EntityState CurrentState
    {
        get { return currentState; }
    }

    public (bool left, bool up) CurrentDirection { get { return _direction; } }

    private void FixedUpdate()
    {
        if (currentState == EntityState.Attack_Wait)
        {
            if (_attackStaticWaitTime > 0)
            {
                _attackStaticWaitTime -= Time.fixedDeltaTime;
            }
            else if (_attackStaticWaitTime > -100)
            {
                Debug.Log($"{thisEntity} enter attack end-wait stage");
                _attackAnimationIndex = 0;
                _attackStaticWaitTime = -100;
                if (Attack_End)
                {
                    skeleton.state.SetAnimation(0, Attack_End, false);
                    // SetAnimation (not AddAnimation) is intentional so that FixedUpdate
                    // can still detect the attack end-wait event during the playback.
                }
                else
                {
                    skeleton.state.SetAnimation(0, Idle, false);
                    // SetAnimation (not AddAnimation) is intentional so that FixedUpdate
                    // can still detect the attack end-wait event during the playback.
                }
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

    // Shared "reset all slots" array. Avoids re-allocating a 9-element array
    // on every Dormancy (per pool return) and on every PreWarm.
    private static readonly AnimationSlot[] AllSlots = {
        AnimationSlot.Default,
        AnimationSlot.Idle,
        AnimationSlot.Move,
        AnimationSlot.AttackRemote,
        AnimationSlot.AttackClose,
        AnimationSlot.AttackBegin,
        AnimationSlot.AttackEnd,
        AnimationSlot.Start,
        AnimationSlot.Die,
    };

    /// <summary>
    /// Resets (or assigns) animation references for the given slots.
    /// </summary>
    /// <param name="resets">Slots to reset to their original (o_*) references</param>
    public void ResetAnimation(AnimationSlot[] resets)
    {
        foreach (AnimationSlot slot in resets)
        {
            switch (slot)
            {
                case AnimationSlot.Default: Default = o_default; break;
                case AnimationSlot.Idle: Idle = o_idle; break;
                case AnimationSlot.Move: Move = o_move; break;
                case AnimationSlot.AttackRemote: Attack_Remote = o_attack_remote; break;
                case AnimationSlot.AttackClose: Attack_Close = o_attack_close; break;
                case AnimationSlot.AttackBegin: Attack_Begin = o_attack_begin; break;
                case AnimationSlot.AttackEnd: Attack_End = o_attack_end; break;
                case AnimationSlot.Start: Start = o_start; break;
                case AnimationSlot.Die: Die = o_die; break;
                default: Debug.LogWarning($"No animation registered for slot {slot}"); break;
            }
        }
    }

    /// <summary>
    /// Adds states to the transition ban list.
    /// </summary>
    /// <param name="statesToBan">States to ban (see <see cref="EntityState"/>)</param>
    public void AddStateToBan(EntityState[] statesToBan)
    {
        for (int i = 0; i < statesToBan.Length; i++)
        {
            if (!this.states_ban.Contains(statesToBan[i]))
            {
                this.states_ban.Add(statesToBan[i]);
            }
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
    /// State index map: 0-default, 1-idle, 2-move, 3-attack_wait, 4-attack, 5-start, 6-die.
    /// </summary>
    /// <param name="state">Target state</param>
    /// <param name="forceChange">If true, ignores priority; if false, only forward transitions are allowed</param>
    /// <returns>True if the transition succeeded</returns>
    public bool TrySetState(EntityState state, bool forceChange)
    {
        if (!forceChange && state > currentState && !states_ban.Contains(state))
        {
            SetState(state);
            return true;
        }
        else if (forceChange && currentState != EntityState.Die && !states_ban.Contains(state))
        {
            SetState(state);
            return true;
        }
        else
        {
            return false;
        }
    }

    public bool TrySetAttackState(bool forceChange, Action attackAction)
    {
        return TryTransitionToAttack(forceChange) && AttachAttackAction(attackAction);
    }

    // Shared transition gate for TrySetState / TrySetAttackState.
    private bool TryTransitionToAttack(bool forceChange)
    {
        if (!forceChange && EntityState.Attack > currentState && !states_ban.Contains(EntityState.Attack))
        {
            SetState(EntityState.Attack);
            return true;
        }
        else if (forceChange && currentState != EntityState.Die && !states_ban.Contains(EntityState.Attack))
        {
            SetState(EntityState.Attack);
            return true;
        }
        else
        {
            return false;
        }
    }

    // Stashes the per-attack callback. Kept separate so future logic can grow here
    // without disturbing the transition gate.
    private bool AttachAttackAction(Action attackAction)
    {
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

    private void SetState(EntityState setState)
    {
        void SetSpineAnimation(AnimationReferenceAsset animation, bool loop, float timeScale)
        {
            skeleton.state.SetAnimation(0, animation, loop).TimeScale = timeScale;
        }
        switch (setState)
        {
            case EntityState.Default:
                SetSpineAnimation(Default, false, 1);
                break;
            case EntityState.Idle:
                SetSpineAnimation(Idle, true, 1);
                break;
            case EntityState.Move:
                SetSpineAnimation(Move, true, 1);
                break;
            case EntityState.Attack:
                OnAttackAnimationBegin?.Invoke();
                Attack = (thisEntity.Movement.ResistList.Count == 0) ? Attack_Remote : Attack_Close;
                int length = Attack.Length;
                AnimationReferenceAsset attack = (_attackAnimationIndex < length)
                    ? Attack[_attackAnimationIndex]
                    : Attack[length - 1];
                float scale = attack.Animation.Duration / thisEntity.AttackBase.BaseAttackTimeS;
                if (Attack_Begin == null && length == 1)
                {
                    SetSpineAnimation(attack, false, 1);
                }
                else if (currentState == EntityState.Attack_Wait || Attack_Begin == null)
                {
                    SetSpineAnimation(attack, false, scale);
                }
                else
                {
                    float scaleB = Attack_Begin.Animation.Duration / thisEntity.AttackBase.BaseAttackTimeS;
                    SetSpineAnimation(Attack_Begin, false, scaleB > 1 ? scaleB : 1);
                    AddSpineAnimation(attack, false, scale, 0);
                }
                break;
            case EntityState.Start:
                SetSpineAnimation(Start, false, 1);
                AddSpineAnimation(Idle, true, 1, 0);
                break;
            case EntityState.Die:
                SetSpineAnimation(Die, false, 1);
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
                break;
            case AnimKind.MoveAnim:
                currentState = EntityState.Move;
                break;
            case AnimKind.AttackAnim:
                currentState = EntityState.Attack;
                if (Attack_End == null && Attack.Length == 1)
                {
                    AddSpineAnimation(Idle, true, 1, 0);
                }
                break;
            case AnimKind.AttackEndAnim:
                AddSpineAnimation(Idle, true, 1, 0);
                break;
            case AnimKind.StartAnim:
                currentState = EntityState.Start;
                break;
            case AnimKind.DieAnim:
                currentState = EntityState.Die;
                SetColor(ColorEffect.FadeOut, Die.Animation.Duration);
                break;
        }
    }

    // Identifies which logical animation just started, preserving the original
    // if-else evaluation order exactly.
    private AnimKind ClassifyAnimation(Spine.TrackEntry entry)
    {
        if (Default && entry.Animation == Default.Animation) return AnimKind.DefaultAnim;
        if (Idle && entry.Animation == Idle.Animation) return AnimKind.IdleAnim;
        if (Move && entry.Animation == Move.Animation) return AnimKind.MoveAnim;
        if (Attack != null)
        {
            bool inRange = _attackAnimationIndex < Attack.Length;
            bool matchesInRange = inRange && entry.Animation == Attack[_attackAnimationIndex].Animation;
            bool matchesLast = !inRange && entry.Animation == Attack[Attack.Length - 1].Animation;
            if (matchesInRange || matchesLast) return AnimKind.AttackAnim;
        }
        if (Attack_End && entry.Animation == Attack_End.Animation) return AnimKind.AttackEndAnim;
        if (Start && entry.Animation == Start.Animation) return AnimKind.StartAnim;
        if (Die && entry.Animation == Die.Animation) return AnimKind.DieAnim;
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

    private enum ColorEffect
    {
        FadeIn,
        FadeOut,
        FlashRed,
    }

    private void HandleAnimationStateComplete(Spine.TrackEntry trackEntry)
    {
        if ((Attack_End || (Attack != null && Attack.Length > 1)) && currentState == EntityState.Attack && trackEntry.Animation == Attack[_attackAnimationIndex].Animation)
        {
            currentState = EntityState.Attack_Wait;
            _attackStaticWaitTime = 0.05f;
            if (Attack.Length > 1)
            {
                _attackAnimationIndex = (_attackAnimationIndex + 1) % Attack.Length;
            }
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
        states_ban = new List<EntityState>();
        _direction = (false, false);
        thisEntity = this.GetComponent<Entity>();
        ResetAnimation(AllSlots);
        event_attack = skeleton.Skeleton.Data.FindEvent("OnAttack");
        event_start = skeleton.Skeleton.Data.FindEvent("OnStart");
        skeleton.AnimationState.Event += HandleAnimationStateEvent;
        skeleton.AnimationState.Start += HandleAnimationStateStart;
        skeleton.AnimationState.Complete += HandleAnimationStateComplete;
        skeleton.AnimationState.Data.DefaultMix = 0.1f;
        SetMixToStart(Default);
        SetMixToStart(Idle);
        SetMixToStart(Move);
        SetMixToStart(Attack_Begin);
        SetMixToStart(Attack_End);
        SetMixToStartAll(Attack_Remote);
        SetMixToStartAll(Attack_Close);
        SetMixToStart(Die);
        // The following block was commented out and is intentionally not restored:
        // it attempted to cross-mix Attack_Close <-> Attack_End but the loops were broken
        // (e.g. `for(int i=;i<Attack_Close.Length)`), so leaving it disabled is correct.
    }

    // Helper: zero mix time from a single animation to Start.
    private void SetMixToStart(AnimationReferenceAsset animation)
    {
        if (animation != null)
        {
            skeleton.AnimationState.Data.SetMix(animation, Start, 0);
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

    public void Initialize()
    {
        SetColor(ColorEffect.FadeIn, 0.2f);
        thisEntity.OnAfterHurt += HandleAfterHurt;
        _attackAnimationIndex = 0;
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
        states_ban.Clear();
        ResetAnimation(AllSlots);
        SetState(EntityState.Default);
    }
}
