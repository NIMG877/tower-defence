using MyUI;
using Spine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using UnityEngine;
using static PlasticGui.PlasticTableCell;


public enum OrderLogic
{
    ResistFirst_Priority_Des,
    Priority_Des,
    Hprate_NoFull_Asc,
    ResistFirst_OtherCampFirst_Priority_Des,
}


public class Entity : MonoBehaviour, IPoolOperation
{
    public EntityData EntityData;
    public AnimationMachine entityAM;
    [HideInInspector] public BuffController buffController;
    [HideInInspector] public AttackBase AttackBase;
    [HideInInspector] public MoveBase MoveBase;
    public InteractableStatic InteractableStatic;
    [HideInInspector] public Skill[] skill;
    [HideInInspector] public Talent[] Talents;

    private (int x, int y)[] _visionRangeF;
    private float _visionRadiusF;
    private float max_hp_first;
    private float def_first;
    private float magic_resistance_first;
    private float _phDoge_first;
    private float _mgDoge_first;
    private int block_occupation_first;
    private int taunt_level_first;

    private (int x, int y)[] _visionRangeS;
    private float _visionRadiusS;
    private float max_hp_second;
    private float def_second;
    private float magic_resistance_second;
    private float _phDoge_second;
    private float _mgDoge_second;
    private int block_occupation_second;
    private int taunt_level_second;
    private int _camp;

    private float current_hp_rate;
    public string NAME { get { return EntityData.ChineseName; } }
    public float DEF_1 { get { return def_first; } }
    public (int x, int y)[] VisionRange_1 { get { return _visionRangeF; } }
    public float MagicResistance_1 { get { return magic_resistance_first; } }
    public int BlockOccupation_1 { get { return block_occupation_first; } }
    public float MaxHp_1 { get { return max_hp_first; } }
    public float DEF_2 { get { return def_second; } }
    public float MagicResistance_2 { get { return magic_resistance_second; } }
    public int BlockOccupation { get { return block_occupation_second; } }
    public (int x, int y)[] VisionRange
    {
        get
        {
            return _visionRangeS;
        }
        set
        {
            _visionRangeS = MapDataManager.Manager.RangeCaculator(value, ((int)(this.transform.position.x + 0.5), (int)(this.transform.position.y + 0.5)), _orientation);
        }
    }
    public float MaxHpS { get { return max_hp_second; } }
    public float CurrentHp { get { return current_hp_rate * max_hp_second; } }
    /// <summary>
    /// 当前血量比例，一般不建议直接使用set修改其值，除非是特殊机�?
    /// </summary>
    public float CurrentHpRate { get { return current_hp_rate; } set { current_hp_rate = Math.Min(1, value); } }
    public float Priority { get { return priority + 1000 * TauntLevel; } set { priority = value; } }
    public int TauntLevel { get { return taunt_level_second; } }
    public float HpRecover { get { return Math.Max(0, buffController.buffValue[BuffType.hprecover_delta_value]); } }
    public int Camp
    {
        get { return _camp; }
        set
        {
            if (_camp != value)
            {
                EntityManager.Manager.RemoveEntityFromBlock(InBlocks, this, _camp);
                if (EntityManager.Manager.RemoveEntityFromList(this, _camp))
                {
                    EntityManager.Manager.AddEntityToList(this, value);
                }
                _camp = value;
                EntityManager.Manager.AddEntityToBlock(InBlocks, this, _camp);
            }
        }
    }
    public Vector2 EntityPosition
    {
        get { return this.transform.position; }
        set
        {
            this.transform.position = value;
            FindSelfInBlocks(value);
        }
    }
    public EntityPool thisEntityPool;
    public (int i, int j)[] InBlocks;
    private float atkTimer;
    public List<Entity> entityResistList;
    public List<Entity> turretsInRange;
    public List<Entity> monstersInRange;

    private Entity[] _attackTarget;
    [SerializeField] public Transform TempContainer;
    protected OrderLogic EntityOrderLogic;
    private float priority;
    /// <summary>
    /// 朝向 0-up�?1-right�?2-down�?3-left
    /// </summary>
    protected int _orientation;
    public bool participateIn;
    /// <summary>
    /// 可受伤的：当值为0时，实体才可受到伤害
    /// </summary>
    public int hurtable;
    /// <summary>
    /// 可被选择的：当值为0时，实体才可被非强制选择所选择
    /// </summary>
    public int selectable;
    /// <summary>
    /// 孤立�?
    /// </summary>
    public bool isolate;
    /// <summary>
    /// 隐匿�?
    /// </summary>
    public bool dormant;

    #region//委托事件与函�?
    public delegate void OperationsBeforeHurt(Entity origin, ref float damage, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int damageType, int applyType);//受伤前事�?
    /// <summary>
    /// 在受到伤害之前调用，可以用来修改造成伤害的倍率�?
    /// </summary>
    public event OperationsBeforeHurt OnBeforeHurt;
    public delegate void OperationsAfterHurt(Entity origin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly);//受伤后事�?
    /// <summary>
    /// 在受到伤害之后调用，可以用来检测是否死亡等
    /// </summary>
    public event OperationsAfterHurt OnAfterHurt;
    public delegate void OperationsBeforeDieAnimation();
    /// <summary>
    /// 在播放死亡动画之前调用，可用于死亡检�?
    /// </summary>
    public event OperationsBeforeDieAnimation OnBeforeDieAnimation;
    #endregion

    protected void FixedUpdate()
    {
        if (!participateIn)
            return;
        AttributesCaculateSecond();
        //AttackTimer();
        if (current_hp_rate < 1)
        {
            current_hp_rate = Math.Min(1, current_hp_rate + HpRecover / MaxHpS);
        }
        HPUpdate();
        if (VisionRange != null)
        {
            monstersInRange = EntityManager.Manager.EntitySelector_Range(VisionRange, Camp, Camp == 2, 1, false);
            turretsInRange = EntityManager.Manager.EntitySelector_Range(VisionRange, Camp, Camp == 1, 1, false);
        }
        else
        {
            monstersInRange = EntityManager.Manager.EntitySelector_Radius((this.transform.position.x, this.transform.position.y), Camp, Camp == 2, _visionRadiusS, false);
            turretsInRange = EntityManager.Manager.EntitySelector_Radius((this.transform.position.x, this.transform.position.y), Camp, Camp == 1, _visionRadiusS, false);
        }
    }
    private void AttributesCaculateFirst()
    {
        print($"EntityData.ID: {EntityData.ID}");
        print(EntityData.VisionRange);
        _visionRangeF = new (int x, int y)[EntityData.VisionRange.Count];   // EntityData.VisionRange 改为 List<Vector2Int> 后：.Length → .Count
        for (int i = 0; i < _visionRangeF.Length; i++)
        {
            _visionRangeF[i] = (EntityData.VisionRange[i].x, EntityData.VisionRange[i].y);
        }
        _visionRadiusF = EntityData.VisionRadius;
        max_hp_first = EntityData.MaxHp;
        def_first = EntityData.Defense;
        magic_resistance_first = EntityData.MagicResistance;
        _phDoge_first = EntityData.PhysicalDodge;
        _mgDoge_first = EntityData.MagicResistance;
        block_occupation_first = EntityData.BlockOccupation;
        taunt_level_first = EntityData.TauntLevel;
    }
    private void AttributesCaculateSecond()
    {
        _visionRadiusS = _visionRadiusF;
        max_hp_second = Math.Max(0.001f, max_hp_first + buffController.buffValue[BuffType.mhp_delta_value] + max_hp_first * buffController.buffValue[BuffType.mhp_delta_percent]);
        def_second = def_first + Math.Max(0, buffController.buffValue[BuffType.def_delta_value] + def_first * buffController.buffValue[BuffType.def_delta_percent]);
        magic_resistance_second = Math.Max(0, magic_resistance_first + buffController.buffValue[BuffType.mgr_delta_value] + magic_resistance_first * buffController.buffValue[BuffType.mgr_delta_percent]);
        _phDoge_second = 1 - (1 - _phDoge_first) * (1 - buffController.buffValue[BuffType.phdoge_delta_rate]);
        _mgDoge_second = 1 - (1 - _mgDoge_first) * (1 - buffController.buffValue[BuffType.mgdoge_delta_rate]);
        block_occupation_second = Math.Max(0, block_occupation_first + (int)buffController.buffValue[BuffType.blo_delta_value]);
        taunt_level_second = taunt_level_first;
    }
    public void Die()
    {
        OnBeforeDieAnimation?.Invoke();
        entityAM.TrySetState(6, false);
        participateIn = false;
        if (Camp == 1)
        {
            if (EntityData.ID.ID_C == "t")
            {
                AudioManager.Manager.PlayAudio("token_die", 1, false, false);
            }
            else
            {
                AudioManager.Manager.PlayAudio("char_die", 1, false, false);
            }
        }
        else
        {
            AudioManager.Manager.PlayAudio("enemy_die", 1, false, false);
        }
    }
    public void HPUpdate()
    {
        if (CurrentHp <= 0)
        {
            Die();
        }
    }
    /// <summary>
    /// 设置朝向
    /// </summary>
    /// <param name="orientation">设置的朝�?:0-up�?1-right�?2-down�?3-left</param>
    public void SetOrientation(int orientation)
    {
        _orientation = orientation;
        VisionRange = _visionRangeF;
    }
    /// <summary>
    /// 造成伤害
    /// </summary>
    /// <param name="damageOrigin">伤害来源实体</param>
    /// <param name="damage">伤害原�?</param>
    /// <param name="multiplyer">伤害倍率</param>
    /// <param name="defPenetrate">无视防御，百分比，范围[0�?1]</param>
    /// <param name="mgrPenetrate">无视法抗，百分比，范围[0�?1]</param>
    /// <param name="defPenetrate_value">无视防御，值，范围[0,inf)</param>
    /// <param name="mgrPenetrate_value">无视法抗，值，范围[0,inf)</param>
    /// <param name="damageType">伤害类型�?0-物理伤害�?1-法术伤害�?2-真实伤害�?3-治疗</param>
    /// <param name="applyType">伤害应用类型�?0-普通伤害，1-溅射伤害�?2-buff伤害</param>
    /// <returns>本次造成伤害是否为致命伤</returns>
    public bool TakeDamage(Entity damageOrigin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType)
    {
        if (damageType <= 2 && (current_hp_rate <= 0 || hurtable > 0))
            return false;
        float minRate = 0.05f;
        float damageC1 = damageType switch
        {
            0 => Math.Max(damage * minRate, damage - def_second),
            1 => Math.Max(damage * minRate, damage * (1 - magic_resistance_second / 100)),
            2 => damage,
            3 => damage,
            _ => 0,
        };
        float finalDamage = damageType switch
        {
            0 => Math.Max(damage * multiplyer * minRate, damage * multiplyer - (1 - defPenetrate) * (def_second - defPenetrate_value)) * Math.Max(0, 1 + buffController.buffValue[BuffType.phd_delta_rate]),
            1 => Math.Max(damage * multiplyer * minRate, damage * multiplyer * (1 - (1 - mgrPenetrate) * (magic_resistance_second - mgrPenetrate_value) / 100)) * Math.Max(0, 1 + buffController.buffValue[BuffType.mgd_delta_rate]),
            2 => damage * multiplyer,
            3 => damage * multiplyer,
            _ => 0,
        };
        //记录伤害
        LevelMessagePanel.Panel.AcceptDamageMessage(this, damageOrigin, finalDamage, damageType);
        //显示伤害
        if (damageType <= 2)
        {
            //判断闪避
            if ((damageType == 0 && RandomHelper.Helper.RandomP(_phDoge_second)) || (damageType == 1 && RandomHelper.Helper.RandomP(_mgDoge_second)))
            {
                LevelMessagePanel.Panel.ShowText(this.transform.position, 5, 0);
                return false;
            }
            OnBeforeHurt?.Invoke(damageOrigin, ref finalDamage, ref multiplyer, ref defPenetrate, ref mgrPenetrate, ref defPenetrate_value, ref mgrPenetrate_value, ref damageType, applyType);
            if (finalDamage >= 1.5f * damageC1)
            {
                LevelMessagePanel.Panel.ShowText(this.transform.position, 0, (int)finalDamage);
            }
            current_hp_rate -= finalDamage / MaxHpS;
            if (current_hp_rate <= 0)
            {
                current_hp_rate = 0;
                OnAfterHurt?.Invoke(damageOrigin, finalDamage, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, applyType, true);
                return true;
            }
            OnAfterHurt?.Invoke(damageOrigin, finalDamage, multiplyer, defPenetrate, mgrPenetrate, defPenetrate_value, mgrPenetrate_value, damageType, applyType, false);
            return false;
        }
        else
        {
            current_hp_rate = Math.Min(1, current_hp_rate + finalDamage / MaxHpS);
            MyUI.LevelMessagePanel.Panel.ShowText(EntityPosition, 1, (int)finalDamage);
            return false;
        }
    }

    public Entity[] EntityUpDate(Entity[] entitiesA)
    {
        List<Entity> entities = new List<Entity>(entitiesA);
        for (int i = entities.Count - 1; i >= 0; i--)
        {
            if (entities[i].participateIn == false || entities[i].selectable != 0)
            {
                entities.RemoveAt(i);
            }
        }
        return entities.ToArray();
    }
    public List<Entity> PriorityOrder(List<Entity> originList, OrderLogic orderLogic)
    {
        List<Entity> entitiesList = new List<Entity>(originList);
        switch (orderLogic)
        {
            case OrderLogic.ResistFirst_Priority_Des:
                for (int i = 0; i < entityResistList.Count; i++)
                {
                    if (entityResistList[i].selectable == 0 && !entitiesList.Contains(entityResistList[i]))
                    {
                        entitiesList.Add(entityResistList[i]);
                    }

                }
                for (int i = 0; i < entitiesList.Count - 1; i++)
                {
                    for (int j = i + 1; j < entitiesList.Count; j++)
                    {
                        bool containI = entityResistList.Contains(entitiesList[i]);
                        bool containJ = entityResistList.Contains(entitiesList[j]);
                        if ((!containI && containJ) || (containI == containJ && entitiesList[i].Priority < entitiesList[j].Priority))
                        {
                            (entitiesList[i], entitiesList[j]) = (entitiesList[j], entitiesList[i]);
                        }
                    }
                }
                break;
            case OrderLogic.Priority_Des:
                entitiesList.Sort((x, y) => -x.Priority.CompareTo(y.Priority));
                break;
            case OrderLogic.Hprate_NoFull_Asc:
                for (int i = entitiesList.Count - 1; i >= 0; i--)
                {
                    if (entitiesList[i].CurrentHpRate >= 1)
                    {
                        entitiesList.RemoveAt(i);
                    }
                }
                entitiesList.Sort((x, y) => x.CurrentHpRate.CompareTo(y.CurrentHpRate));
                break;
            case OrderLogic.ResistFirst_OtherCampFirst_Priority_Des:

                for (int i = 0; i < entitiesList.Count - 1; i++)
                {
                    for (int j = i + 1; j < entitiesList.Count; j++)
                    {
                        bool containI = entityResistList.Contains(entitiesList[i]);
                        bool containJ = entityResistList.Contains(entitiesList[j]);
                        bool isTurretI = entitiesList[i].Camp != Camp;
                        bool isTurretJ = entitiesList[j].Camp != Camp;
                        if ((!containI && containJ) || (containI == containJ && entitiesList[i].Priority < entitiesList[j].Priority))
                        {
                            (entitiesList[i], entitiesList[j]) = (entitiesList[j], entitiesList[i]);
                        }

                    }
                }
                break;
            default: break;
        }

        return entitiesList;
    }
    protected void FindSelfInBlocks(Vector2 point)
    {
        float entityR = EntityManager.EntityR;
        EntityManager.Manager.RemoveEntityFromBlock(InBlocks, this, Camp);
        (int i, int j) ij0 = ((int)(point.y + 0.5), (int)(point.x + 0.5));
        InBlocks[0] = ij0;
        float k = 0.5f - entityR;
        if (Math.Abs(point.x - ij0.j) < k && Math.Abs(point.y - ij0.i) < k)
        {
            InBlocks[1] = (-1, -1);
            InBlocks[2] = (-1, -1);
            InBlocks[3] = (-1, -1);
        }
        else
        {
            int dx = (point.x - ij0.j) > 0 ? 1 : -1;
            int dy = (point.y - ij0.i) > 0 ? 1 : -1;
            Vector2 pV = new Vector2(ij0.j + 0.5f * dx, ij0.i + 0.5f * dy);
            if (Vector2.Distance(pV, point) <= entityR)
            {
                InBlocks[1] = (ij0.i + dy, ij0.j);
                InBlocks[2] = (ij0.i, ij0.j + dx);
                InBlocks[3] = (ij0.i + dy, ij0.j + dx);
            }
            else if (Math.Abs(point.x - ij0.j) >= k && Math.Abs(point.y - ij0.i) >= k)
            {
                InBlocks[1] = (ij0.i + dy, ij0.j);
                InBlocks[2] = (ij0.i, ij0.j + dx);
                InBlocks[3] = (-1, -1);
            }
            else if (Math.Abs(point.x - ij0.j) >= k && Math.Abs(point.y - ij0.i) < k)
            {
                InBlocks[1] = (ij0.i, ij0.j + dx);
                InBlocks[2] = (-1, -1);
                InBlocks[3] = (-1, -1);
            }
            else if (Math.Abs(point.x - ij0.j) < k && Math.Abs(point.y - ij0.i) >= k)
            {
                InBlocks[1] = (ij0.i + dy, ij0.j);
                InBlocks[2] = (-1, -1);
                InBlocks[3] = (-1, -1);
            }
        }
        EntityManager.Manager.AddEntityToBlock(InBlocks, this, Camp);
    }

    public virtual void PreWarm()
    {
        if (this.TryGetComponent(out AnimationMachine aM))
        {
            entityAM = aM;
        }
        else
        {
            Debug.LogWarning("未绑定动画状态机");
        }
        if (this.TryGetComponent(out BuffController bF))
        {
            buffController = bF;
        }
        else
        {
            Debug.LogWarning("未绑定Buff控制�?");
        }
        if (this.TryGetComponent(out AttackBase ab))
        {
            AttackBase = ab;
        }
        else
        {
            Debug.LogWarning("未绑定攻击模�?");
        }
        if (this.TryGetComponent(out MoveBase mb))
        {
            MoveBase = mb;
        }
        else if (this.TryGetComponent(out InteractableStatic iS))
        {
            InteractableStatic = iS;
        }
        else
        {
            Debug.LogWarning("未绑定可移动或交互静止模�?");
        }

        AttributesCaculateFirst();
        //Skill[] skills = entityAttributes.Skills == null ? new Skill[0] : entityAttributes.Skills;
        //skill = skills;
        //int skillSelectNum = entityAttributes.SkillSelect;
        //if (skills.Length > 0 && skillSelectNum > 0)
        //{
        //    for (int i = 0; i < skills.Length; i++)
        //    {
        //        if (i == skillSelectNum - 1)
        //        {

        //            skill = new Skill[1] { entityAttributes.Skills[skillSelectNum - 1] };
        //        }
        //        else
        //        {
        //            DestroyImmediate(skills[i]);
        //        }
        //    }
        //}
        //Talents = entityAttributes.Talents;
        entityResistList = new List<Entity>();
        turretsInRange = new List<Entity>();
        monstersInRange = new List<Entity>();
        InBlocks = new (int x, int y)[4];
    }
    public virtual void Initialize()
    {
        entityAM.TrySetState(5, false);
        current_hp_rate = 1;
        participateIn = true;
        hurtable = 0;
        selectable = 0;
        isolate = false;
        dormant = false;
        atkTimer = 0;
        TempContainer = new GameObject("TempContainer").transform;
        TempContainer.position = this.transform.position;
        TempContainer.parent = this.transform;
        AttributesCaculateSecond();
        if (Camp == 2)
        {
            LevelActionManager.Manager.AddToWaveEntities(this);
        }
        bool canmove = MoveBase;
        bool camp2 = Camp == 2;
        SlidersManager.Manager.SetSlider<HpSliderController>(this, 4, camp2 ? 0 : 1, 0, camp2, canmove);
        if (skill != null && skill.Length == 1)
        {
            SlidersManager.Manager.SetSlider<SpSliderController>(this, 10, camp2 ? 3 : 4, 1, camp2, canmove);
        }

        //entityAM.OnAttackAnimationBegin += new AnimationMachine.OperationsOnAttackAnimationBegin(() =>
        //{
        //    if (_attackTarget.Length > 0)
        //    {
        //        Vector3 centerPos = new Vector3(0, 0, 0);
        //        int i;
        //        for (i = 0; i < _attackTarget.Length; i++)
        //        {
        //            centerPos += _attackTarget[i].transform.position;
        //        }
        //        centerPos /= i;
        //        entityAM.SetDirection(centerPos);
        //    }
        //});
    }
    public virtual void Dormancy()
    {
        entityResistList.Clear();
        turretsInRange.Clear();
        monstersInRange.Clear();

        //清空事件注册的函�?
        OnBeforeHurt = null;
        OnAfterHurt = null;
        OnBeforeDieAnimation = null;
        EntityManager.Manager.RemoveEntityFromList(this, Camp);
        EntityManager.Manager.RemoveEntityFromBlock(InBlocks, this, Camp);
        Destroy(TempContainer.gameObject);
        TempContainer = null;
        if (Camp == 2)
        {
            LevelActionManager.Manager.RemoveFromWaveEntities(this);
        }
    }
}
