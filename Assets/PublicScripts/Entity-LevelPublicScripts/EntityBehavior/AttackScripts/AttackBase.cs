using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;

public class AttackBase : MonoBehaviour, IPoolOperation
{
    [Serializable]
    public struct AttackEffectData
    {
        public BulletData BulletData;
        public Transform BulletSpawnTransform;
        public GameObject AttackEffect;
    }


    public delegate void OperationsOnAttackInterrupt();
    /// <summary>
    /// �ڹ�����ʧȥĿ��������ʱ���ã�һ�ι������������ഥ��һ�Σ�
    /// </summary>
    public event OperationsOnAttackInterrupt OnAttackInterrupt;
    public delegate void OperationsOnAttackSuccessfully();
    /// <summary>
    /// �ڹ����ɹ�ѡ��Ŀ�겢�Ѿ�����˺�ʱ(�޵���)������ӵ�ʱ���е��������ã��������ڻ��ڹ��������ļ�⣨һ�ι������������ഥ��һ�Σ�
    /// </summary>
    public event OperationsOnAttackSuccessfully OnAttackSuccessfully;
    public delegate void OperationsBeforeAttack(Entity target, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int cumbo, ref int damageType, int applyType);//����ǰ�¼�
    /// <summary>
    /// �ڹ���ÿһ��Ŀ��֮ǰ������ã����Ը���Ŀ���������ʻ����ã�һ�ι��������ڿɴ�����Σ�����ĳ��Ŀ�����һ�Σ�
    /// </summary>
    public event OperationsBeforeAttack OnBeforeAttack;
    public delegate void OperationsBeforeTakeDamage(Entity target, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int damageType, int applyType);//����˺�ǰ�¼�
    /// <summary>
    /// �ڶ�ÿһ��Ŀ������˺�֮ǰ���ã����Ը���Ŀ���������ʻ����ã�һ�ι��������ڿɴ�����Σ�����ĳ��Ŀ�����һ�Σ�
    /// </summary>
    public event OperationsBeforeTakeDamage OnBeforeTakeDamage;
    public delegate void OperationsAfterAttack(Entity target, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly);//�������¼�
    /// <summary>
    /// �ڹ���ÿһ��Ŀ��֮�󶼻���ã����Լ�⵱�ι����Ƿ������Ŀ��������һ�ι��������ڿɴ�����Σ�����ĳ��Ŀ�����һ�Σ�
    /// </summary>
    public event OperationsAfterAttack OnAfterAttack;
    public delegate void OperationsAfterTakeDamage(Entity target, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly);//����˺����¼�
    /// <summary>
    /// �ڶ�ÿһ��Ŀ������˺�֮����ã����Լ�⵱�ι����Ƿ������Ŀ��������һ�ι��������ڿɴ�����Σ�����ĳ��Ŀ�����һ�Σ�
    /// </summary>
    public event OperationsAfterTakeDamage OnAfterTakeDamage;
    public delegate void OperationsOnBeforeTargetSelect(List<Entity> targets, ref int selectMaxNum, ref int selectMinNum, ref bool sameComp);
    /// <summary>
    /// ������֮ǰ���ã��ڳ��Թ���ʱ������,�����޸�����Դʵ���б������������������С����������ͬ��Ӫ���
    /// </summary>
    public event OperationsOnBeforeTargetSelect OnBeforeTargetSelect;
    /// <summary>
    /// ������֮����ã��ڳ��Թ���ʱ������,�����޸����н���б�����ȡ���������������С����������ͬ��Ӫ���
    /// </summary>
    public delegate void OperationsOnAfterTargetSelect(List<Entity> targets, int selectMaxNUm, int selectMinNum, bool sameComp);
    public event OperationsOnAfterTargetSelect OnAfterTargetSelect;



    public OrderLogic EntityOrderLogic;
    public int DamageType;
    [HideInInspector] public AttackEffectData _attackEffectData;
    [SerializeField] private AttackEffectData _attackEffectData0;
    public (int x, int y)[] AttackRangeS
    {
        get
        {
            return _attackRangeF;
        }
    }
    public float AttackRadiusS
    {
        get
        {
            return _attackRadiusF;
        }
    }
    public float AttackDamageF
    {
        get
        {
            return _attackDamageF;
        }
    }
    public float AttackDamageS
    {
        get
        {
            return Math.Max(0, _attackDamageF + _thisEntity.buffController.buffValue[BuffType.atk_delta_value] + _attackDamageF * _thisEntity.buffController.buffValue[BuffType.atk_delta_percent]);
        }
    }
    public float BaseAttackTimeS
    {
        get
        {
            return Math.Max(0.001f, (_baseAttackTimeF + _thisEntity.buffController.buffValue[BuffType.batkt_delta_value] + _baseAttackTimeF * _thisEntity.buffController.buffValue[BuffType.batkt_delta_percent]) * 100 / Math.Max(1, 100 + _thisEntity.buffController.buffValue[BuffType.atkspd_delta_value]));
        }
    }
    public int AttackNumS
    {
        get
        {
            if (_attackNumF >= 0)
                return Math.Max(0, _attackNumF + (int)_thisEntity.buffController.buffValue[BuffType.atkn_delta_value]);
            else
                return -1;
        }
    }
    public int AttackMinNumS
    {
        get
        {
            return Math.Max(0, _attackMinNumF + (int)_thisEntity.buffController.buffValue[BuffType.atkminn_delta_value]);
        }
    }

    protected Entity _thisEntity;
    protected float _attackTimer;

    private (int x, int y)[] _attackRangeF;
    private float _attackRadiusF;
    private float _attackDamageF;
    private float _baseAttackTimeF;
    private int _attackNumF;
    private int _attackMinNumF;


    public virtual void Dormancy()
    {
        OnAttackInterrupt = null;
        OnAttackSuccessfully = null;
        OnBeforeAttack = null;
        OnAfterAttack = null;
        OnBeforeTakeDamage = null;
        OnAfterTakeDamage = null;
        OnBeforeTargetSelect = null;
        OnAfterTargetSelect = null;
    }
    public virtual void Initialize()
    {
        _attackTimer = 0;
        DamageType = _thisEntity.EntityData.DamageType;
    }
    public virtual void PreWarm()
    {
        _thisEntity = GetComponent<Entity>();
        _attackEffectData = _attackEffectData0;
        AttributesCaculateFirst();
    }
    private void AttributesCaculateFirst()
    {
        EntityData entityData = _thisEntity.EntityData;
        // EntityData.VisionRange 已改为 List<Vector2Int>（序列化稳定）。_attackRangeF 保留命名元组数组的运行时形态（只在本类用，序列化无关）
        var src = entityData.VisionRange;
        _attackRangeF = new (int x, int y)[src.Count];
        for (int i = 0; i < src.Count; i++) _attackRangeF[i] = (src[i].x, src[i].y);
        _attackRadiusF = entityData.VisionRadius;
        _attackDamageF = entityData.Attack;
        _baseAttackTimeF = entityData.BaseAttackTime;
        _attackNumF = entityData.AttackNum;
    }

    protected virtual void FixedUpdate()
    {
        if (!_thisEntity.Stats.IsActive)
            return;
        if (_attackTimer > 0)
        {
            _attackTimer -= Time.fixedDeltaTime * _baseAttackTimeF / BaseAttackTimeS;
        }
        else if (_thisEntity.entityAM.CurrentState != EntityState.Start && _thisEntity.entityAM.CurrentState != EntityState.Die && TryToAttack(AttackTargetSelect(AttackNumS, AttackMinNumS), false, true))
        {
            _attackTimer = _baseAttackTimeF;
        }
    }
    public bool ForceResetAttack()
    {
        _attackTimer = _baseAttackTimeF;
        return TryToAttack(AttackTargetSelect(AttackNumS, AttackMinNumS), true, true);
    }
    public void ResetAttackEffectData()
    {
        _attackEffectData = _attackEffectData0;
    }
    public void SetAttackEffectData(AttackEffectData attackEffectData)
    {
        _attackEffectData = attackEffectData;
    }
    public virtual bool TryToAttack(Entity[] attackTargets, bool forceChange, bool canBeInterrupt)
    {
        if (attackTargets.Length > 0)
        {
            Vector3 centerPos = new Vector3(0, 0, 0);
            int i;
            for (i = 0; i < attackTargets.Length; i++)
            {
                centerPos += attackTargets[i].transform.position;
            }
            centerPos /= i;
            _thisEntity.entityAM.SetDirection(centerPos);
        }
        return false;
    }
    protected async void AttackByAnimation(Entity[] attackTargets, bool canInterrupt)
    {
        if (canInterrupt)
        {
            attackTargets = _thisEntity.Combat.EntityUpdate(attackTargets);
            if (attackTargets.Length == 0)
            {
                attackTargets = AttackTargetSelect(AttackNumS, AttackMinNumS);
                if (attackTargets.Length == 0)
                {
                    OnAttackInterrupt?.Invoke();
                    _thisEntity.entityAM.TrySetState(EntityState.Idle, true);
                    _attackTimer = 0;
                    return;
                }
            }
        }
        for (int i = 0; i < attackTargets.Length; i++)
        {
            float multiplyer = 1;
            float defPenetrate = 0;
            float mgrPenetrate = 0;
            float defPenetrate_value = 0;
            float mgrPenetrate_value = 0;
            int cumbo = 1;
            int damageType = DamageType;
            OnBeforeAttack?.Invoke(attackTargets[i], ref multiplyer, ref defPenetrate, ref mgrPenetrate, ref defPenetrate_value, ref mgrPenetrate, ref cumbo, ref damageType, 0);
            bool isDeadly = AttackSingleTargetOperation(OnBeforeTakeDamage, OnAfterTakeDamage, _attackEffectData, attackTargets[i], multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType);
            for (int j = 1; j < cumbo; j++)
            {
                await UniTask.WaitForSeconds(0.1f);
                isDeadly = AttackSingleTargetOperation(OnBeforeTakeDamage, OnAfterTakeDamage, _attackEffectData, attackTargets[i], multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType);
            }
            OnAfterAttack?.Invoke(attackTargets[i], multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate, damageType, 0, isDeadly);
        }
        OnAttackSuccessfully?.Invoke();
    }
    protected virtual bool AttackSingleTargetOperation(OperationsBeforeTakeDamage onBeforeTakeDamage, OperationsAfterTakeDamage onAfterTakeDamage, AttackEffectData attackEffectData, Entity attackTarget, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType)
    {
        return false;
    }
    protected Entity[] AttackTargetSelect(int selectNum_Max, int selectNum_Min)
    {
        List<Entity> tmpTarget;
        bool samecomp;
        if (_thisEntity.Movement.Camp == 1 && DamageType != 3)
        {
            tmpTarget = new List<Entity>(_thisEntity.Vision.NearbyMonsters);
            samecomp = false;
        }
        else if (_thisEntity.Movement.Camp == 1 && DamageType == 3)
        {
            tmpTarget = new List<Entity>(_thisEntity.Vision.NearbyTurrets);
            samecomp = true;
        }
        else if (_thisEntity.Movement.Camp == 2 && DamageType != 3)
        {
            tmpTarget = new List<Entity>(_thisEntity.Vision.NearbyTurrets);
            samecomp = false;
        }
        else if (_thisEntity.Movement.Camp == 2 && DamageType == 3)
        {
            tmpTarget = new List<Entity>(_thisEntity.Vision.NearbyMonsters);
            samecomp = true;
        }
        else
        {
            tmpTarget = new List<Entity>(_thisEntity.Vision.NearbyMonsters);
            tmpTarget.AddRange(_thisEntity.Vision.NearbyTurrets);
            samecomp = false;
        }
        tmpTarget = _thisEntity.Combat.PriorityOrder(tmpTarget, EntityOrderLogic);
        OnBeforeTargetSelect?.Invoke(tmpTarget, ref selectNum_Max, ref selectNum_Min, ref samecomp);
        if (tmpTarget.Count > 0 && selectNum_Max >= 0)//��AttackNum==-1ʱ������Ĭ��Ϊ��Χ��ȫ������
        {
            if (tmpTarget.Count > selectNum_Max)
            {
                tmpTarget.RemoveRange(selectNum_Max, tmpTarget.Count - selectNum_Max);
            }
            else if (tmpTarget.Count < selectNum_Min)
            {
                int currentNum = tmpTarget.Count;
                for (int i = 0; i < selectNum_Min - currentNum; i++)
                {
                    tmpTarget.Add(tmpTarget[i % currentNum]);
                }
            }
        }
        OnAfterTargetSelect?.Invoke(tmpTarget, selectNum_Max, selectNum_Min, samecomp);
        return tmpTarget.ToArray();
    }

}
