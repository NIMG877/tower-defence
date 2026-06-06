using System.Collections.Generic;
using UnityEngine;
using Spine.Unity;
using Spine;
using DG.Tweening;
using System;

public class AnimationMachine : MonoBehaviour, IPoolOperation
{
    private enum EntityState
    {
        Default,
        Idle,
        Move,
        Attack_Wait,
        Attack,
        Start,
        Die,
    }
    private EntityState currentState;
    private EntityState targetState;
    private List<EntityState> states_ban;
    private SkeletonAnimation skeleton;
    private Entity thisEntity;
    private EventData event_attack;
    private EventData event_start;
    private (bool left, bool up) _direction;
    private Action _attackAction;
    [SerializeField] private AnimationReferenceAsset o_default, o_idle, o_move, o_attack_begin, o_attack_end, o_start, o_die;
    [SerializeField] private AnimationReferenceAsset[] o_attack_remote, o_attack_close;
    [HideInInspector] public AnimationReferenceAsset Default, Idle, Move, Attack_Begin, Attack_End, Start, Die;
    [HideInInspector] public AnimationReferenceAsset[] Attack_Remote, Attack_Close;
    private int _attackAnimationIndex;
    private float _attackStaticWaitTime;
    private AnimationReferenceAsset[] Attack;
    public delegate void OperationsOnAttackAnimationBegin();
    /// <summary>
    /// 在攻击动画刚开始时被调用，可用于基于攻击动画开始的检测，如技能开启时更换动画
    /// </summary>
    public event OperationsOnAttackAnimationBegin OnAttackAnimationBegin;
    public int CurrentState
    {
        get
        {
            return currentState switch
            {
                EntityState.Default => 0,
                EntityState.Idle => 1,
                EntityState.Move => 2,
                EntityState.Attack => 3,
                EntityState.Attack_Wait => 3,
                EntityState.Start => 4,
                EntityState.Die => 5,
                _ => 0,
            };
        }
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
                Debug.Log($"{thisEntity}结束攻击静滞等待阶段");
                _attackAnimationIndex = 0;
                _attackStaticWaitTime = -100;
                if (Attack_End)
                {
                    skeleton.state.SetAnimation(0, Attack_End, false);
                    //此处破例单独调用SetAnimation，为了能够在FixedUpdate中检测攻击等待事件的归零后设置动画
                }
                else
                {
                    skeleton.state.SetAnimation(0, Idle, false);
                    //此处破例单独调用SetAnimation，为了能够在FixedUpdate中检测攻击等待事件的归零后设置动画
                }
            }
        }

    }
    /// <summary>
    /// 设置颜色
    /// </summary>
    /// <param name="type">设置种类：0-显示，1-消失，2-闪红</param>
    private void SetColor(int type, float duration)
    {
        switch (type)
        {
            case 0:
                DOTween.To((value) =>
                {
                    skeleton.skeleton.SetColor(new Color(value, value, value, Math.Min(value * 2, 1)));
                }, 0, 1, duration);
                break;
            case 1:
                DOTween.To((value) =>
                {
                    skeleton.skeleton.SetColor(new Color(value, value, value, Math.Min(value * 2, 1)));
                }, 1, 0, duration).OnComplete(() =>
                {
                    thisEntity.thisEntityPool.Return(thisEntity);
                });
                break;
            case 2:
                float currentColor_g = skeleton.skeleton.GetColor().g;
                DOTween.To((value) =>
                {
                    if (value < 1)
                    {
                        value = Math.Max(-value + currentColor_g, 0);
                    }
                    else
                    {
                        value = value - 1;
                    }
                    skeleton.skeleton.SetColor(new Color(1, value, value));
                }, 0, 2, duration);
                break;
        }
    }
    /// <summary>
    /// 重置动画【0-default,1-idle,2-move,30-attack_remote,31-attack_close,32-attack_begin,33-attack_end,4-start,5-die】
    /// </summary>
    /// <param name="resets">需重置的动画序号</param>
    public void ResetAnimation(int[] resets)
    {
        foreach (int index in resets)
        {
            switch (index)
            {
                case 0: Default = o_default; break;
                case 1: Idle = o_idle; break;
                case 2: Move = o_move; break;
                case 30: Attack_Remote = o_attack_remote; break;
                case 31: Attack_Close = o_attack_close; break;
                case 32: Attack_Begin = o_attack_begin; break;
                case 33: Attack_End = o_attack_end; break;
                case 4: Start = o_start; break;
                case 5: Die = o_die; break;
                default: Debug.LogWarning($"暂无标记为{index}的动画"); break;
            }
        }
    }
    /// <summary>
    /// 将状态设为禁用
    /// </summary>
    /// <param name="statesToBan">0-Default 1-Idle 2-Move 3-Attack</param>
    public void AddStateToBan(int[] statesToBan)
    {
        for (int i = 0; i < statesToBan.Length; i++)
        {
            switch (statesToBan[i])
            {
                case 0: states_ban.Add(EntityState.Default); break;
                case 1: states_ban.Add(EntityState.Idle); break;
                case 2: states_ban.Add(EntityState.Move); break;
                case 3: states_ban.Add(EntityState.Attack); break;
                default: Debug.LogWarning($"暂无标记为{statesToBan[i]}的状态或无法将标记为{statesToBan[i]}的状态设为禁用"); break;
            }
        }
    }
    /// <summary>
    /// 解除一层状态禁用
    /// </summary>
    /// <param name="statesfromBan">0-Default 1-Idle 2-Move 3-Attack</param>
    public void RemoveStateFromBan(int[] statesfromBan)
    {
        foreach (int index in statesfromBan)
        {
            switch (index)
            {
                case 0: states_ban.Remove(EntityState.Default); break;
                case 1: states_ban.Remove(EntityState.Idle); break;
                case 2: states_ban.Remove(EntityState.Move); break;
                case 3: states_ban.Remove(EntityState.Attack); break;
                default: Debug.LogWarning($"暂无标记为{index}的状态或无法将标记为{index}的状态设为禁用"); break;
            }
        }
    }
    /// <summary>
    /// 尝试转换动画状态机【0-default,1-idle,2-move,3-attack_wait,4-attack,5-start,6-die】
    /// </summary>
    /// <param name="stateIndex">尝试转换的状态编码</param>
    /// <param name="forceChange">是否强制转换</param>
    /// <returns>返回是否转换成功</returns>
    public bool TrySetState(int stateIndex, bool forceChange)
    {
        EntityState tryset = (EntityState)stateIndex;
        if (!forceChange && tryset > currentState && !states_ban.Contains(tryset))
        {
            SetState(tryset);
            return true;
        }
        else if (forceChange && currentState != EntityState.Die && !states_ban.Contains(tryset))
        {
            SetState(tryset);
            return true;
        }
        else
        {
            return false;
        }
    }
    public bool TrySetAttackState(bool forceChange, Action attackAction)
    {
        
        if (!forceChange && EntityState.Attack > currentState && !states_ban.Contains(EntityState.Attack))
        {
            SetState(EntityState.Attack);
            _attackAction = attackAction;
            return true;
        }
        else if (forceChange && currentState != EntityState.Die && !states_ban.Contains(EntityState.Attack))
        {
            SetState(EntityState.Attack);
            _attackAction = attackAction;
            return true;
        }
        else
        {
            return false;
        }
    }
    /// <summary>
    /// 设置实体朝向
    /// </summary>
    /// <param name="target">目标朝向点</param>
    public void SetDirection(Vector2 target)
    {
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
        float dx, dy;
        dx = target.x - transform.position.x;
        dy = target.y - transform.position.y;
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
        SetColor(1, 0.2f);
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
        float scale;
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
                if (thisEntity.entityResistList.Count == 0)
                {
                    Attack = Attack_Remote;
                }
                else
                {
                    Attack = Attack_Close;
                }
                AnimationReferenceAsset attack;
                int length = Attack.Length;
                if (_attackAnimationIndex < length)
                {
                    attack = Attack[_attackAnimationIndex];
                }
                else
                {
                    attack = Attack[length - 1];
                }
                scale = attack.Animation.Duration / thisEntity.AttackBase.BaseAttackTimeS;
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
            Debug.LogWarning($"暂未定义名为{e.Data.Name}的动画事件");
        }
    }
    private void HandleAnimationStateStart(Spine.TrackEntry trackEntry)
    {
        if (Default && trackEntry.Animation == Default.Animation) { currentState = EntityState.Default; }
        else if (Idle && trackEntry.Animation == Idle.Animation) currentState = EntityState.Idle;
        else if (Move && trackEntry.Animation == Move.Animation) currentState = EntityState.Move;
        else if (Attack != null && ((_attackAnimationIndex < Attack.Length && trackEntry.Animation == Attack[_attackAnimationIndex].Animation) || (_attackAnimationIndex >= Attack.Length && trackEntry.Animation == Attack[Attack.Length - 1].Animation)))
        {
            currentState = EntityState.Attack;
            if (Attack_End == null && Attack.Length == 1)
            {
                AddSpineAnimation(Idle, true, 1, 0);
            }
        }
        else if (Attack_End && trackEntry.Animation == Attack_End.Animation)
        {
            AddSpineAnimation(Idle, true, 1, 0);
        }
        else if (Start && trackEntry.Animation == Start.Animation) currentState = EntityState.Start;
        else if (Die && trackEntry.Animation == Die.Animation) { currentState = EntityState.Die; SetColor(1, Die.Animation.Duration); }
    }
    private void HandleAnimationStateComplete(Spine.TrackEntry trackEntry)
    {
        if ((Attack_End || (Attack != null && Attack.Length > 1)) && currentState == EntityState.Attack && trackEntry.Animation == Attack[_attackAnimationIndex].Animation)
        {
            currentState = EntityState.Attack_Wait;
            _attackStaticWaitTime = 0.05f;
            //Debug.Log($"{thisEntity}进入{_attackStaticWaitTime}s的攻击静滞等待阶段");
            if (Attack.Length > 1)
            {
                _attackAnimationIndex = (_attackAnimationIndex + 1) % Attack.Length;
            }
            return;
        }
    }


    public void PreWarm()
    {
        if (this.transform.GetChild(0).TryGetComponent(out SkeletonAnimation skeletonAnimation))
        {
            skeleton = skeletonAnimation;
            states_ban = new List<EntityState>();
            _direction = (false, false);
            thisEntity = this.GetComponent<Entity>();
            ResetAnimation(new int[9] { 0, 1, 2, 30, 31, 32, 33, 4, 5 });
            event_attack = skeleton.Skeleton.Data.FindEvent("OnAttack");
            event_start = skeleton.Skeleton.Data.FindEvent("OnStart");
            skeleton.AnimationState.Event += HandleAnimationStateEvent;
            skeleton.AnimationState.Start += HandleAnimationStateStart;
            skeleton.AnimationState.Complete += HandleAnimationStateComplete;
            skeleton.AnimationState.Data.DefaultMix = 0.1f;
            if (Default != null)
            {
                skeleton.AnimationState.Data.SetMix(Default, Start, 0);
            }
            if (Idle != null)
            {
                skeleton.AnimationState.Data.SetMix(Idle, Start, 0);
            }
            if (Move != null)
            {
                skeleton.AnimationState.Data.SetMix(Move, Start, 0);
            }
            if (Attack_Begin != null)
            {
                skeleton.AnimationState.Data.SetMix(Attack_Begin, Start, 0);
            }
            if (Attack_End != null)
            {
                skeleton.AnimationState.Data.SetMix(Attack_End, Start, 0);
            }
            if (Attack_Remote != null)
            {
                for (int i = 0; i < Attack_Remote.Length; i++)
                {
                    skeleton.AnimationState.Data.SetMix(Attack_Remote[i], Start, 0);
                }
            }
            if (Attack_Close != null)
            {
                for (int i = 0; i < Attack_Close.Length; i++)
                {
                    skeleton.AnimationState.Data.SetMix(Attack_Close[i], Start, 0);
                }
            }
            if (Die != null)
            {
                skeleton.AnimationState.Data.SetMix(Die, Start, 0);
            }
            //if (Attack_End)
            //{
            //    if (Attack_Close!=null)
            //    {
            //        for(int i=;i<Attack_Close.Length)
            //        skeleton.AnimationState.Data.SetMix(Attack_Close, Attack_End, 0);
            //        skeleton.AnimationState.Data.SetMix(Attack_End, Attack_Close, 0);
            //    }
            //}
        }
        else
        {
            Debug.LogError("找不到骨骼动画");
        }
    }
    public void Initialize()
    {
        SetColor(0, 0.2f);
        thisEntity.OnAfterHurt += new Entity.OperationsAfterHurt((Entity origin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly) =>
        {
            if (applyType != 2)
            {
                SetColor(2, 0.2f);
            }
        });
        _attackAnimationIndex = 0;
    }
    public void Dormancy()
    {
        OnAttackAnimationBegin = null;
        states_ban.Clear();
        ResetAnimation(new int[9] { 0, 1, 2, 30, 31, 32, 33, 4, 5 });
        SetState(EntityState.Default);
    }
}
