using System;
using MyUI;

/// <summary>
/// 实体属性子系统（POCO）。
/// 持有：
///   1. 战斗基准属性（_xxxBase：EntityData 原始值 + 关卡环境"基础数值修改" buff，整场战斗不变；储存字段）
///   2. 含战斗过程 buff 的计算属性（XxxS：computed property，每次访问实时计算 _xxxBase + 战斗 buff）
///   3. 状态（HP rate、participateIn、hurtable、selectable、isolate、dormant）
///
/// 数据计算流水线（两类 buff 都走现成 BuffController.buffValue）：
///   EntityData 原始值 → [+ 关卡环境基础 buff] → _xxxBase（字段）→ [+ 战斗过程 buff] → XxxS（property）
///
/// 设计要点：
///   - POCO，无 MonoBehaviour 依赖。构造接受 Entity 引用作为事件桥。
///   - 事件触发走 Entity 上的 internal RaiseOnXxx 桥方法——保留 Entity.OnBeforeHurt/OnAfterHurt/OnBeforeDieAnimation 公开事件 API。
///   - BuffController 由 Entity 在 PreWarm 中通过 BindBuffController 显式注入（EntityStats 构造早于 BuffController 获取）。
///   - AttributesCaculateFirst 在 PreWarm 时调用一次，整场战斗不再重算（除非重新进入关卡重建实体）。
///   - XxxS 为 computed property，buff 变化时无需手动重算；移除后调用方不可能读到陈旧值。
/// </summary>
public class EntityStats
{
    private readonly Entity _entity;
    private BuffController _buffController;

    // === 基础属性（来自 EntityData；原 _first） ===
    private float _maxHpBase;
    private float _defBase;
    private float _magicResistanceBase;
    private float _physicalDodgeBase;
    private float _magicDodgeBase;
    private int _blockOccupationBase;
    private int _tauntLevelBase;

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
    /// 注入 BuffController。Entity 在 PreWarm 中、buffController 被获取后调用。
    /// </summary>
    public void BindBuffController(BuffController bc)
    {
        _buffController = bc;
    }

    // === 基础属性读（替代 Entity.DEF_1 等） ===
    public float DefBase => _defBase;
    public float MagicResistanceBase => _magicResistanceBase;
    public float MaxHpBase => _maxHpBase;
    public int BlockOccupationBase => _blockOccupationBase;

    // === 计算属性（computed property：_xxxBase + 战斗过程 buff，O(1) 实时计算） ===
    // 流水线末段，无中间储存；buff 变化时无需手动重算，调用方不可能读到陈旧值。
    public float MaxHpS => Math.Max(0.001f, _maxHpBase + _buffController.buffValue[BuffType.mhp_delta_value] + _maxHpBase * _buffController.buffValue[BuffType.mhp_delta_percent]);
    public float DefS => _defBase + Math.Max(0, _buffController.buffValue[BuffType.def_delta_value] + _defBase * _buffController.buffValue[BuffType.def_delta_percent]);
    public float MagicResistanceS => Math.Max(0, _magicResistanceBase + _buffController.buffValue[BuffType.mgr_delta_value] + _magicResistanceBase * _buffController.buffValue[BuffType.mgr_delta_percent]);
    public float PhysicalDodgeS => 1 - (1 - _physicalDodgeBase) * (1 - _buffController.buffValue[BuffType.phdoge_delta_rate]);
    public float MagicDodgeS => 1 - (1 - _magicDodgeBase) * (1 - _buffController.buffValue[BuffType.mgdoge_delta_rate]);
    public int BlockOccupationS => Math.Max(0, _blockOccupationBase + (int)_buffController.buffValue[BuffType.blo_delta_value]);
    public int TauntLevel => _tauntLevelBase;  // 嘲讽等级无战斗 buff

    // === HP ===
    public float CurrentHp => _currentHpRate * MaxHpS;
    public float CurrentHpRate
    {
        get => _currentHpRate;
        set => _currentHpRate = Math.Min(1, value);
    }
    public float HpRecover => Math.Max(0, _buffController.buffValue[BuffType.hprecover_delta_value]);

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
        // 1. 从 EntityData 读取原始值（局部变量，便于下一步插值）
        float maxHp = data.MaxHp;
        float def = data.Defense;
        float magicResistance = data.MagicResistance;
        float physicalDodge = data.PhysicalDodge;
        float magicDodge = data.MagicDodge;
        int blockOccupation = data.BlockOccupation;
        int tauntLevel = data.TauntLevel;

        // 2. [关卡环境"基础数值修改" buff]——使用现成 BuffController.buffValue，语法与 Second 阶段一致。
        //    预期 BuffType 新增：mhp_base_delta_value / mhp_base_delta_percent / def_base_delta_value / ...
        //    储存尚未实现，暂跳过。储存就位后，在此累加 buff value 即可。
        // TODO(level-base-buffs): 接入 _buffController.buffValue[BuffType.xxx_base_delta_value] / xxx_base_delta_percent

        // 3. 写入 _xxxBase（base-buff 系统就位后，这里存的就是"原始值 + 关卡环境 buff"的结果）
        _maxHpBase = maxHp;
        _defBase = def;
        _magicResistanceBase = magicResistance;
        _physicalDodgeBase = physicalDodge;
        _magicDodgeBase = magicDodge;
        _blockOccupationBase = blockOccupation;
        _tauntLevelBase = tauntLevel;
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
        float damageC1 = damageType switch
        {
            0 => Math.Max(damage * minRate, damage - DefS),
            1 => Math.Max(damage * minRate, damage * (1 - MagicResistanceS / 100)),
            2 => damage,
            3 => damage,
            _ => 0,
        };
        float finalDamage = damageType switch
        {
            0 => Math.Max(damage * multiplyer * minRate, damage * multiplyer - (1 - defPenetrate) * (DefS - defPenetrate_value)) * Math.Max(0, 1 + _buffController.buffValue[BuffType.phd_delta_rate]),
            1 => Math.Max(damage * multiplyer * minRate, damage * multiplyer * (1 - (1 - mgrPenetrate) * (MagicResistanceS - mgrPenetrate_value) / 100)) * Math.Max(0, 1 + _buffController.buffValue[BuffType.mgd_delta_rate]),
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
