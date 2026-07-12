using System;
using MyUI;

/// <summary>
/// 实体属性子系统（POCO）。
/// 持有：
///   1. 战斗基准属性（_xxxBase：EntityData 原始值 + 关卡环境"基础数值修改" buff，整场战斗不变；储存字段）
///   2. 含战斗过程 buff 的计算属性（XxxS：computed property，每次访问实时从 AttributeStore.GetFinal 取）
///   3. 状态（HP rate、participateIn、hurtable、selectable、isolate、dormant）
///
/// 数据计算流水线（buff 数值走 AttributeStore，Modifier 入口在 BuffController）：
///   EntityData 原始值 → [+ 关卡环境基础 buff] → _xxxBase（字段，注入 store.SetBase）
///     → [+ 战斗过程 buff（store 聚合 Modifier）] → XxxS（property = _store.GetFinal）
///
/// 设计要点：
///   - POCO，无 MonoBehaviour 依赖。构造接受 Entity 引用作为事件桥。
///   - 事件触发走 Entity 上的 internal RaiseOnXxx 桥方法——保留 Entity.OnBeforeHurt/OnAfterHurt/OnBeforeDieAnimation 公开事件 API。
///   - AttributeStore 由 Entity 在 PreWarm 中构造并通过 Bind 注入（EntityStats 构造早于 store 创建）。
///   - AttributesCaculateFirst 在 PreWarm 时调用一次，整场战斗不再重算（除非重新进入关卡重建实体）。
///   - XxxS 为 computed property，store 置脏即影响下次读取；调用方不可能读到陈旧值。
/// </summary>
public class EntityStats
{
    private readonly Entity _entity;
    private AttributeStore _store;

    // === 基础属性 ===
    // 读侧统一收口：所有数值属性经 store（GetFinal），XxxS 薄壳只做 clamp/强转/复合公式。
    // base 一律 SetBase 进 store，不再用 _xxxBase 字段参与加法（避免 base 算两次）。
    // 例外（仍留字段）：
    //   _physicalDodgeBase/_magicDodgeBase：Dodge 固有闪避，参与薄壳 1-(1-固有)*Final（store base=1 是"未命中乘数"）。
    //   _attackNumBase：仅用于 <0 哨兵判断（AttackNum<0 表示无限攻击次数），不参与加法。
    //   _attackBase/_baseAttackTimeBase：下游 AttackBase 读原始基准，故留字段+公开。
    private float _physicalDodgeBase;
    private float _magicDodgeBase;
    private int _attackNumBase;
    private float _attackBase;
    private float _baseAttackTimeBase;

    // === 状态 ===
    private float _currentHpRate;
    private bool _participateIn;
    private int _hurtable;
    private int _selectable;
    private bool _isolate;
    private bool _dormant;

    public EntityStats(Entity entity)
    {
        _entity = entity;
    }

    /// <summary>
    /// 注入 AttributeStore。Entity 在 PreWarm 中构造 store 后、AttributesCaculateFirst 之前调用。
    /// </summary>
    public void Bind(AttributeStore store)
    {
        _store = store;
    }

    // === 基础属性读（仅暴露下游真正需要的原始基准值；其余经 store 聚合） ===
    public float AttackBase => _attackBase;
    public float BaseAttackTimeBase => _baseAttackTimeBase;
    // TauntLevel：纯读 store（base 已 SetBase，无 clamp，语义值）。
    public int TauntLevel => (int)_store.GetFinal("TauntLevel");

    // === 计算属性（computed property：_store.GetFinal，O(1) 实时计算） ===
    // 流水线末段，无中间储存；store 置脏即影响下次读取，调用方不可能读到陈旧值。
    // 薄壳只做 clamp/强转/复合公式，base+mod 全在 store 内。
    public float MaxHpS => Math.Max(0.001f, _store.GetFinal("MaxHp"));
    public float DefS => Math.Max(0, _store.GetFinal("Defense"));
    public float MagicResistanceS => Math.Max(0, _store.GetFinal("MagicResistance"));
    public float PhysicalDodgeS => 1 - (1 - _physicalDodgeBase) * _store.GetFinal("PhysicalDodge");
    public float MagicDodgeS => 1 - (1 - _magicDodgeBase) * _store.GetFinal("MagicDodge");
    public int BlockOccupationS => Math.Max(0, (int)_store.GetFinal("BlockOccupation"));
    public float AttackS => Math.Max(0, _store.GetFinal("Attack"));
    public float BaseAttackTimeS => Math.Max(0.001f, _store.GetFinal("BaseAttackTime") * 100 / Math.Max(1, _store.GetFinal("AttackSpeed")));
    public int AttackNumS
    {
        get
        {
            // _attackNumBase 仅作 <0 哨兵（无限攻击次数），不参与加法；base+mod 全在 store。
            if (_attackNumBase >= 0)
                return Math.Max(0, (int)_store.GetFinal("AttackNum"));
            else
                return -1;
        }
    }
    public int AttackMinNumS => Math.Max(0, (int)_store.GetFinal("AttackMinNum"));
    public float MoveSpeedS => Math.Max(0.01f, _store.GetFinal("MoveSpeed"));

    // === 全字段接入计算属性（下游读方逐步迁移到此） ===
    // base+mod 全在 store，薄壳只做强转（int/bool/enum）。bool 读 GetFinal>0，enum 强转回枚举。
    public int DefaultCampS => (int)_store.GetFinal("DefaultCamp");
    public int CharacterRarityS => (int)_store.GetFinal("CharacterRarity");
    public int CharacterJobS => (int)_store.GetFinal("CharacterJob");
    public int MonsterStatusS => (int)_store.GetFinal("MonsterStatus");
    public bool MonsterIsPrimaryS => _store.GetFinal("MonsterIsPrimary") > 0;
    public bool MonsterCountOperatedS => _store.GetFinal("MonsterCountOperated") > 0;
    public int MonsterLevelHpConsumeS => (int)_store.GetFinal("MonsterLevelHpConsume");
    public int DamageTypeS => (int)_store.GetFinal("DamageType");
    public OrderLogic TargetPriorityS => (OrderLogic)(int)_store.GetFinal("TargetPriority");
    public bool StunImmuneS => _store.GetFinal("StunImmune") > 0;
    public bool SilenceImmuneS => _store.GetFinal("SilenceImmune") > 0;
    public bool SleepImmuneS => _store.GetFinal("SleepImmune") > 0;
    public bool FrozenImmuneS => _store.GetFinal("FrozenImmune") > 0;
    public bool LevitateImmuneS => _store.GetFinal("LevitateImmune") > 0;
    public bool DisarmedCombatImmuneS => _store.GetFinal("DisarmedCombatImmune") > 0;
    public bool FearedImmuneS => _store.GetFinal("FearedImmune") > 0;
    public bool IsStaticS => _store.GetFinal("IsStatic") > 0;
    public int CostS => (int)_store.GetFinal("Cost");
    public bool CanCallBackS => _store.GetFinal("CanCallBack") > 0;
    public bool NeedsDirectionSelectionS => _store.GetFinal("NeedsDirectionSelection") > 0;
    public int CanSetTypeS => (int)_store.GetFinal("CanSetType");
    public float RespawnTimeS => _store.GetFinal("RespawnTime");
    public int RespawnStrategyS => (int)_store.GetFinal("RespawnStrategy");
    public bool CanRespawnS => _store.GetFinal("CanRespawn") > 0;
    public float RespawnCostUpS => _store.GetFinal("RespawnCostUp");
    public int MaxOccupyCountS => (int)_store.GetFinal("MaxOccupyCount");
    public int MassLevelS => (int)_store.GetFinal("MassLevel");
    public int MoveMethodS => (int)_store.GetFinal("MoveMethod");
    public float VisionRadiusS => _store.GetFinal("VisionRadius");

    // === HP ===
    public float CurrentHp => _currentHpRate * MaxHpS;
    public float CurrentHpRate
    {
        get => _currentHpRate;
        set => _currentHpRate = Math.Min(1, value);
    }
    public float HpRecover => Math.Max(0, _store.GetFinal("HpRecover"));

    // === 状态标志 ===
    public bool IsActive
    {
        get => _participateIn;
        set => _participateIn = value;
    }
    public int Hurtable
    {
        get => _hurtable;
        set => _hurtable = value;
    }
    public int Selectable
    {
        get => _selectable;
        set => _selectable = value;
    }
    public bool IsIsolated => _isolate;
    public bool IsDormant => _dormant;

    // === 计数变更（替代原 selectable++/hurtable++ 写法） ===
    public void AddSelectable(int delta) { _selectable += delta; }
    public void AddHurtable(int delta) { _hurtable += delta; }

    // === 从 EntityData 装填战斗基准属性（PreWarm 时调用一次，整场战斗不变） ===
    // 流水线：EntityData 原始值 → [关卡环境"基础数值修改" buff] → _xxxBase
    // 战斗过程 buff 通过 XxxS computed property 在访问时实时计算。
    public void AttributesCaculateFirst(EntityData data)
    {
        // [关卡环境"基础数值修改" buff]——尚未接入。设计上应作为永久 Modifier 组注入 store，
        // 与战斗 buff 同机制（区别仅在生命周期），而非并入 SetBase（并入会抹掉"固有基础 vs 环境修正"的区分）。
        // TODO(level-base-buffs): 关卡环境基础 buff 落地后，在此作为永久 group AddFlat/AddPercent 即可。

        // 仍需字段的 base（Dodge 固有值参与薄壳复合公式；AttackNum 仅作 <0 哨兵；Attack/BaseAttackTime 下游读原始基准）。
        _physicalDodgeBase = data.PhysicalDodge;
        _magicDodgeBase = data.MagicDodge;
        _attackNumBase = data.AttackNum;
        _attackBase = data.Attack;
        _baseAttackTimeBase = data.BaseAttackTime;

        // === 注入基础值到 AttributeStore ===
        // 所有数值属性的 base 一律进 store（含 bool 转 0/1、enum 转 (int)），XxxS 薄壳只读 GetFinal。
        // AttackSpeed base=100（"100 攻速=正常速度"，设计常量，非来自 EntityData）。
        // HpRecover base=0（纯增量属性）。Dodge/Rate 类属性 base 见下方说明：
        //   Dodge base=1：Final 是"未命中乘数"（无 buff=1），薄壳 1-(1-固有)*Final 还原闪避。
        //   DamageRate base=1：无减免 buff 时 Final=1，伤害不变。
        // AttackMinNum base=0：EntityData 未暴露此字段，留 0 兼容。
        _store.SetBase("MaxHp", data.MaxHp);
        _store.SetBase("Defense", data.Defense);
        _store.SetBase("MagicResistance", data.MagicResistance);
        _store.SetBase("PhysicalDodge", 1f);
        _store.SetBase("MagicDodge", 1f);
        _store.SetBase("BlockOccupation", data.BlockOccupation);
        _store.SetBase("Attack", data.Attack);
        _store.SetBase("BaseAttackTime", data.BaseAttackTime);
        _store.SetBase("AttackSpeed", 100f);
        _store.SetBase("AttackNum", data.AttackNum);
        _store.SetBase("AttackMinNum", 0f);
        _store.SetBase("MoveSpeed", data.MoveSpeed);
        _store.SetBase("HpRecover", 0f);
        _store.SetBase("PhysicalDamageRate", 1f);
        _store.SetBase("MagicDamageRate", 1f);
        // === 全字段接入：以下 base = data 值（含 bool/enum 已转 0/1/int） ===
        _store.SetBase("TauntLevel", data.TauntLevel);
        _store.SetBase("DefaultCamp", data.DefaultCamp);
        _store.SetBase("CharacterRarity", data.CharacterRarity);
        _store.SetBase("CharacterJob", data.CharacterJob);
        _store.SetBase("MonsterStatus", data.MonsterStatus);
        _store.SetBase("MonsterIsPrimary", data.MonsterIsPrimary ? 1f : 0f);
        _store.SetBase("MonsterCountOperated", data.MonsterCountOperated ? 1f : 0f);
        _store.SetBase("MonsterLevelHpConsume", data.MonsterLevelHpConsume);
        _store.SetBase("DamageType", data.DamageType);
        _store.SetBase("TargetPriority", (int)data.TargetPriority);
        _store.SetBase("StunImmune", data.StunImmune ? 1f : 0f);
        _store.SetBase("SilenceImmune", data.SilenceImmune ? 1f : 0f);
        _store.SetBase("SleepImmune", data.SleepImmune ? 1f : 0f);
        _store.SetBase("FrozenImmune", data.FrozenImmune ? 1f : 0f);
        _store.SetBase("LevitateImmune", data.LevitateImmune ? 1f : 0f);
        _store.SetBase("DisarmedCombatImmune", data.DisarmedCombatImmune ? 1f : 0f);
        _store.SetBase("FearedImmune", data.FearedImmune ? 1f : 0f);
        _store.SetBase("IsStatic", data.IsStatic ? 1f : 0f);
        _store.SetBase("Cost", data.Cost);
        _store.SetBase("CanCallBack", data.CanCallBack ? 1f : 0f);
        _store.SetBase("NeedsDirectionSelection", data.NeedsDirectionSelection ? 1f : 0f);
        _store.SetBase("CanSetType", data.CanSetType);
        _store.SetBase("RespawnTime", data.RespawnTime);
        _store.SetBase("RespawnStrategy", data.RespawnStrategy);
        _store.SetBase("CanRespawn", data.CanRespawn ? 1f : 0f);
        _store.SetBase("RespawnCostUp", data.RespawnCostUp);
        _store.SetBase("MaxOccupyCount", data.MaxOccupyCount);
        _store.SetBase("MassLevel", data.MassLevel);
        _store.SetBase("MoveMethod", data.MoveMethod);
        _store.SetBase("VisionRadius", data.VisionRadius);
    }

    // === HP 自然恢复（原 Entity.FixedUpdate 中 current_hp_rate < 1 分支） ===
    public void RecoverTick()
    {
        if (_currentHpRate < 1)
        {
            _currentHpRate = Math.Min(1, _currentHpRate + HpRecover / MaxHpS);
        }
    }

    // === 死亡判定（原 Entity.HPUpdate） ===
    public void CheckDeath()
    {
        if (CurrentHp <= 0)
        {
            _entity.Die();
        }
    }

    /// <summary>
    /// 死亡开始。触发 OnBeforeDieAnimation 事件并标记 IsActive=false。
    /// AM 状态切换与音频由 Entity.Die() 负责。
    /// </summary>
    public void BeginDie()
    {
        _entity.RaiseOnBeforeDieAnimation();
        _participateIn = false;
    }

    /// <summary>
    /// 受到伤害。完全替代原 Entity.TakeDamage。
    /// 事件 OnBeforeHurt/OnAfterHurt 通过 _entity 桥触发（ref 参数由 Entity 透传）。
    /// 返回：true=本次伤害致命，false=未致命/被闪避/治疗。
    /// </summary>
    public bool ApplyDamage(Entity damageOrigin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType)
    {
        if (damageType <= 2 && (_currentHpRate <= 0 || _hurtable > 0))
            return false;
        float minRate = 0.05f;
        // Def/Mgr 在 damageC1 与 finalDamage 两个分支都要用，各取一次复用（避免重复 GetFinal 查表）。
        float def = DefS;
        float mgr = MagicResistanceS;
        float damageC1 = damageType switch
        {
            0 => Math.Max(damage * minRate, damage - def),
            1 => Math.Max(damage * minRate, damage * (1 - mgr / 100)),
            2 => damage,
            3 => damage,
            _ => 0,
        };
        float finalDamage = damageType switch
        {
            0 => Math.Max(damage * multiplyer * minRate, damage * multiplyer - (1 - defPenetrate) * (def - defPenetrate_value)) * Math.Max(0, _store.GetFinal("PhysicalDamageRate")),
            1 => Math.Max(damage * multiplyer * minRate, damage * multiplyer * (1 - (1 - mgrPenetrate) * (mgr - mgrPenetrate_value) / 100)) * Math.Max(0, _store.GetFinal("MagicDamageRate")),
            2 => damage * multiplyer,
            3 => damage * multiplyer,
            _ => 0,
        };
        //记录伤害
        LevelMessagePanel.Panel.AcceptDamageMessage(_entity, damageOrigin, finalDamage, damageType);
        //显示伤害
        if (damageType <= 2)
        {
            //判断闪避
            if ((damageType == 0 && RandomHelper.Helper.RandomP(PhysicalDodgeS)) || (damageType == 1 && RandomHelper.Helper.RandomP(MagicDodgeS)))
            {
                LevelMessagePanel.Panel.ShowText(_entity.transform.position, 5, 0);
                return false;
            }
            _entity.RaiseOnBeforeHurt(damageOrigin, ref finalDamage, ref multiplyer, ref defPenetrate, ref mgrPenetrate, ref defPenetrate_value, ref mgrPenetrate_value, ref damageType, applyType);
            if (finalDamage >= 1.5f * damageC1)
            {
                LevelMessagePanel.Panel.ShowText(_entity.transform.position, 0, (int)finalDamage);
            }
            _currentHpRate -= finalDamage / MaxHpS;
            if (_currentHpRate <= 0)
            {
                _currentHpRate = 0;
                _entity.RaiseOnAfterHurt(damageOrigin, finalDamage, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, applyType, true);
                return true;
            }
            _entity.RaiseOnAfterHurt(damageOrigin, finalDamage, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, applyType, false);
            return false;
        }
        else
        {
            _currentHpRate = Math.Min(1, _currentHpRate + finalDamage / MaxHpS);
            LevelMessagePanel.Panel.ShowText(_entity.Movement.Position, 1, (int)finalDamage);
            return false;
        }
    }

    // === 池激活时重置状态（原 Entity.Initialize 中的 current_hp_rate=1/participateIn=true/...） ===
    public void ResetState()
    {
        _currentHpRate = 1;
        _participateIn = true;
        _hurtable = 0;
        _selectable = 0;
        _isolate = false;
        _dormant = false;
    }
}
