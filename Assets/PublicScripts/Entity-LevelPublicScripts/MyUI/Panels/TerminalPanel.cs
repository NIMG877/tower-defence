using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MyUI
{
    public class TerminalPanel : BasePanel
    {
        private static TerminalPanel _instance;
        public static TerminalPanel Panel
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TerminalPanel();
                }
                return (TerminalPanel)_instance;
            }
        }
        private DirectoryInfo _main, _s1;
        private LevelCollectionData[] _mains, _s1s;
        private LevelCollectionData[] _currentDatas;

        private List<Button> _coSelectBtns;
        private Button _enterB;
        private TextMeshProUGUI _description;
        private Transform _coSelectGroup;
        private Transform _p1;

        private TerminalPanel() : base(new UIType("Prefabs/UI/MyUIs/TerminalPanel"))
        {
            _p1 = GetComponentInChildrenByPath<Transform>("p1");
            _coSelectGroup = GetComponentInChildrenByPath<Transform>("p1/frame/leftNaveBar");
            _enterB = GetComponentInChildrenByPath<Button>("p1/right/enter");
            _description = GetComponentInChildrenByPath<TextMeshProUGUI>("p1/right/description");
            _coSelectBtns = new List<Button>() { GetComponentInChildrenByPath<Button>("p1/frame/leftNaveBar/b") };
            _coSelectBtns[0].onClick.AddListener(() => ShowCollectionData(0));
            //��ȡָ��·�������������Դ�ļ�  
            if (Directory.Exists("Assets/Resources/Prefabs/Levels/Main/"))
                _main = new DirectoryInfo("Assets/Resources/Prefabs/Levels/Main/");
            if (Directory.Exists("Assets/Resources/Prefabs/Levels/S1/"))
                _s1 = new DirectoryInfo("Assets/Resources/Prefabs/Levels/S1/");
            Button mainB = GetComponentInChildrenByPath<Button>("DownNaveBar/main");
            if (mainB != null)
            {
                mainB.onClick.AddListener(() =>
                    ShowMain()
                );
            }
            Button s1B = GetComponentInChildrenByPath<Button>("DownNaveBar/s1");
            if (s1B != null)
            {
                s1B.onClick.AddListener(() =>
                    ShowS1()
                );
            }
        }
        public override void OnEnter()
        {
            base.OnEnter();
            ShowDefaultCollection();
        }
        public override void OnResume()
        {
            base.OnResume();
            ShowDefaultCollection();
        }
        /// <summary>
        /// 每次展示重置为默认视图：主线集合列表 + 第一个集合的详情，避免 prefab 占位文字露出
        /// </summary>
        private void ShowDefaultCollection()
        {
            ShowMain();
            ShowCollectionData(0);
        }
        private void ShowMain()
        {
            if (_mains == null)
            {
                FileInfo[] files = _main.GetFiles("*.asset", SearchOption.TopDirectoryOnly);
                _mains = new LevelCollectionData[files.Length];
                for (int i = 0; i < files.Length; i++)
                {
                    _mains[i] = Resources.Load<LevelCollectionData>("Prefabs/Levels/Main/" + files[i].Name.Split('.')[0]);
                }
            }
            _p1.gameObject.SetActive(true);
            ChangeCoSelectBtns(_mains);
        }
        private void ShowS1()
        {
            if (_s1s == null)
            {
                FileInfo[] files = _s1.GetFiles("*.asset", SearchOption.TopDirectoryOnly);
                _s1s = new LevelCollectionData[files.Length];
                for (int i = 0; i < files.Length; i++)
                {
                    _s1s[i] = Resources.Load<LevelCollectionData>("Prefabs/Levels/S1/" + files[i].Name.Split('.')[0]);
                }
            }
            _p1.gameObject.SetActive(true);
            ChangeCoSelectBtns(_s1s);
        }
        private void ChangeCoSelectBtns(LevelCollectionData[] datas)
        {
            _currentDatas = datas;
            int count = _coSelectBtns.Count;
            if (count <= datas.Length)
            {
                for (int i = 0; i < count; i++)
                {
                    _coSelectBtns[i].GetComponentInChildren<TextMeshProUGUI>().text = datas[i].LevelCollectionName;
                    _coSelectBtns[i].gameObject.SetActive(true);
                }
                for (int i = count; i < datas.Length; i++)
                {
                    _coSelectBtns.Add(UnityEngine.Object.Instantiate(_coSelectBtns[0], _coSelectGroup));
                    _coSelectBtns[i].GetComponentInChildren<TextMeshProUGUI>().text = datas[i].LevelCollectionName;
                    _coSelectBtns[i].gameObject.SetActive(true);
                    _coSelectBtns[i].onClick.RemoveAllListeners();
                    int ii = i;
                    _coSelectBtns[i].onClick.AddListener(() => ShowCollectionData(ii));
                }
            }
            else
            {
                for (int i = 0; i < datas.Length; i++)
                {
                    _coSelectBtns[i].GetComponentInChildren<TextMeshProUGUI>().text = datas[i].LevelCollectionName;
                    _coSelectBtns[i].gameObject.SetActive(true);
                }
                for (int i = datas.Length; i < count; i++)
                {
                    _coSelectBtns[i].gameObject.SetActive(false);
                }
            }
        }
        private void ShowCollectionData(int index)
        {
            _description.text = _currentDatas[index].LevelCollectionDescription;
            _enterB.onClick.RemoveAllListeners();
            _enterB.onClick.AddListener(() =>
            {
                LevelSelectorPanel levelSelectorPanel = LevelSelectorPanel.Panel;
                levelSelectorPanel.SetLevelCollectionData(_currentDatas[index]);
                PanelManager.Push(levelSelectorPanel);
            });
        }
    }
}

