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

        private void ShowLevelMessage(int index)
        {
            _currentIndex = index;
            _name.text = _levelDatas[index].LevelName;
            _description.text = _levelDatas[index].LevelDescription;
        }
    }
}

