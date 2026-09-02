using System.Collections.Generic;
using NUnit.Framework;

namespace AbilitySystem.Tests
{
    /// <summary>EntityStateMachine（纯C#逻辑状态机）的 EditMode 契约测试。
    /// 转换规则与旧 AnimationMachine.TrySetState 逐条等价：非强制=优先级更高（Attack 连击窗口特例）；
    /// 强制=当前非 Die；ban 表两个分支都拦截。Cast 为粘性演出态：播完保持、由技能显式强制转出。</summary>
    public class EntityStateMachineTests
    {
        private EntityStateMachine sm;

        [SetUp]
        public void SetUp()
        {
            sm = new EntityStateMachine();
        }

        [Test]
        public void InitialState_IsDefault()
        {
            Assert.That(sm.CurrentState, Is.EqualTo(EntityState.Default));
            Assert.That(sm.CurrentAttackPhase, Is.EqualTo(AttackPhase.None));
        }

        [Test]
        public void PriorityOf_FollowsDeclarationOrder()
        {
            Assert.That(EntityStateMachine.PriorityOf(EntityState.Default), Is.LessThan(EntityStateMachine.PriorityOf(EntityState.Idle)));
            Assert.That(EntityStateMachine.PriorityOf(EntityState.Idle), Is.LessThan(EntityStateMachine.PriorityOf(EntityState.Move)));
            Assert.That(EntityStateMachine.PriorityOf(EntityState.Move), Is.LessThan(EntityStateMachine.PriorityOf(EntityState.Attack)));
            Assert.That(EntityStateMachine.PriorityOf(EntityState.Attack), Is.LessThan(EntityStateMachine.PriorityOf(EntityState.Start)));
            Assert.That(EntityStateMachine.PriorityOf(EntityState.Start), Is.LessThan(EntityStateMachine.PriorityOf(EntityState.Cast)));
            Assert.That(EntityStateMachine.PriorityOf(EntityState.Cast), Is.LessThan(EntityStateMachine.PriorityOf(EntityState.Die)));
        }

        [Test]
        public void NonForce_OnlyForward_ByPriority()
        {
            Assert.That(sm.TrySetState(EntityState.Move, false), Is.True, "Default→Move 前向允许");
            Assert.That(sm.TrySetState(EntityState.Idle, false), Is.False, "Move→Idle 后向拒绝");
            Assert.That(sm.TrySetState(EntityState.Die, false), Is.True, "Move→Die 前向允许");
        }

        [Test]
        public void NonForce_SameState_Rejected()
        {
            sm.TrySetState(EntityState.Move, false);
            Assert.That(sm.TrySetState(EntityState.Move, false), Is.False, "Move→Move 非强制拒绝（优先级不严格更高）");
        }

        [Test]
        public void Force_SameState_And_Backward_Allowed_ExceptFromDie()
        {
            sm.TrySetState(EntityState.Move, true);
            Assert.That(sm.TrySetState(EntityState.Move, true), Is.True, "强制同态重入允许（Move 换分支依赖此语义）");
            Assert.That(sm.TrySetState(EntityState.Idle, true), Is.True, "强制后向允许");
            sm.TrySetState(EntityState.Die, true);
            Assert.That(sm.TrySetState(EntityState.Idle, true), Is.False, "Die 是终态，强制也出不去");
        }

        [Test]
        public void Cast_NonForceBlocksLowerStates_ForceExits()
        {
            sm.TrySetState(EntityState.Cast, true);
            Assert.That(sm.TrySetState(EntityState.Attack, false), Is.False, "演出中非强制不可降级");
            Assert.That(sm.TrySetState(EntityState.Move, false), Is.False);
            Assert.That(sm.TrySetState(EntityState.Die, false), Is.True, "死亡可打断演出（Cast<Die）");
            sm.ResetForPool();
            sm.TrySetState(EntityState.Cast, true);
            Assert.That(sm.TrySetState(EntityState.Idle, true), Is.True, "转出由技能显式强制负责");
        }

        [Test]
        public void Ban_BlocksBothBranches()
        {
            sm.AddStateToBan(new[] { EntityState.Move });
            Assert.That(sm.TrySetState(EntityState.Move, false), Is.False);
            Assert.That(sm.TrySetState(EntityState.Move, true), Is.False);
            sm.RemoveStateFromBan(new[] { EntityState.Move });
            Assert.That(sm.TrySetState(EntityState.Move, false), Is.True);
        }

        [Test]
        public void StateChanged_FiresOnEverySuccessfulSet_IncludingReentry()
        {
            var log = new List<(EntityState from, EntityState to)>();
            sm.StateChanged += (f, t) => log.Add((f, t));
            sm.TrySetState(EntityState.Move, false);
            sm.TrySetState(EntityState.Move, true);
            Assert.That(log, Is.EqualTo(new[] { (EntityState.Default, EntityState.Move), (EntityState.Move, EntityState.Move) }));
        }

        [Test]
        public void Attack_ComboWindow_AllowsNonForceReentry()
        {
            sm.TrySetState(EntityState.Attack, true);
            sm.NotifyAttackActiveCompleted(2); // 双段攻击组：进入 ComboWindow 并推进连击索引
            Assert.That(sm.CurrentAttackPhase, Is.EqualTo(AttackPhase.ComboWindow));
            Assert.That(sm.AttackComboIndex, Is.EqualTo(1));
            Assert.That(sm.TrySetState(EntityState.Attack, false), Is.True, "连击窗口内 Attack→Attack 非强制允许");
            Assert.That(sm.CurrentAttackPhase, Is.EqualTo(AttackPhase.Active), "新攻击实例相位归 Active");
        }

        [Test]
        public void Attack_ActiveComplete_SingleGroup_IndexStaysZero()
        {
            sm.TrySetState(EntityState.Attack, true);
            sm.NotifyAttackActiveCompleted(1);
            Assert.That(sm.AttackComboIndex, Is.EqualTo(0));
            Assert.That(sm.CurrentAttackPhase, Is.EqualTo(AttackPhase.ComboWindow));
        }

        [Test]
        public void Tick_ComboWindowExpiry_RaisesEndPhase_AndResetsIndex()
        {
            AttackPhase? seen = null;
            sm.AttackPhaseChanged += p => seen = p;
            sm.TrySetState(EntityState.Attack, true);
            sm.NotifyAttackActiveCompleted(2);
            sm.Tick(0.04f);
            Assert.That(sm.CurrentAttackPhase, Is.EqualTo(AttackPhase.ComboWindow), "0.05s 窗口未到");
            sm.Tick(0.02f);
            Assert.That(sm.CurrentAttackPhase, Is.EqualTo(AttackPhase.ComboWindow), "越零当拍只递减不判定（旧 FixedUpdate 语义：递减与到期分拍）");
            sm.Tick(0.02f);
            Assert.That(sm.CurrentAttackPhase, Is.EqualTo(AttackPhase.End));
            Assert.That(seen, Is.EqualTo(AttackPhase.End));
            Assert.That(sm.AttackComboIndex, Is.EqualTo(0));
        }

        [Test]
        public void NotifyAttackEndCompleted_TransitionsToIdle()
        {
            sm.TrySetState(EntityState.Attack, true);
            sm.NotifyAttackActiveCompleted(1);
            sm.Tick(0.06f); // 越零递减（旧语义当拍仍 ComboWindow；本方法对非 None 相位均收尾）
            sm.NotifyAttackEndCompleted();
            Assert.That(sm.CurrentState, Is.EqualTo(EntityState.Idle));
            Assert.That(sm.CurrentAttackPhase, Is.EqualTo(AttackPhase.None));
        }

        [Test]
        public void NotifyAttackEndCompleted_FromActive_SingleAttackNoEnd()
        {
            sm.TrySetState(EntityState.Attack, true);
            sm.NotifyAttackEndCompleted(); // 单发无 End：主动段播完直接回 Idle
            Assert.That(sm.CurrentState, Is.EqualTo(EntityState.Idle));
        }

        [Test]
        public void NotifyStartAnimationCompleted_ReturnsStartToIdle()
        {
            sm.TrySetState(EntityState.Start, false);
            sm.NotifyStartAnimationCompleted();
            Assert.That(sm.CurrentState, Is.EqualTo(EntityState.Idle));
        }

        [Test]
        public void NotifyStartAnimationCompleted_IgnoredWhenStateMovedOn()
        {
            sm.TrySetState(EntityState.Start, false);
            sm.TrySetState(EntityState.Die, true);
            sm.NotifyStartAnimationCompleted(); // 晚到的播完上报不得把 Die 拉回 Idle
            Assert.That(sm.CurrentState, Is.EqualTo(EntityState.Die));
        }

        [Test]
        public void AttackAction_FiredOnceOnNotifyAttackFrame()
        {
            int fired = 0;
            sm.TrySetAttackState(true, () => fired++);
            sm.NotifyAttackFrame();
            sm.NotifyAttackFrame(); // 一次性：Spine 每次攻击只应有一个 OnAttack 事件，多余事件不得重复触发
            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void TrySetAttackState_Failure_RejectedInDieState()
        {
            sm.TrySetState(EntityState.Die, true);
            Assert.That(sm.TrySetAttackState(true, () => { }), Is.False, "Die 终态拒一切");
        }

        [Test]
        public void DieAnimationCompleted_RaisedOnlyInDieState()
        {
            int raised = 0;
            sm.DieAnimationCompleted += () => raised++;
            sm.TrySetState(EntityState.Die, true);
            sm.NotifyDieAnimationCompleted();
            sm.NotifyDieAnimationCompleted();
            Assert.That(raised, Is.EqualTo(2));
        }

        [Test]
        public void ResetForPool_BackToDefault_AndClearsEverything()
        {
            sm.AddStateToBan(new[] { EntityState.Move });
            sm.TrySetAttackState(true, () => { });
            sm.NotifyAttackActiveCompleted(2);
            sm.TrySetState(EntityState.Die, true);
            sm.ResetForPool();
            Assert.That(sm.CurrentState, Is.EqualTo(EntityState.Default));
            Assert.That(sm.CurrentAttackPhase, Is.EqualTo(AttackPhase.None));
            Assert.That(sm.AttackComboIndex, Is.EqualTo(0));
            Assert.That(sm.TrySetState(EntityState.Move, false), Is.True, "ban 表已清");
        }

        [Test]
        public void ResetForPool_RaisesStateChanged_SoPresenterCanPlayDefault()
        {
            EntityState? to = null;
            sm.StateChanged += (_, t) => to = t;
            sm.TrySetState(EntityState.Die, true);
            sm.ResetForPool();
            Assert.That(to, Is.EqualTo(EntityState.Default));
        }
    }
}
