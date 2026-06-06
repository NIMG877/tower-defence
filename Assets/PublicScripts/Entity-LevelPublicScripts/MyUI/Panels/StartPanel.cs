using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace MyUI
{
    public class StartPanel : BasePanel
    {
        private static StartPanel _instance;
        public static StartPanel Panel
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new StartPanel();
                }
                return (StartPanel)_instance;
            }
        }
        private StartPanel() : base(new UIType("Prefabs/UI/MyUIs/StartPanel"))
        {
            Button login = GetComponentInChildrenByPath<Button>("LogIn");
            if (login != null)
            {
                login.onClick.AddListener(() =>
                    PanelManager.Push(HomePanel.Panel)
                );
            }
        }
    }
}

