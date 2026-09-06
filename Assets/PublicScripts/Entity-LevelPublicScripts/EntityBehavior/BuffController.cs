using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[Serializable]
public class Buff
{
    public Modifier[] modifiers;       // 不可变快照，所有改动经 SetBuffValues 重建
    public float buff_time;
    public string buff_name;
    public Entity origin_entity;
    public GameObject buff_effect;
    /// <summary>store 侧的 group token，AddModifiers 时分配，RemoveModifiers 时回传。0=未加入。</summary>
    public int modifierToken;
    public Buff(float buff_time, string buff_name, Entity origin_entity, Modifier[] modifiers, GameObject buff_effect)
    {
        this.modifiers = modifiers ?? System.Array.Empty<Modifier>();
        this.buff_time = buff_time;
        this.buff_name = buff_name;
        this.origin_entity = origin_entity;
        this.buff_effect = buff_effect;
        this.modifierToken = 0;
    }
}
/// <summary>
/// buff 归属列表。Normal/WhiteList 随 Dormancy（每次回收）清除；
/// Level（局内 buff）跨 Dormancy 存活，随实体 GameObject 销毁（池生命周期与关卡对齐）
/// 自然消亡，无需显式清理点。
/// </summary>
public enum BuffScope
{
    Normal,
    WhiteList,
    Level,
}

public class BuffController : MonoBehaviour, IPoolOperation
{
    private class DOTData
    {
        public Entity OriginEntity;
        public GameObject DOTEffect;
        public string Name;
        public float Duration;
        public float Frequency;
        public float Timer;
        public float Damage;
        public int DamageType;
        public bool Deadly;
        public DOTData(Entity originEntity, GameObject dotEffect, string name, float duration, float frequency, float currentTimer, float damage, int damageType, bool deadly)
        {
            DOTEffect = dotEffect;
            OriginEntity = originEntity;
            Name = name;
            Duration = duration;
            Frequency = frequency;
            Timer = currentTimer;
            Damage = damage;
            DamageType = damageType;
            Deadly = deadly;
        }
    }
    private Entity _thisEntity;
    // 状态容器在组件构造时创建（而非 PreWarm）：池逆序 PreWarm 中 Entity（runner 派发
    // OnPreWarm）先于 BuffController.PreWarm 执行，OnPreWarm 触发的 CreateBuff/CreateDOT/
    // AddAbnormalState 依赖容器已就绪。池复用只 Clear/归零、不置 null，引用跨部署有效；
    // PreWarm 每 GameObject 恰一次（CreateNewEntity），与字段初始化一一对应。
    private List<Buff> white_list_buffs = new List<Buff>();
    private List<Buff> normal_buffs = new List<Buff>();
    private List<Buff> level_buffs = new List<Buff>();
    private float[] _abnormalStateTime = new float[AbnormalStateTypeCount];
    private List<DOTData> _dotDatas = new List<DOTData>();
    private AttributeStore _store;
    public List<Buff> Buffs
    {
        get
        {
            List<Buff> list = new List<Buff>();
            list.AddRange(white_list_buffs);
            list.AddRange(normal_buffs);
            list.AddRange(level_buffs);
            return list;
        }
    }

    /// <summary>
    /// 无分配成员判定（aura 同步等高频路径用，避免 .Buffs 的 new List + AddRange）。
    /// </summary>
    public bool ContainsBuff(Buff b)
        => white_list_buffs.Contains(b) || normal_buffs.Contains(b) || level_buffs.Contains(b);

    /// <summary>
    /// 注入 AttributeStore。Entity 在 PreWarm 中调用。
    /// </summary>
    public void Bind(AttributeStore store)
    {
        _store = store;
    }

    private void FixedUpdate()
    {
        BuffUpdate();
        AbnormalStateUpdate();
        DOTUpdate();
    }


    #region///buff管理
    /// <summary>
    /// 创建 buff。所有数值改动经 SetBuffValues 重建 modifier 快照并通知 store 置脏。
    /// </summary>
    /// <param name="modifiers">modifier 数组（不可变快照）</param>
    /// <param name="buffEffect">特效</param>
    /// <param name="buffName">名称（同名复用既有特效）</param>
    /// <param name="buffTime">时间，小于-5为永久</param>
    /// <param name="scope">归属列表：Normal/WhiteList 随回收（Dormancy）清除；Level 局内 buff 跨回收存活，随池销毁（退关）自然消亡</param>
    /// <returns>buff 实例</returns>
    public Buff CreateBuff(Modifier[] modifiers, GameObject buffEffect, string buffName, float buffTime, BuffScope scope)
    {
        List<Buff> list = scope == BuffScope.WhiteList ? white_list_buffs : scope == BuffScope.Level ? level_buffs : normal_buffs;
        // 同名 buff 复用既有特效，否则实例化新特效。
        foreach (Buff b in list)
        {
            if (b.buff_name == buffName)
            {
                buffEffect = b.buff_effect;
                Buff reuse = new Buff(buffTime, buffName, _thisEntity, modifiers, buffEffect);
                SetBuffValues(modifiers, reuse);
                list.Add(reuse);
                return reuse;
            }
        }
        if (buffEffect)
        {
            buffEffect = Instantiate(buffEffect, _thisEntity.TempContainer.position, Quaternion.identity, _thisEntity.TempContainer);
        }
        Buff addBuff = new Buff(buffTime, buffName, _thisEntity, modifiers, buffEffect);
        SetBuffValues(modifiers, addBuff);
        list.Add(addBuff);
        return addBuff;
    }
    /// <summary>
    /// 销毁 buff，移除其所有 modifier（置脏）。
    /// </summary>
    /// <param name="destroyBuff">要销毁的 buff 实例</param>
    public void DestroyBuff(Buff destroyBuff)
    {
        if (destroyBuff.modifierToken != 0) _store.RemoveModifiers(destroyBuff.modifierToken);
        destroyBuff.modifierToken = 0;
        white_list_buffs.Remove(destroyBuff);
        normal_buffs.Remove(destroyBuff);
        level_buffs.Remove(destroyBuff);
        if (!(white_list_buffs.Contains(destroyBuff) || normal_buffs.Contains(destroyBuff) || level_buffs.Contains(destroyBuff)))
        {
            Destroy(destroyBuff.buff_effect);
        }
    }
    /// <summary>
    /// 设置 Buff 的 modifier 数组（不可变快照替换）。
    /// 所有 buff 数值改动（创建赋值、动态更新、销毁）统一经此入口：
    /// 移除旧快照 → 替换为新快照 → 加入新快照（store 置脏）。
    /// </summary>
    /// <param name="newModifiers">新的 modifier 数组</param>
    /// <param name="setTarget">目标 Buff</param>
    public void SetBuffValues(Modifier[] newModifiers, Buff setTarget)
    {
        // 移除旧 group（若已加入），再为新快照分配新 group
        if (setTarget.modifierToken != 0) _store.RemoveModifiers(setTarget.modifierToken);
        setTarget.modifiers = newModifiers ?? System.Array.Empty<Modifier>();
        setTarget.modifierToken = _store.AddModifiers(setTarget.modifiers);
    }
    private void BuffUpdate()
    {
        for (int i = 0; i < white_list_buffs.Count; i++)
        {
            if (white_list_buffs[i].buff_time > 0)
            {
                white_list_buffs[i].buff_time -= Time.fixedDeltaTime;
            }
            else if (white_list_buffs[i].buff_time > -5)
            {
                DestroyBuff(white_list_buffs[i--]);
            }
        }
        for (int i = 0; i < normal_buffs.Count; i++)
        {
            if (normal_buffs[i].buff_time > 0)
            {
                normal_buffs[i].buff_time -= Time.fixedDeltaTime;
            }
            else if (normal_buffs[i].buff_time > -5)
            {
                DestroyBuff(normal_buffs[i--]);
            }
        }
        // 局内 buff：持续时间语义与另两列一致（-5 及以下免倒计时）。
        for (int i = 0; i < level_buffs.Count; i++)
        {
            if (level_buffs[i].buff_time > 0)
            {
                level_buffs[i].buff_time -= Time.fixedDeltaTime;
            }
            else if (level_buffs[i].buff_time > -5)
            {
                DestroyBuff(level_buffs[i--]);
            }
        }
    }
    #endregion
    #region///异常状态
    /// <summary>异常状态永久阈值：施加时长 ≤ 此值视为无限持续，不随帧衰减，仅显式移除。</summary>
    private const float PermanentAbnormalTime = -5f;
    /// <summary>异常状态类型数：0-束缚、1-失衡、2-沉默、3-无敌、4-停顿。</summary>
    private const int AbnormalStateTypeCount = 5;
    /// <summary>停顿(type 4)激活时的移动速度因子（降低 80%）。</summary>
    private const float HaltSpeedFactor = 0.2f;
    /// <summary>异常状态类型合法性判定，施加/移除组件共用。</summary>
    public static bool IsValidAbnormalType(int abnormalType)
    {
        return abnormalType >= 0 && abnormalType < AbnormalStateTypeCount;
    }
    /// <summary>
    /// 已激活时的刷新判定 = "取更长"，永久为最高档：永久不被限时覆盖，
    /// 限时可升级为永久（击退滑行以 -10 施加失衡、与技能限时失衡并存的场景依赖此语义）。
    /// </summary>
    private static bool ShouldRefreshAbnormal(float current, float incoming)
    {
        if (current <= PermanentAbnormalTime) return false;
        if (incoming <= PermanentAbnormalTime) return true;
        return incoming > current;
    }
    /// <summary>
    /// 添加异常状态
    /// </summary>
    /// <param name="abnormalTime">添加异常状态的时长,值小于等于-5表示无限时长持续</param>
    /// <param name="abnormalType">添加异常状态类型:0-束缚,1-失衡,2-沉默,3-无敌,4-停顿</param>
    public void AddAbnormalState(float abnormalTime, int abnormalType)
    {
        switch (abnormalType)
        {
            case 0:
                if (_abnormalStateTime[0] <= 0 && _abnormalStateTime[0] > PermanentAbnormalTime)
                {
                    _abnormalStateTime[0] = abnormalTime;
                    _thisEntity.StateMachine.AddStateToBan(new[] { EntityState.Move });
                    if (_thisEntity.StateMachine.CurrentState == EntityState.Move)
                    {
                        _thisEntity.StateMachine.TrySetState(EntityState.Idle, true);
                    }
                }
                else if (ShouldRefreshAbnormal(_abnormalStateTime[0], abnormalTime))
                {
                    _abnormalStateTime[0] = abnormalTime;
                }
                break;
            case 1:
                if (_abnormalStateTime[1] <= 0 && _abnormalStateTime[1] > PermanentAbnormalTime)
                {
                    _abnormalStateTime[1] = abnormalTime;
                    _thisEntity.StateMachine.AddStateToBan(new[] { EntityState.Move, EntityState.Attack });
                    _thisEntity.StateMachine.TrySetState(EntityState.Idle, true);
                }
                else if (ShouldRefreshAbnormal(_abnormalStateTime[1], abnormalTime))
                {
                    _abnormalStateTime[1] = abnormalTime;
                }
                break;
            case 2:
                if (_abnormalStateTime[2] <= 0 && _abnormalStateTime[2] > PermanentAbnormalTime)
                {
                    _abnormalStateTime[2] = abnormalTime;
                    _thisEntity.StateMachine.AddStateToBan(new[] { EntityState.Attack });
                    if (_thisEntity.StateMachine.CurrentState == EntityState.Attack)
                    {
                        _thisEntity.StateMachine.TrySetState(EntityState.Idle, true);
                    }
                }
                else if (ShouldRefreshAbnormal(_abnormalStateTime[2], abnormalTime))
                {
                    _abnormalStateTime[2] = abnormalTime;
                }
                break;
            case 3:
                if (_abnormalStateTime[3] <= 0 && _abnormalStateTime[3] > PermanentAbnormalTime)
                {
                    _abnormalStateTime[3] = abnormalTime;
                    _thisEntity.Stats.AddSelectable(1);
                    _thisEntity.Stats.AddHurtable(1);
                }
                else if (ShouldRefreshAbnormal(_abnormalStateTime[3], abnormalTime))
                {
                    _abnormalStateTime[3] = abnormalTime;
                }
                break;

            case 4:
                // 停顿无进入副作用（减速由 GetMoveSpeedFactor 在速度出口查询），纯计时
                if (ShouldRefreshAbnormal(_abnormalStateTime[4], abnormalTime))
                {
                    _abnormalStateTime[4] = abnormalTime;
                }
                break;

            default: break;
        }
    }
    public void TryRemoveAbnormalState(int abnormalType)
    {
        if (_abnormalStateTime[abnormalType] > 0 || _abnormalStateTime[abnormalType] <= PermanentAbnormalTime)
        {
            switch (abnormalType)
            {
                case 0:
                    _abnormalStateTime[0] = 0;
                    _thisEntity.StateMachine.RemoveStateFromBan(new[] { EntityState.Move });
                    break;
                case 1:
                    _abnormalStateTime[1] = 0;
                    _thisEntity.StateMachine.RemoveStateFromBan(new[] { EntityState.Move, EntityState.Attack });
                    break;
                case 2:
                    _abnormalStateTime[2] = 0;
                    _thisEntity.StateMachine.RemoveStateFromBan(new[] { EntityState.Attack });
                    break;
                case 3:
                    _abnormalStateTime[3] = 0;
                    _thisEntity.Stats.AddHurtable(-1);
                    _thisEntity.Stats.AddSelectable(-1);
                    break;
                case 4:
                    _abnormalStateTime[4] = 0;
                    break;
                default: break;
            }
        }
    }
    public bool FetchAbnormalState(int abnormalType)
    {
        if (_abnormalStateTime[abnormalType] > 0 || _abnormalStateTime[abnormalType] <= PermanentAbnormalTime)
        {
            return true;
        }
        return false;
    }
    /// <summary>
    /// 读取异常状态当前计时：>0 为限时剩余，≤-5 为永久，0 为未激活。
    /// </summary>
    public float FetchAbnormalStateTime(int abnormalType)
    {
        return _abnormalStateTime[abnormalType];
    }
    /// <summary>
    /// 移动速度因子，由移动出口（MoveBase.MoveSpeedS）统一结算：
    /// 停顿(type 4)激活时降至 20%，否则 1。
    /// </summary>
    public float GetMoveSpeedFactor()
    {
        return FetchAbnormalState(4) ? HaltSpeedFactor : 1f;
    }
    private void AbnormalStateUpdate()
    {
        for (int i = 0; i < _abnormalStateTime.Length; i++)
        {
            if (_abnormalStateTime[i] - Time.fixedDeltaTime > 0)
            {
                _abnormalStateTime[i] -= Time.fixedDeltaTime;
            }
            else if (_abnormalStateTime[i] > PermanentAbnormalTime)
            {
                TryRemoveAbnormalState(i);
            }
        }
    }
    #endregion
    #region///DOT伤害
    public void CreateDOT(Entity originEntity, GameObject dotEffect, string name, float duration, float frequency, float timer, float damage, int damageType, bool isDeadly)
    {
        int index = ContainDOT(name);
        if (index == -1)
        {
            if (dotEffect != null)
                dotEffect = Instantiate(dotEffect, _thisEntity.TempContainer.position, Quaternion.identity, _thisEntity.TempContainer);
        }
        else
        {
            dotEffect = _dotDatas[index].DOTEffect;
        }
        _dotDatas.Add(new DOTData(originEntity, dotEffect, name, duration, frequency, timer, damage, damageType, isDeadly));
    }
    public void RemoveDOT(string name)
    {
        for (int i = 0; i < _dotDatas.Count; i++)
        {
            if (_dotDatas[i].Name == name)
            {
                GameObject dotEffect = _dotDatas[i].DOTEffect;
                _dotDatas.RemoveAt(i);
                for (int j = i; j < _dotDatas.Count; j++)
                {
                    if (_dotDatas[j].Name == name)
                    {
                        return;
                    }
                }
                Destroy(dotEffect);
                return;
            }
        }
    }
    public int ContainDOT(string name)
    {
        for (int i = 0; i < _dotDatas.Count; i++)
        {
            if (_dotDatas[i].Name == name)
            {
                return i;
            }
        }
        return -1;
    }
    private void DOTUpdate()
    {
        for (int i = 0; i < _dotDatas.Count; i++)
        {
            if (_dotDatas[i].Duration > 0)
            {
                _dotDatas[i].Duration -= Time.fixedDeltaTime;
                if (_dotDatas[i].Timer > 0)
                {
                    _dotDatas[i].Timer -= Time.fixedDeltaTime;
                }
                else
                {
                    if (_thisEntity.Stats.CurrentHp - 1 > _dotDatas[i].Damage)
                    {
                        _thisEntity.Stats.ApplyDamage(null, _dotDatas[i].Damage, 1, 0, 0, 0, 0, _dotDatas[i].DamageType, 2);
                    }
                    else if (_thisEntity.Stats.CurrentHp > 1)
                    {
                        _thisEntity.Stats.ApplyDamage(null, _thisEntity.Stats.CurrentHp - 1, 1, 0, 0, 0, 0, _dotDatas[i].DamageType, 2);
                    }
                    _dotDatas[i].Timer = _dotDatas[i].Frequency - Time.fixedDeltaTime;
                }
            }
            else if (_dotDatas[i].Duration > -5)
            {
                string dotName = _dotDatas[i].Name;
                GameObject dotEffect = _dotDatas[i].DOTEffect;
                _dotDatas.RemoveAt(i--);
                if (ContainDOT(dotName) == -1)
                {
                    Destroy(dotEffect);
                }
            }

        }
    }
    #endregion


    public void PreWarm()
    {
        // 状态容器见字段初始化器；此处只取场景引用（依赖 GameObject 组件布局）。
        _thisEntity = this.transform.GetComponent<Entity>();
    }
    public void Initialize()
    {

    }
    public void Dormancy()
    {
        // Normal/WhiteList 随回收清除；Level（局内 buff）保留，随实体销毁自然消亡。
        ClearListBuffs(white_list_buffs);
        ClearListBuffs(normal_buffs);
        DestroyLevelBuffEffects();
        for (int i = 0; i < _abnormalStateTime.Length; i++)
            _abnormalStateTime[i] = 0;
        _dotDatas.Clear();
    }

    /// <summary>
    /// 销毁局内 buff 的特效并置空引用。特效挂在 TempContainer 下、随 Entity.Dormancy
    /// 一并销毁——不置空会让 level_buffs 持有 fake-null。局内 buff 跨回收保留的是
    /// 数值效果，特效跟随部署周期（重建属表现层需求，另行处理）。
    /// </summary>
    private void DestroyLevelBuffEffects()
    {
        for (int i = 0; i < level_buffs.Count; i++)
        {
            Destroy(level_buffs[i].buff_effect);
            level_buffs[i].buff_effect = null;
        }
    }

    /// <summary>清空指定列表：销毁特效 + 按 group 移除 store modifier，不动其他列表。</summary>
    private void ClearListBuffs(List<Buff> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            Destroy(list[i].buff_effect);
            if (list[i].modifierToken != 0) _store.RemoveModifiers(list[i].modifierToken);
            list[i].modifierToken = 0;
        }
        list.Clear();
    }

}
