using System;
using System.Collections.Generic;
using UnityEngine;

namespace AbilitySystem.Components
{
    /// <summary>
    /// 为载弹落点取 <c>count</c> 个点，随机流水（2026-08-29 定稿，取代首版覆盖
    /// 最优搜索）：① 攻击范围内敌群（<c>Vision.NearbyMonsters</c>，索敌同口径，
    /// 排除 <c>excludeKey</c> 名单——艾雅法拉传泡泡花名册）随机取原位；
    /// ② 不足补射程格（<c>Vision.Range</c>）：地面层先于高台层、层内随机不重复，
    /// 每点加 ±<c>offset</c> 双轴随机偏移；③ 两池去重名额用尽后从敌人重新
    /// 选起（敌人↔格点交替，允许重复）。敌人位置取触发时快照，飞行期间移动
    /// 不追踪。结果写黑板 <c>outputKey</c>（<c>List&lt;Vector2&gt;</c> 世界坐标）。
    /// </summary>
    [RegisterComponent("SelectLandingPoints")]
    public class SelectLandingPoints : AbilityComponentBase
    {
        private ParamList _args;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            _args = p;
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (ctx.entity == null || ctx.sharedBlackboard == null) return;

            (int x, int y)[] cells = ctx.entity.Vision?.Range;
            if (cells == null)
            {
                Debug.LogError("[SelectLandingPoints] Host Vision.Range is null (radius-based vision?); landing-point selection needs the cell attack range.");
                return;
            }

            List<Entity> excluded = null;
            string excludeKey = _args.GetStringLazy("excludeKey", "", ctx.sharedBlackboard)();
            if (!string.IsNullOrEmpty(excludeKey))
                excluded = ctx.sharedBlackboard.Get<List<Entity>>(excludeKey, null);

            Vector2[] enemyPositions = ExtractEnemyPositions(ctx.entity.Vision.NearbyMonsters, excluded);

            bool[] isHighland = new bool[cells.Length];
            if (MapDataManager.Manager == null)
            {
                Debug.LogError("[SelectLandingPoints] MapDataManager is not loaded; cannot classify ground/highland cells.");
                return;
            }
            for (int i = 0; i < cells.Length; i++)
                isHighland[i] = MapDataManager.Manager.GetPosBlock(cells[i].y, cells[i].x).highland;

            int count = _args.GetIntLazy("count", 4, ctx.sharedBlackboard)();
            float offset = _args.GetFloatLazy("offset", 0.24f, ctx.sharedBlackboard)();
            List<Vector2> points = PickLandingPoints(cells, isHighland, enemyPositions, count, offset,
                () => RandomHelper.Helper.RandomF());
            if (points == null)
            {
                Debug.LogError($"[SelectLandingPoints] No selectable source: Range has {cells.Length} cells, {enemyPositions.Length} in-range enemies, count {count}; check vision/map config.");
                return;
            }

            string key = _args.GetStringLazy("outputKey", "", ctx.sharedBlackboard)();
            if (!string.IsNullOrEmpty(key)) ctx.sharedBlackboard.Set(key, points);
        }

        /// <summary>敌群转位置数组，剔除排除名单（null 名单/名单内 null 项直通）。
        /// public 供 EditMode 测试直接断言。</summary>
        public static Vector2[] ExtractEnemyPositions(List<Entity> enemies, List<Entity> excluded)
        {
            if (enemies == null) return Array.Empty<Vector2>();
            var positions = new List<Vector2>(enemies.Count);
            for (int i = 0; i < enemies.Count; i++)
            {
                Entity e = enemies[i];
                if (e == null) continue;
                if (excluded != null && excluded.Contains(e)) continue;
                positions.Add(e.transform.position);
            }
            return positions.ToArray();
        }

        /// <summary>选点纯函数（public 供 EditMode 测试直接断言）。
        /// 随机源注入 <c>random01</c>（[0,1]，同一序列驱动洗牌/索引/偏移）。
        /// cells null / count&lt;=0 / 无格点且无敌人返回 null，由调用方当配线错误报出；
        /// 其余情形必有结果（重复回退兜住）。</summary>
        public static List<Vector2> PickLandingPoints(
            (int x, int y)[] cells, bool[] isHighland, Vector2[] enemyPositions, int count, float offset, Func<float> random01)
        {
            if (cells == null || count <= 0 || (cells.Length == 0 && enemyPositions.Length == 0)) return null;

            var points = new List<Vector2>(count);

            // ① 敌人：随机不重复，原位不加偏移。
            int[] enemyOrder = ShuffledIndices(enemyPositions.Length, random01);
            for (int i = 0; i < enemyOrder.Length && points.Count < count; i++)
                points.Add(enemyPositions[enemyOrder[i]]);

            // ② 格点：地面层先于高台层，层内随机不重复，每点 ±offset 偏移。
            if (points.Count < count)
            {
                var ground = new List<int>(cells.Length);
                var platform = new List<int>(cells.Length);
                for (int c = 0; c < cells.Length; c++)
                    (isHighland != null && c < isHighland.Length && isHighland[c] ? platform : ground).Add(c);
                ShuffleInPlace(ground, random01);
                ShuffleInPlace(platform, random01);
                ground.AddRange(platform);
                for (int i = 0; i < ground.Count && points.Count < count; i++)
                    points.Add(CellWithOffset(cells[ground[i]], offset, random01));
            }

            // ③ 去重名额用尽：敌人重新选起，敌人↔格点交替、允许重复（空池自动跳过）。
            bool pickEnemy = true;
            while (points.Count < count)
            {
                if (pickEnemy && enemyPositions.Length > 0)
                    points.Add(enemyPositions[RandomIndex(enemyPositions.Length, random01)]);
                else
                    points.Add(CellWithOffset(cells[RandomIndex(cells.Length, random01)], offset, random01));
                pickEnemy = !pickEnemy;
            }
            return points;
        }

        private static Vector2 CellWithOffset((int x, int y) cell, float offset, Func<float> random01)
        {
            return new Vector2(
                cell.x + (random01() * 2f - 1f) * offset,
                cell.y + (random01() * 2f - 1f) * offset);
        }

        private static int[] ShuffledIndices(int length, Func<float> random01)
        {
            int[] indices = new int[length];
            for (int i = 0; i < length; i++) indices[i] = i;
            ShuffleInPlace(indices, random01);
            return indices;
        }

        private static void ShuffleInPlace(List<int> list, Func<float> random01)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = RandomIndex(i + 1, random01);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private static void ShuffleInPlace(int[] array, Func<float> random01)
        {
            for (int i = array.Length - 1; i > 0; i--)
            {
                int j = RandomIndex(i + 1, random01);
                (array[i], array[j]) = (array[j], array[i]);
            }
        }

        // % length 折回含上界 1.0 的边角（RandomHelper.RandomL 同款约定）。
        private static int RandomIndex(int length, Func<float> random01)
        {
            return (int)(random01() * length) % length;
        }
    }
}
