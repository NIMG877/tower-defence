using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MyUI
{
    public class HomePanel : BasePanel
    {
        private static HomePanel _instance;
        public static HomePanel Panel
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new HomePanel();
                }
                return (HomePanel)_instance;
            }
        }
        private HomePanel() : base(new UIType("Prefabs/UI/MyUIs/HomePanel"))
        {
            Button terminalb = GetComponentInChildrenByPath<Button>("RightNaveBar/terminal");
            terminalb.onClick.AddListener(() => PanelManager.Push(TerminalPanel.Panel));
            Button handBook = GetComponentInChildrenByPath<Button>("RightNaveBar/handbook");
            handBook.onClick.AddListener(() => PanelManager.Push(MonsterHandbookPanel.Panel));
            Button editor = GetComponentInChildrenByPath<Button>("RightNaveBar/editor");
            editor.onClick.AddListener(() => PanelManager.Push(MapEditorPanel.Panel));
            Button team = GetComponentInChildrenByPath<Button>("RightNaveBar/team");
            team.onClick.AddListener(() =>
            {
                TeamPanel teamPanel = TeamPanel.Panel;
                teamPanel.SetTeamManagementMode();
                PanelManager.Push(teamPanel);
            });
        }
    }
}

