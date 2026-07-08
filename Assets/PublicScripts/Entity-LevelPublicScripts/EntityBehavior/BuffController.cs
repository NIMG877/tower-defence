using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static PlasticGui.PlasticTableCell;
[Serializable]
public class Buff
{
    public Modifier[] modifiers;       // 不可变快照，所有改动经 SetBuffValues 重建
    public float buff_time;
    public string buff_name;
    public Entity origin_entity;
    public GameObject buff_effect;
    public Buff(float buff_time, string buff_name, Entity origin_entity, Modifier[] modifiers, GameObject buff_effect)
    {
        this.modifiers = modifiers ?? System.Array.Empty<Modifier>();
        this.buff_time = buff_time;
        this.buff_name = buff_name;
        this.origin_entity = origin_entity;
        this.buff_effect = buff_effect;
    }
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
    private List<Buff> white_list_buffs;
    private List<Buff> normal_buffs;
    private float[] _abnormalStateTime;
    private List<DOTData> _dotDatas;
    private AttributeStore _store;
    public List<Buff> Buffs
    {
        get
        {
            List<Buff> list = new List<Buff>();
            list.AddRange(white_list_buffs);
            list.AddRange(normal_buffs);
            return list;
        }
    }

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
    /// <param name="isWhiteList">白名单</param>
    /// <returns>buff 实例</returns>
    public Buff CreateBuff(Modifier[] modifiers, GameObject buffEffect, string buffName, float buffTime, bool isWhiteList)
    {
        Buff addBuff;
        if (isWhiteList)
        {
            foreach (Buff b in white_list_buffs)
            {
                if (b.buff_name == buffName)
                {
                    buffEffect = b.buff_effect;
                    addBuff = new Buff(buffTime, buffName, _thisEntity, modifiers, buffEffect);
                    SetBuffValues(modifiers, addBuff);
                    white_list_buffs.Add(addBuff);
                    return addBuff;
                }
            }
            if (buffEffect)
            {
                buffEffect = Instantiate(buffEffect, _thisEntity.TempContainer.position, Quaternion.identity, _thisEntity.TempContainer);
            }
            addBuff = new Buff(buffTime, buffName, _thisEntity, modifiers, buffEffect);
            SetBuffValues(modifiers, addBuff);
            white_list_buffs.Add(addBuff);
            return addBuff;
        }
        else
        {
            foreach (Buff b in normal_buffs)
            {
                if (b.buff_name == buffName)
                {
                    buffEffect = b.buff_effect;
                    addBuff = new Buff(buffTime, buffName, _thisEntity, modifiers, buffEffect);
                    SetBuffValues(modifiers, addBuff);
                    normal_buffs.Add(addBuff);
                    return addBuff;
                }
            }
            if (buffEffect)
            {
                buffEffect = Instantiate(buffEffect, _thisEntity.TempContainer.position, Quaternion.identity, _thisEntity.TempContainer);
            }
            addBuff = new Buff(buffTime, buffName, _thisEntity, modifiers, buffEffect);
            SetBuffValues(modifiers, addBuff);
            normal_buffs.Add(addBuff);
            return addBuff;
        }
    }
    /// <summary>
    /// 销毁 buff，移除其所有 modifier（置脏）。
    /// </summary>
    /// <param name="destroyBuff">要销毁的 buff 实例</param>
    public void DestroyBuff(Buff destroyBuff)
    {
        if (destroyBuff.modifiers != null) _store.RemoveModifiers(destroyBuff.modifiers);
        white_list_buffs.Remove(destroyBuff);
        normal_buffs.Remove(destroyBuff);
        if (!(white_list_buffs.Contains(destroyBuff) || normal_buffs.Contains(destroyBuff)))
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
        Modifier[] old = setTarget.modifiers;
        if (old != null) _store.RemoveModifiers(old);
        setTarget.modifiers = newModifiers ?? System.Array.Empty<Modifier>();
        _store.AddModifiers(setTarget.modifiers);
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
    }
    #endregion
    #region///异常状态
    /// <summary>
    /// 添加异常状态
    /// </summary>
    /// <param name="abnormalTime">添加异常状态的时长,值小于等于-5表示无限时长持续</param>
    /// <param name="abnormalType">添加异常状态类型:0-眩晕,1-失衡,2-沉默,3-无敌</param>
    public void AddAbnormalState(float abnormalTime, int abnormalType)
    {
        switch (abnormalType)
        {
            case 0:
                if (_abnormalStateTime[0] <= 0 && _abnormalStateTime[0] > -5)
                {
                    _abnormalStateTime[0] = abnormalTime;
                    _thisEntity.entityAM.AddStateToBan(new[] { EntityState.Move });
                    if (_thisEntity.entityAM.CurrentState == EntityState.Move)
                    {
                        _thisEntity.entityAM.TrySetState(EntityState.Idle, true);
                    }
                }
                else if (_abnormalStateTime[0] < abnormalTime)
                {
                    _abnormalStateTime[0] = abnormalTime;
                }
                break;
            case 1:
                if (_abnormalStateTime[1] <= 0 && _abnormalStateTime[1] > -5)
                {
                    _abnormalStateTime[1] = abnormalTime;
                    _thisEntity.entityAM.AddStateToBan(new[] { EntityState.Move, EntityState.Attack });
                    _thisEntity.entityAM.TrySetState(EntityState.Idle, true);
                }
                else if (_abnormalStateTime[1] < abnormalTime)
                {
                    _abnormalStateTime[1] = abnormalTime;
                }
                break;
            case 2:
                if (_abnormalStateTime[2] <= 0 && _abnormalStateTime[2] > -5)
                {
                    _abnormalStateTime[2] = abnormalTime;
                    _thisEntity.entityAM.AddStateToBan(new[] { EntityState.Attack });
                    if (_thisEntity.entityAM.CurrentState == EntityState.Attack)
                    {
                        print(_thisEntity.entityAM.TrySetState(EntityState.Idle, true));
                    }
                }
                else if (_abnormalStateTime[2] < abnormalTime)
                {
                    _abnormalStateTime[2] = abnormalTime;
                }
                break;
            case 3:
                if (_abnormalStateTime[3] <= 0 && _abnormalStateTime[3] > -5)
                {
                    _abnormalStateTime[3] = abnormalTime;
                    _thisEntity.Stats.AddSelectable(1);
                    _thisEntity.Stats.AddHurtable(1);
                }
                else if (_abnormalStateTime[3] < abnormalTime)
                {
                    _abnormalStateTime[3] = abnormalTime;
                }
                break;

            default: break;
        }
    }
    public void TryRemoveAbnormalState(int abnormalType)
    {
        if (_abnormalStateTime[abnormalType] > 0 || _abnormalStateTime[abnormalType] <= -5)
        {
            switch (abnormalType)
            {
                case 0:
                    _abnormalStateTime[0] = 0;
                    _thisEntity.entityAM.RemoveStateFromBan(new[] { EntityState.Move });
                    break;
                case 1:
                    _abnormalStateTime[1] = 0;
                    _thisEntity.entityAM.RemoveStateFromBan(new[] { EntityState.Move, EntityState.Attack });
                    break;
                case 2:
                    _abnormalStateTime[2] = 0;
                    _thisEntity.entityAM.RemoveStateFromBan(new[] { EntityState.Attack });
                    break;
                case 3:
                    _abnormalStateTime[3] = 0;
                    _thisEntity.Stats.AddHurtable(-1);
                    _thisEntity.Stats.AddSelectable(-1);
                    break;
                default: break;
            }
        }
    }
    public bool FetchAbnormalState(int abnormalType)
    {
        if (_abnormalStateTime[abnormalType] > 0 || _abnormalStateTime[abnormalType] <= -5)
        {
            return true;
        }
        return false;
    }
    private void AbnormalStateUpdate()
    {
        for (int i = 0; i < _abnormalStateTime.Length; i++)
        {
            if (_abnormalStateTime[i] - Time.fixedDeltaTime > 0)
            {
                _abnormalStateTime[i] -= Time.fixedDeltaTime;
            }
            else if (_abnormalStateTime[i] > -5)
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
                        _thisEntity.TakeDamage(null, _dotDatas[i].Damage, 1, 0, 0, 0, 0, _dotDatas[i].DamageType, 2);
                    }
                    else if (_thisEntity.Stats.CurrentHp > 1)
                    {
                        _thisEntity.TakeDamage(null, _thisEntity.Stats.CurrentHp - 1, 1, 0, 0, 0, 0, _dotDatas[i].DamageType, 2);
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
        white_list_buffs = new List<Buff>();
        normal_buffs = new List<Buff>();
        _thisEntity = this.transform.GetComponent<Entity>();
        _abnormalStateTime = new float[4];
        _dotDatas = new List<DOTData>();
    }
    public void Initialize()
    {

    }
    public void Dormancy()
    {
        for (int i = 0; i < white_list_buffs.Count; i++)
        {
            Destroy(white_list_buffs[i].buff_effect);
        }
        for (int i = 0; i < normal_buffs.Count; i++)
        {
            Destroy(normal_buffs[i].buff_effect);
        }
        if (_store != null) _store.Clear();
        white_list_buffs.Clear();
        normal_buffs.Clear();
        for (int i = 0; i < _abnormalStateTime.Length; i++)
            _abnormalStateTime[i] = 0;
        _dotDatas.Clear();
    }

}
