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
    // AttackBase/BaseAttackTimeBase：下游 AttackBase 当"原始基准"读（与聚合值 XxxS 并列），故留字段+公开。
    // TauntLevel：无 store 入口，字段是唯一存储。
    // BlockOccupation/AttackNum/AttackMinNum：XxxS = _xxxBase + (int)GetFinal(同 attr)，字段参与聚合（见对应 property）。
    // Dodge 两项：store base=0（modifier 存未命中概率），固有闪避留字段、在 PhysicalDodgeS/MagicDodgeS 合并。
    // Defense/MagicResistance/MaxHp/MoveSpeed 的基础值直接经 SetBase 进 store，XxxS 全读 store，不再留字段。
    private float _physicalDodgeBase;
    private float _magicDodgeBase;
    private int _blockOccupationBase;
    private int _tauntLevelBase;
    private float _attackBase;
    private float _baseAttackTimeBase;
    private int _attackNumBase;
    private int _attackMinNumBase;

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
    public int TauntLevel => _tauntLevelBase;  // 嘲讽等级无战斗 buff，亦无 store 入口

    // === 计算属性（computed property：_store.GetFinal，O(1) 实时计算） ===
    // 流水线末段，无中间储存；store 置脏即影响下次读取，调用方不可能读到陈旧值。
    public float MaxHpS => Math.Max(0.001f, _store.GetFinal("MaxHp"));
    public float DefS => Math.Max(0, _store.GetFinal("Defense"));
    public float MagicResistanceS => Math.Max(0, _store.GetFinal("MagicResistance"));
    public float PhysicalDodgeS => 1 - (1 - _physicalDodgeBase) * _store.GetFinal("PhysicalDodge");
    public float MagicDodgeS => 1 - (1 - _magicDodgeBase) * _store.GetFinal("MagicDodge");
    public int BlockOccupationS => Math.Max(0, _blockOccupationBase + (int)_store.GetFinal("BlockOccupation"));
    public float AttackS => Math.Max(0, _store.GetFinal("Attack"));
    public float BaseAttackTimeS => Math.Max(0.001f, _store.GetFinal("BaseAttackTime") * 100 / Math.Max(1, _store.GetFinal("AttackSpeed")));
    public int AttackNumS
    {
        get
        {
            if (_attackNumBase >= 0)
                return Math.Max(0, _attackNumBase + (int)_store.GetFinal("AttackNum"));
            else
                return -1;
        }
    }
    public int AttackMinNumS => Math.Max(0, _attackMinNumBase + (int)_store.GetFinal("AttackMinNum"));
    public float MoveSpeedS => Math.Max(0.01f, _store.GetFinal("MoveSpeed"));

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

        // 写入仍需字段的 base（XxxS 或下游会读原始基准）；Defense/MagicResistance/MaxHp/MoveSpeed 的基础值直接进 store。
        _physicalDodgeBase = data.PhysicalDodge;
        _magicDodgeBase = data.MagicDodge;
        _blockOccupationBase = data.BlockOccupation;
        _tauntLevelBase = data.TauntLevel;
        _attackBase = data.Attack;
        _baseAttackTimeBase = data.BaseAttackTime;
        _attackNumBase = data.AttackNum;
        _attackMinNumBase = 0;  // EntityData 未暴露此字段，留 0 兼容（无 atkminn_delta_value 数据源时恒为 0）

        // === 注入基础值到 AttributeStore ===
        // AttackSpeed base=100（"100 攻速=正常速度"，设计常量，非来自 EntityData）。
        // HpRecover base=0（纯增量属性）。Dodge/Rate 类属性 base 见下方说明：
        //   Dodge base=0：modifier 存未命中概率(1-旧值)，基础闪避在下游 _xxxDodgeBase 体现。
        //   DamageRate base=1：无减免 buff 时 Final=1，伤害不变。
        _store.SetBase("MaxHp", data.MaxHp);
        _store.SetBase("Defense", data.Defense);
        _store.SetBase("MagicResistance", data.MagicResistance);
        _store.SetBase("PhysicalDodge", 0f);
        _store.SetBase("MagicDodge", 0f);
        _store.SetBase("BlockOccupation", _blockOccupationBase);
        _store.SetBase("Attack", _attackBase);
        _store.SetBase("BaseAttackTime", _baseAttackTimeBase);
        _store.SetBase("AttackSpeed", 100f);
        _store.SetBase("AttackNum", _attackNumBase);
        _store.SetBase("AttackMinNum", _attackMinNumBase);
        _store.SetBase("MoveSpeed", data.MoveSpeed);
        _store.SetBase("HpRecover", 0f);
        _store.SetBase("PhysicalDamageRate", 1f);
        _store.SetBase("MagicDamageRate", 1f);
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
            LevelMessagePanel.Panel.ShowText(_entity.EntityPosition, 1, (int)finalDamage);
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
