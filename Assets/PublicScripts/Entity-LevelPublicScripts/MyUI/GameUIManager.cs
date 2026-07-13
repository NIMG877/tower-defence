using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MyUI;

public class GameUIManager : MonoBehaviour
{
    void Start()
    {
        PanelManager.Push(HomePanel.Panel);
    }


}
