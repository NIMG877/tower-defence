using System;
using System.Collections.Generic;
using UnityEngine;

namespace AbilitySystem.Components
{
    /// <summary>
    /// 通过 <c>EntityPoolManager</c>/<c>EntityManager</c> 生成实体。召唤目标只能
    /// 取自宿主 <c>EntityData.CanSpawnEntityIds</c> 注册表，<c>spawnIndex</c> 挑
    /// 第几项；全部参数支持 <c>fromBlackboard</c>。
    ///
    /// <para>lazy getter 在 <c>OnTrigger</c> 时按执行上下文黑板绑定（而非在
    /// <c>OnInit</c> 绑宿主黑板）：脱离执行跑在 fork 时克隆的黑板上，宿主板届时
    /// 可能已被池回收成别的实体，OnInit 绑定会把参数读到错误的数据上。</para>
    ///
    /// <para>配置错误（注册表缺失/spawnIndex 越界/未知 token/池缺失）仅
    /// <c>LogError</c> 并跳过本次生成，序列照常继续——组件路径没有失败语义；
    /// 将来若需要"失败即中止序列"，再给组件层补状态通道。</para>
    /// </summary>
    [RegisterComponent("SpawnEntity")]
    public class SpawnEntity : AbilityComponentBase
    {
        private ParamList _args;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            _args = p;
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            EntityID id = ResolveSpawnId(ctx, _args);
            if (id.IsNull) return; // ResolveSpawnId 已记录具体配置错误。

            if (!ResolvePosition(ctx, _args, out Vector2 position)) return;

            int camp = _args.GetIntLazy("camp", -1, ctx.sharedBlackboard)();
            if (camp < 0)
            {
                DetachedExecutionSnapshot snapshot = ctx.stepExecution?.DetachedSnapshot;
                camp = snapshot != null ? snapshot.camp
                    : ctx.entity != null ? ctx.entity.Camp : 1;
            }

            EntityPool pool = EntityPoolManager.Manager.FetchEntityPool(id);
            if (pool == null)
            {
                Debug.LogError($"[SpawnEntity] Entity pool '{id}' is not available.");
                return;
            }

            string placement = NormalizeToken(
                _args.GetStringLazy("placement", "auto", ctx.sharedBlackboard)());
            bool isStatic;
            switch (placement)
            {
                case "static": isStatic = true; break;
                case "move": isStatic = false; break;
                case "auto":
                    isStatic = pool.EntityData != null && pool.EntityData.IsStatic;
                    break;
                default:
                    Debug.LogError($"[SpawnEntity] Unknown placement '{placement}' (expected static/move/auto).");
                    return;
            }

            Entity spawned = isStatic
                ? EntityManager.Manager.SetStaticEntity(
                    id,
                    position,
                    camp,
                    _args.GetIntLazy("orientation", 0, ctx.sharedBlackboard)())
                : EntityManager.Manager.SetMovableEntity(
                    id,
                    position,
                    camp,
                    ResolvePathSerial(ctx, _args));

            string outputKey = _args.GetStringLazy("outputKey", "", ctx.sharedBlackboard)();
            if (spawned != null && !string.IsNullOrEmpty(outputKey))
                ctx.sharedBlackboard?.Set(outputKey, spawned);
        }

        /// <summary>召唤目标只能取自宿主 EntityData.CanSpawnEntityIds 注册表，
        /// spawnIndex 挑第几项；脱离执行只读 fork 快照的列表（活宿主可能已被
        /// 池回收成别的实体）。越界或未注册直接报错返回 Null，不做裁剪。</summary>
        private static EntityID ResolveSpawnId(AbilityContext ctx, ParamList args)
        {
            List<EntityID> ids = ctx.stepExecution?.DetachedSnapshot != null
                ? ctx.stepExecution.DetachedSnapshot.spawnEntityIds
                : DetachedExecutionSnapshot.HostSpawnIds(ctx.entity);
            if (ids == null || ids.Count == 0)
            {
                Debug.LogError("[SpawnEntity] Host entity data has no CanSpawnEntityIds entry.");
                return EntityID.Null;
            }

            int index = args.GetIntLazy("spawnIndex", 0, ctx.sharedBlackboard)();
            if (index < 0 || index >= ids.Count)
            {
                Debug.LogError($"[SpawnEntity] spawnIndex {index} out of range: host CanSpawnEntityIds has {ids.Count} entries.");
                return EntityID.Null;
            }
            return ids[index];
        }

        private static bool ResolvePosition(AbilityContext ctx, ParamList args, out Vector2 position)
        {
            string mode = NormalizeToken(
                args.GetStringLazy("positionMode", "self", ctx.sharedBlackboard)());
            switch (mode)
            {
                case "self":
                    // Detached executions resolve "self" from the fork-time
                    // snapshot; the host may be recycled by now.
                    Vector2? snap = ctx.stepExecution?.DetachedSnapshot?.position;
                    position = snap ?? (ctx.entity != null ? ctx.entity.Movement.Position : Vector2.zero);
                    break;
                case "eventtarget":
                    Entity eventTarget = ResolveEventEntity(ctx.currentEvent);
                    position = eventTarget != null
                        ? eventTarget.Movement.Position
                        : ctx.entity != null ? ctx.entity.Movement.Position : Vector2.zero;
                    break;
                case "fixed":
                    position = args.GetVector2IntLazy("position", default, ctx.sharedBlackboard)();
                    break;
                default:
                    position = default; // 走不到消费方；仅为满足 out 明确赋值。
                    Debug.LogError($"[SpawnEntity] Unknown positionMode '{mode}' (expected self/eventTarget/fixed).");
                    return false;
            }
            position += (Vector2)args.GetVector2IntLazy("offset", default, ctx.sharedBlackboard)();
            return true;
        }

        /// <summary>pathSerial 缺省继承宿主当前路径（脱离执行读 fork 快照），
        /// 显式传入时字面量优先——镜像 camp 的"负值/缺省选默认"语义。</summary>
        private static int ResolvePathSerial(AbilityContext ctx, ParamList args)
        {
            if (args.HasKey("pathSerial"))
                return args.GetIntLazy("pathSerial", 0, ctx.sharedBlackboard)();
            DetachedExecutionSnapshot snapshot = ctx.stepExecution?.DetachedSnapshot;
            if (snapshot != null) return snapshot.pathSerial;
            return ctx.entity != null && ctx.entity.MoveBase != null
                ? ctx.entity.MoveBase.CurrentPathSerial
                : 0;
        }

        private static Entity ResolveEventEntity(AbilityEvent evt)
        {
            if (evt is DamageEventBase damage) return damage.target;
            if (evt is HurtEventBase hurt) return hurt.origin;
            return null;
        }

        private static string NormalizeToken(string value) =>
            (value ?? string.Empty).Trim().ToLowerInvariant();
    }
}
