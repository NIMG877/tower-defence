using System.Collections.Generic;
using AbilitySystem.Components;
using NUnit.Framework;
using UnityEngine;

namespace AbilitySystem.Tests
{
    /// <summary>SetEntityState（切目标实体状态机状态）的 EditMode 契约测试。
    /// 状态机本身的优先级/ban/连击语义已由 EntityStateMachineTests 覆盖，这里只测
    /// 组件契约：参数解析（非法状态名告警跳过）、目标解析三通道（blackboardKey
    /// 优先/toSelf/皆无静默）、切换失败静默、多目标独立切换。</summary>
    public class SetEntityStateTests
    {
        private readonly List<GameObject> _scratch = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _scratch.Count; i++)
            {
                Object.DestroyImmediate(_scratch[i]);
            }
            _scratch.Clear();
        }

        [Test]
        public void ToSelf_NonForce_HigherPriorityState_Applies()
        {
            Entity e = NewEntity();
            var ctx = Ctx(e);

            var comp = new SetEntityState();
            comp.OnInit(ctx, Params(("state", "Cast")));
            comp.OnTrigger(ctx);

            Assert.That(e.StateMachine.CurrentState, Is.EqualTo(EntityState.Cast));
        }

        [Test]
        public void UnknownStateName_DoesNotSwitch()
        {
            Entity e = NewEntity();
            e.StateMachine.TrySetState(EntityState.Cast, true);
            var ctx = Ctx(e);

            var comp = new SetEntityState();
            comp.OnInit(ctx, Params(("state", "Fly")));
            comp.OnTrigger(ctx);

            Assert.That(e.StateMachine.CurrentState, Is.EqualTo(EntityState.Cast), "非法状态名告警跳过，状态不变");
        }

        [Test]
        public void EmptyStateName_DoesNotSwitch()
        {
            Entity e = NewEntity();
            var ctx = Ctx(e);

            var comp = new SetEntityState();
            comp.OnInit(ctx, Params()); // state 未配置 → 空串 parse 失败 → 告警跳过
            comp.OnTrigger(ctx);

            Assert.That(e.StateMachine.CurrentState, Is.EqualTo(EntityState.Default));
        }

        [Test]
        public void BlackboardKey_TakesPrecedence_OverToSelf()
        {
            Entity self = NewEntity(), other = NewEntity();
            var bb = new Blackboard();
            bb.Set("victims", new List<Entity> { other });
            var ctx = new AbilityContext { sharedBlackboard = bb, entity = self };

            var comp = new SetEntityState();
            comp.OnInit(ctx, Params(("state", "Cast"), ("blackboardKey", "victims"), ("toSelf", "true")));
            comp.OnTrigger(ctx);

            Assert.That(other.StateMachine.CurrentState, Is.EqualTo(EntityState.Cast), "黑板目标被切换");
            Assert.That(self.StateMachine.CurrentState, Is.EqualTo(EntityState.Default), "配了 blackboardKey 时不切自己");
        }

        [Test]
        public void BlackboardKey_Missing_SilentlySkips()
        {
            Entity e = NewEntity();
            var bb = new Blackboard();
            var ctx = new AbilityContext { sharedBlackboard = bb, entity = e };

            var comp = new SetEntityState();
            comp.OnInit(ctx, Params(("state", "Cast"), ("blackboardKey", "no_such_key")));
            comp.OnTrigger(ctx);

            Assert.That(e.StateMachine.CurrentState, Is.EqualTo(EntityState.Default), "键缺失（值 null）静默不切");
        }

        [Test]
        public void NoToSelf_NoKey_SilentlySkips()
        {
            Entity e = NewEntity();
            var ctx = Ctx(e);

            var comp = new SetEntityState();
            comp.OnInit(ctx, Params(("state", "Cast"), ("toSelf", "false")));
            comp.OnTrigger(ctx);

            Assert.That(e.StateMachine.CurrentState, Is.EqualTo(EntityState.Default), "toSelf=false 且未配 key：无目标，静默不切");
        }

        [Test]
        public void NonForce_BlockedByPriority_StaysSilent()
        {
            Entity e = NewEntity();
            e.StateMachine.TrySetState(EntityState.Cast, true); // 优先级 5
            var ctx = Ctx(e);

            var comp = new SetEntityState();
            comp.OnInit(ctx, Params(("state", "Idle"))); // 优先级 1 < 5，非强制切不进
            comp.OnTrigger(ctx);

            Assert.That(e.StateMachine.CurrentState, Is.EqualTo(EntityState.Cast), "优先级不够=合法竞争失败，静默保持");
        }

        [Test]
        public void Force_SwitchesToLowerPriorityState()
        {
            Entity e = NewEntity();
            e.StateMachine.TrySetState(EntityState.Cast, true);
            var ctx = Ctx(e);

            var comp = new SetEntityState();
            comp.OnInit(ctx, Params(("state", "Idle"), ("force", "true")));
            comp.OnTrigger(ctx);

            Assert.That(e.StateMachine.CurrentState, Is.EqualTo(EntityState.Idle));
        }

        [Test]
        public void MultipleTargets_SwitchIndependently()
        {
            Entity a = NewEntity(), b = NewEntity();
            b.StateMachine.TrySetState(EntityState.Die, true); // b 已死：force 也切不动
            var bb = new Blackboard();
            bb.Set("victims", new List<Entity> { a, b });
            var ctx = new AbilityContext { sharedBlackboard = bb };

            var comp = new SetEntityState();
            comp.OnInit(ctx, Params(("state", "Cast"), ("force", "true"), ("blackboardKey", "victims")));
            comp.OnTrigger(ctx);

            Assert.That(a.StateMachine.CurrentState, Is.EqualTo(EntityState.Cast), "单个目标失败不影响其余");
            Assert.That(b.StateMachine.CurrentState, Is.EqualTo(EntityState.Die), "Die 是 force 的唯一硬边界");
        }

        private Entity NewEntity()
        {
            var go = new GameObject("set_state_test_entity");
            _scratch.Add(go);
            return go.AddComponent<Entity>();
        }

        private static AbilityContext Ctx(Entity e)
        {
            return new AbilityContext { sharedBlackboard = new Blackboard(), entity = e };
        }

        private static ParamList Params(params (string key, string value)[] kv)
        {
            var entries = new ParamEntry[kv.Length];
            for (int i = 0; i < kv.Length; i++)
            {
                entries[i] = new ParamEntry
                {
                    key = kv[i].key,
                    value = kv[i].value,
                    fromBlackboard = false,
                    type = ParamValueType.String,
                };
            }
            return new ParamList { entries = entries };
        }
    }
}
