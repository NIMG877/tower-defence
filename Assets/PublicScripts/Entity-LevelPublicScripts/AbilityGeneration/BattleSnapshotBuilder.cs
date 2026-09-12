using System;
using System.Collections.Generic;
using AbilitySystem;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 战局快照序列化器：把当前战局（敌我/静态实体、地图可部署格、C# 预算的交叉项）
/// 投影成紧凑 JSON，供服务端组装 LLM 提示词。快照在请求时刻固化，实体位置/血量
/// 均为请求瞬间的取值。
/// 交叉项词表与 AbilitySystem.SnapshotBlackboardKeys（GameData 侧）对应；阶段三把
/// 交叉项同步写入宿主黑板后，生成技能的 fromBlackboard 参数引用同一词表。
/// </summary>
public static class BattleSnapshotBuilder
{
    public class SnapshotPos
    {
        public float x;
        public float y;
    }

    public class SnapshotEntity
    {
        public string id;
        public string name;
        public int camp;
        public bool isStatic;
        public float hp;
        public float maxHp;
        public float hpRate;
        public SnapshotPos pos;
        public float attack;
        public float defence;
        public float magicRes;
        public int job;         // 角色职业（EntityData.CharacterJob），非角色为 -1
        public string label;    // 怪物标签（EntityData.MonsterLabel），非怪物为 null
    }

    public class SnapshotSkill
    {
        public string abilityId;
        public float currentSp;
        public int totalSp;
        public bool active;
    }

    public class SnapshotCross
    {
        public int enemyCount;
        public int allyCount;   // 不含自身
        public float nearestEnemyDistance;   // 场上无敌人 = -1
        public float lowestEnemyHpRate;
        public string lowestEnemyId;
    }

    public class BattleSnapshot
    {
        public string selfId;
        public int camp;
        public float selfHpRate;
        public SnapshotSkill skill;     // 首个已构建技能的 SP 状态，无则 null
        public int mapI;
        public int mapJ;
        public string[] canSetHigher;   // 高台可部署格 "i,j" 列表
        public string[] canSetLower;    // 地面可部署格 "i,j" 列表
        public SnapshotEntity[] entities;   // 不含自身
        public SnapshotCross cross;
    }

    /// <summary>
    /// 从活动战局构建快照。仅可在战斗进行中调用（依赖 EntityManager/MapDataManager
    /// 单例）；实体遍历走 EntitySelector_Radius（radius&lt;0 = 不限距离、force 忽略可选性），
    /// 静态实体（召唤物等）经 StaticEntityExistBlock 逐格补齐并按实例去重。
    /// </summary>
    public static BattleSnapshot Build(Entity self)
    {
        if (self == null) throw new ArgumentNullException(nameof(self));
        if (EntityManager.Manager == null) throw new InvalidOperationException("EntityManager is not initialized; snapshot requires an active battle.");
        if (MapDataManager.Manager == null) throw new InvalidOperationException("MapDataManager is not initialized; snapshot requires an active battle.");

        Vector2 selfPos = self.Movement.Position;
        List<SnapshotEntity> entities = new List<SnapshotEntity>();
        HashSet<Entity> seen = new HashSet<Entity> { self };

        // _turrets/_monsters 两个活实体源：radius<0 走 ignoreDistance，force 忽略可选性/休眠过滤
        AppendAll(entities, seen,
            EntityManager.Manager.EntitySelector_Radius((selfPos.x, selfPos.y), self.Camp, true, -1f, true));
        AppendAll(entities, seen,
            EntityManager.Manager.EntitySelector_Radius((selfPos.x, selfPos.y), self.Camp, false, -1f, true));

        // 静态实体不在两个活实体源里，逐格补齐
        MapDataManager map = MapDataManager.Manager;
        for (int i = 0; i < map.iSize; i++)
        {
            for (int j = 0; j < map.jSize; j++)
            {
                Entity staticEntity = EntityManager.Manager.GetStaticEntityInBlock(i, j);
                if (staticEntity != null && seen.Add(staticEntity))
                    entities.Add(ToSnapshotEntity(staticEntity));
            }
        }

        var snapshot = new BattleSnapshot
        {
            selfId = self.EntityData.ID.ToString(),
            camp = self.Camp,
            selfHpRate = Round(self.Stats.CurrentHpRate, 3),
            skill = BuildSkillSnapshot(self),
            mapI = map.iSize,
            mapJ = map.jSize,
            canSetHigher = CollectCanSet(map.HigherCanSetBlock),
            canSetLower = CollectCanSet(map.LowerCanSetBlock),
            entities = entities.ToArray(),
            cross = BuildCross(selfPos, entities, self.Camp),
        };
        return snapshot;
    }

    public static string ToJson(BattleSnapshot snapshot, bool indented = false)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        return JsonConvert.SerializeObject(snapshot, indented ? Formatting.Indented : Formatting.None);
    }

    /// <summary>单实体投影。纯函数，公开供 EditMode 测试。</summary>
    public static SnapshotEntity ToSnapshotEntity(Entity entity)
    {
        EntityData data = entity.EntityData;
        return new SnapshotEntity
        {
            id = data.ID.ToString(),
            name = data.ChineseName,
            camp = entity.Camp,
            isStatic = data.IsStatic,
            hp = Round(entity.Stats.CurrentHp, 1),
            maxHp = Round(entity.Stats.MaxHpS, 1),
            hpRate = Round(entity.Stats.CurrentHpRate, 3),
            pos = new SnapshotPos { x = Round(entity.Movement.Position.x, 2), y = Round(entity.Movement.Position.y, 2) },
            attack = Round(entity.Stats.AttackS, 1),
            defence = Round(entity.Stats.DefS, 1),
            magicRes = Round(entity.Stats.MagicResistanceS, 1),
            job = data.CharacterJob,
            label = data.MonsterLabel,
        };
    }

    /// <summary>
    /// 交叉项计算。纯函数，公开供 EditMode 测试。敌我按 camp 区分（camp 与自身
    /// 不同即视为敌方）；nearestEnemyDistance 场上无敌人时为 -1；lowestEnemyHpRate
    /// 初值 1（无敌人时保持 1、id 为 null）。
    /// </summary>
    public static SnapshotCross BuildCross(Vector2 selfPos, List<SnapshotEntity> entities, int selfCamp)
    {
        var cross = new SnapshotCross
        {
            nearestEnemyDistance = -1f,
            lowestEnemyHpRate = 1f,
            lowestEnemyId = null,
        };
        if (entities == null) return cross;

        for (int i = 0; i < entities.Count; i++)
        {
            SnapshotEntity e = entities[i];
            if (e == null || e.camp == selfCamp)
            {
                if (e != null) cross.allyCount++;
                continue;
            }
            cross.enemyCount++;
            float dx = e.pos.x - selfPos.x;
            float dy = e.pos.y - selfPos.y;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (cross.nearestEnemyDistance < 0f || dist < cross.nearestEnemyDistance)
                cross.nearestEnemyDistance = Round(dist, 2);
            if (e.hpRate < cross.lowestEnemyHpRate)
            {
                cross.lowestEnemyHpRate = e.hpRate;
                cross.lowestEnemyId = e.id;
            }
        }
        return cross;
    }

    private static void AppendAll(List<SnapshotEntity> destination, HashSet<Entity> seen, List<Entity> source)
    {
        if (source == null) return;
        for (int i = 0; i < source.Count; i++)
        {
            Entity entity = source[i];
            if (entity != null && seen.Add(entity))
                destination.Add(ToSnapshotEntity(entity));
        }
    }

    private static SnapshotSkill BuildSkillSnapshot(Entity self)
    {
        var runner = self.AbilityRunner;
        if (runner == null || runner.Skills.Count == 0) return null;
        AbilityRuntime ability = runner.Skills[0];
        if (ability == null) return null;
        return new SnapshotSkill
        {
            abilityId = ability.config != null ? ability.config.abilityId : null,
            currentSp = Round(ability.spEngine != null ? ability.spEngine.CurrentSp : 0f, 1),
            totalSp = ability.config != null && ability.config.sp != null ? ability.config.sp.totalSp : 0,
            active = ability.spEngine != null && ability.spEngine.IsActive,
        };
    }

    private static string[] CollectCanSet(bool[,] canSetBlock)
    {
        if (canSetBlock == null) return Array.Empty<string>();
        List<string> cells = new List<string>();
        for (int i = 0; i < canSetBlock.GetLength(0); i++)
        {
            for (int j = 0; j < canSetBlock.GetLength(1); j++)
            {
                if (canSetBlock[i, j]) cells.Add($"{i},{j}");
            }
        }
        return cells.ToArray();
    }

    private static float Round(float value, int digits) => (float)Math.Round(value, digits);
}
