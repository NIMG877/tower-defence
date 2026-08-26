using System;
using System.Collections.Generic;
using UnityEngine;

namespace AbilitySystem.Components
{
    /// <summary>
    /// 对目标列表施加一次伤害。目标来源 <c>targetMode</c>：eventTarget（默认）/
    /// blackboard/self。伤害来源 <c>attackerMode</c>：self（默认，ctx.entity）/
    /// summoner（读生成物黑板固定协议 key <see cref="SpawnEntity.SummonerKey"/>
    /// 的召唤者引用）。
    ///
    /// <para>脱离执行（ctx.entity==null）支持：目标与来源都从 fork 克隆的黑板取，
    /// 伤害基值须用 fixed（baseValue 支持从黑板读）；attack 基值读伤害来源的实时
    /// 攻击（attacker.Stats.AttackS），无来源即配置错误，报错跳过。attacker 为
    /// null（self 模式 + 脱离执行）且基值 fixed 时按"无主伤害"放行——伤害管线
    /// 全链（OnBeforeHurt/OnAfterHurt/DamageResolved 消费方）不解引用 origin。</para>
    /// </summary>
    [RegisterComponent("ApplyDamage")]
    public class ApplyDamage : AbilityComponentBase
    {
        private Func<string> _targetMode;
        private Func<string> _blackboardKey;
        private Func<string> _attackerMode;
        private Func<string> _baseValueMode;
        private Func<float> _baseValue;
        private Func<float> _multiplier;
        private Func<float> _defPenetrate;
        private Func<float> _mgrPenetrate;
        private Func<float> _defPenetrateValue;
        private Func<float> _mgrPenetrateValue;
        private Func<int> _damageType;
        private Func<int> _applyType;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            Blackboard bb = ctx.sharedBlackboard;
            _targetMode = p.GetStringLazy("targetMode", "eventTarget", bb);
            _blackboardKey = p.GetStringLazy("blackboardKey", "", bb);
            _attackerMode = p.GetStringLazy("attackerMode", "self", bb);
            _baseValueMode = p.GetStringLazy("baseValueMode", "attack", bb);
            _baseValue = p.GetFloatLazy("baseValue", 0f, bb);
            _multiplier = p.GetFloatLazy("multiplier", 1f, bb);
            _defPenetrate = p.GetFloatLazy("defPenetrate", 0f, bb);
            _mgrPenetrate = p.GetFloatLazy("mgrPenetrate", 0f, bb);
            _defPenetrateValue = p.GetFloatLazy("defPenetrateValue", 0f, bb);
            _mgrPenetrateValue = p.GetFloatLazy("mgrPenetrateValue", 0f, bb);
            _damageType = p.GetIntLazy("damageType", 0, bb);
            _applyType = p.GetIntLazy("applyType", 2, bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (!TryResolveAttacker(ctx, out Entity attacker)) return;

            List<Entity> targets = ResolveTargets(ctx);
            float damage;
            if (Normalize(_baseValueMode()) == "fixed")
            {
                damage = _baseValue();
            }
            else
            {
                if (attacker == null)
                {
                    Debug.LogError("[ApplyDamage] baseValueMode 'attack' has no attacker entity (detached execution without attackerMode=blackboard); skipping.");
                    return;
                }
                damage = attacker.Stats.AttackS;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                Entity target = targets[i];
                if (target == null) continue;
                target.Stats.ApplyDamage(
                    attacker,
                    damage,
                    _multiplier(),
                    _defPenetrate(),
                    _mgrPenetrate(),
                    _defPenetrateValue(),
                    _mgrPenetrateValue(),
                    _damageType(),
                    _applyType());
            }
        }

        /// <summary>伤害来源实体。summoner 模式读黑板固定协议 key
        /// <see cref="SpawnEntity.SummonerKey"/>（取不到=配置错误，报错跳过）；
        /// self 模式取 ctx.entity——脱离执行为 null，此时仅 fixed 基值的
        /// "无主伤害"合法。</summary>
        private bool TryResolveAttacker(AbilityContext ctx, out Entity attacker)
        {
            switch (Normalize(_attackerMode()))
            {
                case "summoner":
                    attacker = ctx.sharedBlackboard != null
                        ? ctx.sharedBlackboard.Get<Entity>(SpawnEntity.SummonerKey, null)
                        : null;
                    if (attacker == null)
                    {
                        Debug.LogError($"[ApplyDamage] attackerMode 'summoner' found no entity at blackboard key '{SpawnEntity.SummonerKey}'; skipping.");
                        return false;
                    }
                    return true;
                default:
                    attacker = ctx.entity;
                    return true;
            }
        }

        private List<Entity> ResolveTargets(AbilityContext ctx)
        {
            switch (Normalize(_targetMode()))
            {
                case "self":
                    return new List<Entity> { ctx.entity };
                case "blackboard":
                case "blackboardentities":
                    string key = _blackboardKey();
                    return string.IsNullOrEmpty(key) || ctx.sharedBlackboard == null
                        ? new List<Entity>()
                        : ctx.sharedBlackboard.Get<List<Entity>>(key, null) ?? new List<Entity>();
                default:
                    Entity eventTarget = ctx.currentEvent is DamageEventBase damageEvent ? damageEvent.target : null;
                    return eventTarget == null ? new List<Entity>() : new List<Entity> { eventTarget };
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Trim().ToLowerInvariant();
        }
    }
}
