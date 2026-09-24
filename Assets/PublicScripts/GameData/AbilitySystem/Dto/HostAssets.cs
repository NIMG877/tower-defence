using System.Collections.Generic;
using UnityEngine;

namespace AbilitySystem
{
    /// <summary>
    /// hostAssets 投影：请求 payload 的组成部分。
    /// 定义在 GameData 程序集：AgentGenerateRequest（BasicScripts，引用 GameData）
    /// 也能看到，依赖方向不逆行。
    /// </summary>
    public class HostAssets
    {
        public int job;
        public int subJob;
        public int cost;
        public float baseAttackTime;
        public int damageType;
        public int blockOccupation;
        public string targetPriority;
        public float visionRadius;
        public List<Vector2Int> visionRange;
        public AnimationNames animations;
        public List<BulletEntry> bullets;
        public List<SpawnableEntry> canSpawnEntities;

        public class AnimationNames
        {
            public List<string> named;
            public List<string> groups;
        }

        public class BulletEntry
        {
            public int index;
            public int bulletType;
            public bool allowNoTarget;
        }

        public class SpawnableEntry
        {
            public string id;
            public string name;
            public bool isStatic;
            public float hp;
            public float maxHp;
            public float hpRate;
            public float attack;
            public float defence;
            public float magicRes;
            public int job;
            public string label;
            public int massLevel;
            public int count;
        }

        /// <summary>从 EntityData 静态数据构建投影。战斗中任一时刻重投影结果一致，
        /// 客户端终检可安全重建。</summary>
        public static HostAssets FromEntityData(EntityData data)
        {
            var result = new HostAssets
            {
                animations = new AnimationNames
                {
                    named = new List<string>(),
                    groups = new List<string>(),
                },
                bullets = new List<BulletEntry>(),
                canSpawnEntities = new List<SpawnableEntry>(),
            };

            result.job = data.CharacterJob;
            result.subJob = data.CharacterSubJob;
            result.cost = data.Cost;
            result.baseAttackTime = data.BaseAttackTime;
            result.damageType = data.DamageType;
            result.blockOccupation = data.BlockOccupation;
            result.targetPriority = data.TargetPriority.ToString();
            result.visionRadius = data.VisionRadius;
            result.visionRange = data.VisionRange;

            AnimationResources animations = data.AnimationResources;
            if (animations != null)
            {
                result.animations.named = animations.GetAnimationNames();
                result.animations.groups = animations.GetAnimationGroupNames();
            }

            if (data.Bullets != null)
            {
                for (int i = 0; i < data.Bullets.Count; i++)
                {
                    BulletData bullet = data.Bullets[i];
                    result.bullets.Add(new BulletEntry
                    {
                        index = i,
                        bulletType = bullet != null ? bullet.BulletType : 0,
                        allowNoTarget = bullet != null && bullet.AllowNoTarget,
                    });
                }
            }

            if (data.CanSpawnEntityIds != null)
            {
                for (int i = 0; i < data.CanSpawnEntityIds.Count; i++)
                {
                    EntityID id = data.CanSpawnEntityIds[i];
                    int count = data.CanSpawnEntityCounts != null
                                && i < data.CanSpawnEntityCounts.Count
                        ? data.CanSpawnEntityCounts[i]
                        : 1;
                    result.canSpawnEntities.Add(ToSpawnableProjection(id, count));
                }
            }
            return result;
        }

        /// <summary>可召唤物的静态投影（实体同构：spawn 时还没有运行时实例，
        /// hp 取 MaxHp、hpRate 恒 1、无 pos/camp）。</summary>
        private static SpawnableEntry ToSpawnableProjection(EntityID id, int count)
        {
            var entry = new SpawnableEntry
            {
                id = id.ToString(),
                count = count,
            };
            EntityData spawnable = GameDataService.EntityRepository.Get(id);
            if (spawnable == null)
            {
                return entry;
            }
            entry.name = spawnable.ChineseName;
            entry.isStatic = spawnable.IsStatic;
            entry.hp = spawnable.MaxHp;
            entry.maxHp = spawnable.MaxHp;
            entry.hpRate = 1f;
            entry.attack = spawnable.Attack;
            entry.defence = spawnable.Defense;
            entry.magicRes = spawnable.MagicResistance;
            entry.job = spawnable.CharacterJob;
            entry.label = spawnable.MonsterLabel;
            entry.massLevel = spawnable.MassLevel;
            return entry;
        }
    }
}
