using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;

namespace MyUI
{
    public static class PanelManager
    {
        private static List<BasePanel> stackPanel;
        private static int stackPanelCount;
        public static Transform Panel;

        static PanelManager()
        {
            Panel = GameObject.Find("GameUI/Panels").transform;
            if (Panel == null)
            {
                Debug.LogError("没有GameUI,请创建");
            }
            stackPanel = new List<BasePanel>();
            stackPanelCount = 0;
        }
        public static void HidePauseUI()
        {
            for (int i = 0; i < stackPanelCount - 1; i++)
            {
                stackPanel[i].UIObject.SetActive(false);
            }
        }
        public static void Push(BasePanel nextPanel)
        {
            if (stackPanel.Count > 0)
            {
                stackPanel[stackPanelCount - 1].OnPause();
            }
            stackPanel.Add(nextPanel);
            stackPanelCount++;
            nextPanel.OnEnter();
        }
        public static void Pop(int popCount)
        {
            for (int i = stackPanelCount - 1; i >= 0; i--)
            {
                if (stackPanelCount - i > popCount)
                {
                    stackPanelCount -= popCount;
                    stackPanel.RemoveRange(stackPanelCount, popCount);
                    stackPanel[stackPanelCount - 1].OnResume();
                    return;
                }
                stackPanel[i].OnExit();
            }
            stackPanel.Clear();
            stackPanelCount = 0;
        }
        public static void PopTo(BasePanel popToPanel)
        {
            
            for (int i = stackPanelCount - 1; i >= 0; i--)
            {
                if (stackPanel[i].UIType.Name != popToPanel.UIType.Name)
                {
                    stackPanel[i].OnExit();
                    stackPanel.RemoveAt(i);
                    stackPanelCount--;
                }
                else
                {
                    stackPanel[i].OnResume();
                    return;
                }
            }
            stackPanel.Clear();
            stackPanelCount = 0;
        }
    }

}
