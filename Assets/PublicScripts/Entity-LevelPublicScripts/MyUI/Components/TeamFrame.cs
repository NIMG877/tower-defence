using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

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
        private Button _deleteButton;
        private TextMeshProUGUI _deleteText;

        /// <summary>编队人数上限，同时决定 teamFrame 的角色格数量。</summary>
        public const int MaxMembers = 12;

        /// <summary>删除了当前编队（TeamPanel 据此刷新编队下拉——删除后 currentTeam 已切到相邻一支）。</summary>
        public event Action TeamDeleted;

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
            _deleteButton = delete;
            _deleteText = delete.GetComponentInChildren<TextMeshProUGUI>();
            RectTransform teamFrame = Resources.Load<RectTransform>("Prefabs/UI/MyUIs/Components/teamFrame");
            _teamFrame = Object.Instantiate(teamFrame.gameObject, parent).GetComponent<RectTransform>();
            _teamFrame.anchoredPosition = anchorPos;
            _characterTabs = new Button[MaxMembers];
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
                if (SaveSystem.GetTeamMembers(_teamName).Count == 0)
                {
                    // 空编队：删除编队本身（唯一一支时按钮已禁用，走不到这里）
                    NoticeManager.NM.LaunchMessageBox(
                        "是否确认删除当前编队？",
                        () =>
                        {
                            SaveSystem.DeleteTeam(_teamName);
                            _teamName = SaveSystem.CurrentTeamName;
                            UpdateCharacterTab();
                            TeamDeleted?.Invoke();
                        },
                        () => { });
                }
                else
                {
                    NoticeManager.NM.LaunchMessageBox(
                        "是否确认清空当前编队中的所有干员？",
                        () =>
                        {
                            SaveSystem.SetTeamMembers(_teamName, new List<EntityID>());
                            UpdateCharacterTab();
                        },
                        () => { });
                }
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
            var skillSelects = SaveSystem.GetTeamSkillSelects(_teamName);
            _selectedCharacter = new EntityID[members.Count];
            for (int i = 0; i < _selectedCharacter.Length; i++) _selectedCharacter[i] = members[i];
            for (int i = 0; i < _selectedCharacter.Length; i++)
            {
                CharacterCardManager.cardManager.SetCharacterCardAllowNull(_characterTabs[i], _selectedCharacter[i], skillSelects[i]);
            }
            if (_lastSelectNum > _selectedCharacter.Length)
            {
                for (int i = _selectedCharacter.Length; i < _lastSelectNum; i++)
                {
                    CharacterCardManager.cardManager.SetCharacterCardAllowNull(_characterTabs[i],EntityID.Null);
                }
            }
            _lastSelectNum = _selectedCharacter.Length;

            // 空且非唯一编队时为"删除编队"；空且是唯一编队仍显"清空编队"，仅禁用按钮（不允许删光所有编队）
            bool empty = _selectedCharacter.Length == 0;
            bool soleTeam = SaveSystem.Current.teams.Count == 1;
            _deleteText.text = empty && !soleTeam ? "删除编队" : "清空编队";
            _deleteButton.interactable = !(empty && soleTeam);
        }
    }
}

