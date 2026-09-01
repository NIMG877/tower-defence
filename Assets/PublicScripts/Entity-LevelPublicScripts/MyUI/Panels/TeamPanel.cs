using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace MyUI
{
    public class TeamPanel : BasePanel
    {
        private static TeamPanel _instance;
        private LevelData _levelData;
        public static TeamPanel Panel
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TeamPanel();
                }
                return (TeamPanel)_instance;
            }
        }
        private TeamFrame _teamFrame;
        private TeamSwitchUI _teamSwitch;
        private Button _startButton;
        private TeamPanel() : base(new UIType("Prefabs/UI/MyUIs/TeamPanel"))
        {
            _startButton = GetComponentInChildrenByPath<Button>("start");
            if (_startButton != null)
            {
                _startButton.transform.SetAsLastSibling();
                _startButton.onClick.AddListener(() => EnterBattle());
            }
            _teamFrame = new TeamFrame(new Vector2(-50.2938f, -20), UIObject.GetComponent<RectTransform>(), GetComponentInChildrenByPath<Button>("delete"), GetComponentInChildrenByPath<Button>("fastadd"));
            _teamFrame.TeamName = SaveSystem.CurrentTeamName;
            _teamSwitch = new TeamSwitchUI(GetComponentInChildrenByPath<TMP_Dropdown>("teamswitch"));
            _teamSwitch.TeamSelected += name =>
            {
                _teamFrame.TeamName = name;
                _teamFrame.UpdateCharacterTab();
            };
            _teamSwitch.CurrentTeamRenamed += name => _teamFrame.TeamName = name;
            _teamSwitch.TeamAdded += _teamFrame.UpdateCharacterTab;
            _teamFrame.TeamDeleted += _teamSwitch.RefreshOptions;
        }
        public void SetLevelData(LevelData levelData)
        {
            _levelData = levelData;
            SetStartButtonVisible(levelData != null);
        }
        public void SetTeamManagementMode()
        {
            _levelData = null;
            SetStartButtonVisible(false);
        }
        private void SetStartButtonVisible(bool visible)
        {
            if (_startButton != null)
            {
                _startButton.gameObject.SetActive(visible);
            }
        }
        private void EnterBattle()
        {
            new LevelResourceSharing();
            LevelResourceSharing.LD = _levelData;
            PanelManager.Push(CutToLevelPanel.Panel);
        }
        public override void OnEnter()
        {
            base.OnEnter();
            _teamFrame.TeamName = SaveSystem.CurrentTeamName;
            _teamSwitch.RefreshOptions();
            _teamFrame.UpdateCharacterTab();
        }
        public override void OnResume()
        {
            base.OnResume();
            _teamFrame.UpdateCharacterTab();
        }
    }
}

