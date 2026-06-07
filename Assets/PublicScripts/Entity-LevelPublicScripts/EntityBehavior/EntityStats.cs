using System;
using MyUI;

/// <summary>
/// 实体属性子系统（POCO）。
/// 持有：
///   1. 基础属性（_Base/原 _first，来自 EntityData）
///   2. 含 buff 的计算属性（_S/原 _second）
///   3. 状态（HP rate、participateIn、hurtable、selectable、isolate、dormant）
///
/// 设计要点：
///   - POCO，无 MonoBehaviour 依赖。构造接受 Entity 引用作为事件桥。
///   - 事件触发走 Entity 上的 internal RaiseOnXxx 桥方法——保留 Entity.OnBeforeHurt/OnAfterHurt/OnBeforeDieAnimation 公开事件 API。
///   - BuffController 由 Entity 在 PreWarm 中通过 BindBuffController 显式注入（EntityStats 构造早于 BuffController 获取）。
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

    // === 计算属性（含 buff；原 _second） ===
    private float _maxHpS;
    private float _defS;
    private float _magicResistanceS;
    private float _physicalDodgeS;
    private float _magicDodgeS;
    private int _blockOccupationS;
    private int _tauntLevelS;

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

    // === 计算属性读（替代 Entity.DEF_2 等） ===
    public float MaxHpS => _maxHpS;
    public float DefS => _defS;
    public float MagicResistanceS => _magicResistanceS;
    public float PhysicalDodgeS => _physicalDodgeS;
    public float MagicDodgeS => _magicDodgeS;
    public int BlockOccupationS => _blockOccupationS;
    public int TauntLevel => _tauntLevelS;

    // === HP ===
    public float CurrentHp => _currentHpRate * _maxHpS;
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

    // === 从 EntityData 装填基础属性（原 AttributesCaculateFirst 的属性部分） ===
    public void AttributesCaculateFirst(EntityData data)
    {
        _maxHpBase = data.MaxHp;
        _defBase = data.Defense;
        _magicResistanceBase = data.MagicResistance;
        _physicalDodgeBase = data.PhysicalDodge;
        // 注：原 Entity.cs:196 写的是 _mgDoge_first = EntityData.MagicResistance——明显 copy-paste bug（应为 MagicDodge）。
        // 重构期保留原行为以避免引入静默差异；留待 Task 2.5 清理 PR 修复。
        _magicDodgeBase = data.MagicResistance;
        _blockOccupationBase = data.BlockOccupation;
        _tauntLevelBase = data.TauntLevel;
    }

    // === 用 buff 重新计算所有 _second 属性（原 Entity.AttributesCaculateSecond） ===
    public void AttributesCaculateSecond()
    {
        _maxHpS = Math.Max(0.001f, _maxHpBase + _buffController.buffValue[BuffType.mhp_delta_value] + _maxHpBase * _buffController.buffValue[BuffType.mhp_delta_percent]);
        _defS = _defBase + Math.Max(0, _buffController.buffValue[BuffType.def_delta_value] + _defBase * _buffController.buffValue[BuffType.def_delta_percent]);
        _magicResistanceS = Math.Max(0, _magicResistanceBase + _buffController.buffValue[BuffType.mgr_delta_value] + _magicResistanceBase * _buffController.buffValue[BuffType.mgr_delta_percent]);
        _physicalDodgeS = 1 - (1 - _physicalDodgeBase) * (1 - _buffController.buffValue[BuffType.phdoge_delta_rate]);
        _magicDodgeS = 1 - (1 - _magicDodgeBase) * (1 - _buffController.buffValue[BuffType.mgdoge_delta_rate]);
        _blockOccupationS = Math.Max(0, _blockOccupationBase + (int)_buffController.buffValue[BuffType.blo_delta_value]);
        _tauntLevelS = _tauntLevelBase;
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
            0 => Math.Max(damage * minRate, damage - _defS),
            1 => Math.Max(damage * minRate, damage * (1 - _magicResistanceS / 100)),
            2 => damage,
            3 => damage,
            _ => 0,
        };
        float finalDamage = damageType switch
        {
            0 => Math.Max(damage * multiplyer * minRate, damage * multiplyer - (1 - defPenetrate) * (_defS - defPenetrate_value)) * Math.Max(0, 1 + _buffController.buffValue[BuffType.phd_delta_rate]),
            1 => Math.Max(damage * multiplyer * minRate, damage * multiplyer * (1 - (1 - mgrPenetrate) * (_magicResistanceS - mgrPenetrate_value) / 100)) * Math.Max(0, 1 + _buffController.buffValue[BuffType.mgd_delta_rate]),
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
            if ((damageType == 0 && RandomHelper.Helper.RandomP(_physicalDodgeS)) || (damageType == 1 && RandomHelper.Helper.RandomP(_magicDodgeS)))
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
