using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static PlasticGui.PlasticTableCell;

public enum BuffType
{
    /// <summary>
    /// 攻击力变化，值
    /// </summary>
    atk_delta_value,
    /// <summary>
    /// 攻击力变化，百分比
    /// </summary>
    atk_delta_percent,
    /// <summary>
    /// 防御力变化，值
    /// </summary>
    def_delta_value,
    /// <summary>
    /// 防御力变化，百分比
    /// </summary>
    def_delta_percent,
    /// <summary>
    /// 法抗变化，值
    /// </summary>
    mgr_delta_value,
    /// <summary>
    /// 法抗变化，百分比
    /// </summary>
    mgr_delta_percent,
    /// <summary>
    /// 最大生命上限变化，值
    /// </summary>
    mhp_delta_value,
    /// <summary>
    /// 最大生命上限变化，百分比
    /// </summary>
    mhp_delta_percent,
    /// <summary>
    /// 物理脆弱变化，概率
    /// </summary>
    phd_delta_rate,
    /// <summary>
    /// 法术脆弱变化，概率
    /// </summary>
    mgd_delta_rate,
    /// <summary>
    /// 物理闪避变化，概率
    /// </summary>
    phdoge_delta_rate,
    /// <summary>
    /// 法术闪避变化，概率
    /// </summary>
    mgdoge_delta_rate,
    /// <summary>
    /// 攻击间隔变化，值
    /// </summary>
    batkt_delta_value,
    /// <summary>
    /// 攻击间隔变化，百分比
    /// </summary>
    batkt_delta_percent,
    /// <summary>
    /// 攻击速度变化，值
    /// </summary>
    atkspd_delta_value,
    /// <summary>
    /// 阻挡数变化，值
    /// </summary>
    blo_delta_value,
    /// <summary>
    /// 最大攻击数量变化，值
    /// </summary>
    atkn_delta_value,
    /// <summary>
    /// 最小攻击数量变化，值
    /// </summary>
    atkminn_delta_value,
    /// <summary>
    /// 生命回复变化，值
    /// </summary>
    hprecover_delta_value,
    /// <summary>
    /// 移动速度变化，值
    /// </summary>
    mspeed_delta_value,
    /// <summary>
    /// 移动速度变化，百分比
    /// </summary>
    mspeed_delta_percent,
}
[Serializable]
public class Buff
{
    public float[] buff_values;
    public float buff_time;
    public string buff_name;
    public Entity origin_entity;
    public BuffType[] buff_types;
    public GameObject buff_effect;
    public Buff(float buff_time, string buff_name, Entity origin_entity, BuffType[] buff_types, GameObject buff_effect)
    {
        this.buff_values = new float[buff_types.Length];
        this.buff_time = buff_time;
        this.buff_name = buff_name;
        this.origin_entity = origin_entity;
        this.buff_types = buff_types;
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
    public Dictionary<BuffType, float> buffValue;
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

    private void FixedUpdate()
    {
        BuffUpdate();
        AbnormalStateUpdate();
        DOTUpdate();
    }


    #region///buff管理
    /// <summary>
    /// buff创建
    /// </summary>
    /// <param name="buffTypes">种类,percent类填写x时的含义为100x%</param>
    /// <param name="buffEffect">特效</param>
    /// <param name="buffName">名称</param>
    /// <param name="buffValue">值</param>
    /// <param name="buffTime">时间，小等于-5为永久</param>
    /// <param name="isWhiteList">白名单</param>
    /// <returns>buff数据</returns>
    public Buff CreateBuff(BuffType[] buffTypes, GameObject buffEffect, string buffName, float[] buffValue, float buffTime, bool isWhiteList)
    {
        Buff addBuff;
        if (isWhiteList)
        {
            foreach (Buff b in white_list_buffs)
            {
                if (b.buff_name == buffName)
                {
                    buffEffect = b.buff_effect;
                    addBuff = new Buff(buffTime, buffName, _thisEntity, buffTypes, buffEffect);
                    SetBuffValues(buffValue, addBuff);
                    white_list_buffs.Add(addBuff);
                    return addBuff;
                }
            }
            if (buffEffect)
            {
                buffEffect = Instantiate(buffEffect, _thisEntity.TempContainer.position, Quaternion.identity, _thisEntity.TempContainer);
            }
            addBuff = new Buff(buffTime, buffName, _thisEntity, buffTypes, buffEffect);
            SetBuffValues(buffValue, addBuff);
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
                    addBuff = new Buff(buffTime, buffName, _thisEntity, buffTypes, buffEffect);
                    SetBuffValues(buffValue, addBuff);
                    normal_buffs.Add(addBuff);
                    return addBuff;
                }
            }
            if (buffEffect)
            {
                buffEffect = Instantiate(buffEffect, _thisEntity.TempContainer.position, Quaternion.identity, _thisEntity.TempContainer);
            }
            addBuff = new Buff(buffTime, buffName, _thisEntity, buffTypes, buffEffect);
            SetBuffValues(buffValue, addBuff);
            normal_buffs.Add(addBuff);
            return addBuff;
        }
    }
    /// <summary>
    /// 单个buff销毁
    /// </summary>
    /// <param name="destroyBuff">需要销毁的buff数据</param>
    public void DestroyBuff(Buff destroyBuff)
    {
        SetBuffValues(new float[destroyBuff.buff_types.Length], destroyBuff);
        white_list_buffs.Remove(destroyBuff);
        normal_buffs.Remove(destroyBuff);
        if (!(white_list_buffs.Contains(destroyBuff) || normal_buffs.Contains(destroyBuff)))
        {
            Destroy(destroyBuff.buff_effect);
        }
    }
    /// <summary>
    /// 获取buff
    /// </summary>
    /// <param name="buffName">需要获取的buff名称</param>
    /// <returns>获取到的buff，若没有获取到则为null</returns>
    public Buff FetchBuff(string buffName)
    {
        foreach (Buff buff in normal_buffs)
        {
            if (buff.buff_name == buffName)
            {
                return buff;
            }
        }
        foreach (Buff buff in white_list_buffs)
        {
            if (buff.buff_name == buffName)
            {
                return buff;
            }
        }
        return null;
    }
    /// <summary>
    /// 设置Buff数值
    /// </summary>
    /// <param name="newBuffValues">要设置的Buff数值（长度必须与原数值长度相等）</param>
    /// <param name="setTarget">需要设置的目标Buff</param>
    public void SetBuffValues(float[] newBuffValues, Buff setTarget)
    {
        for (int i = 0; i < newBuffValues.Length; i++)
        {
            if (newBuffValues[i] != setTarget.buff_values[i])
                BuffResultStatistic(setTarget.buff_types[i], newBuffValues[i] - setTarget.buff_values[i]);
        }
        setTarget.buff_values = newBuffValues;
    }
    private void BuffResultStatistic(BuffType buffType, float value)
    {
        buffValue[buffType] += buffType switch
        {
            BuffType.atk_delta_value => value,
            BuffType.atk_delta_percent => value,
            BuffType.def_delta_value => value,
            BuffType.def_delta_percent => value,
            BuffType.mgr_delta_value => value,
            BuffType.mgr_delta_percent => value,
            BuffType.mhp_delta_value => value,
            BuffType.mhp_delta_percent => value,
            BuffType.phd_delta_rate => value * (1 + buffValue[buffType]),
            BuffType.mgd_delta_rate => value * (1 + buffValue[buffType]),
            BuffType.phdoge_delta_rate => value * (1 + buffValue[buffType]),
            BuffType.mgdoge_delta_rate => value * (1 + buffValue[buffType]),
            BuffType.batkt_delta_value => value,
            BuffType.batkt_delta_percent => value,
            BuffType.atkspd_delta_value => value,
            BuffType.blo_delta_value => value,
            BuffType.atkn_delta_value => value,
            BuffType.atkminn_delta_value => value,
            BuffType.hprecover_delta_value => value,
            BuffType.mspeed_delta_value => value,
            BuffType.mspeed_delta_percent => value,
            _ => 0,
        };
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
    /// <param name="abnormalTime">添加异常状态持续时间,值小于等于-5表示持续时间无限</param>
    /// <param name="abnormalType">添加异常状态类型:0-束缚,1-失衡,2-缴械,3-无敌</param>
    public void AddAbnormalState(float abnormalTime, int abnormalType)
    {
        switch (abnormalType)
        {
            case 0:
                if (_abnormalStateTime[0] <= 0 && _abnormalStateTime[0] > -5)
                {
                    _abnormalStateTime[0] = abnormalTime;
                    _thisEntity.entityAM.AddStateToBan(new int[1] { 2 });
                    if (_thisEntity.entityAM.CurrentState == 2)
                    {
                        _thisEntity.entityAM.TrySetState(1, true);
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
                    _thisEntity.entityAM.AddStateToBan(new int[2] { 2, 3 });
                    _thisEntity.entityAM.TrySetState(1, true);
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
                    _thisEntity.entityAM.AddStateToBan(new int[1] { 3 });
                    if (_thisEntity.entityAM.CurrentState == 3)
                    {
                        print(_thisEntity.entityAM.TrySetState(1, true));
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
                    _thisEntity.selectable += 1;
                    _thisEntity.hurtable += 1;
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
                    _thisEntity.entityAM.RemoveStateFromBan(new int[1] { 2 });
                    break;
                case 1:
                    _abnormalStateTime[1] = 0;
                    _thisEntity.entityAM.RemoveStateFromBan(new int[2] { 2, 3 });
                    break;
                case 2:
                    _abnormalStateTime[2] = 0;
                    _thisEntity.entityAM.RemoveStateFromBan(new int[1] { 3 });
                    break;
                case 3:
                    _abnormalStateTime[3] = 0;
                    _thisEntity.hurtable -= 1;
                    _thisEntity.selectable -= 1;
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
                    if (_thisEntity.CurrentHp - 1 > _dotDatas[i].Damage)
                    {
                        _thisEntity.TakeDamage(null, _dotDatas[i].Damage, 1, 0, 0, 0, 0, _dotDatas[i].DamageType, 2);
                    }
                    else if (_thisEntity.CurrentHp > 1)
                    {
                        _thisEntity.TakeDamage(null, _thisEntity.CurrentHp - 1, 1, 0, 0, 0, 0, _dotDatas[i].DamageType, 2);
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
        buffValue = new Dictionary<BuffType, float>();
        _abnormalStateTime = new float[4];
        _dotDatas = new List<DOTData>();
        for (int i = 0; i < System.Enum.GetNames(typeof(BuffType)).Length; i++)
        {
            buffValue.Add((BuffType)i, 0);
        }
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
        for (int i = 0; i < buffValue.Count; i++)
        {
            buffValue[(BuffType)i] = 0;
        }
        white_list_buffs.Clear();
        normal_buffs.Clear();
        for (int i = 0; i < _abnormalStateTime.Length; i++)
            _abnormalStateTime[i] = 0;
        _dotDatas.Clear();
    }

}
