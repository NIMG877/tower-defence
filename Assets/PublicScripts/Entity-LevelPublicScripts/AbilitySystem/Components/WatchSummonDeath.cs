using System;
using System.Collections.Generic;
using UnityEngine;

namespace AbilitySystem.Components
{
    /// <summary>
    /// 订阅黑板实体列表中各实体的死亡（<c>OnBeforeDieAnimation</c>），把"召唤物死了"
    /// 桥接成宿主侧的 <see cref="SummonDeathEvent"/> 派发。实体死亡只会在它自己的
    /// runner 上派发事件，宿主（召唤者）的规则本来听不到；本组件补上这条通道，
    /// 供"召唤物死亡触发宿主效果"类能力使用（爆炸、分裂、计数维护等）。
    ///
    /// <para>死亡处理：把死亡实体移出黑板列表、退订，然后携带死亡位置快照在宿主
    /// <c>EntityAbilityRunner</c> 上 <c>DispatchEvent</c>。位置在派发时快照——后续
    /// 步骤若经 <c>delay</c> 延迟，实体可能已被池回收，只能读事件里的快照。</para>
    ///
    /// <para>订阅按列表差量维护：OnTrigger 做一次初始同步，OnTick 每物理帧补差
    /// （新 spawn 进列表的实体下一个 tick 被订阅；被外部移出的实体退订）。
    /// 计数不在此维护——纯算术（write_blackboard add ±1）由规则步骤完成，
    /// 本组件只负责事件桥与列表回收这一件事。</para>
    /// </summary>
    [RegisterComponent("WatchSummonDeath")]
    public class WatchSummonDeath : AbilityComponentBase
    {
        private Func<string> _blackboardKey;
        // 每个被订阅实体持有其 handler 委托：闭包每次构造都是新实例，
        // 退订必须 -= 当初 += 的那一个，否则退订不生效。
        private readonly Dictionary<Entity, Entity.OperationsBeforeDieAnimation> _handlers =
            new Dictionary<Entity, Entity.OperationsBeforeDieAnimation>();
        private Entity _host;
        private Blackboard _board;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            var bb = ctx.sharedBlackboard;
            _blackboardKey = p.GetStringLazy("blackboardKey", "", bb);
            _host = ctx.entity;
            _board = ctx.sharedBlackboard;
            UnsubscribeAll();
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            SyncSubscriptions();
        }

        public override void OnTick(AbilityContext ctx, float dt)
        {
            SyncSubscriptions();
        }

        public override void OnTeardown(AbilityContext ctx)
        {
            UnsubscribeAll();
        }

        private void SyncSubscriptions()
        {
            if (_board == null || _host == null) return;
            string key = _blackboardKey();
            var current = string.IsNullOrEmpty(key)
                ? null
                : _board.Get<List<Entity>>(key, null);
            if (current == null)
            {
                UnsubscribeAll();
                return;
            }

            // 退订已离开列表的实体（死亡退订在 OnWatchedDeath 里已完成，
            // 这里兜住被规则/其它组件从列表移除的情况）。
            List<Entity> departed = null;
            foreach (KeyValuePair<Entity, Entity.OperationsBeforeDieAnimation> pair in _handlers)
            {
                if (pair.Key == null || !current.Contains(pair.Key))
                {
                    (departed ??= new List<Entity>()).Add(pair.Key);
                }
            }
            if (departed != null)
            {
                for (int i = 0; i < departed.Count; i++) Unsubscribe(departed[i]);
            }

            // 订阅新进列表的实体。
            for (int i = 0; i < current.Count; i++)
            {
                Entity entity = current[i];
                if (entity == null || _handlers.ContainsKey(entity)) continue;
                Entity.OperationsBeforeDieAnimation handler = MakeDeathHandler(entity);
                entity.OnBeforeDieAnimation += handler;
                _handlers[entity] = handler;
            }
        }

        private Entity.OperationsBeforeDieAnimation MakeDeathHandler(Entity entity)
        {
            return () => OnWatchedDeath(entity);
        }

        private void OnWatchedDeath(Entity entity)
        {
            Unsubscribe(entity);

            string key = _blackboardKey();
            if (_board != null && !string.IsNullOrEmpty(key))
            {
                var list = _board.Get<List<Entity>>(key, null);
                if (list != null) list.Remove(entity);
            }

            if (_host == null || _host.AbilityRunner == null) return;
            var position = entity.Movement != null
                ? entity.Movement.Position
                : (Vector2)entity.transform.position;
            _host.AbilityRunner.DispatchEvent(new SummonDeathEvent
            {
                target = entity,
                position = position,
            });
        }

        private void Unsubscribe(Entity entity)
        {
            if (entity == null
                || !_handlers.TryGetValue(entity, out Entity.OperationsBeforeDieAnimation handler)) return;
            entity.OnBeforeDieAnimation -= handler;
            _handlers.Remove(entity);
        }

        private void UnsubscribeAll()
        {
            foreach (KeyValuePair<Entity, Entity.OperationsBeforeDieAnimation> pair in _handlers)
            {
                if (pair.Key != null) pair.Key.OnBeforeDieAnimation -= pair.Value;
            }
            _handlers.Clear();
        }
    }
}
