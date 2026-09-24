using System;
using System.Collections.Generic;
using UnityEngine;

namespace AbilitySystem.Components
{
    /// <summary>
    /// 向黑板 <c>List&lt;Vector2&gt;</c> 点列表逐一发纯视觉载弹（无实体目标、
    /// 零伤害——伤害语义由落点侧规则负责），弹着时在宿主 runner 上派发
    /// <see cref="BulletLandedEvent"/>（position=销毁时刻实际位置：抛物线弹型
    /// 弧线含随机偏移、非瞄准点；直线弹落点即瞄准点）。
    /// 弹幕配置取宿主 <c>EntityAttack._bulletDatas[bulletDataIndex]</c>：
    /// GameObject 引用装不进 ParamList，带引用的弹幕配置登记在实体攻击数据上。
    /// </summary>
    [RegisterComponent("FireBullets")]
    public class FireBullets : AbilityComponentBase
    {
        private ParamList _args;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            _args = p;
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (ctx.entity == null || ctx.entity.Attack == null)
            {
                Debug.LogError("[FireBullets] Host entity has no EntityAttack; bullet data unavailable.");
                return;
            }

            string pointsKey = _args.GetStringLazy("pointsKey", "", ctx.sharedBlackboard)();
            List<Vector2> points = string.IsNullOrEmpty(pointsKey) || ctx.sharedBlackboard == null
                ? null
                : ctx.sharedBlackboard.Get<List<Vector2>>(pointsKey, null);
            if (points == null)
            {
                Debug.LogError($"[FireBullets] pointsKey '{pointsKey}' is missing on the blackboard (expected List<Vector2>; wire select_landing_points first).");
                return;
            }

            int index = _args.GetIntLazy("bulletDataIndex", 0, ctx.sharedBlackboard)();
            if (!ctx.entity.Attack.TryGetBulletData(index, out EntityAttack.AttackBulletData data))
                return; // TryGetBulletData 已记录越界详情。

            if (data.BulletData == null || data.BulletData.BulletPrefab == null)
            {
                Debug.LogError($"[FireBullets] Bullet data [{index}] has no BulletData projectile asset.");
                return;
            }

            if (data.BulletSpawnTransform == null)
            {
                Debug.LogError($"[FireBullets] Bullet data [{index}] has no BulletSpawnTransform; carrier bullets need a spawn bone.");
                return;
            }

            Vector2 spawnPos = data.BulletSpawnTransform.position;
            Entity origin = ctx.entity;
            for (int i = 0; i < points.Count; i++)
            {
                // 视觉载弹参数固化：damage 0 / multiplyer 1 /
                // 穿透与伤害类型 0。落点回调闭包捕获发射时的宿主引用——宿主在
                // 飞行期间死亡时事件仍照发，runner 缺失才跳过。
                new Bullet(null, null, pos => DispatchLanded(origin, pos), data.BulletData, origin, null,
                    points[i], spawnPos, 0f, 1f, 0f, 0f, 0f, 0f, 0, 0);
            }
        }

        private static void DispatchLanded(Entity origin, Vector2 pos)
        {
            if (origin == null || origin.AbilityRunner == null) return;
            origin.AbilityRunner.DispatchEvent(new BulletLandedEvent { position = pos });
        }
    }
}
