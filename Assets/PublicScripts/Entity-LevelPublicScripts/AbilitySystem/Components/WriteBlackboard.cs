using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace AbilitySystem.Components
{
    /// <summary>
    /// Writes a value to the per-Entity shared Blackboard. Two modes:
    ///
    /// <para><b>"set"</b> (default): writes the value as-is to the configured key. The
    /// value's runtime type is determined by ParamEntry.type (Int/Float/Bool/String/
    /// Vector2Int, plus the 4 Unity asset types which fall through to string). Both
    /// key and value support fromBlackboard=true. Alternative value sources via
    /// <c>source</c>: <c>event</c>/<c>entity</c> paths, or <c>listCount</c> which
    /// writes <c>list.Count × scale</c> (float) of the Blackboard List&lt;Entity&gt;
    /// at <c>path</c>.</para>
    ///
    /// <para><b>"add" / "mult" / "div"</b>: reads the existing value at the key, applies
    /// the operation with the configured value, and writes the result back. Only
    /// supported for numeric existing values (int/float/double); bool/string/Vector2Int
    /// log a warning and skip. Math semantics mirror <see cref="MathOps.Apply{T}"/>.</para>
    /// </summary>
    [RegisterComponent("WriteBlackboard")]
    public class WriteBlackboard : AbilityComponentBase
    {
        private Func<string>  _key;
        private Func<object> _value;
        private Func<string>  _method;
        private Func<string> _source;
        private Func<string> _path;
        private Func<float> _scale;
        private Func<bool> _asString;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            var bb = ctx.sharedBlackboard;
            _key    = p.GetStringLazy("key",    "",    bb);
            _value  = p.GetValueLazy ("value",  null,  bb);
            _method = p.GetStringLazy("method", "set", bb);
            _source = p.GetStringLazy("source", "value", bb);
            _path = p.GetStringLazy("path", "", bb);
            _scale = p.GetFloatLazy("scale", 1f, bb);
            _asString = p.GetBoolLazy("asString", false, bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (ctx.sharedBlackboard == null) return;
            string key = _key();
            if (string.IsNullOrEmpty(key)) return;

            object value = ResolveValue(ctx);
            string method = _method();

            switch (method)
            {
                case "set":
                    if (value != null) ctx.sharedBlackboard.Set(key, value);
                    return;
                case "add":
                case "mult":
                case "div":
                    ApplyOp(ctx.sharedBlackboard, key, value, method);
                    return;
                default:
                    Debug.LogWarning($"WriteBlackboard: unknown method '{method}'; skipping");
                    return;
            }
        }

        private object ResolveValue(AbilityContext ctx)
        {
            object value;
            switch (Normalize(_source()))
            {
                case "event":
                    value = ResolveEventValue(ctx.currentEvent, Normalize(_path()));
                    break;
                case "entity":
                    value = ResolveEntityValue(ctx.entity, Normalize(_path()));
                    break;
                case "listcount":
                    value = ResolveListCount(ctx, Normalize(_path()), _scale());
                    break;
                default:
                    value = _value();
                    break;
            }

            // scale 只对 listCount 有意义；配在其它源上属笔误，一次性告警（不静默吞掉）。
            if (Normalize(_source()) != "listcount" && Math.Abs(_scale() - 1f) > 1e-6f)
            {
                OneShotWarn.WarnOnce("write-bb-scale-source",
                    $"WriteBlackboard: scale only applies to source 'listCount' (got '{_source()}'); ignoring scale.");
            }

            if (!_asString() || value == null || value is List<Entity>) return value;
            return value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value.ToString();
        }

        private static object ResolveEventValue(AbilityEvent evt, string path)
        {
            if (evt == null) return null;

            if (evt is DamageEventBase damageEvent)
            {
                switch (path)
                {
                    case "target": return ToEntityList(damageEvent.target);
                    case "multiplier":
                    case "multiplyer": return damageEvent.multiplyer;
                    case "defpenetrate": return damageEvent.defPenetrate;
                    case "mgrpenetrate": return damageEvent.mgrPenetrate;
                    case "defpenetrate_value": return damageEvent.defPenetrate_value;
                    case "mgrpenetrate_value": return damageEvent.mgrPenetrate_value;
                    case "damagetype": return damageEvent.damageType;
                    case "applytype": return damageEvent.applyType;
                }
            }

            if (evt is HurtEventBase hurtEvent)
            {
                switch (path)
                {
                    case "origin": return ToEntityList(hurtEvent.origin);
                    case "damage": return hurtEvent.damage;
                    case "multiplier":
                    case "multiplyer": return hurtEvent.multiplyer;
                    case "defpenetrate": return hurtEvent.defPenetrate;
                    case "mgrpenetrate": return hurtEvent.mgrPenetrate;
                    case "defpenetrate_value": return hurtEvent.defPenetrate_value;
                    case "mgrpenetrate_value": return hurtEvent.mgrPenetrate_value;
                    case "damagetype": return hurtEvent.damageType;
                    case "applytype": return hurtEvent.applyType;
                    case "isdeadly": return hurtEvent.isDeadly;
                }
            }

            if (evt is BeforeTargetSelectEvent selectEvent)
            {
                switch (path)
                {
                    // 副本快照：黑板不与攻击系统的 live 候选列表共享引用（沿用
                    // ToEntityList 的"只提取数据、不交出事件内部可变状态"约定）。
                    // 筛选后经 override_attack_targets 提交回 live 列表。
                    case "targets": return new List<Entity>(selectEvent.targets);
                    case "selectmaxnum": return selectEvent.selectMaxNum;
                    case "selectminnum": return selectEvent.selectMinNum;
                    case "samecomp": return selectEvent.sameComp;
                }
            }

            if (path == "isdeadly")
            {
                if (evt is AfterAttackEvent afterAttack) return afterAttack.isDeadly;
                if (evt is AfterTakeDamageEvent afterDamage) return afterDamage.isDeadly;
            }
            if (path == "cumbo" && evt is BeforeAttackEvent beforeAttack) return beforeAttack.cumbo;

            WarnUnknownContextPath("event", path);
            return null;
        }

        /// <summary>listCount 取值源：读 path 指向的黑板 List&lt;Entity&gt;，返回 count×scale
        /// （float；scale 默认 1）。列表缺失属配线错误，一次性告警并返回 null（写入随之跳过）。</summary>
        private static object ResolveListCount(AbilityContext ctx, string path, float scale)
        {
            if (ctx.sharedBlackboard == null || string.IsNullOrEmpty(path)) return null;
            List<Entity> list = ctx.sharedBlackboard.Get<List<Entity>>(path, null);
            if (list == null)
            {
                OneShotWarn.WarnOnce(
                    "write-bb-list-count:" + path,
                    $"WriteBlackboard: source 'listCount' found no entity list at '{path}'; skipping.");
                return null;
            }
            return list.Count * scale;
        }

        // internal：SpawnEntity 的 passStat 复用同一套实体路径词汇表（"attack" 等）。
        internal static object ResolveEntityValue(Entity entity, string path)
        {
            if (entity == null) return null;

            switch (path)
            {
                case "":
                case "self": return ToEntityList(entity);
                case "camp": return entity.Camp;
                case "currenthp": return entity.Stats.CurrentHp;
                case "currenthprate": return entity.Stats.CurrentHpRate;
                case "maxhp": return entity.Stats.MaxHpS;
                case "attack": return entity.Stats.AttackS;
                case "monsterstatus": return entity.EntityData != null ? entity.EntityData.MonsterStatus : 0;
                default:
                    WarnUnknownContextPath("entity", path);
                    return null;
            }
        }

        private static List<Entity> ToEntityList(Entity entity)
        {
            return entity == null ? new List<Entity>() : new List<Entity> { entity };
        }

        private static void WarnUnknownContextPath(string source, string path)
        {
            OneShotWarn.WarnOnce(
                $"write-bb-context:{source}:{path}",
                $"WriteBlackboard: unsupported context path '{source}.{path}'; skipping.");
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Trim().ToLowerInvariant();
        }

        // "add" / "mult" / "div" path. Reads existing at key, parses value to existing's type,
        // applies the op, writes back. No existing value = no-op (designer should set first
        // or use method="set" to seed). Null value also no-ops (otherwise a missing param
        // would zero out BB via mult, or no-op via add — explicit is better).
        private static void ApplyOp(Blackboard bb, string key, object value, string method)
        {
            if (value == null) return;
            object existing = bb.Get<object>(key, null);
            if (existing == null) return;

            Type t = existing.GetType();
            if (!MathOps.TryParse(method, out var op))
            {
                // Defensive: OnTrigger's switch already rejects unknown methods.
                Debug.LogWarning($"WriteBlackboard: unknown method '{method}'; skipping");
                return;
            }
            try
            {
                if (t == typeof(int))
                    bb.Set(key, MathOps.Apply((int)existing, Convert.ToInt32(value), op));
                else if (t == typeof(float))
                    bb.Set(key, MathOps.Apply((float)existing, Convert.ToSingle(value), op));
                else if (t == typeof(double))
                    bb.Set(key, MathOps.Apply((double)existing, Convert.ToDouble(value), op));
                else
                    Debug.LogWarning($"WriteBlackboard: method='{method}' unsupported for type {t.Name}; skipping");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"WriteBlackboard: failed to convert value to {t.Name}: {ex.Message}; skipping");
            }
        }
    }
}
