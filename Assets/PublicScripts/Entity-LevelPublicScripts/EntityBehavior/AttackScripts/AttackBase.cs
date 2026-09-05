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



    [NonSerialized] public OrderLogic TargetPriority;
    [NonSerialized] public int DamageType;
    [HideInInspector] public AttackEffectData _attackEffectData;
    [SerializeField] private AttackEffectData _attackEffectData0;
    // 技能/资产步骤共用的额外攻击特效配置（含 BulletData 与出生骨骼）。
    // ParamList 纯 string 装不下 GameObject 引用——带引用的弹幕配置登记在
    // 攻击组件上，资产经 FireBullets.effectDataIndex 引用（与 SpawnEntity
    // 的 spawnIndex→CanSpawnEntityIds 登记处模式同构）。
    public List<AttackEffectData> _extraEffectDatas = new List<AttackEffectData>();
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
    protected Entity _thisEntity;
    protected float _attackTimer;

    private (int x, int y)[] _attackRangeF;
    private float _attackRadiusF;


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
        TargetPriority = _thisEntity.Stats.TargetPriorityS;
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
        // AttackDamage / BaseAttackTime / AttackNum / AttackMinNum 已迁出至 EntityStats（XxxBase 在 PreWarm 阶段 AttributesCaculateFirst 写入）
    }

    protected virtual void FixedUpdate()
    {
        if (!_thisEntity.Stats.IsActive)
            return;
        if (_attackTimer > 0)
        {
            _attackTimer -= Time.fixedDeltaTime * _thisEntity.Stats.BaseAttackTimeBase / _thisEntity.Stats.BaseAttackTimeS;
        }
        else if (_thisEntity.StateMachine.CurrentState != EntityState.Start && _thisEntity.StateMachine.CurrentState != EntityState.Cast && _thisEntity.StateMachine.CurrentState != EntityState.Die && TryToAttack(AttackTargetSelect(_thisEntity.Stats.AttackNumS, _thisEntity.Stats.AttackMinNumS), false, true))
        {
            _attackTimer = _thisEntity.Stats.BaseAttackTimeBase;
        }
    }
    public bool ForceResetAttack()
    {
        _attackTimer = _thisEntity.Stats.BaseAttackTimeBase;
        return TryToAttack(AttackTargetSelect(_thisEntity.Stats.AttackNumS, _thisEntity.Stats.AttackMinNumS), true, true);
    }
    public void ResetAttackEffectData()
    {
        _attackEffectData = _attackEffectData0;
    }
    public void SetAttackEffectData(AttackEffectData attackEffectData)
    {
        _attackEffectData = attackEffectData;
    }
    public bool TryGetExtraEffectData(int index, out AttackEffectData data)
    {
        if (index < 0 || index >= _extraEffectDatas.Count)
        {
            Debug.LogError($"[AttackBase] Extra effect data index {index} out of range ({_extraEffectDatas.Count} entries on {name}).");
            data = default;
            return false;
        }
        data = _extraEffectDatas[index];
        return true;
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
            _thisEntity.facing.SetDirection(centerPos);
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
                attackTargets = AttackTargetSelect(_thisEntity.Stats.AttackNumS, _thisEntity.Stats.AttackMinNumS);
                if (attackTargets.Length == 0)
                {
                    OnAttackInterrupt?.Invoke();
                    _thisEntity.StateMachine.TrySetState(EntityState.Idle, true);
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
            OnBeforeAttack?.Invoke(attackTargets[i], ref multiplyer, ref defPenetrate, ref mgrPenetrate, ref defPenetrate_value, ref mgrPenetrate_value, ref cumbo, ref damageType, 0);
            bool isDeadly = AttackSingleTargetOperation(OnBeforeTakeDamage, OnAfterTakeDamage, _attackEffectData, attackTargets[i], multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType);
            for (int j = 1; j < cumbo; j++)
            {
                await UniTask.WaitForSeconds(0.1f);
                isDeadly = AttackSingleTargetOperation(OnBeforeTakeDamage, OnAfterTakeDamage, _attackEffectData, attackTargets[i], multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType);
            }
            OnAfterAttack?.Invoke(attackTargets[i], multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, 0, isDeadly);
        }
        OnAttackSuccessfully?.Invoke();
    }
    protected virtual bool AttackSingleTargetOperation(OperationsBeforeTakeDamage onBeforeTakeDamage, OperationsAfterTakeDamage onAfterTakeDamage, AttackEffectData attackEffectData, Entity attackTarget, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType)
    {
        return false;
    }

    /// <summary>
    /// 单目标完整命中（直伤路径统一入口，AttackBase 子类共用）：
    /// 攻方 OnBeforeTakeDamage → ApplyDamage → OnAfterTakeDamage → 溅射。
    /// 溅射复用主目标命中后的最终参数（攻方 OnBefore 链的改参对溅射同样生效）。
    /// </summary>
    protected bool HitTargetAndSplash(Entity attackTarget, OperationsBeforeTakeDamage onBeforeTakeDamage, OperationsAfterTakeDamage onAfterTakeDamage, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType)
    {
        onBeforeTakeDamage?.Invoke(attackTarget, ref multiplyer, ref defPenetrate, ref mgrPenetrate, ref defPenetrate_value, ref mgrPenetrate_value, ref damageType, 0);
        bool isDeadly = attackTarget.Stats.ApplyDamage(_thisEntity, damage, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, 0);
        onAfterTakeDamage?.Invoke(attackTarget, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, 0, isDeadly);
        SplashAroundTarget(attackTarget, damage, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, onBeforeTakeDamage, onAfterTakeDamage);
        return isDeadly;
    }

    /// <summary>
    /// 以 center 为圆心结算溅射：SplashRadiusS 半径内捞可选目标（排除 center），
    /// 逐个走完整攻方事件链 + ApplyDamage（受击方管线完整，含闪避/OnBeforeHurt/治疗封顶）。
    /// 伤害参数复用本次命中快照，不做额外修改；溅射不嵌套（受害者不再作为圆心二次扩散）。
    /// </summary>
    protected void SplashAroundTarget(Entity center, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, OperationsBeforeTakeDamage onBeforeTakeDamage, OperationsAfterTakeDamage onAfterTakeDamage)
    {
        float radius = _thisEntity.Stats.SplashRadiusS;
        if (radius <= 0) return;
        // 候选营地板随 AttackTargetSelect：伤害型打敌方阵营，治疗型（DamageType==3）打己方阵营
        List<Entity> victims = EntityManager.Manager.EntitySelector_Radius(
            (center.transform.position.x, center.transform.position.y),
            _thisEntity.Movement.Camp, DamageType == 3, radius, false);
        for (int i = 0; i < victims.Count; i++)
        {
            if (victims[i] == center) continue;
            // 溅射受害者 applyType=1（主目标命中仍为 0），下游事件与结算可据此区分伤害来源
            HitTarget(victims[i], onBeforeTakeDamage, onAfterTakeDamage, damage, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, 1);
        }
    }

    /// <summary>
    /// 单目标命中：攻方 OnBeforeTakeDamage → ApplyDamage → OnAfterTakeDamage。
    /// 每个受害者各持一份参数拷贝（ref 改参互不影响）；applyType 由调用方给定
    /// （主目标 0 / 溅射受害者 1）。
    /// </summary>
    private bool HitTarget(Entity target, OperationsBeforeTakeDamage onBeforeTakeDamage, OperationsAfterTakeDamage onAfterTakeDamage, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType)
    {
        onBeforeTakeDamage?.Invoke(target, ref multiplyer, ref defPenetrate, ref mgrPenetrate, ref defPenetrate_value, ref mgrPenetrate_value, ref damageType, applyType);
        bool isDeadly = target.Stats.ApplyDamage(_thisEntity, damage, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, applyType);
        onAfterTakeDamage?.Invoke(target, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, applyType, isDeadly);
        return isDeadly;
    }

    /// <summary>
    /// 攻击组件的子弹发射统一入口（三个子类共用）：
    /// 伤害取命中时刻的 AttackS 快照；OnAfterTakeDamage 链尾追加溅射（落点命中即扩散）；
    /// AllowNoTarget 存活弹在主目标死后落地时，仍以落点主目标为圆心溅射。
    /// </summary>
    protected void FireBullet(OperationsBeforeTakeDamage onBeforeTakeDamage, OperationsAfterTakeDamage onAfterTakeDamage, Bullet.OperationsOnBulletDestroy onBulletDestroy, BulletData bulletData, Entity attackTarget, Vector2 bulletSpawnPosition, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType)
    {
        float damage = _thisEntity.Stats.AttackS;
        OperationsAfterTakeDamage afterChain = onAfterTakeDamage;
        afterChain += (hitTarget, m, dp, mp, dpv, mpv, dt, at, deadly) =>
            SplashAroundTarget(hitTarget, damage, m, dp, mp, dpv, mpv, dt, onBeforeTakeDamage, onAfterTakeDamage);
        new Bullet(onBeforeTakeDamage, afterChain, onBulletDestroy, bulletData, _thisEntity, attackTarget, Vector2.zero, bulletSpawnPosition, damage, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, 0,
            landCenter => SplashAroundTarget(landCenter, damage, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, onBeforeTakeDamage, onAfterTakeDamage));
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
        tmpTarget = _thisEntity.Combat.PriorityOrder(tmpTarget, TargetPriority);
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
