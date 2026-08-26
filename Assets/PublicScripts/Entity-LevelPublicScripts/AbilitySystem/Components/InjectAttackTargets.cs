using System;
using System.Collections.Generic;

namespace AbilitySystem.Components
{
    /// <summary>
    /// 把黑板实体列表注入攻击目标候选的最前（<c>OnBeforeTargetSelect</c>）：先把列表
    /// 内实体从候选中移除，再整体插到索引 0——注入的实体无条件获得最高目标优先级
    /// （含视野外实体），且只作用于被订阅的那个 AttackBase，不影响其它单位。
    /// 与 EntityFilter（只能剔除、不能注入）互补。
    ///
    /// <para>生命周期沿用 EntityFilter 模式：非 OnAbilityEnd 触发按 <c>toggle</c>
    /// 订阅/退订（默认 on=订阅，off=退订）；OnAbilityEnd 触发一律退订；
    /// teardown 防御性退订。注入时实时读黑板列表，列表为空时是无操作。
    /// 黑板引用在 OnInit 捕获（runner 的共享板是稳定实例，重部署只 Clear 不换）。</para>
    /// </summary>
    [RegisterComponent("InjectAttackTargets")]
    public class InjectAttackTargets : AbilityComponentBase
    {
        private Func<string> _toggle;
        private Func<string> _blackboardKey;
        private Func<bool> _toSelf;
        private readonly List<AttackBase> _subscribedAttacks = new List<AttackBase>();
        private Blackboard _board;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            Blackboard bb = ctx.sharedBlackboard;
            _toggle = p.GetStringLazy("toggle", "on", bb);
            _blackboardKey = p.GetStringLazy("blackboardKey", "", bb);
            _toSelf = p.GetBoolLazy("toSelf", true, bb);
            _board = ctx.sharedBlackboard;
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (ctx.currentEvent is AbilityEndEvent)
            {
                UnsubscribeAll();
                return;
            }

            if (Normalize(_toggle()) == "off")
            {
                UnsubscribeAll();
                return;
            }

            List<Entity> targets = ResolveSubscribeTargets(ctx);
            if (targets == null) return;

            for (int i = 0; i < targets.Count; i++)
            {
                AttackBase attack = targets[i]?.AttackBase;
                if (attack == null || _subscribedAttacks.Contains(attack)) continue;
                attack.OnBeforeTargetSelect += InjectTargets;
                _subscribedAttacks.Add(attack);
            }
        }

        public override void OnTeardown(AbilityContext ctx)
        {
            UnsubscribeAll();
        }

        private void InjectTargets(List<Entity> targets, ref int selectMaxNum, ref int selectMinNum, ref bool sameCamp)
        {
            List<Entity> inject = ReadInjectList();
            if (inject == null || inject.Count == 0) return;

            // 先移除候选里已有的列表实体（避免重复），再整体插到最前。
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                if (inject.Contains(targets[i]))
                {
                    targets.RemoveAt(i);
                }
            }
            targets.InsertRange(0, inject);
        }

        private List<Entity> ReadInjectList()
        {
            string key = _blackboardKey();
            if (string.IsNullOrEmpty(key) || _board == null) return null;
            return _board.Get<List<Entity>>(key, null);
        }

        // toSelf=true 订阅宿主自己的目标选择；false 时按黑板列表批量订阅（与
        // EntityFilter/ForceResetAttack 的 ResolveTargets 口径一致）。
        private List<Entity> ResolveSubscribeTargets(AbilityContext ctx)
        {
            if (_toSelf()) return ctx.entity != null ? new List<Entity> { ctx.entity } : null;
            string key = _blackboardKey();
            if (string.IsNullOrEmpty(key) || ctx.sharedBlackboard == null) return null;
            return ctx.sharedBlackboard.Get<List<Entity>>(key, null);
        }

        private void UnsubscribeAll()
        {
            for (int i = 0; i < _subscribedAttacks.Count; i++)
            {
                AttackBase attack = _subscribedAttacks[i];
                if (attack != null) attack.OnBeforeTargetSelect -= InjectTargets;
            }
            _subscribedAttacks.Clear();
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Trim().ToLowerInvariant();
        }
    }
}
