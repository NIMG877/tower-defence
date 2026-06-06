using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单个实体的全部静态数据（角色属性 / 怪物属性 / 塔属性 / 召唤物属性）。
/// 由 <see cref="EntityDataCollection"/>（ScriptableObject）持有、由 <see cref="EntityDataRepository"/> 提供查询。
/// 全局命名空间，跨层共享（可被存档、网络包、测试夹具复用）。
/// </summary>
[Serializable]
public struct EntityData
{
    // 身份
    public EntityID ID;
    public string ChineseName;
    public string EnglishName;
    public string description;

    // 美术资源
    public GameObject prefab;
    public Sprite headImg;
    public Sprite halfbodyImg;
    public Sprite wholeImg;

    // 角色属性（当 entity 是 "c" 类别时使用）
    public int    character_rarity;
    public string character_influence;
    public int    character_job;          // 0=先锋,1=近卫,...,9=_
    public string character_subjob;

    // 怪物属性（当 entity 是 "m" 类别时使用）
    public int    monster_status;
    public string monster_label;
    public bool   monster_isPrimary;
    public bool   monster_countOperated;
    public int    monster_levelHPConsume;

    // 战斗属性
    public List<Vector2Int> visionRange_L;
    public float visionRadius;
    public float attack;
    public float baseAttackTime;
    public int   attackNum;
    public int   damageType;            // 0=物伤,1=法伤,...
    public float maxHP;
    public float defence;
    public float magicResistance;
    public float physicalDoge;
    public float magicDoge;
    public int   blockOccupation;
    public int   tauntLevel;
    public int   defaultCamp;

    // 免疫
    public bool stunImmune;
    public bool silenceImmune;
    public bool sleepImmune;
    public bool frozenImmune;
    public bool levitateImmune;
    public bool disarmedCombatImmune;
    public bool fearedImmune;

    // 召唤
    public List<EntityID> canSpawnEntityID_L;
    public List<int>      canSpawnEntityNum_L;

    // 部署
    public int   cost;
    public bool  canCallBack;
    public bool  needSelectDirection;
    public int   canSetType;
    public float respawnTime;
    public int   respawnStrategy;       // 0=默认,1=唯一,2=禁用,3=_
    public bool  canRespawn;
    public float respawnCostUp;
    public int   canSetNumOccupy;

    // 移动
    public float moveSpeed;
    public int   massLevel;
    public int   moveMethod;
}
