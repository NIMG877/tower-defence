using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
namespace MyUI
{
    public class LevelSelectorPanel : BasePanel
    {
        private static LevelSelectorPanel _instance;
        public static LevelSelectorPanel Panel
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new LevelSelectorPanel();
                }
                return (LevelSelectorPanel)_instance;
            }
        }
        private LevelData[] _levelDatas;
        private List<Button> levelBs;
        private Transform _frame;
        private int _currentIndex;

        private TextMeshProUGUI _name;
        private TextMeshProUGUI _description;

        private LevelSelectorPanel() : base(new UIType("Prefabs/UI/MyUIs/LevelSelectorPanel"))
        {
            _name = GetComponentInChildrenByPath<TextMeshProUGUI>("rightframe/name");
            _description = GetComponentInChildrenByPath<TextMeshProUGUI>("rightframe/description");
            _frame = GetComponentInChildrenByPath<Transform>("leftframe/scrollContent/frame");

            levelBs = new List<Button>() { GetComponentInChildrenByPath<Button>("leftframe/scrollContent/frame/b0") };
            levelBs[0].onClick.AddListener(() => ShowLevelMessage(0));

            Button enterB = GetComponentInChildrenByPath<Button>("rightframe/enter");
            if (enterB != null)
            {
                enterB.onClick.AddListener(() =>
                {
                    TeamPanel teamPanel = TeamPanel.Panel;
                    teamPanel.SetLevelData(_levelDatas[_currentIndex]);
                    PanelManager.Push(teamPanel);
                });
            }

            Button entityDataB = GetComponentInChildrenByPath<Button>("rightframe/entityData");
            if (entityDataB != null)
            {
                entityDataB.onClick.AddListener(() =>
                {
                    MonsterHandbookPanel.Panel.SetMonsterDatas(CollectLevelEntities(_levelDatas[_currentIndex]));
                    PanelManager.Push(MonsterHandbookPanel.Panel);
                });
            }
        }
        public override void OnEnter()
        {
            base.OnEnter();
            ShowLevelMessage(0);
        }
        public override void OnResume()
        {
            base.OnResume();
        }
        public void SetLevelCollectionData(LevelCollectionData data)
        {
            string[] _levelPaths = data.LevelPaths;
            _levelDatas = new LevelData[_levelPaths.Length];
            for (int i = 0; i < _levelPaths.Length; i++)
            {
                _levelDatas[i] = Resources.Load<LevelData>(_levelPaths[i]);
            }

            int countB = levelBs.Count;
            if (countB <= _levelDatas.Length)
            {
                for (int i = 0; i < countB; i++)
                {
                    levelBs[i].gameObject.SetActive(true);
                    levelBs[i].transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = _levelDatas[i].LevelCode;
                }
                for (int i = countB; i < _levelDatas.Length; i++)
                {
                    levelBs.Add(Object.Instantiate(levelBs[0], _frame));
                    levelBs[i].gameObject.SetActive(true);
                    levelBs[i].onClick.RemoveAllListeners();
                    int ii = i;
                    levelBs[i].onClick.AddListener(() => ShowLevelMessage(ii));
                    levelBs[i].transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = _levelDatas[i].LevelCode;
                }
            }
            else
            {
                for (int i = 0; i < _levelDatas.Length; i++)
                {
                    levelBs[i].gameObject.SetActive(true);
                    levelBs[i].transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = _levelDatas[i].LevelCode;
                }
                for (int i = _levelDatas.Length; i < countB; i++)
                {
                    levelBs[i].gameObject.SetActive(false);
                }
            }

        }

        /// <summary>
        /// 收集本关会出现的实体(与运行时同口径:Locked 轨道不加载、仅 CommandType 0/1 的动作、按 EntityID 去重保序),
        /// 供图鉴面板做"本关情报"数据源。
        /// </summary>
        private static List<EntityData> CollectLevelEntities(LevelData level)
        {
            List<EntityData> entities = new List<EntityData>();
            if (level == null || level.Waves == null) return entities;
            HashSet<EntityID> seen = new HashSet<EntityID>();
            for (int w = 0; w < level.Waves.Length; w++)
            {
                LevelActions.Track[] tracks = level.Waves[w].Tracks;
                if (tracks == null) continue;
                for (int t = 0; t < tracks.Length; t++)
                {
                    if (tracks[t].Locked) continue;
                    LevelActions.Action[] actions = tracks[t].Actions;
                    if (actions == null) continue;
                    for (int a = 0; a < actions.Length; a++)
                    {
                        LevelActions.Action action = actions[a];
                        if (action.CommandType != 0 && action.CommandType != 1) continue;
                        EntityID id = action.EntityPrefabID;
                        if (id.IsNull || !seen.Add(id)) continue;
                        EntityData data = GameDataService.EntityRepository.Get(id);
                        if (data.ID.IsNull)
                        {
                            Debug.LogError($"实体 {id} 不在 EntityRepository,图鉴跳过该条情报");
                            continue;
                        }
                        entities.Add(data);
                    }
                }
            }
            return entities;
        }

        private void ShowLevelMessage(int index)
        {
            _currentIndex = index;
            _name.text = _levelDatas[index].LevelName;
            _description.text = _levelDatas[index].LevelDescription;
        }
    }
}

