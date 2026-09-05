using System;
using System.Collections.Generic;
using UnityEngine;
using AbilitySystem;

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
    
    // 默认阵营
    public int   DefaultCamp;

    // 角色属性（当 entity 是 "c" 类别时使用）
    public int    CharacterRarity;
    public int    CharacterJob;          // 0=先锋,1=近卫,...,9=_
    public int    CharacterSubJob;       // 0=无,1=秘术师,2=冲锋手,3=凝滞师,...（中文映射见 XLSX2DataAsset.ParseSubJob）

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
    public OrderLogic TargetPriority;   // 目标优先级排序逻辑（见 OrderLogic 枚举）
    public float SplashRadius;          // 攻击溅射半径
    public float MaxHp;
    public float Defense;
    public float MagicResistance;
    public float PhysicalDodge;
    public float MagicDodge;
    public int   BlockOccupation;
    public int   TauntLevel;

    // 召唤
    public List<EntityID> CanSpawnEntityIds;
    public List<int>      CanSpawnEntityCounts;

    // 是否是静态实体
    public bool IsStatic;

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

    // 技能 / 天赋 / 子职业特性（静态数据驱动框架）
    // Skills: 技能,SPEngine 充能式触发
    // Talents: 天赋,出生即生效(可扩展为 SPEngine 驱动)
    // 字段分开存储;PreWarm 时按列表来源赋值 AbilityRuntime.Kind
    // ExtraAbility 继续走运行时 AddExtraAbility 路径,不在这里
    public List<AbilityConfig> Skills = new List<AbilityConfig>();
    public List<AbilityConfig> Talents = new List<AbilityConfig>();

    // 子职业特性:与子职业一一对应的共享模板(非列表),Rebuild 工具(XLSX2DataAsset)
    // 按 CharacterSubJob 从 Prefabs/Abilities/SubJobs/ 装载;
    // 运行时装配(PreWarm 构建/AbilityKind)为后续阶段,暂无消费方
    public AbilityConfig SubJobTrait;

    // 动画资源
    public AnimationResources AnimationResources;
}
