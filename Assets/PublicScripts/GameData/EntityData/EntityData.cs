using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单个实体的全部静态数据（角色属性 / 怪物属性 / 塔属性 / 召唤物属性）。
/// 由 <see cref="EntityDataCollection"/>（ScriptableObject）持有、由 <see cref="EntityDataRepository"/> 提供查询。
/// 全局命名空间，跨层共享（可被存档、网络包、测试夹具复用）。
///
/// 命名约定：公开字段 PascalCase；类型为 <c>class</c>（引用语义），默认值为 <c>null</c>。
/// 内部 List 字段在反序列化后由 Unity 填充，运行时访问前需确认已初始化。
/// </summary>
[Serializable]
public class EntityData
{
    // 身份
    public EntityID ID;
    public string ChineseName;
    public string EnglishName;
    public string Description;

    // 美术资源
    public GameObject Prefab;
    public Sprite HeadImage;
    public Sprite HalfBodyImage;
    public Sprite WholeImage;

    // 角色属性（当 entity 是 "c" 类别时使用）
    public int    CharacterRarity;
    public string CharacterInfluence;
    public int    CharacterJob;          // 0=先锋,1=近卫,...,9=_
    public string CharacterSubJob;

    // 怪物属性（当 entity 是 "m" 类别时使用）
    public int    MonsterStatus;
    public string MonsterLabel;
    public bool   MonsterIsPrimary;
    public bool   MonsterCountOperated;
    public int    MonsterLevelHpConsume;

    // 战斗属性
    public List<Vector2Int> VisionRange;
    public float VisionRadius;
    public float Attack;
    public float BaseAttackTime;
    public int   AttackNum;
    public int   DamageType;            // 0=物伤,1=法伤,...
    public float MaxHp;
    public float Defense;
    public float MagicResistance;
    public float PhysicalDodge;
    public float MagicDodge;
    public int   BlockOccupation;
    public int   TauntLevel;
    public int   DefaultCamp;

    // 免疫
    public bool StunImmune;
    public bool SilenceImmune;
    public bool SleepImmune;
    public bool FrozenImmune;
    public bool LevitateImmune;
    public bool DisarmedCombatImmune;
    public bool FearedImmune;

    // 召唤
    public List<EntityID> CanSpawnEntityIds;
    public List<int>      CanSpawnEntityCounts;

    // 部署
    public int   Cost;
    public bool  CanCallBack;
    public bool  NeedsDirectionSelection;
    public int   CanSetType;
    public float RespawnTime;
    public int   RespawnStrategy;       // 0=默认,1=唯一,2=禁用,3=_
    public bool  CanRespawn;
    public float RespawnCostUp;
    public int   MaxOccupyCount;

    // 移动
    public float MoveSpeed;
    public int   MassLevel;
    public int   MoveMethod;
}
