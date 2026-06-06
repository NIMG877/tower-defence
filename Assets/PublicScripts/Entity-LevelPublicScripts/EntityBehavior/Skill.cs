using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Skill : MonoBehaviour, IPoolOperation
{
    protected Entity _thisEntity;
    [Header("技能显示UI相关")]
    [SerializeField, Tooltip("技能名称")] private string _skillName;
    [SerializeField, Tooltip("技能图标")] private Sprite _skillImg;
    [SerializeField, Tooltip("技能描述"), TextArea(2, 5)] private string _skillDescription;
    [Header("技能基础属性设置")]
    [SerializeField, Tooltip("技力回复模式:0-自然回复,1-攻击回复,2-受击回复,3-其它回复方法")] protected int _spRecoverMode;
    [SerializeField, Tooltip("技力消耗模式:0-自然消耗,1-攻击消耗,2-受击消耗,3-不消耗,4-其它消耗方法")] protected int _spConsumeMode;
    [SerializeField, Tooltip("技能开启模式:0-自然开启,1-攻击开启,2-受击开启,3-手动开启,4-其它开启方法")] protected int _skillOpenMode;
    [SerializeField, Tooltip("技能充能层数")] protected int _chargeNum;
    [SerializeField, Tooltip("总技力需求")] protected int _totalSp;
    [SerializeField, Tooltip("初始技力")] protected int _initialSp;
    [SerializeField, Tooltip("技能量:当值大于0时正常消耗，等于0时瞬间消耗")] protected float _skillAmount;
    [SerializeField, Tooltip("技能期间持有阻回")] protected bool _recoverForbidDuringSkill;
    [SerializeField, Tooltip("技能可手动关闭")] protected bool _canCloseSkill;
    [Header("技能详细设置")]
    [SerializeField, Tooltip("技能期间攻击范围")] protected Vector2Int[] _skillAttackRange;
    private float _currentSkillAmount;
    private int _recoverForbid;
    private float _currentSp;
    private int _currentChargeNum;
    public (float currentSpRate, int currentChargeNum, bool isSkill) SkillMessage { get { if (_currentSkillAmount > 0) return (_currentSkillAmount / _skillAmount, _currentChargeNum, true); else return (_currentSp / _totalSp, _currentChargeNum, false); } }
    public string SkillName { get { return _skillName; } }
    public Sprite SkillImg { get { return _skillImg; } }
    public string SkillDescription { get { return _skillDescription; } }
    public int SpRecoverMode { get { return _spRecoverMode; } }
    public int SkillOpenMode { get { return _skillOpenMode; } }
    public int SpComsumeMode { get { return _spConsumeMode; } }
    public int TotalSp { get { return _totalSp; } }
    public int InitialSp { get { return _initialSp; } }
    public bool CanCloseSkill { get { return _canCloseSkill; } }
    public float SkillAmount { get { return _skillAmount; } }
    public (int x, int y)[] SkillAttackRange
    {
        get
        {
            if (_skillAttackRange != null)
            {
                (int x, int y)[] atkRange = new (int x, int y)[_skillAttackRange.Length];
                for (int i = 0; i < atkRange.Length; i++)
                {
                    atkRange[i].x = _skillAttackRange[i].x;
                    atkRange[i].y = _skillAttackRange[i].y;
                }
                return atkRange;
            }
            else
            {
                return null;
            }
        }
    }
    protected virtual void FixedUpdate()
    {
        if (_spRecoverMode == 0)
        {
            SpRecover(Time.fixedDeltaTime, false);
        }
        if (_spConsumeMode == 0)
        {
            SkillAmountConsume(Time.fixedDeltaTime, false);
        }
        if (_currentSp == _totalSp && _skillOpenMode == 0)
        {
            SkillBegin();
        }
    }
    public void SkilllRecoverForbid(bool forbid)
    {
        if (forbid)
            _recoverForbid += 1;
        else if (_recoverForbid > 0)
            _recoverForbid -= 1;
    }
    public void SpRecover(float value, bool show)
    {
        if (_chargeNum <= 1)
        {
            if (_recoverForbid == 0 && _currentSp < _totalSp)
            {
                _currentSp += value;
                if (_currentSp > _totalSp)
                {
                    _currentSp = _totalSp;
                }
            }
        }
        else
        {
            if (_recoverForbid == 0 && _currentSp < _totalSp && _currentChargeNum < _chargeNum)
            {
                _currentSp += value;
                if (_currentSp >= _totalSp)
                {
                    int addChargeNum = (int)(_currentSp / _totalSp);
                    _currentChargeNum += addChargeNum;
                    _currentSp -= _totalSp * addChargeNum;
                    if (_currentChargeNum >= _chargeNum)
                    {
                        _currentChargeNum = _chargeNum;
                        _currentSp = 0;
                    }
                }
            }
        }
    }
    public void SkillAmountConsume(float value, bool show)
    {
        if (_currentSkillAmount > 0)
        {
            if (value > 0)
            {
                _currentSkillAmount -= value;
                if (_currentSkillAmount <= 0)
                {
                    SkillEnd();
                }
            }
        }
        else if (_currentSkillAmount < 0)
        {
            SkillEnd();
        }
    }
    public bool SkillCanBegin()
    {
        return _totalSp * _currentChargeNum + _currentSp >= _totalSp && _currentSkillAmount <= 0;
    }
    public bool SPFull()
    {
        if (_chargeNum <= 1)
        {
            if (_currentSp >= _totalSp)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        else
        {
            if (_currentChargeNum == _chargeNum)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
    public virtual bool SkillBegin()
    {
        if (!SkillCanBegin())
            return false;
        if (_recoverForbidDuringSkill)
        {
            SkilllRecoverForbid(true);
        }
        if (_currentChargeNum == 0)
        {
            _currentSp = 0;
        }
        else
        {
            _currentChargeNum--;
        }
        if (_skillAmount > 0)
        {
            _currentSkillAmount = _skillAmount;
        }
        else
        {
            _currentSkillAmount = -1;
        }
        if (SkillAttackRange != null && SkillAttackRange.Length > 0)
        {
            _thisEntity.VisionRange = SkillAttackRange;
        }
        return true;
    }
    public virtual void SkillEnd()
    {
        if (_recoverForbidDuringSkill)
        {
            SkilllRecoverForbid(false);
        }
        if (SkillAttackRange != null)
        {
            _thisEntity.VisionRange = _thisEntity.VisionRange_1;
        }
        _currentSkillAmount = 0;
    }
    public virtual void PreWarm()
    {
        if (this)
        {
            _thisEntity = this.gameObject.GetComponent<Entity>();
        }
    }
    public virtual void Initialize()
    {
        //将当前技力设置为初始技力
        _currentSp = _initialSp;
        //设置技能充能层数
        _currentChargeNum = 0;
        //取消阻回
        _recoverForbid = 0;
        //将当前技能量置为0
        _currentSkillAmount = 0;
        //根据不同的技力回复模式设置
        switch (_spRecoverMode)
        {
            case 1:
                _thisEntity.AttackBase.OnAttackSuccessfully += new AttackBase.OperationsOnAttackSuccessfully(() =>
                {
                    Debug.Log("add");
                    SpRecover(1, false);
                });
                break;
            case 2:
                _thisEntity.OnAfterHurt += new Entity.OperationsAfterHurt((Entity origin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly) =>
                {
                    if (applyType == 0 || applyType == 1)
                    {
                        SpRecover(1, false);
                    }
                });
                break;
            default: break;
        }

        //根据不同的技力消耗模式设置
        switch (_spConsumeMode)
        {
            case 1:
                _thisEntity.AttackBase.OnAttackSuccessfully += new AttackBase.OperationsOnAttackSuccessfully(() =>
                {
                    SkillAmountConsume(1, false);
                });
                break;//如果技力消耗模式是攻击消耗，设置单次消耗量
            case 2:
                _thisEntity.OnAfterHurt += new Entity.OperationsAfterHurt((Entity origin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly) =>
                {
                    if (applyType == 0 || applyType == 1)
                    {
                        SkillAmountConsume(1, false);
                    }
                });
                break;//如果技力消耗模式是受击消耗，设置单次消耗量
            case 3:
                _skillAmount = 1;
                break;
            default: break;
        }

        //根据不同的技能开启模式设置
        switch (_skillOpenMode)
        {
            case 1:
                _thisEntity.entityAM.OnAttackAnimationBegin += new AnimationMachine.OperationsOnAttackAnimationBegin(() =>
                {
                    if (_currentSp >= _totalSp)
                    {
                        SkillBegin();
                    }
                });
                break;
            case 2:
                _thisEntity.OnBeforeHurt += new Entity.OperationsBeforeHurt((Entity origin, ref float damage, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int damageType, int applyType) =>
                {
                    if (applyType == 0 || applyType == 1)
                    {
                        if (_currentSp >= _totalSp)
                        {
                            SkillBegin();
                        }
                    }
                });
                break;
            case 3: break;
            default: break;
        }
    }
    public virtual void Dormancy()
    {

    }
}
