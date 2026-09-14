using System;
using System.Collections.Generic;
using AbilitySystem;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 战局快照序列化器：把当前战局<b>基本信息</b>（敌我实体/地图可部署格）投影成
/// 紧凑 JSON，供服务端 Agent 分析。派生交叉项（最近敌距/半径内清单等）不在
/// 客户端预计算——服务端 Agent 用 compute_cross_items 工具按需计算（crossitems.py），
/// 减少上下文消耗与噪声。
/// 契约 v2：self 与其它实体同构拍平进 entities（selfId 作指针），实体记录含
/// massLevel（失衡/击退类技能需要质量对比）。本类只负责序列化，不向宿主黑板
/// 写入任何快照键；生成技能的实时战局条件走 select_targets（写实体列表）+
/// write_blackboard source=listCount（桥接计数）链路。
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
        public int massLevel;   // 质量（EntityData.MassLevel），失衡/击退对比用
    }

    public class BattleSnapshot
    {
        public string selfId;           // 指针：entities 中与 self 同构的记录
        public int mapI;
        public int mapJ;
        public string[] canSetHigher;   // 高台可部署格 "i,j" 列表
        public string[] canSetLower;    // 地面可部署格 "i,j" 列表
        public SnapshotEntity[] entities;   // 含自身（首条为 self）
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
        List<SnapshotEntity> entities = new List<SnapshotEntity> { ToSnapshotEntity(self) };
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
            mapI = map.iSize,
            mapJ = map.jSize,
            canSetHigher = CollectCanSet(map.HigherCanSetBlock),
            canSetLower = CollectCanSet(map.LowerCanSetBlock),
            entities = entities.ToArray(),
        };
        return snapshot;
    }

    public static string ToJson(BattleSnapshot snapshot, bool indented = false)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        return JsonConvert.SerializeObject(snapshot, indented ? Formatting.Indented : Formatting.None);
    }

    /// <summary>单实体投影（含 self——契约 v2 把 self 拍平进 entities）。纯函数，
    /// 公开供 EditMode 测试。</summary>
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
            massLevel = data.MassLevel,
        };
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
