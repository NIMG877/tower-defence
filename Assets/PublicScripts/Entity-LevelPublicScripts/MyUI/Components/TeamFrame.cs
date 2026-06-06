using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MyUI
{
    public class TeamFrame
    {
        private Button[] _characterTabs;
        private RectTransform _teamFrame;
        private CharacterSelectPanel _characterSelectPanel;
        private string _teamName;
        private EntityID[] _selectedCharacter;
        private int _lastSelectNum;
        public string TeamName
        {
            set
            {
                if (value != _teamName)
                {
                    _teamName = value;
                }
            }
        }
        public TeamFrame(Vector2 anchorPos, RectTransform parent, Button delete, Button fastadd)
        {
            RectTransform teamFrame = Resources.Load<RectTransform>("Prefabs/UI/MyUIs/Components/teamFrame");
            _teamFrame = Object.Instantiate(teamFrame.gameObject, parent).GetComponent<RectTransform>();
            _teamFrame.anchoredPosition = anchorPos;
            _characterTabs = new Button[12];
            _characterTabs[0] = _teamFrame.Find("0").GetComponent<Button>();
            _characterSelectPanel = CharacterSelectPanel.Panel;
            for (int i = 0; i < _characterTabs.Length; i++)
            {
                if (i >= 1)
                    _characterTabs[i] = Object.Instantiate(_characterTabs[0], _teamFrame);
                int index = i;
                _characterTabs[index].onClick.AddListener(() =>
                {
                    _characterSelectPanel.TeamName = _teamName;
                    _characterSelectPanel.SelectIndex = index;
                    PanelManager.Push(_characterSelectPanel);
                });
            }
            delete.onClick.AddListener(() =>
            {
                //������ʾ
            });
            fastadd.onClick.AddListener(() =>
            {
                _characterSelectPanel.TeamName = _teamName;
                _characterSelectPanel.SelectIndex = -1;
                PanelManager.Push(_characterSelectPanel);
            });
        }
        public void UpdateCharacterTab()
        {
            var members = SaveSystem.GetTeamMembers(_teamName);
            _selectedCharacter = new EntityID[members.Count];
            for (int i = 0; i < _selectedCharacter.Length; i++) _selectedCharacter[i] = members[i];
            for (int i = 0; i < _selectedCharacter.Length; i++)
            {
                CharacterCardManager.cardManager.SetCharacterCardAllowNull(_characterTabs[i], _selectedCharacter[i]);
            }
            if (_lastSelectNum > _selectedCharacter.Length)
            {
                for (int i = _selectedCharacter.Length; i < _lastSelectNum; i++)
                {
                    CharacterCardManager.cardManager.SetCharacterCardAllowNull(_characterTabs[i],EntityID.Null);
                }
            }
            _lastSelectNum = _selectedCharacter.Length;
        }
    }
}

