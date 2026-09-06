using System;
using System.Collections.Generic;
using UnityEngine;
namespace AbilitySystem.Components
{
    [RegisterComponent("SharedTargetExtraAttack")]
    public class SharedTargetExtraAttack : AbilityComponentBase
    {
        private Func<string> _queueKey;
        private Func<string> _activeSourceKey;
        private Func<string> _attackAnimation;
        private Func<int> _abnormalType;
        private Func<float> _abnormalTime;
        private bool _isExtraAttack;
        private bool _abnormalApplied;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            Blackboard bb = ctx.sharedBlackboard;
            _queueKey = p.GetStringLazy("queueKey", "shared_attack_requests", bb);
            _activeSourceKey = p.GetStringLazy("activeSourceKey", "shared_attack_source", bb);
            _attackAnimation = p.GetStringLazy("attackAnimation", "", bb);
            _abnormalType = p.GetIntLazy("abnormalType", 0, bb);
            _abnormalTime = p.GetFloatLazy("abnormalTime", -10f, bb);
            _isExtraAttack = false;
            _abnormalApplied = false;
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (ctx.currentEvent is TickEvent)
            {
                ProcessQueue(ctx);
                return;
            }
            if (ctx.currentEvent is AttackSuccessfullyEvent)
            {
                CompleteExtraAttack(ctx);
                return;
            }
            if (ctx.currentEvent is AttackInterruptEvent && _isExtraAttack)
            {
                _isExtraAttack = false;
                Blackboard bb = ctx.sharedBlackboard;
                if (bb == null) return;
                bb.Remove(_activeSourceKey());
                bb.Remove(_queueKey());
                ReleaseAbnormal(ctx.entity);
            }
        }

        public override void OnTeardown(AbilityContext ctx)
        {
            ReleaseAbnormal(ctx.entity);
            _isExtraAttack = false;
            if (ctx.sharedBlackboard == null) return;
            ctx.sharedBlackboard.Remove(_queueKey());
            ctx.sharedBlackboard.Remove(_activeSourceKey());
        }

        private void ProcessQueue(AbilityContext ctx)
        {
            if (ctx.entity == null || ctx.entity.AttackBase == null || ctx.sharedBlackboard == null) return;
            List<SharedAttackRequest> queue = GetQueue(ctx.sharedBlackboard);
            RemoveInvalidFront(queue);
            if (queue.Count == 0)
            {
                ctx.sharedBlackboard.Remove(_queueKey());
                ReleaseAbnormal(ctx.entity);
                return;
            }
            
            ApplyAbnormal(ctx.entity);
            if (_isExtraAttack || ctx.entity.entityAM == null
                || ctx.entity.StateMachine.CurrentState == EntityState.Start)
                return;

            SharedAttackRequest request = queue[0];
            string animation = _attackAnimation();
            var animations = new AnimationOverride
            {
                [AnimationSlot.AttackClose] = animation,
                [AnimationSlot.AttackRemote] = animation,
            };

            _isExtraAttack = true;
            ctx.sharedBlackboard.Set(_activeSourceKey(), request.Sender);
            AnimationOverrideHandle handle = ctx.entity.entityAM.AddOneShotOverride(this, animations);
            if (!ctx.entity.AttackBase.TryToAttack(new[] { request.Target }, false, true))
            {
                // 攻击未启动则当场撤销待用条目，否则下一次自然攻击会播到协作动画。
                ctx.entity.entityAM.RemoveOverride(handle);
                _isExtraAttack = false;
                ctx.sharedBlackboard.Remove(_activeSourceKey());
            }
        }

        private void CompleteExtraAttack(AbilityContext ctx)
        {
            if (!_isExtraAttack || ctx.sharedBlackboard == null) return;
            List<SharedAttackRequest> queue = GetQueue(ctx.sharedBlackboard);
            if (queue.Count > 0) queue.RemoveAt(0);
            _isExtraAttack = false;
            ctx.sharedBlackboard.Remove(_activeSourceKey());
            if (queue.Count == 0)
            {
                ctx.sharedBlackboard.Remove(_queueKey());
                ReleaseAbnormal(ctx.entity);
            }
            else
            {
                ctx.sharedBlackboard.Set(_queueKey(), queue);
            }
        }

        private List<SharedAttackRequest> GetQueue(Blackboard bb)
        {
            string key = _queueKey();
            if (string.IsNullOrEmpty(key)) return new List<SharedAttackRequest>();
            return bb.Get<List<SharedAttackRequest>>(key, null) ?? new List<SharedAttackRequest>();
        }

        private static void RemoveInvalidFront(List<SharedAttackRequest> queue)
        {
            while (queue.Count > 0)
            {
                SharedAttackRequest request = queue[0];
                Entity target = request?.Target;
                if (target != null && target.Stats != null && target.Stats.IsActive
                    && target.Stats.Selectable == 0)
                    return;
                queue.RemoveAt(0);
            }
        }

        private void ApplyAbnormal(Entity entity)
        {
            if (_abnormalApplied || entity?.buffController == null) return;
            int type = _abnormalType();
            if (!BuffController.IsValidAbnormalType(type)) return;
            entity.buffController.AddAbnormalState(_abnormalTime(), type);
            _abnormalApplied = true;
        }

        private void ReleaseAbnormal(Entity entity)
        {
            if (!_abnormalApplied || entity?.buffController == null) return;
            int type = _abnormalType();
            if (BuffController.IsValidAbnormalType(type)) entity.buffController.TryRemoveAbnormalState(type);
            _abnormalApplied = false;
        }
    }
}
