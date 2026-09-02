# 动画状态机重构实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 AnimationMachine 拆成 纯C#逻辑状态机(EntityStateMachine) + 表现映射器(AnimationMachine) + EntityVisuals/EntityFacing，数据流单向化，新增 Cast 状态替换 Die 后门，槽位字典化。

**Architecture:** Entity 持有 EntityStateMachine（readonly 字段构造，同 AttributeStore 先例）；AnimationMachine 订阅 StateChanged 播动画、经 Notify* 上报动画时机；回收链归 Entity（订阅 DieAnimationCompleted）；槽位解析全部字典化。spec 见 `docs/superpowers/specs/2026-09-01-animation-machine-refactor-design.md`。

**Tech Stack:** Unity 2022.3.62f3 / Spine (spine-unity) / DOTween / NUnit EditMode 测试。

---

## 全局约定

- **Unity 可执行文件**: `E:/UnityHub/Editor/2022.3.62f3/Editor/Unity.exe`
- **EditMode 测试命令**（要求该项目的 Unity 编辑器已关闭，否则报 project lock 错）:
  ```bash
  "/e/UnityHub/Editor/2022.3.62f3/Editor/Unity.exe" -batchmode -nographics -projectPath "E:/Unity/projects/TD" -runTests -testPlatform EditMode -testResults "E:/Unity/projects/TD/Temp/results.xml" -logFile "E:/Unity/projects/TD/Temp/unity-test.log"; echo "exit=$?"
  ```
  退出码 0=全部通过；2=有失败（读 `Temp/results.xml` 定位）；其他=运行错误（读 log）。若报 project 已被占用：请用户关闭 Unity 编辑器后重试，或请用户在 Test Runner 里手动跑。
- **提交信息**：中文、沿用仓库风格，结尾加 `Co-Authored-By: Claude Code <noreply@anthropic.com>`。
- **行为等价基准**：`Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/AnimationMachine.cs` 的当前实现（refactor 起点 HEAD=1405cb3）。攻击编排的缩放/夹紧/时序逻辑只换宿主不"优化"。
- **组件顺序事实**（池装配决定，`EntityPoolManager.CreateNewEntity` 逆序 PreWarm / `CallOut` 正序 Initialize / `Return` 正序 Dormancy）：
  - PreWarm 逆序：Facing → Visuals → Entity → BuffController → prefab 组件（AM 最后）。AM.PreWarm 可安全读 `GetComponent<Entity>().StateMachine`（SM 是 readonly 字段初始化器，随 AddComponent 即构造）。
  - Initialize 正序：AM 最先（分支复位）→ 技能 → BuffController → Entity（SM→Start）→ Visuals（FadeIn+订阅）→ Facing。
  - Dormancy 正序：AM 最先（清 overrides）→ … → Entity.Dormancy 最后（SM.ResetForPool→StateChanged(Default)→AM 播 Default，此时 overrides 已清=播基础资产，等价旧 AM.Dormancy 内 SetState(Default)）。
- **旧 Die 后门机制（考古结论，源码已证实）**：旧技能用 `AddOverride(Start=x)+TrySetState(Die)`——`SetState(Die)` 播的是 **SO 的 Die 槽资产**（非覆盖的 Start）；覆盖 Start 的作用是劫持 `ClassifyAnimation`（其判定顺序 Start 在 Die 之前）把播放中的动画分类成 StartAnim → `currentState=Start` → Die-complete 分支（要求 `currentState==Die`）不触发 → 不淡出不回池。该链路依赖 SO 配置与分类顺序的隐式耦合；且三个旧技能的终局意图（钻出后恢复行走/复活后存活/变身后显式退场）在旧机制下并无通路——属意图明确、机制残缺的遗留代码。本计划按意图重写（用户已批准"边缘重对齐"）。
- **Cast 语义（关键设计决策）**：**粘性**——Cast 槽动画播完保持末帧、不自动回 Idle（钻地潜伏等演出依赖末帧保持）；转出由技能显式 `TrySetState(Idle/Default, true)` 负责。Start 仍自动回 Idle（旧 `SetState(Start)` 排队 Idle 的行为，AnimationMachine.cs:517-521）。
- 已知的有意差异（spec 批准的"边缘重对齐"）：① ComboWindow 无 End 资产时回到循环 Idle（原为非循环）；② EntityVisuals.Dormancy 会 Kill 未完成的 tween（防双回池）；③ Die 后门技能按意图重写（见 Task 5）。

---

### Task 1: EntityStateMachine（纯C#）+ Cast 状态 + EditMode 测试

**Files:**
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntityState.cs`
- Create: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntityStateMachine.cs`
- Create: `Assets/Tests/Editor/AbilitySystem/EntityStateMachineTests.cs`（放在现有 AbilitySystem.Tests.Editor asmdef 下，因其已引用 BasicScripts 与 NUnit；纯C#测试与程序集名无关）

- [ ] **Step 1.1: EntityState 增加 Cast**

`EntityState.cs` 整文件替换为：

```csharp
/// <summary>
/// 实体逻辑状态机状态（EntityStateMachine 持有）。
/// 声明顺序即转换优先级（见 EntityStateMachine.PriorityOf：Default&lt;Idle&lt;Move&lt;Attack&lt;Start&lt;Cast&lt;Die）。
/// 注：动画资源槽位由 <see cref="AnimationSlot"/> 标识，与本枚举的语义不重合。
/// </summary>
public enum EntityState
{
    Default,
    Idle,
    Move,
    Attack,
    Start,
    Cast,
    Die,
}
```

注意：本任务只加枚举值，不动 AnimationMachine（其 `state > currentState` 比较在无调用方传 Cast 时语义不变；`SetState` 的 switch 走 `default: break` 不播任何东西，可接受——Task 4 会接管）。

- [ ] **Step 1.2: 写失败测试**

创建 `Assets/Tests/Editor/AbilitySystem/EntityStateMachineTests.cs`：

```csharp
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
```

- [ ] **Step 1.3: 跑测试确认失败**

运行全局约定的测试命令。预期：编译错误 `EntityStateMachine/AttackPhase/EntityState.Cast 不存在`（这正是失败信号）。

- [ ] **Step 1.4: 实现 EntityStateMachine**

创建 `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntityStateMachine.cs`：

```csharp
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
        _attackPhase = AttackPhase.None;
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

    /// <summary>攻击收尾（End 段播完，或单发无 End 时主动段播完）→ 回 Idle。</summary>
    public void NotifyAttackEndCompleted()
    {
        if (_current != EntityState.Attack || _attackPhase == AttackPhase.None) return;
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
```

- [ ] **Step 1.5: 跑测试确认通过**

运行测试命令。预期：`EntityStateMachineTests` 全绿；既有测试（AnimationOverrideCoveredSlotsTests 等）不受影响。

- [ ] **Step 1.6: Commit**

```bash
cd "e:/Unity/projects/TD" && git add Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntityState.cs Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntityStateMachine.cs Assets/Tests/Editor/AbilitySystem/EntityStateMachineTests.cs && git commit -m "重构：新增纯C#逻辑状态机EntityStateMachine（含Cast粘性演出态与全量转换测试）

Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 2: 槽位字典化（AnimationOverride/AnimationSet 独立文件）+ 全部覆盖调用方索引化

本任务只换数据结构，行为等价。AnimationMachine 的状态机/编排逻辑不动（Task 4 才动），仅替换其槽位存取代码。

**Files:**
- Create: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/AnimationOverride.cs`
- Create: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/AnimationSet.cs`
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/AnimationMachine.cs`
- Modify: `Assets/PublicScripts/GameData/Animation/AnimationResources.cs`（DefaultAnimationTemplate 加 Cast）
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/ApplyAnimationOverride.cs`
- Modify: `Assets/Tests/Editor/AbilitySystem/AnimationOverrideCoveredSlotsTests.cs`
- Modify（仅初始化器语法）:
  - `Assets/PublicScripts/Entity-LevelPublicScripts/AbilitySystem/Components/SharedTargetExtraAttack.cs`
  - `Assets/Resources/Prefabs/Monsters/Origin/Wdslm/Scripts/Wdslm/WdslmSkill2.cs`
  - `Assets/Resources/Prefabs/Monsters/Origin/Wdslm/Scripts/Wdslm/WdslmSkill3.cs`
  - `Assets/Resources/Prefabs/Monsters/MC/HeadSeter/Scripts/HeadSeterSkill1.cs`
  - `Assets/Resources/Prefabs/Monsters/MC/HeadSeter/Scripts/HeadSeterTalent1.cs`
  - `Assets/Resources/Prefabs/Monsters/MC/Wither/Scripts/WitherTalent2.cs`
  - `Assets/Resources/Prefabs/Monsters/MC/Witch/Scripts/WitchSkill.cs`
  - `Assets/Resources/Prefabs/Levels/Main/AdventureOfMinecraft/Devices/Meats/BeefSkill.cs`

- [ ] **Step 2.1: AnimationSlot 枚举加 Cast，并连同 AnimationOverride 挪到独立文件**

创建 `AnimationOverride.cs`（AnimationSlot/AnimationOverride 从 AnimationMachine.cs L14-85 删除）：

```csharp
using System.Collections.Generic;

/// <summary>
/// 动画资源槽位。用于解析动画资源，以及在 <see cref="AnimationOverride"/> 中显式清空某个覆盖槽。
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
    Cast,
    Die,
    AttackRemote,
    AttackClose,
    AttackBegin,
    AttackEnd,
    ChargeBegin,
    Charge,
    ChargeEnd,
}

/// <summary>
/// 一次动画覆盖申请：槽位→资源名。经索引器读写（空值=移除，不算覆盖槽）。
/// <see cref="Clear"/> 显式清空槽位（覆盖到"无动画"）。
/// </summary>
public sealed class AnimationOverride
{
    private readonly Dictionary<AnimationSlot, string> _entries = new Dictionary<AnimationSlot, string>();

    /// <summary>显式清空的槽位（覆盖为"无动画"），也算覆盖槽。</summary>
    internal readonly HashSet<AnimationSlot> ClearedSlots = new HashSet<AnimationSlot>();

    /// <summary>槽位→资源名。赋 null/空串=移除该槽。</summary>
    public string this[AnimationSlot slot]
    {
        get { return _entries.TryGetValue(slot, out string value) ? value : null; }
        set
        {
            if (string.IsNullOrEmpty(value)) _entries.Remove(slot);
            else _entries[slot] = value;
        }
    }

    internal Dictionary<AnimationSlot, string> Entries { get { return _entries; } }

    public AnimationOverride Clear(params AnimationSlot[] slots)
    {
        foreach (AnimationSlot slot in slots)
        {
            ClearedSlots.Add(slot);
        }
        return this;
    }

    /// <summary>
    /// Slots this override affects: every slot with a non-empty resource, plus
    /// cleared slots. One-shot entries seed their pending-consumption set from this.
    /// </summary>
    public HashSet<AnimationSlot> GetCoveredSlots()
    {
        var covered = new HashSet<AnimationSlot>(ClearedSlots);
        covered.UnionWith(_entries.Keys);
        return covered;
    }
}
```

- [ ] **Step 2.2: AnimationSet 独立文件（字典实现）**

创建 `AnimationSet.cs`（嵌套 AnimationSet 类从 AnimationMachine.cs L649-836 删除；私有静态 `MoveBranchSlot`（L284-293）与 `GetMove` 一并由新形态取代）：

```csharp
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
```

- [ ] **Step 2.3: AnimationResources 模板加 Cast**

`AnimationResources.cs` 的 `DefaultAnimationTemplate` 改为（只加一行字段）：

```csharp
    [Serializable]
    public sealed class DefaultAnimationTemplate
    {
        public AnimationReferenceAsset Default;
        public AnimationReferenceAsset Idle;
        public AnimationReferenceAsset Start;
        public AnimationReferenceAsset Cast;
        public AnimationReferenceAsset Die;
    }
```

既有 26 个 .asset 未序列化该字段=null，符合预期（Cast 属技能演出槽，按需配置；headSeter.asset 已有 `Skill_Begin/Skill_End` 等命名资源可供 Task 5 覆盖引用）。

- [ ] **Step 2.4: AnimationMachine 槽位存取改为字典 API**

`AnimationMachine.cs` 就地修改（本任务不改其状态机行为）：

1. 删除已迁出的 `AnimationSlot`/`AnimationOverride`/嵌套 `AnimationSet`/私有静态 `MoveBranchSlot`；删除 `GetMove` 方法（调用点改 `GetSingle(AnimationSet.MoveBranchSlot(branch))`）。
2. `PreWarm()` 末尾 14 行 mix 清单（L909-922）与 `RegisterMixes(AnimationOverride)` 内 14 行清单（L949-962）合并。删除 `SetMixToStartAll`，`SetMixToStart` 保留原实现（null 守卫照旧）。新代码：

```csharp
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
```

（注意旧 PreWarm 的 mix 清单不含 Start 槽自身，`EnumerateSingles` 会多出一项 Start→Start 的 SetMix，行为无差。）

3. `ResolveAnimationDuration`（L216-220）改为：

```csharp
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
```

4. 具名字段访问全部改字典 API（机械替换，模式：`_activeAnimations.X` → `_activeAnimations.GetSingle(AnimationSlot.X)`，组槽 → `GetGroup(AnimationSlot.X)`）：
   - `PlayAttackAnimation`（L443-488）：`_activeAnimations.ChargeBegin/Charge/ChargeEnd/AttackBegin/AttackRemote/AttackClose/AttackEnd` → 对应 GetSingle/GetGroup。
   - `SetState`（L490-528）：`Default/Idle/Start/Idle(排队)/Die` 各 case → GetSingle；Move case：`_activeAnimations.GetMove(moveBranch)` → `_activeAnimations.GetSingle(AnimationSet.MoveBranchSlot(moveBranch))`，`ConsumeOneShots(MoveBranchSlot(moveBranch))` → `ConsumeOneShots(AnimationSet.MoveBranchSlot(moveBranch))`。
   - `FinishComboWindow`（L438）：`_activeAnimations.Idle` → GetSingle。
   - `ClassifyAnimation`（L589-605）：`_activeAnimations.Default/Idle/Start/Die` → GetSingle；`IsMoveAnimation` 改：

```csharp
        private bool IsMoveAnimation(Spine.Animation animation)
        {
            return Matches(_activeAnimations.GetSingle(AnimationSlot.Move), animation)
                || Matches(_activeAnimations.GetSingle(AnimationSlot.JumpBegin), animation)
                || Matches(_activeAnimations.GetSingle(AnimationSlot.JumpLoop), animation)
                || Matches(_activeAnimations.GetSingle(AnimationSlot.JumpEnd), animation);
        }
```

   （`Matches` 从嵌套 AnimationSet 的私有静态提为 AnimationMachine 的私有静态，原实现不变。）
   - `HandleAnimationStateStart`（L546-585）：`Attack[_attackAnimationIndex]`/`_activeAnimations.Idle` 中的具名访问同模式改。
   - `HandleAnimationStateComplete`（L860-880）：`_activeAnimations.Die` 两处 → GetSingle。

- [ ] **Step 2.5: ApplyAnimationOverride 字典化**

`ApplyAnimationOverride.cs`：删除 `TrySetSlot` 整个方法，`BuildOverride` 改为：

```csharp
        private AnimationOverride BuildOverride()
        {
            string[] slots = _slots();
            string[] resources = _resources();
            if (slots == null || resources == null) return null;
            int count = Math.Min(slots.Length, resources.Length);
            if (slots.Length != resources.Length)
            {
                Debug.LogWarning(
                    $"ApplyAnimationOverride: slots/resources length mismatch " +
                    $"({slots.Length}/{resources.Length}); applying first {count} entries");
            }
            if (count == 0) return null;

            var result = new AnimationOverride();
            bool hasValidEntry = false;
            for (int i = 0; i < count; i++)
            {
                if (string.IsNullOrEmpty(resources[i])) continue;
                if (!Enum.TryParse(slots[i], true, out AnimationSlot parsed))
                {
                    Debug.LogWarning($"ApplyAnimationOverride: unknown animation slot '{slots[i]}'; skipping");
                    continue;
                }
                result[parsed] = resources[i];
                hasValidEntry = true;
            }
            return hasValidEntry ? result : null;
        }
```

- [ ] **Step 2.6: 全部覆盖调用方改索引初始化器**

逐文件替换初始化器（行为不变，仅语法；对象初始化器的具名字段赋值 → 索引器赋值）：

`SharedTargetExtraAttack.cs`（L80-84）：
```csharp
            var animations = new AnimationOverride
            {
                [AnimationSlot.AttackClose] = animation,
                [AnimationSlot.AttackRemote] = animation,
            };
```

`WdslmSkill2.cs`（约 L43）：
```csharp
        _animationOverride = _thisEntity.entityAM.AddOverride(this, new AnimationOverride
        {
            [AnimationSlot.AttackClose] = _inciteDefectionAnimation,
            [AnimationSlot.AttackRemote] = _inciteDefectionAnimation,
        });
```

`WdslmSkill3.cs` SkillBegin（L27-31；本文件 Task 5 再改语义，本步只换语法）：
```csharp
        _thisEntity.entityAM.AddOverride(this, new AnimationOverride
        {
            [AnimationSlot.Start] = _skillStart,
            [AnimationSlot.Idle] = _skillLoop,
        });
```
SkillEnd（L53）：
```csharp
        _thisEntity.entityAM.AddOverride(this, new AnimationOverride { [AnimationSlot.Start] = _skillEnd });
```

`HeadSeterSkill1.cs`（L27/31/43/47；本文件 Task 5 再改语义，本步只换语法）：
```csharp
            am.AddOverride(this, new AnimationOverride { [AnimationSlot.Start] = _begin });
// L31 else 分支：
            am.AddOverride(this, new AnimationOverride { [AnimationSlot.Start] = _begin_d });
// L43：
            am.AddOverride(this, new AnimationOverride { [AnimationSlot.Start] = _end });
// L47 else 分支：
            am.AddOverride(this, new AnimationOverride { [AnimationSlot.Start] = _end_d });
```

`HeadSeterTalent1.cs`（三处多槽初始化器，字段名→索引器一一对应）：
```csharp
                am.AddOverride(this, new AnimationOverride
                {
                    [AnimationSlot.Default] = _default_d,
                    [AnimationSlot.Move] = _move_d,
                    [AnimationSlot.Idle] = _idle_d,
                    [AnimationSlot.Die] = _die_d,
                    [AnimationSlot.AttackClose] = _set_d,
                    [AnimationSlot.AttackRemote] = _set_d,
                });
```
（另两处同理：`_default_1/_move_1/_idle_1` 三槽版、`_attack` 双攻击槽版——以原文件初始化器里的字段清单为准逐项转索引器。）

`WitherTalent2.cs`（L15、L27）：
```csharp
        _thisEntity.entityAM.AddOverride(this, new AnimationOverride { [AnimationSlot.Idle] = _recoverLoop });
// L27：
        _thisEntity.entityAM.AddOverride(this, new AnimationOverride { [AnimationSlot.Start] = _start2 });
```

`WitchSkill.cs`（约 L16）：
```csharp
        _animationOverride = _thisEntity.entityAM.AddOverride(this, new AnimationOverride
        {
            [AnimationSlot.AttackClose] = _drink,
            [AnimationSlot.AttackRemote] = _drink,
        });
```

`BeefSkill.cs`（L13）：
```csharp
        AnimationOverrideHandle handle = _thisEntity.entityAM.AddOneShotOverride(this, new AnimationOverride { [AnimationSlot.Idle] = _skill });
```

- [ ] **Step 2.7: 适配 CoveredSlots 测试 + 新增 AnimationSet 测试**

`AnimationOverrideCoveredSlotsTests.cs` 整文件替换：

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Spine.Unity;
using UnityEngine;

namespace AbilitySystem.Tests
{
    /// <summary>AnimationOverride.GetCoveredSlots（一次性覆盖的消费槽位集合）的 EditMode 契约测试。
    /// 覆盖槽 = 非空资源槽 + ClearedSlots；空值不算。一次性覆盖条目靠它初始化待消费集合，
    /// 漏一个槽就永远不消费（条目滞留），所以逐槽位断言。</summary>
    public class AnimationOverrideCoveredSlotsTests
    {
        [Test]
        public void CoveredSlots_MixesSetResourcesAndClearedSlots_SkipsEmpty()
        {
            var ov = new AnimationOverride { [AnimationSlot.Idle] = "anim_idle", [AnimationSlot.AttackRemote] = "anim_atk" };
            ov.Clear(AnimationSlot.Die);

            HashSet<AnimationSlot> covered = ov.GetCoveredSlots();

            Assert.That(covered, Is.EquivalentTo(new[]
            {
                AnimationSlot.Idle, AnimationSlot.AttackRemote, AnimationSlot.Die,
            }));
        }

        [Test]
        public void CoveredSlots_AllSixteenSlots_WhenFullyPopulated()
        {
            var ov = new AnimationOverride
            {
                [AnimationSlot.Default] = "a", [AnimationSlot.Idle] = "a", [AnimationSlot.Move] = "a",
                [AnimationSlot.JumpBegin] = "a", [AnimationSlot.JumpLoop] = "a", [AnimationSlot.JumpEnd] = "a",
                [AnimationSlot.Start] = "a", [AnimationSlot.Cast] = "a", [AnimationSlot.Die] = "a",
                [AnimationSlot.AttackBegin] = "a", [AnimationSlot.AttackEnd] = "a",
                [AnimationSlot.AttackRemote] = "a", [AnimationSlot.AttackClose] = "a",
                [AnimationSlot.ChargeBegin] = "a", [AnimationSlot.Charge] = "a", [AnimationSlot.ChargeEnd] = "a",
            };

            Assert.That(ov.GetCoveredSlots().Count, Is.EqualTo(16));
        }

        [Test]
        public void CoveredSlots_EmptyOverride_IsEmpty()
        {
            Assert.That(new AnimationOverride().GetCoveredSlots(), Is.Empty);
        }

        [Test]
        public void Indexer_NullOrEmptyValue_RemovesSlot()
        {
            var ov = new AnimationOverride { [AnimationSlot.Idle] = "x" };
            ov[AnimationSlot.Idle] = null;
            Assert.That(ov.GetCoveredSlots(), Is.Empty);
            Assert.That(ov[AnimationSlot.Idle], Is.Null);
        }

        [Test]
        public void Indexer_GetUnsetSlot_ReturnsNull()
        {
            Assert.That(new AnimationOverride()[AnimationSlot.Die], Is.Null);
        }
    }

    /// <summary>AnimationSet（槽位字典）解析契约：覆盖应用、清槽、组/单槽路由、拷贝独立性。</summary>
    public class AnimationSetTests
    {
        private static AnimationReferenceAsset Anim(string name)
        {
            var asset = ScriptableObject.CreateInstance<AnimationReferenceAsset>();
            asset.name = name;
            return asset;
        }

        [Test]
        public void Apply_RoutesSingleAndGroupSlots_BySlotKind()
        {
            AnimationReferenceAsset idle = Anim("idle"), cast = Anim("cast");
            AnimationReferenceAsset[] remote = { Anim("r1"), Anim("r2") };
            var resolved = new AnimationSet();
            var ov = new AnimationOverride
            {
                [AnimationSlot.Idle] = "idle",
                [AnimationSlot.Cast] = "cast",
                [AnimationSlot.AttackRemote] = "remote",
            };

            resolved.Apply(ov,
                name => name == "idle" ? idle : cast,
                name => remote);

            Assert.That(resolved.GetSingle(AnimationSlot.Idle), Is.SameAs(idle));
            Assert.That(resolved.GetSingle(AnimationSlot.Cast), Is.SameAs(cast));
            Assert.That(resolved.GetGroup(AnimationSlot.AttackRemote), Is.SameAs(remote));
        }

        [Test]
        public void Apply_ClearSlots_RemovesResolvedEntries()
        {
            var resolved = new AnimationSet();
            resolved.SetSingle(AnimationSlot.Idle, Anim("idle"));
            resolved.SetGroup(AnimationSlot.Charge, new[] { Anim("c") });

            var ov = new AnimationOverride();
            ov.Clear(AnimationSlot.Idle, AnimationSlot.Charge);
            resolved.Apply(ov, name => Anim(name), name => new[] { Anim(name) });

            Assert.That(resolved.GetSingle(AnimationSlot.Idle), Is.Null);
            Assert.That(resolved.GetGroup(AnimationSlot.Charge), Is.Null);
        }

        [Test]
        public void Copy_IsIndependentOfSource()
        {
            var source = new AnimationSet();
            source.SetSingle(AnimationSlot.Idle, Anim("idle"));
            AnimationSet copy = source.Copy();
            copy.SetSingle(AnimationSlot.Idle, Anim("other"));

            Assert.That(source.GetSingle(AnimationSlot.Idle).name, Is.EqualTo("idle"));
        }

        [Test]
        public void IsGroup_DistinguishesGroupSlots()
        {
            Assert.That(AnimationSet.IsGroup(AnimationSlot.AttackRemote), Is.True);
            Assert.That(AnimationSet.IsGroup(AnimationSlot.AttackClose), Is.True);
            Assert.That(AnimationSet.IsGroup(AnimationSlot.Charge), Is.True);
            Assert.That(AnimationSet.IsGroup(AnimationSlot.Idle), Is.False);
            Assert.That(AnimationSet.IsGroup(AnimationSlot.Cast), Is.False);
        }

        [Test]
        public void Apply_UnknownResourceName_ThrowsKeyNotFound()
        {
            var resolved = new AnimationSet();
            var ov = new AnimationOverride { [AnimationSlot.Idle] = "nope" };
            // 解析器抛 KeyNotFound（等价 AnimationResources.GetAnimation 的行为）→ 配置错误必须暴露
            Assert.Throws<KeyNotFoundException>(
                () => resolved.Apply(ov, name => throw new KeyNotFoundException(name), name => null));
        }
    }
}
```

- [ ] **Step 2.8: 跑测试确认全绿**

运行测试命令。预期：新增 AnimationSetTests + 适配后的 CoveredSlots 测试 + 既有全部测试通过（ApplyAnimationOverrideTests 走组件 API，不需改动）。

- [ ] **Step 2.9: Commit**

```bash
cd "e:/Unity/projects/TD" && git add -A Assets/PublicScripts Assets/Resources Assets/Tests && git commit -m "重构：动画槽位字典化——AnimationOverride/AnimationSet独立文件，覆盖API索引化，mix注册清单合一，新增Cast槽

Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 3: EntityVisuals / EntityFacing 拆出 + Entity.ArriveEnd + 池装配

**Files:**
- Create: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntityVisuals.cs`
- Create: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/EntityFacing.cs`
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/LevelOperator/EntityPoolManager.cs`（CreateNewEntity 装配）
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs`（visuals/facing 字段 + ArriveEnd 方法）
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/AnimationMachine.cs`（删视觉/朝向代码）
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/MoveScripts/MoveBase.cs`（ArriveEnd）
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/AttackScripts/AttackBase.cs`（SetDirection）
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Bullet.cs`（CurrentDirection）

- [ ] **Step 3.1: 创建 EntityVisuals**

`EntityVisuals.cs`（视觉逻辑自 AnimationMachine.cs L151-214 的 FadeAlpha/ApplyFlashRed/_flashRedBaselineG + L967/981-985 的 FadeIn/HandleAfterHutoff 原样迁移）：

```csharp
using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 实体纯视觉反馈：受击闪红、淡入/淡出。自订阅 OnAfterHurt（Entity.Dormancy 置空事件，无需手动退订）。
/// 回收由 Entity 决定：FadeOut 的完成回调交给调用方，本组件不发起回池。
/// </summary>
public class EntityVisuals : MonoBehaviour, IPoolOperation
{
    // applyType 约定值：2=无受击表现（与 EntityStats.ApplyDamage 调用方的约定一致，原 AnimationMachine 同判据）
    private const int ApplyTypeSuppressHurtFlash = 2;

    private Entity _entity;
    private SkeletonAnimation _skeleton;
    private Tween _tween;
    // Snapshotted g-channel at the moment FlashRed starts; the tween body
    // reads this each frame instead of capturing a closure.
    private float _flashRedBaselineG;

    public void PreWarm()
    {
        _entity = GetComponent<Entity>();
        _skeleton = transform.GetChild(0).GetComponent<SkeletonAnimation>();
    }

    public void Initialize()
    {
        FadeIn(0.2f);
        _entity.OnAfterHurt += HandleAfterHurt;
    }

    public void Dormancy()
    {
        // 中断未完成的补间：防止跨池复用的陈旧 FadeOut 完成回调触发二次回池
        if (_tween != null && _tween.IsActive()) _tween.Kill();
        _tween = null;
    }

    public void FadeIn(float duration)
    {
        _tween = DOTween.To(FadeAlpha, 0, 1, duration);
    }

    public void FadeOut(float duration, Action onComplete)
    {
        _tween = DOTween.To(FadeAlpha, 1, 0, duration).OnComplete(() => onComplete?.Invoke());
    }

    public void FlashRed(float duration)
    {
        _flashRedBaselineG = _skeleton.skeleton.GetColor().g;
        _tween = DOTween.To(ApplyFlashRed, 0, 2, duration);
    }

    // Method group (cached, no per-checkout closure) subscribed to OnAfterHurt in Initialize.
    private void HandleAfterHurt(Entity origin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly)
    {
        if (applyType != ApplyTypeSuppressHurtFlash)
        {
            FlashRed(0.2f);
        }
    }

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
        _skeleton.skeleton.SetColor(new Color(1, value, value));
    }

    // Applies a greyscale-with-double-alpha curve used by fade-in / fade-out.
    private void FadeAlpha(float value)
    {
        _skeleton.skeleton.SetColor(new Color(value, value, value, Math.Min(value * 2, 1)));
    }
}
```

- [ ] **Step 3.2: 创建 EntityFacing**

`EntityFacing.cs`（SetDirection 自 AnimationMachine.cs L372-408 原样迁移，含那个有意保留的 `return;`）：

```csharp
using UnityEngine;

/// <summary>
/// 实体朝向（左右翻转 + 上下标记）。朝向跨池复用保持原状（skeleton 子物体旋转同样跨复用持久，
/// 两者必须一致地不复位——与拆分前行为等价）。
/// </summary>
public class EntityFacing : MonoBehaviour, IPoolOperation
{
    private SkeletonAnimation _skeleton;
    private (bool left, bool up) _direction;

    public (bool left, bool up) CurrentDirection { get { return _direction; } }

    public void PreWarm()
    {
        _skeleton = transform.GetChild(0).GetComponent<SkeletonAnimation>();
    }

    public void Initialize() { }
    public void Dormancy() { }

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
            float ry = _skeleton.transform.rotation.y;
            if (_direction.left)
            {
                if (ry != 1) _skeleton.transform.Rotate(new Vector3(0, (1 - ry) * 180, 0));
            }
            else
            {
                if (ry != 0) _skeleton.transform.Rotate(new Vector3(0, -ry * 180, 0)); return;
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
}
```

- [ ] **Step 3.3: 池装配（运行时 AddComponent，同 BuffController/Entity 先例，零 prefab 改动）**

`EntityPoolManager.cs` `CreateNewEntity`（L26-43）改为：

```csharp
    private Entity CreateNewEntity(int skillIndex = 0)
    {
        GameObject gameObject = UnityEngine.Object.Instantiate(EntityData.Prefab, entity_pool);
        gameObject.SetActive(false);
        if (isStatic)
            gameObject.AddComponent<InteractableStatic>();
        gameObject.AddComponent<BuffController>();
        Entity newEntity = gameObject.AddComponent<Entity>();
        gameObject.AddComponent<EntityVisuals>();
        gameObject.AddComponent<EntityFacing>();
        newEntity.thisEntityPool = this;
        newEntity.EntityData = EntityData;
        newEntity.SelectedSkillIndex = skillIndex;
        IPoolOperation[] poolOperations = newEntity.GetComponents<IPoolOperation>();
        for (int j = poolOperations.Length - 1; j >= 0; j--)
        {
            poolOperations[j].PreWarm();
        }
        return newEntity;
    }
```

- [ ] **Step 3.4: Entity 挂引用 + ArriveEnd**

`Entity.cs`：
1. 字段区 `public AnimationMachine entityAM;`（L49）之后加：

```csharp
    [HideInInspector] public EntityVisuals visuals;
    [HideInInspector] public EntityFacing facing;
```

2. `PreWarm()` 的 `if (this.TryGetComponent(out MoveBase mb))…else…` 块（L218-229）之后、`// === 构造子系统` 之前加（池装配保证存在，缺件=NRE 当场暴露）：

```csharp
        visuals = GetComponent<EntityVisuals>();
        facing = GetComponent<EntityFacing>();
```

3. `Die()` 方法（L163-182）之后新增：

```csharp
    /// <summary>到达终点退场：转 Default + 淡出 + 回池（原 MoveBase.ArriveEnd 的表现部分上收）。</summary>
    public void ArriveEnd()
    {
        entityAM.TrySetState(EntityState.Default, true);
        visuals.FadeOut(0.2f, () => thisEntityPool.Return(this));
    }
```

（走旧 AM API——Task 4 才换 `_stateMachine`；Die 的淡出链 Task 3.5 先接 visuals。）

- [ ] **Step 3.5: AnimationMachine 删除视觉/朝向代码**

`AnimationMachine.cs`：
1. 删除：`SetColor`+`ColorEffect` 枚举（L166-189、L853-858）、`ApplyFlashRed`（L197-208）、`FadeAlpha`（L211-214）、`_flashRedBaselineG`（L193）、`HandleAfterHurt`（L976-985）、`SetDirection`（L372-408）、`_direction` 字段（L115）、`CurrentDirection`（L149）、`ArriveEnd()`（L410-413）、`Initialize` 中的 `SetColor(ColorEffect.FadeIn, 0.2f);` 与 `thisEntity.OnAfterHurt += HandleAfterHurt;`（L967-968）、`PreWarm` 中的 `_direction = (false, false);`（L892）。
2. `HandleAnimationStateComplete` 的 Die 分支（L875-879）改为：

```csharp
        if (_activeAnimations.GetSingle(AnimationSlot.Die) != null && currentState == EntityState.Die && trackEntry.Animation == _activeAnimations.GetSingle(AnimationSlot.Die).Animation)
        {
            thisEntity.visuals.FadeOut(0.2f, () => thisEntity.thisEntityPool.Return(thisEntity));
            return;
        }
```

（Task 4 会把这条链改为 NotifyDieAnimationCompleted → Entity 订阅；本步为行为等价的中间态。）

- [ ] **Step 3.6: 调用方迁移（本任务涉及的三个文件）**

`MoveBase.cs` `ArriveEnd()`（L264-274）改为：

```csharp
    protected virtual void ArriveEnd()
    {
        _thisEntity.ArriveEnd();
        _thisEntity.Stats.IsActive = false;
        if (_levelHpComsume > 0)
        {
            LevelResourceManager.Manager.LevelHpLeft -= LevelHpConsume;
            AudioManager.Manager.PlayAudio("alarm", 1, false, false);
        }
    }
```

（`_levelHpComsume`/`LevelHpConsume` 的拼写差异是原文件既有事实，照抄。）

`AttackBase.cs` L172：

```csharp
            _thisEntity.facing.SetDirection(centerPos);
```

`Bullet.cs` L83：

```csharp
            EffectManager.Manager.CreateEffect(bulletData.BulletSpawnEffect, bulletSpawnPosition, Quaternion.Euler(0, originEntity.facing.CurrentDirection.left ? 180 : 0, 0), LevelResourceSharing.LM, 1, true);
```

- [ ] **Step 3.7: 跑测试确认全绿**

运行测试命令（编译全量通过即覆盖本任务；无新测试）。

- [ ] **Step 3.8: Commit**

```bash
cd "e:/Unity/projects/TD" && git add -A Assets/PublicScripts && git commit -m "重构：拆出EntityVisuals/EntityFacing（受击变色/朝向归独立组件），ArriveEnd回收链上收Entity，池运行时装配

Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 4: 状态机上线——AnimationMachine 改造为表现层 + 全部调用方迁移 + 回收链归 Entity

本任务是最大的一步。AnimationMachine 整文件重写。

**Files:**
- Rewrite: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/AnimationMachine.cs`
- Modify: `Assets/PublicScripts/Entity-LevelPublicScripts/EntityBehavior/Entity.cs`
- Modify: `MoveBase.cs`、`JumpMove.cs`、`AttackBase.cs`、`NormalAttack.cs`、`ChargeAttack.cs`、`WitherAttack.cs`、`BuffController.cs`、`SharedTargetExtraAttack.cs`、`BeefSkill.cs`（路径见 Task 2/3 列表）

- [ ] **Step 4.1: AnimationMachine 整文件重写为表现映射器**

`AnimationMachine.cs` 整文件替换为：

```csharp
using System;
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
    private AnimationResources _animationResources;
    private AnimationSet _baseAnimations;
    private AnimationSet _activeAnimations;
    private readonly List<OverrideEntry> _overrides = new List<OverrideEntry>();
    private int _nextOverrideId;
    private MoveAnimationBranch _moveBranch;
    private AttackAnimationBranch _attackBranch;

    // 当前攻击编排段（Animation 对象身份判定哪段播完——与旧实现同判据；跨段同资产的配置歧义与旧实现一致）
    private AnimationReferenceAsset[] _attackGroup;
    private AnimationReferenceAsset _currentAttackBegin;
    private AnimationReferenceAsset _currentAttackEnd;
    private Spine.Animation _activeAttackAnim;
    private Spine.Animation _endAnim;
    private Spine.Animation _dieAnim;
    private Spine.Animation _startAnim;

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
        _moveBranch = MoveAnimationBranch.Normal;
        _attackBranch = AttackAnimationBranch.Normal;
        _attackGroup = null;
        _currentAttackBegin = null;
        _currentAttackEnd = null;
        _activeAttackAnim = null;
        _endAnim = null;
        _dieAnim = null;
        _startAnim = null;
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
                // 粘性演出态：不循环播放，播完保持末帧；转出由技能显式 TrySetState 负责
                PlaySingle(AnimationSlot.Cast, false, 1);
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
        else
        {
            Debug.LogWarning($"Unregistered animation event: {e.Data.Name}");
        }
    }

    private void HandleAnimationStateComplete(Spine.TrackEntry trackEntry)
    {
        if (_activeAttackAnim != null && trackEntry.Animation == _activeAttackAnim)
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
        if (_endAnim != null && trackEntry.Animation == _endAnim)
        {
            _sm.NotifyAttackEndCompleted();
            return;
        }
        if (_dieAnim != null && trackEntry.Animation == _dieAnim)
        {
            _sm.NotifyDieAnimationCompleted();
            return;
        }
        if (_startAnim != null && trackEntry.Animation == _startAnim)
        {
            _sm.NotifyStartAnimationCompleted();
        }
        // Cast 播完不上报：粘性演出态，末帧保持，转出由技能负责
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
```

删除项对照（旧→新）：`currentState`/`states_ban`/`CurrentState`/`TrySetState`/`TrySetMoveState`/`TrySetAttackState`/`AddStateToBan`/`RemoveStateFromBan`/`_attackAnimationIndex`/`_attackPhase`(嵌套枚举一并)/`_attackAction`/`_attackStaticWaitTime`/`Attack` 字段/`FixedUpdate`/`SetState`/`PlayAttackAnimation`/`FinishComboWindow`/`HandleAnimationStateStart`/`ClassifyAnimation`/`AnimKind`/`event_start`——全部由 SM + 事件编排取代。

- [ ] **Step 4.2: Entity 接线**

`Entity.cs`：
1. 子系统字段区（L55-61）加：

```csharp
    private readonly EntityStateMachine _stateMachine = new EntityStateMachine();
```

2. 子系统属性区（L66-74 附近）加：

```csharp
    /// <summary>逻辑状态子系统：EntityState/AttackPhase/转换规则/ban 表（唯一状态真相源）。</summary>
    public EntityStateMachine StateMachine { get { return _stateMachine; } }
```

3. `PreWarm()` 构造区 `_skillRunner = new EntityAbilityRunner(this);`（L239）之后加（放 PreWarm 而非字段初始化——闭包捕获 this 与 visuals 引用）：

```csharp
        _stateMachine.DieAnimationCompleted += () => visuals.FadeOut(0.2f, () => thisEntityPool.Return(this));
```

4. `FixedUpdate()`（L153-161）改为：

```csharp
    protected void FixedUpdate()
    {
        if (!Stats.IsActive)
            return;
        Stats.RecoverTick();
        Stats.CheckDeath();
        Vision.Refresh();
        _stateMachine.Tick(Time.fixedDeltaTime);
        if (_skillRunner != null) _skillRunner.Tick(Time.fixedDeltaTime);
    }
```

5. `Die()` L166：`entityAM.TrySetState(EntityState.Die, false);` → `_stateMachine.TrySetState(EntityState.Die, false);`
6. `Initialize()` L250：`entityAM.TrySetState(EntityState.Start, false);` → `_stateMachine.TrySetState(EntityState.Start, false);`
7. `Dormancy()` 开头（L271 `Movement.ResistList.Clear();` 之前）加：`_stateMachine.ResetForPool();`
8. `ArriveEnd()`（Task 3 加的）改走状态机：

```csharp
    /// <summary>到达终点退场：转 Default + 淡出 + 回池（原 MoveBase.ArriveEnd 的表现部分上收）。</summary>
    public void ArriveEnd()
    {
        _stateMachine.TrySetState(EntityState.Default, true);
        visuals.FadeOut(0.2f, () => thisEntityPool.Return(this));
    }
```

- [ ] **Step 4.3: MoveBase / JumpMove 迁移**

`MoveBase.cs`（`_thisAM` 字段保留——JumpMove 的时长查询与分支设置仍用）：
- 被阻挡处（L105-108）：

```csharp
                        if (_thisEntity.StateMachine.CurrentState == EntityState.Move)
                        {
                            _thisEntity.StateMachine.TrySetState(EntityState.Idle, true);
                        }
```

- `Move()`（L239-243）：

```csharp
            if (_thisEntity.StateMachine.CurrentState != EntityState.Move && _thisEntity.Movement.ResistList.Count == 0)
            {
                _thisAM.SetMoveBranch(MoveAnimationBranch.Normal);
                _thisEntity.StateMachine.TrySetState(EntityState.Move, false);
            }
            if (_thisEntity.StateMachine.CurrentState == EntityState.Move)
```

- 强制停步处（L252-255）：

```csharp
                    if (_forceUnmoveTime > 0)
                    {
                        _thisEntity.StateMachine.TrySetState(EntityState.Idle, true);
                    }
```

`JumpMove.cs` `MovePosition`（L26-32）：

```csharp
            if (_thisEntity.StateMachine.CurrentState != EntityState.Move && _thisEntity.Movement.ResistList.Count == 0)
            {
                _thisAM.SetMoveBranch(MoveAnimationBranch.JumpBegin);
                if (_thisEntity.StateMachine.TrySetState(EntityState.Move, false))
                {
                    Jump(this.transform.position, _currentSection[_currentPointSerial].targetPosition);
                }
            }
```

`Jump` 内三处（L51、L61、L76）：

```csharp
        _thisAM.SetMoveBranch(MoveAnimationBranch.JumpLoop);
        _thisEntity.StateMachine.TrySetState(EntityState.Move, true);
// ...
        _thisAM.SetMoveBranch(MoveAnimationBranch.JumpEnd);
        _thisEntity.StateMachine.TrySetState(EntityState.Move, true);
// ...
        _thisEntity.StateMachine.TrySetState(EntityState.Idle, true);
```

三处 `ResolveAnimationDuration` 调用（L46-48）不变。

- [ ] **Step 4.4: 攻击四脚本迁移**

`AttackBase.cs` `FixedUpdate` 攻击门禁（L132，加 Cast——演出中不攻击，等价旧后门把 currentState 劫持为 Start 后被门禁挡住的效果）：

```csharp
        else if (_thisEntity.StateMachine.CurrentState != EntityState.Start && _thisEntity.StateMachine.CurrentState != EntityState.Cast && _thisEntity.StateMachine.CurrentState != EntityState.Die && TryToAttack(AttackTargetSelect(_thisEntity.Stats.AttackNumS, _thisEntity.Stats.AttackMinNumS), false, true))
```

`AttackBase.cs` `AttackByAnimation` 中断分支（L187）：

```csharp
                    _thisEntity.StateMachine.TrySetState(EntityState.Idle, true);
```

`NormalAttack.cs` `TryToAttack`（L3-17）：

```csharp
    public override bool TryToAttack(Entity[] attackTargets, bool forceChange, bool canBeInterrupt)
    {
        if (attackTargets.Length > 0 || canBeInterrupt == false)
        {
            _thisEntity.entityAM.SetAttackBranch(AttackAnimationBranch.Normal);
            if (_thisEntity.StateMachine.TrySetAttackState(
                forceChange,
                () => { AttackByAnimation(attackTargets, canBeInterrupt); }))
            {
                base.TryToAttack(attackTargets, forceChange, canBeInterrupt);
                return true;
            }
        }
        return false;
    }
```

`ChargeAttack.cs` `TryToAttack`（L76-100；蓄力失败回设 Normal，替代旧 TrySetAttackState 的原子分支回退）：

```csharp
    public override bool TryToAttack(Entity[] attackTargets, bool forceChange, bool canBeInterrupt)
    {
        if (attackTargets.Length > 0)
        {
            _thisEntity.entityAM.SetAttackBranch(AttackAnimationBranch.Normal);
            if (_thisEntity.StateMachine.TrySetAttackState(
                forceChange,
                () => { AttackByAnimation(attackTargets, canBeInterrupt); }))
            {
                base.TryToAttack(attackTargets, forceChange, canBeInterrupt);
                return true;
            }
        }
        else if (CanStoreCharge())
        {
            _thisEntity.entityAM.SetAttackBranch(AttackAnimationBranch.Charge);
            if (_thisEntity.StateMachine.TrySetAttackState(
                forceChange,
                () => { SpawnChargeEffect(); }))
            {
                return true;
            }
            _thisEntity.entityAM.SetAttackBranch(AttackAnimationBranch.Normal);
        }
        return false;
    }
```

`WitherAttack.cs` `TryToAttack` 内层（L35-47；前段目标收集 L16-34 不动）：

```csharp
        if (attackTargets.Length > 0)
        {
            _thisEntity.entityAM.SetAttackBranch(AttackAnimationBranch.Normal);
            if (_thisEntity.StateMachine.TrySetAttackState(
                forceChange,
                () => { AttackByAnimation(attackTargets, canBeInterrupt); }))
            {
                _currentNum = 0;
                base.TryToAttack(attackTargets, forceChange, canBeInterrupt);
                return true;
            }
        }
        return false;
```

- [ ] **Step 4.5: BuffController 迁移**

`BuffController.cs` 六处状态调用改走 SM（L193-196、208-209、220-223、255、259、263），模式 `entityAM.X` → `StateMachine.X`。以 case0 为例：

```csharp
                    _thisEntity.StateMachine.AddStateToBan(new[] { EntityState.Move });
                    if (_thisEntity.StateMachine.CurrentState == EntityState.Move)
                    {
                        _thisEntity.StateMachine.TrySetState(EntityState.Idle, true);
                    }
```

六个替换点清单（其余照此模式）：
- case0 添加：ban `{Move}` + 条件强制 Idle（如上）
- case1 添加：ban `{Move, Attack}` + 无条件 `TrySetState(Idle, true)`
- case2 添加：ban `{Attack}` + 条件（`CurrentState == Attack`）强制 Idle
- case0 解禁：`RemoveStateFromBan(new[] { EntityState.Move })`
- case1 解禁：`RemoveStateFromBan(new[] { EntityState.Move, EntityState.Attack })`
- case2 解禁：`RemoveStateFromBan(new[] { EntityState.Attack })`

（case3 无敌走 Stats.AddSelectable/AddHurtable，无状态交互，不动。）

- [ ] **Step 4.6: SharedTargetExtraAttack / BeefSkill 迁移**

`SharedTargetExtraAttack.cs`（L74-75；保留 `entityAM == null` 的 headless 判空）：

```csharp
            if (_isExtraAttack || ctx.entity.entityAM == null
                || ctx.entity.StateMachine.CurrentState == EntityState.Start)
                return;
```

`BeefSkill.cs` `SkillBegin`（L13-14）：

```csharp
        AnimationOverrideHandle handle = _thisEntity.entityAM.AddOneShotOverride(this, new AnimationOverride { [AnimationSlot.Idle] = _skill });
        if (!_thisEntity.StateMachine.TrySetState(EntityState.Idle, true))
```

（其余行不变——失败即 `RemoveOverride(handle)` 的既有注释与逻辑保留。）

- [ ] **Step 4.7: 编译与全量测试**

运行测试命令。预期：编译零错误、全部 EditMode 测试通过。编译错误集中在漏迁移的调用点——逐一按上述模式迁移，直到全局搜索为 0 处：

```bash
cd "e:/Unity/projects/TD" && grep -rn "entityAM.TrySetState\|entityAM.CurrentState\|entityAM.AddStateToBan\|entityAM.RemoveStateFromBan\|entityAM.TrySetMoveState\|entityAM.TrySetAttackState" Assets --include="*.cs"; echo "grep_exit=$?"
```

- [ ] **Step 4.8: Commit**

```bash
cd "e:/Unity/projects/TD" && git add -A Assets && git commit -m "重构：状态机上线——AnimationMachine改造为表现层（订阅StateChanged+时机上报），回收链归Entity，全部逻辑调用方迁移至EntityStateMachine

Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 5: Die 后门技能重写（Cast 通道）

三个技能按意图重写（考古结论见全局约定）：Cast 播真实演出动画（非旧机制的 SO Die 槽资产），粘性保持末帧，显式转出。只有 HeadSeter 在数据管线内（PlayMode 可验）；Wither/Wdslm 为编译级验证。

**Files:**
- Modify: `Assets/Resources/Prefabs/Monsters/MC/HeadSeter/Scripts/HeadSeterSkill1.cs`
- Modify: `Assets/Resources/Prefabs/Monsters/MC/Wither/Scripts/WitherTalent2.cs`
- Modify: `Assets/Resources/Prefabs/Monsters/Origin/Wdslm/Scripts/Wdslm/WdslmSkill3.cs`

- [ ] **Step 5.1: HeadSeterSkill1（钻地）改 Cast**

`SkillBegin`（L21-37）与 `SkillEnd`（L38-53）替换为（`PreWarm`/`Initialize`/`FlashMove`/`IsDie` 不动；文件头加 `using Cysharp.Threading.Tasks;`）：

```csharp
    public override bool SkillBegin()
    {
        if (_thisEntity.StateMachine.CurrentState >= EntityState.Attack || _thisEntity.Movement.ResistList.Count == 0 || !base.SkillBegin())
            return false;
        am.AddOverride(this, new AnimationOverride { [AnimationSlot.Cast] = IsDie ? _begin_d : _begin });
        _thisEntity.StateMachine.TrySetState(EntityState.Cast, true);
        _thisEntity.buffController.AddAbnormalState(-10, 0);
        _thisEntity.buffController.AddAbnormalState(-10, 3);
        return true;
    }
    public override void SkillEnd()
    {
        base.SkillEnd();
        am.AddOverride(this, new AnimationOverride { [AnimationSlot.Cast] = IsDie ? _end_d : _end });
        _thisEntity.StateMachine.TrySetState(EntityState.Cast, true);
        _thisEntity.buffController.TryRemoveAbnormalState(0);
        _thisEntity.buffController.TryRemoveAbnormalState(3);
        FlashMove(_moveDis);
        EmergeResume();
    }
    // 钻地/钻出演出：覆盖 Cast 槽并转入 Cast（粘性，播完保持末帧=潜伏姿态）。
    // 旧 Die 后门播的是 SO Die 槽资产且钻出后无恢复通路；此处播真实技能动画（Skill_Begin/Skill_End，
    // headSeter.asset 已登记）并由钻出动画播完后显式回 Idle 恢复行走。
    private async void EmergeResume()
    {
        await UniTask.WaitForSeconds(am.ResolveNamedAnimationDuration(IsDie ? _end_d : _end), false, PlayerLoopTiming.Update, LevelResourceSharing.LevelCtk);
        _thisEntity.StateMachine.TrySetState(EntityState.Idle, true);
    }
```

要点：① 演出动画用**持久覆盖**（旧代码同样从不移除，靠池 Dormancy 清）；钻出覆盖 id 更高、解析时排在后=生效。② `SkillBegin` 的忙碌守卫 `>= EntityState.Attack` 保留（新枚举序下 Cast/Die 一并被拒）。③ SkillEnd 无条件 `TrySetState(Cast, true)`——旧的非强制 `TrySetState(Die, false)` 在 Cast→Cast 下必失败，force 是新机制下的正确形态。④ 钻出后 `EmergeResume` 等动画播完强制回 Idle（MoveBase 下一帧自然接管 Move）——旧机制无此通路，属按意图补全。

- [ ] **Step 5.2: WitherTalent2（凋灵复活）终局改 Cast + 存活**

`EnterRecoverMode` 的 `DOTween...OnComplete` 回调（L31-83）替换为（`Initialize` 与 `EnterRecoverMode` 前半段不动，仅 L26 的 `TrySetState(Idle, true)` 改 `_thisEntity.StateMachine.TrySetState(EntityState.Idle, true);`、L15/L27 的初始化器已在 Task 2 换过）：

```csharp
        DOTween.To((value) =>
        {
            _thisEntity.Stats.CurrentHpRate = value;
        }, 0.001f, 1, _recoverTime).OnComplete(async () =>
        {
            // 终局演出：Cast 播 _start2，播完接爆炸，随后 HpCheck 狂暴 + 显式回 Idle（存活，恢复正常行为）
            _thisEntity.entityAM.RemoveOverrides(this);
            _thisEntity.entityAM.AddOneShotOverride(this, new AnimationOverride { [AnimationSlot.Cast] = _start2 });
            _thisEntity.StateMachine.TrySetState(EntityState.Cast, true);
            _boomEffect.SetActive(true);
            _recoverEffect.SetActive(false);
            await UniTask.WaitForSeconds(_thisEntity.entityAM.ResolveNamedAnimationDuration(_start2) * 0.7f, false, PlayerLoopTiming.Update, LevelResourceSharing.LevelCtk);
            List<Entity> targets = EntityManager.Manager.EntitySelector_Radius((_thisEntity.Movement.Position.x, _thisEntity.Movement.Position.y), 2, false, _boomRadius, false);
            targets.AddRange(EntityManager.Manager.EntitySelector_Radius((_thisEntity.Movement.Position.x, _thisEntity.Movement.Position.y), 1, false, _boomRadius, false));
            float eneityR = EntityManager.EntityR;
            for (int i = 0; i < targets.Count; i++)
            {
                float r = Vector2.Distance(targets[i].Movement.Position, _thisEntity.Movement.Position);
                if (r <= eneityR)
                {
                    targets[i].Stats.ApplyDamage(_thisEntity, _thisEntity.Stats.AttackS, 5, 0, 0, 0, 0, 0, 1);
                    if (targets[i].MoveBase)
                    {
                        targets[i].MoveBase.TryToAddImpulse((targets[i].Movement.Position - _thisEntity.Movement.Position).normalized, 5);
                    }
                }
                else if (r <= 2 * eneityR)
                {
                    targets[i].Stats.ApplyDamage(_thisEntity, _thisEntity.Stats.AttackS, 4, 0, 0, 0, 0, 0, 1);
                    if (targets[i].MoveBase)
                    {
                        targets[i].MoveBase.TryToAddImpulse((targets[i].Movement.Position - _thisEntity.Movement.Position).normalized, 4);
                    }
                }
                else if (r <= 1.414 + eneityR)
                {
                    targets[i].Stats.ApplyDamage(_thisEntity, _thisEntity.Stats.AttackS, 3, 0, 0, 0, 0, 0, 1);
                    if (targets[i].MoveBase)
                    {
                        targets[i].MoveBase.TryToAddImpulse((targets[i].Movement.Position - _thisEntity.Movement.Position).normalized, 3);
                    }
                }
                else
                {
                    targets[i].Stats.ApplyDamage(_thisEntity, _thisEntity.Stats.AttackS, 2, 0, 0, 0, 0, 0, 1);
                    if (targets[i].MoveBase)
                    {
                        targets[i].MoveBase.TryToAddImpulse((targets[i].Movement.Position - _thisEntity.Movement.Position).normalized, 2);
                    }
                }
            }
            await UniTask.WaitForSeconds(_thisEntity.entityAM.ResolveNamedAnimationDuration(_start2) * 0.3f, false, PlayerLoopTiming.Update, LevelResourceSharing.LevelCtk);
            _boomEffect.SetActive(false);
            _thisEntity.buffController.TryRemoveAbnormalState(3);
            _thisEntity.buffController.TryRemoveAbnormalState(0);
            _thisEntity.buffController.TryRemoveAbnormalState(2);
            _thisEntity.GetComponent<WitherTalent3>().HpCheck();
            _thisEntity.StateMachine.TrySetState(EntityState.Idle, true);
        });
```

要点：① `RemoveOverrides(this)` 必须在 `AddOneShotOverride` **之前**（同 owner，后清会连自己的 one-shot 一起删掉；也顺带清掉复活期的 Idle 覆盖）。② 爆炸分层的阈值/伤害/冲量逐行保留原值。③ `WaitForSeconds` 补全 `LevelCtk` 取消参数（对齐项目 async 惯例，WdslmSkill3 同款）。④ 旧代码的 `TrySetState(Die, true)`（L33）被 Cast 演出取代；凋灵**存活**（HpCheck=狂暴护盾监视器，等 HP<50% 给增益），不再有隐式回池。

- [ ] **Step 5.3: WdslmSkill3（变身）改 Cast + 显式退场**

`SkillBegin`（L18-35）：

```csharp
    public override bool SkillBegin()
    {
        if (TargetEntity == null)
            return false;
        if (!base.SkillBegin())
            return false;
        _thisEntity.buffController.AddAbnormalState(-10, 3);
        _thisEntity.buffController.AddAbnormalState(-10, 2);
        _thisEntity.buffController.AddAbnormalState(-10, 0);
        _thisEntity.entityAM.AddOverride(this, new AnimationOverride
        {
            [AnimationSlot.Idle] = _skillLoop,
        });
        _thisEntity.entityAM.AddOneShotOverride(this, new AnimationOverride
        {
            [AnimationSlot.Cast] = _skillStart,
        });
        _thisEntity.StateMachine.TrySetState(EntityState.Cast, true);
        SummonMachine();
        return true;
    }
```

`SummonMachine`（L36-43，末尾加显式回 Idle——粘性 Cast 下变身循环 _skillLoop 由 Idle 态接管）：

```csharp
    private async void SummonMachine()
    {
        await UniTask.WaitForSeconds(_thisEntity.entityAM.ResolveNamedAnimationDuration(_skillStart), false, PlayerLoopTiming.Update, LevelResourceSharing.LevelCtk);
        _machine = EntityManager.Manager.SetMovableEntity(FlyMachineID, _thisEntity.Movement.Position, _thisEntity.Camp, _thisEntity.MoveBase.CurrentPathSerial);
        _machine.MoveBase.SetMoveParameters(_thisEntity.MoveBase.CurrentPathSerial, _thisEntity.MoveBase.CurrentSectionSerial, _thisEntity.MoveBase.CurrentPointSerial);
        _machine.buffController.AddAbnormalState(-10, 3);
        _machine.GetComponent<MachineTalent1>().ProjectEntity(TargetEntity, this);
        _thisEntity.StateMachine.TrySetState(EntityState.Idle, true);
    }
```

`SkillEnd`（L45-60）：

```csharp
    public override void SkillEnd()
    {
        base.SkillEnd();
        _machine.Die();
        if (TargetEntity.Stats.IsActive)
            TargetEntity.Die();
        TargetEntity = null;
        _thisEntity.entityAM.RemoveOverrides(this);
        _thisEntity.entityAM.AddOneShotOverride(this, new AnimationOverride
        {
            [AnimationSlot.Cast] = _skillEnd,
        });
        _thisEntity.StateMachine.TrySetState(EntityState.Cast, true);
        RetireAfterSkillEnd();
        _thisEntity.buffController.TryRemoveAbnormalState(0);
        _thisEntity.buffController.TryRemoveAbnormalState(2);
        _thisEntity.buffController.TryRemoveAbnormalState(3);
        _skill2.SkilllRecoverForbid(false);
        SkilllRecoverForbid(true);
    }

    // 结束演出播完后显式退场（Default+淡出+回池，原借 Die 淡出链的效果）
    private async void RetireAfterSkillEnd()
    {
        await UniTask.WaitForSeconds(_thisEntity.entityAM.ResolveNamedAnimationDuration(_skillEnd), false, PlayerLoopTiming.Update, LevelResourceSharing.LevelCtk);
        _thisEntity.ArriveEnd();
    }
```

- [ ] **Step 5.4: 编译 + 全量测试**

运行测试命令。预期：编译零错误、测试全绿。验证旧后门清零：

```bash
cd "e:/Unity/projects/TD" && grep -rn "TrySetState(EntityState.Die" Assets --include="*.cs"; echo "grep_exit=$?"
```

预期仅 `Entity.Die()` 内一处（正规死亡）。

- [ ] **Step 5.5: Commit**

```bash
cd "e:/Unity/projects/TD" && git add -A Assets && git commit -m "重构：技能演出走Cast通道——HeadSeterSkill1钻地/WitherTalent2终局存活/WdslmSkill3变身重写，Die回归纯死亡语义

Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 6: 收尾验证与文档

**Files:**
- Modify: 记忆文件（`C:\Users\NING\.claude\projects\e--Unity-projects-TD\memory\animation-machine-coupling-audit.md`、`MEMORY.md`、`project-subsystem-map.md` 若提及 AM 职责）

- [ ] **Step 6.1: 残留扫描**

```bash
cd "e:/Unity/projects/TD" && grep -rn "entityAM.TrySetState\|entityAM.CurrentState\|entityAM.AddStateToBan\|entityAM.RemoveStateFromBan\|entityAM.TrySetMoveState\|entityAM.TrySetAttackState\|entityAM.SetDirection\|entityAM.ArriveEnd\|entityAM.CurrentDirection\|ClassifyAnimation\|states_ban" Assets --include="*.cs"; echo "grep_exit=$?"
```

预期：0 处输出（exit=1）。

- [ ] **Step 6.2: 全量 EditMode 测试最终跑**

运行全局约定的测试命令。预期 exit=0。

- [ ] **Step 6.3: 更新记忆**

`animation-machine-coupling-audit.md` 末尾追加状态行（重构完成日期、四组件落点：EntityStateMachine/AnimationMachine(presenter)/EntityVisuals/EntityFacing），`MEMORY.md` 对应行同步；`project-subsystem-map.md` 若描述了 AnimationMachine 旧职责则更新为新的四件套划分。

- [ ] **Step 6.4: 交用户 PlayMode 验收**

验收清单（告知用户）：
1. 干员部署（Start 演出→Idle）、移动/停止、攻击（含弹道方向翻转）、受击闪红、死亡淡出回池；
2. 已接入管线的 6 种怪物（slime/creeper/headSeter/skeleton/witch/zombie）同上 + 拦截（Move→Idle 阻挡）；
3. HeadSeter 钻地技能：Skill_Begin 播出并保持潜伏姿态 → Skill_End 钻出 → 恢复行走；
4. 高攻速干员连击缩放正常（de3f195 不回归）；
5. 怪物到达终点漏怪扣血 + 淡出退场。

- [ ] **Step 6.5: 最终 Commit（如有代码改动）**

```bash
cd "e:/Unity/projects/TD" && git add -A Assets && git commit -m "重构收尾：动画状态机重构完成——残留扫描清零+全量测试通过

Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

（若 Step 6.1/6.2 无代码改动则跳过本提交；记忆文件在仓库外不入库。）

---

## Self-Review 记录（v2，源码核实后）

- **源码核实范围**：AnimationMachine.cs 全文（两段）、Entity.cs 全文、EntityPoolManager.cs、NormalAttack/ChargeAttack/WitherAttack 全文、HeadSeterSkill1/WitherTalent2/WitherTalent3/WdslmSkill3/BeefSkill 全文、SharedTargetExtraAttack L60-104、BuffController L160-280、AttackBase L115-199、MoveBase L95-124+L228-282、JumpMove 全文、headSeter.asset。计划中所有"替换后代码"均以真实文件为基准。
- **v1→v2 关键修正**：① Cast 改粘性（SM 删 `NotifyStateAnimationCompleted`，新增 `NotifyStartAnimationCompleted`；AM 无 Cast 播完上报）——旧机制靠 Die 态"不接续"保持潜伏末帧，自动回 Idle 会破坏演出；② WitherTalent2 终局改存活（HpCheck=狂暴监视器，非死亡判定），删 v1 错误的 `_thisEntity.Die()` 尾调用；③ HeadSeterSkill1 保留 `am` 字段、新增 EmergeResume（旧代码钻出后无恢复行走通路，属机制残缺，按意图补全）；④ WdslmSkill3 SummonMachine 末尾显式回 Idle 接管 _skillLoop 循环；⑤ AttackBase 门禁加 Cast（等价旧分类劫持为 Start 的防攻击效果）；⑥ BeefSkill 真实路径 `Devices/Meats/`；⑦ 旧 Die 后门机制考古结论（SetState(Die) 播 SO Die 槽资产 + Start 覆盖劫持 ClassifyAnimation 顺序）写入全局约定。
- **Spec 覆盖**：spec §3/§4 → Task 1/4；§4.3 的 Begin 相位留在表现层（spec 已同步修订）；§4.4 API → Task 1（NotifyStartAnimationCompleted 为 spec 修订后签名）；§5/§5.1/§5.2 → Task 2/4；§6 → Task 3/4；§7 重对齐表 → Task 5（spec 已同步修订：WitherTalent2 存活、WdslmSkill3 落地回 Idle、Cast 粘性）；§8 映射 → Task 2/3/4；§9 测试 → Task 1/2/6。无缺口。
- **类型一致性**：`EntityStateMachine` 全部公共成员（TrySetState/TrySetAttackState/CanTransition/PriorityOf/AddStateToBan/RemoveStateFromBan/CurrentState/CurrentAttackPhase/AttackComboIndex/Tick/ResetForPool/NotifyStartAnimationCompleted/NotifyAttackActiveCompleted/NotifyAttackEndCompleted/NotifyAttackFrame/NotifyDieAnimationCompleted + StateChanged/AttackStarted/AttackPhaseChanged/DieAnimationCompleted）在 Task 4/5 的引用与 Task 1 定义一致；`AnimationSet`（From/GetSingle/GetGroup/SetSingle/SetGroup/Copy/Apply×2/EnumerateSingles/EnumerateGroups/IsGroup/MoveBranchSlot/GroupSlots）在 Task 2/4 一致；`AnimationOverride`（索引器/Clear/GetCoveredSlots/Entries/ClearedSlots）在 Task 2/4/5 一致；`Entity.StateMachine/visuals/facing/ArriveEnd()` 在 Task 3/4/5 一致；AM 公共 API（SetMoveBranch/SetAttackBranch/ResolveAnimationDuration/ResolveNamedAnimationDuration/AddOverride/AddOneShotOverride/RemoveOverride/RemoveOverrides/OnAttackAnimationBegin）在 Task 4/5 一致。
- **已知取舍**（非占位符）：Task 2 的 AM 属中间态（字典 API + 旧编排），Task 4 整文件重写覆盖；HeadSeterTalent1 三处初始化器的逐项字段→索引器转换以原文件字段清单为准（机械替换模式已给出示例两处）；JumpMove 的 `UniTask.WaitForSeconds` 旧无参调用保持原样（核心等价，不顺带加取消参数），WitherTalent2/WdslmSkill3 的新写代码统一带 `LevelCtk`（对齐 WdslmSkill3 既有惯例）。
