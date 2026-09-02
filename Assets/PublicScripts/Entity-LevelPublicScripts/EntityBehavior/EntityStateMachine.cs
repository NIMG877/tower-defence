using System;
using System.Collections.Generic;

/// <summary>攻击子相位。旧实现的 Begin（前摇段）从未被任何判定读取，
/// 现作为表现细节留在 AnimationMachine 的编排里，逻辑侧不再建模。</summary>
public enum AttackPhase
{
    None,
    Active,
    ComboWindow,
    End,
}

/// <summary>
/// 实体逻辑状态机（纯 C#，由 Entity 构造并持有，同 AttributeStore/EntityMovement 先例）。
/// 唯一的逻辑状态真相源：EntityState / AttackPhase / 连击索引 / 转换规则 / ban 表。
/// 表现层（AnimationMachine）订阅 StateChanged/AttackStarted/AttackPhaseChanged 播动画，
/// 并通过 Notify* 上报动画时机；本类不引用任何表现层类型。
/// 转换规则与旧 AnimationMachine.TrySetState 等价：非强制=目标优先级更高（Attack 连击窗口特例），
/// 强制=当前非 Die；ban 表对两个分支都生效。
/// Start 播完自动回 Idle（沿用旧 SetState(Start) 排队 Idle 的行为）；
/// Cast 是粘性演出态——播完保持末帧不自动转出，退出由技能显式 TrySetState(…, true) 负责。
/// </summary>
public sealed class EntityStateMachine
{
    private const float ComboWindowDuration = 0.05f;

    private readonly HashSet<EntityState> _banned = new HashSet<EntityState>();
    private EntityState _current = EntityState.Default;
    private AttackPhase _attackPhase = AttackPhase.None;
    private int _attackComboIndex;
    private float _comboWindowTimer;
    private Action _attackAction;

    public EntityState CurrentState { get { return _current; } }
    public AttackPhase CurrentAttackPhase { get { return _attackPhase; } }
    /// <summary>连击组内索引：主动段播完推进（循环），ComboWindow 结束归零。表现层按它选组内动画。</summary>
    public int AttackComboIndex { get { return _attackComboIndex; } }

    /// <summary>状态成功切换时触发（含同态重入：Move 换分支 / Attack 连击）。Attack 的播放由 <see cref="AttackStarted"/> 负责。</summary>
    public event Action<EntityState, EntityState> StateChanged;
    /// <summary>攻击动画开始编排时触发（StateChanged 之后）。continueCombo=连击续播（无前摇直入主动段）。</summary>
    public event Action<bool> AttackStarted;
    /// <summary>攻击相位变化。表现层只关心 End（播后摇段）。</summary>
    public event Action<AttackPhase> AttackPhaseChanged;
    /// <summary>Die 动画播完。Entity 订阅以驱动淡出与回池。</summary>
    public event Action DieAnimationCompleted;

    public static int PriorityOf(EntityState state)
    {
        return state switch
        {
            EntityState.Default => 0,
            EntityState.Idle => 1,
            EntityState.Move => 2,
            EntityState.Attack => 3,
            EntityState.Start => 4,
            EntityState.Cast => 5,
            EntityState.Die => 6,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
    }

    public bool TrySetState(EntityState state, bool forceChange)
    {
        if (!CanTransition(_current, state, forceChange)) return false;
        SetState(state);
        return true;
    }

    /// <summary>进入 Attack 并登记一次性攻击回调（动画 OnAttack 帧经 NotifyAttackFrame 触发）。</summary>
    public bool TrySetAttackState(bool forceChange, Action attackAction)
    {
        if (!TrySetState(EntityState.Attack, forceChange)) return false;
        _attackAction = attackAction;
        return true;
    }

    /// <summary>转换判定。注：连击特例读取的是机器的实时相位——from 仅在等于 CurrentState 时才有意义。</summary>
    public bool CanTransition(EntityState from, EntityState to, bool forceChange)
    {
        if (_banned.Contains(to)) return false;
        if (forceChange) return from != EntityState.Die;
        bool canContinueCombo = to == EntityState.Attack && from == EntityState.Attack
            && _attackPhase == AttackPhase.ComboWindow;
        return PriorityOf(to) > PriorityOf(from) || canContinueCombo;
    }

    public void AddStateToBan(EntityState[] statesToBan)
    {
        foreach (EntityState state in statesToBan) _banned.Add(state);
    }

    public void RemoveStateFromBan(EntityState[] statesFromBan)
    {
        foreach (EntityState state in statesFromBan) _banned.Remove(state);
    }

    /// <summary>逻辑帧驱动（Entity.FixedUpdate 调用）：ComboWindow 倒计时，到期进 End 并通知表现层播后摇。</summary>
    public void Tick(float deltaTime)
    {
        if (_attackPhase != AttackPhase.ComboWindow) return;
        if (_comboWindowTimer > 0)
        {
            _comboWindowTimer -= deltaTime;
            return;
        }
        _attackComboIndex = 0;
        _attackPhase = AttackPhase.End;
        AttackPhaseChanged?.Invoke(AttackPhase.End);
    }

    private void SetState(EntityState state)
    {
        EntityState previous = _current;
        bool wasComboWindow = _attackPhase == AttackPhase.ComboWindow;
        _attackPhase = state == EntityState.Attack ? AttackPhase.Active : AttackPhase.None;
        _current = state;
        StateChanged?.Invoke(previous, state);
        if (state == EntityState.Attack) AttackStarted?.Invoke(wasComboWindow);
    }

    /// <summary>Start 动画播完 → 回 Idle（旧 SetState(Start) 排队 Idle 的显式化）。
    /// 带守卫：状态已迁移则忽略晚到的上报。Cast 是粘性演出态，不走此通道。</summary>
    public void NotifyStartAnimationCompleted()
    {
        if (_current != EntityState.Start) return;
        _current = EntityState.Idle;
        StateChanged?.Invoke(EntityState.Start, EntityState.Idle);
    }

    /// <summary>攻击主动段播完。groupLength=当前攻击组长度（表现层解析后传入），用于推进连击索引。</summary>
    public void NotifyAttackActiveCompleted(int groupLength)
    {
        if (_current != EntityState.Attack || _attackPhase != AttackPhase.Active) return;
        _attackPhase = AttackPhase.ComboWindow;
        _comboWindowTimer = ComboWindowDuration;
        if (groupLength > 1) _attackComboIndex = (_attackComboIndex + 1) % groupLength;
    }

    /// <summary>攻击收尾（End 段播完，或单发无 End 时主动段播完）→ 回 Idle。
    /// 相位为 Active 或 End 时均合法（单发无后摇从 Active 直接收尾）。</summary>
    public void NotifyAttackEndCompleted()
    {
        if (_current != EntityState.Attack) return;
        _attackPhase = AttackPhase.None;
        _current = EntityState.Idle;
        StateChanged?.Invoke(EntityState.Attack, EntityState.Idle);
    }

    /// <summary>Spine OnAttack 事件帧：触发一次性攻击回调。</summary>
    public void NotifyAttackFrame()
    {
        _attackAction?.Invoke();
        _attackAction = null;
    }

    /// <summary>Die 动画播完（状态保持 Die，回收由订阅方决定）。</summary>
    public void NotifyDieAnimationCompleted()
    {
        if (_current != EntityState.Die) return;
        DieAnimationCompleted?.Invoke();
    }

    /// <summary>回池复位（Entity.Dormancy 调用）。触发 StateChanged(Default) 让表现层播 Default。
    /// 事件订阅（表现层/Entity 的 PreWarm 期订阅）不在此清除。</summary>
    public void ResetForPool()
    {
        _attackAction = null;
        _attackComboIndex = 0;
        _comboWindowTimer = 0f;
        _banned.Clear();
        _attackPhase = AttackPhase.None;
        EntityState previous = _current;
        _current = EntityState.Default;
        StateChanged?.Invoke(previous, EntityState.Default);
    }
}
