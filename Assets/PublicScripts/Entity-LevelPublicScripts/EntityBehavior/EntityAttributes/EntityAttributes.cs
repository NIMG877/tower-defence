using System;
using UnityEngine;

public enum CharacterClassType
{
    Vanguard,
    Guard,
    Defender,
    Sniper,
    Caster,
    Medic,
    Supporter,
    Specialist,
    Device
}
public class EntityAttributes : MonoBehaviour
{
    [Serializable]
    private struct CanSpawnEntityData
    {
        public GameObject CanSpawnEntityPrefab;
        public int CanSpawnEntityNum;
    }
    [SerializeField] private string entity_name;
    [SerializeField, TextArea(2, 5)] private string description;
    [SerializeField] private Sprite entity_head_img;
    [SerializeField] private Sprite entity_halfBody_img;
    [SerializeField] private Sprite entity_hole_img;
    [Space(20)]
    [SerializeField] private Vector2Int[] atk_range;
    [SerializeField] private float atk_radius;
    [SerializeField] private float max_hp;
    [SerializeField] private float atk;
    [SerializeField] private float base_attack_time;
    [SerializeField] private float def;
    [SerializeField] private float magic_resistance;
    [SerializeField] private float phDoge;
    [SerializeField] private float mgDoge;
    [SerializeField] private int block_occupation;
    [SerializeField] private int attack_num;
    [SerializeField, Tooltip("嘲讽等级")] private int taunt_level;
    [SerializeField, Tooltip("伤害类型：0-物理，1-法术，2-真实，3-治疗")] private int damageType;
    [Space(20)]
    public int SkillSelect;
    [SerializeField] private Skill[] skills;
    [SerializeField] private Talent[] talents;
    [SerializeField] private CanSpawnEntityData[] can_spawn_entity_datas;
    [Space(20), Header("as a character")]
    [SerializeField, Tooltip("稀有度：1~6")] private int characterRarity;
    [SerializeField, Tooltip("势力")] private string characterInfluence;
    [SerializeField, Tooltip("职业")] private CharacterClassType characterClass;
    [Space(20), Header("is not a character")]
    [SerializeField, Tooltip("ID")] private int monsterId;
    [SerializeField, Tooltip("地位：0-普通，1-精英，2-领袖")] private int monsterStatus;
    [SerializeField, Tooltip("标签")] private string monsterLabel;
    [SerializeField, Tooltip("首要目标")] private bool isPrimary;
    [SerializeField, Tooltip("计算击杀数")] private bool countOperated;
    [SerializeField, Tooltip("消耗目标点生命数")] private int _levelHpComsume;
    public string EntityName { get { return entity_name; } }
    public string Description { get { return description; } }
    public Sprite EntityHeadImg { get { return entity_head_img; } }
    public Sprite EntityHalfBodyImg { get { return entity_halfBody_img; } }
    public Sprite EntityHoleImg { get { return entity_hole_img; } }
    public (int x, int y)[] ATK_Range
    {
        get
        {
            if (atk_range != null)
            {
                (int x, int y)[] atkRange = new (int x, int y)[atk_range.Length];
                for (int i = 0; i < atkRange.Length; i++)
                {
                    atkRange[i].x = atk_range[i].x;
                    atkRange[i].y = atk_range[i].y;
                }
                return atkRange;
            }
            else
            {
                return null;
            }

        }
    }
    public float ATK_Radius { get { return atk_radius; } }
    public float Max_HP { get { return max_hp; } }
    public float ATK { get { return atk; } }
    public float BaseAttackTime { get { return base_attack_time; } }
    public float DEF { get { return def; } }
    public float MagicResistance { get { return magic_resistance; } }
    public float PhDoge { get { return phDoge; } }
    public float MgDoge { get { return mgDoge; } }
    public int BlockOccupation { get { return block_occupation; } }
    public int AttackNum { get { return attack_num; } }
    public int TauntLevel { get { return taunt_level; } }
    public int DamageType { get { return damageType; } }
    public Skill[] Skills { get { return skills; } }
    public Talent[] Talents { get { return talents; } }
    public (GameObject[] prefabs, int[] nums) CanSpawnEntityDatas
    {
        get
        {
            int l = can_spawn_entity_datas.Length;
            GameObject[] prefabs = new GameObject[l];
            int[] nums = new int[l];
            for (int i = 0; i < l; i++)
            {
                prefabs[i] = can_spawn_entity_datas[i].CanSpawnEntityPrefab;
                nums[i] = can_spawn_entity_datas[i].CanSpawnEntityNum;
            }
            return (prefabs, nums);
        }
    }
    public int CharacterRarity { get { return characterRarity; } }
    public string CharacterInfluence { get { return characterInfluence; } }
    public CharacterClassType CharacterClass { get { return characterClass; } }
    public int MonsterId { get { return monsterId; } }
    public int MonsterStatus { get { return monsterStatus; } }
    public string MonsterLabel { get { return monsterLabel; } }
    public bool IsPrimary { get { return isPrimary; } }
    public bool CountOperated { get { return countOperated; } }
    public int LevelHpComsume { get { return _levelHpComsume; } }

}
